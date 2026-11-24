# Pocket Cast

An experimental Windows receiver for sharing an iPhone's screen and audio over AirPlay, with a USB tethering mode for supported Apple network drivers.

**[Download page](https://jjdelite06.github.io/pocket-cast/)** · **[Windows download](https://github.com/JJDELITE06/pocket-cast/releases/download/v1.1.0/PocketCast-Windows-1.1.zip)** · **[Release notes](https://github.com/JJDELITE06/pocket-cast/releases/tag/v1.1.0)**

## Status

This is an **experimental prerelease**. The frontend compiles and its WPF markup parses. Local checks cover receiver discovery, RTSP responses, media playback components, and explicit network interface selection. Live iPhone mirroring and USB cable behavior have not been verified. Windows Application Control blocked execution of the updated frontend in the build environment. See [VALIDATION.txt](VALIDATION.txt) for the complete scope.

## Connect over Wi-Fi

1. Download and extract the entire Windows ZIP. Open `PocketCast.exe` with the `runtime` folder beside it.
2. Connect the iPhone and PC to the same local network. Start the receiver.
3. On iPhone, open **Control Center → Screen Mirroring**, select the receiver name, and enter the displayed PIN if asked.

The picture appears in a separate resizable window. Audio uses the PC's default output. Stop with **Stop receiver** or the iPhone's Screen Mirroring menu. Allow the included receiver through Windows Firewall on your trusted private network if prompted.

School, guest, and enterprise Wi-Fi may separate clients or block discovery even when both devices show the same Wi-Fi name. This app cannot change the router's network policy.

## USB tethering

USB mode carries AirPlay over the iPhone's USB network connection. It requires a data-capable cable, Apple device support for Windows, an active **Apple Mobile Device Ethernet** connection, and **Personal Hotspot** enabled on iPhone. It is not raw QuickTime USB capture.

Select **USB tethering - Personal Hotspot** in the app and follow **USB setup**. The app checks for a working Apple network adapter and stops the receiver if that adapter disappears. Carrier support for Personal Hotspot is required, and other Windows traffic may use cellular data.

**Windows on ARM:** a compatible ARM64 iPhone Ethernet driver is required. Recognizing the iPhone in Apple Devices does not prove USB networking works; an x64 kernel driver cannot substitute for an ARM64 driver. The app does not install or replace drivers.

## Quality and requirements

- Windows 10/11 x64; the receiver can also run under Windows 11 ARM's x64 emulation, subject to device/driver compatibility.
- .NET Framework 4.8+, Direct3D 11 graphics, and a working audio output.
- Maximum mode requests up to **3840 × 2160 at 60 fps** with HEVC. H.264 modes request 1080p or 720p at up to 60 fps.
- The iPhone chooses the actual stream. Resolution, frame rate, quality, and latency depend on the device, content, network, and PC. AirPlay video is compressed; native-resolution, lossless video, HDR, and 120 fps are not guaranteed.
- Protected content can block mirroring. Microphone/call audio is not guaranteed. This app does not remotely control the iPhone.

## Source and integrity

The app ZIP contains its frontend source and license notices. The [complete corresponding-source bundle](https://github.com/JJDELITE06/pocket-cast/releases/download/v1.1.0/PocketCast-Corresponding-Source-1.1.zip) includes the pinned UxPlay source, the local USB-interface patch, and exact source packages for the bundled native dependencies. It is about 1.2 GB and is not needed to run the app.

Verify downloads against [SHA256SUMS-1.1.txt](https://github.com/JJDELITE06/pocket-cast/releases/download/v1.1.0/SHA256SUMS-1.1.txt). No private pairing keys, user settings, or codec caches are included.

To build the frontend on Windows with .NET Framework 4.8:

```powershell
./build.ps1
```

The build normally runs the argument self-test. `-SkipSelfTest` compiles without executing it; use that only when the execution limit is recorded. `validate-markup.ps1` parses the WPF resource graph without opening a window. For the native receiver, see [native/BUILDING.txt](native/BUILDING.txt), the supplied patch, and the complete source release asset. Place the native runtime beside the built frontend to run it.

## License and credits

Pocket Cast's frontend is GPL-3.0-or-later. The included receiver is [UxPlay](https://github.com/FDH2/UxPlay), pinned to commit `48f5ce47c9ef694afccfac907c9e8828329657fe` and modified by the included USB-interface patch. Third-party components retain their own licenses. See [LICENSE](LICENSE), [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt), and the runtime notices in the downloads.

Pocket Cast is independent of Apple. iPhone and AirPlay are Apple trademarks.
