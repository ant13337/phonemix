# PhoneMix

> Route Windows audio through an Android phone to Bluetooth headphones.

PhoneMix sends Windows playback audio to a browser on your Android phone:

```text
Windows audio -> local Wi-Fi -> Chrome on Android -> Bluetooth headphones
```

The phone does not need an app or Android Studio. Pair the phone with your Bluetooth
headphones, open the local PhoneMix webpage, and use the PC-audio slider there.
Android's own media volume remains separate, so you can play a podcast or music on
the phone and adjust the streamed PC audio independently.

## Requirements

- Windows 10 or later with the .NET 10 SDK.
- An Android phone with a modern browser.
- Both devices on the same private Wi-Fi/LAN.
- Bluetooth headphones paired to the phone.

## Run it

On the Windows PC:

```powershell
cd C:\why\windows-sender
dotnet restore
dotnet run
```

The sender prints a complete address such as
`http://192.168.1.20:47800/?token=...`. Open that complete address in Chrome on the
phone, tap **Start PC audio**, and allow Windows Firewall access on private networks
if prompted. The token is randomly generated every time the sender starts; do not
share the complete URL with untrusted people.

The first build downloads the NAudio dependency from NuGet. Windows Firewall may
need a rule allowing the sender on private networks.

Both devices must be on the same local network. The server is local-only in intent:
it does not upload audio to a cloud service. The current stream is uncompressed PCM
over WebSocket, which keeps the prototype dependency-free but uses more bandwidth
than a production Opus/WebRTC implementation. PCM is lossless, so it preserves the
captured Windows samples; WebSocket runs over TCP, so a Wi-Fi packet loss is repaired
by retransmission rather than becoming an audible click. The trade-off is that a
serious Wi-Fi interruption can briefly increase latency while TCP catches up.

The browser uses a small startup jitter buffer and bounded queue. It waits for about
100 ms of audio before starting, can absorb short delivery hiccups, and discards
oldest buffered audio rather than allowing latency to grow without limit.

The PC-audio slider ranges from 0% to 300%, so it can be louder than phone media.
The phone's own media volume is still controlled separately by Android.

## Limitations

- Wi-Fi and browser buffering add latency, so this is not ideal for competitive games.
- Keep the page open; Android may suspend background browser tabs.
- Phone calls can still switch Bluetooth into hands-free mode and interrupt or reduce
  audio quality.
- This prototype streams the default Windows playback device, not individually chosen
  applications.
- Only private IPv4 LAN addresses are printed, and the audio WebSocket requires the
  per-run token. This is protection against casual LAN access, not encryption; use a
  trusted network and do not port-forward PhoneMix to the internet.

## Security

PhoneMix is intended for a trusted home network. Audio is sent over plain HTTP and
WebSocket, so it is not encrypted. The per-run token prevents casual connections,
but it is not a substitute for HTTPS or network security. Never expose port 47800
to the public internet.

## License

PhoneMix is available under the [MIT License](./LICENSE).
