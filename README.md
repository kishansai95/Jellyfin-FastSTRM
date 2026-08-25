# Jellyfin FastSTRM Plugin

A Jellyfin plugin that speeds up playback of `.strm` files. It removes the long startup delay caused by the server probing the remote direct link for transcoding decisions before playback begins.

## How it works

The plugin intercepts `/PlaybackInfo` requests. When the requested item is a `.strm` video, it reads the direct http(s) URL from the file and returns a media source marked as direct play (no transcoding, no probing) instead of letting Jellyfin analyse the remote stream. It also rewrites external subtitle delivery URLs so they carry a valid auth token, and chooses the default audio and subtitle tracks.

## Installation

### Option 1: Download the prebuilt DLL (recommended)

1. Download the latest `FastSTRM.dll` from the [Releases](https://github.com/mmyysnd/Jellyfin-FastSTRM/releases) page.
2. Put it in the `plugins/FastSTRM` folder inside your Jellyfin server data directory (create the `FastSTRM` folder if it does not exist):
   - Docker: `/config/plugins/FastSTRM`
   - Linux (native): `/var/lib/jellyfin/plugins/FastSTRM`
   - Windows (tray/portable): `%LOCALAPPDATA%\jellyfin\plugins\FastSTRM`
   - Windows (service): `C:\ProgramData\Jellyfin\Server\plugins\FastSTRM`
3. Restart the Jellyfin server.
4. Open Dashboard -> Plugins and confirm that FastSTRM is loaded.

### Option 2: Build from source

Requires the .NET 9.0 SDK.

```bash
git clone https://github.com/kishansai95/Jellyfin-FastSTRM.git
cd Jellyfin-FastSTRM
dotnet build -c Release
```

The DLL is produced at `bin/Release/net9.0/FastSTRM.dll`. Deploy it as described in Option 1.

## Configuration

Dashboard -> Plugins -> FastSTRM. Both fields take comma separated language codes in order of preference; matching is by prefix, so `en` matches `en` and `eng`, and `ta` matches `ta` and `tam`.

| Setting | Default | Description |
| --- | --- | --- |
| Preferred audio languages | `en,ta` | Language order used to pick the default audio track. |
| Preferred subtitle languages | `en,ta` | Language order used to pick the default subtitle track. |

Track selection order, first match wins:

- Audio: index requested by the client -> preferred language -> track flagged default in the media -> first audio track.
- Subtitle: index requested by the client -> preferred language -> track flagged default in the media -> first external track -> first subtitle track.

Leaving a field empty skips the language step and falls back to the media's own default track.

## Requirements

- Jellyfin server 10.11.x
- .NET 9.0 SDK (only to build from source)
