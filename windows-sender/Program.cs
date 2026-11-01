using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using NAudio.Wave;

const int port = 47800;
const int channels = 2;
const int bytesPerSample = 2;
const int framesPerPacket = 480;
var accessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();

var clients = new ConcurrentDictionary<Guid, AudioClient>();
using var capture = new WasapiLoopbackCapture();
var packet = new byte[framesPerPacket * channels * bytesPerSample];
var outputOffset = 0;

capture.DataAvailable += (_, eventArgs) =>
{
    var source = eventArgs.Buffer;
    var sourceChannels = capture.WaveFormat.Channels;
    var frameBytes = sourceChannels * sizeof(float);

    for (var inputOffset = 0; inputOffset + frameBytes <= eventArgs.BytesRecorded; inputOffset += frameBytes)
    {
        for (var channel = 0; channel < channels; channel++)
        {
            var sourceChannel = Math.Min(channel, sourceChannels - 1);
            var sample = BitConverter.ToSingle(source, inputOffset + sourceChannel * sizeof(float));
            var value = (short)(Math.Clamp(sample, -1.0f, 1.0f) * short.MaxValue);
            packet[outputOffset++] = (byte)value;
            packet[outputOffset++] = (byte)(value >> 8);
        }

        if (outputOffset == packet.Length)
        {
            foreach (var client in clients.Values)
            {
                client.Queue.Writer.TryWrite(packet);
            }

            packet = new byte[packet.Length];
            outputOffset = 0;
        }
    }
};

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
var app = builder.Build();
app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot"))
});

app.Map("/audio", async context =>
{
    if (!HasValidToken(context) || !context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var client = new AudioClient(socket);
    clients[client.Id] = client;
    try
    {
        await client.SendLoopAsync(context.RequestAborted);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
    }
    finally
    {
        clients.TryRemove(client.Id, out _);
        client.Queue.Writer.TryComplete();
    }
});

var lanAddresses = GetLanAddresses();
if (lanAddresses.Count == 0)
{
    Console.WriteLine($"Open http://<this-PC's-LAN-IP>:{port}/?token={accessToken} on your phone.");
}
else
{
    foreach (var address in lanAddresses)
    {
        Console.WriteLine($"Open http://{address}:{port}/?token={accessToken} on your phone.");
    }
}
Console.WriteLine("The URL contains a private access token; do not share it.");
Console.WriteLine("Pair the phone with your Bluetooth headphones first.");
Console.WriteLine("Press Ctrl+C to stop.");

capture.StartRecording();
try
{
    await app.RunAsync();
}
finally
{
    capture.StopRecording();
    foreach (var client in clients.Values)
    {
        client.Queue.Writer.TryComplete();
    }
}

static IReadOnlyList<IPAddress> GetLanAddresses()
{
    return NetworkInterface.GetAllNetworkInterfaces()
        .Where(network => network.OperationalStatus == OperationalStatus.Up)
        .Where(network => network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(network => network.GetIPProperties().UnicastAddresses)
        .Select(address => address.Address)
        .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
        .Where(address => !IPAddress.IsLoopback(address) && !address.ToString().StartsWith("169.254."))
        .Where(IsPrivateAddress)
        .Distinct()
        .ToArray();
}

bool HasValidToken(HttpContext context)
{
    var expected = accessToken;
    var provided = context.Request.Query["token"].ToString();
    return provided.Length == expected.Length &&
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(provided),
            System.Text.Encoding.UTF8.GetBytes(expected));
}

static bool IsPrivateAddress(IPAddress address)
{
    var bytes = address.GetAddressBytes();
    return bytes[0] == 10 ||
        (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
        (bytes[0] == 192 && bytes[1] == 168);
}

sealed class AudioClient
{
    public Guid Id { get; } = Guid.NewGuid();
    public Channel<byte[]> Queue { get; } = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(20) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly WebSocket socket;

    public AudioClient(WebSocket socket) => this.socket = socket;

    public async Task SendLoopAsync(CancellationToken token)
    {
        await foreach (var audioPacket in Queue.Reader.ReadAllAsync(token))
        {
            await socket.SendAsync(audioPacket, WebSocketMessageType.Binary, true, token);
        }
    }
}
