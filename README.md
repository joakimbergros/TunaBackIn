# TunaBackIn 🐟

*Tune back in to your music after the show.*

Watching an episode wipes out the music queue you had going. This plugin remembers that queue. When
the episode ends, jellyfin-web shows

> Continue music streaming of "Album name" [Resume] [Dismiss]

Resume starts the same queue again at the same track and position.

- Targets **Jellyfin 12.4.0** (.NET 10). The `Jellyfin.Controller`/`Jellyfin.Model` package versions in
  `Directory.Build.props` (`JellyfinVersion`) must match the server, otherwise the plugin shows as *NotSupported*.
- The prompt works in clients that use jellyfin-web: browsers and Jellyfin Media Player.
  Native clients can still use the HTTP endpoints, for example from Home Assistant.
- License: GPLv3.

See [DESIGN.md](DESIGN.md) for how it works.

## Build

```sh
dotnet test Jellyfin.Plugin.TunaBackIn.slnx
dotnet publish src/Jellyfin.Plugin.TunaBackIn -c Release -o artifacts
```

This produces `artifacts/Jellyfin.Plugin.TunaBackIn.dll`. It is the only file the server needs.

## Install

Copy the DLL into a folder under the server's `plugins` directory, then restart Jellyfin:

```sh
# Fedora RPM install (data dir /var/lib/jellyfin):
scp artifacts/Jellyfin.Plugin.TunaBackIn.dll 192.168.8.50:/tmp/
# The folder must be owned by jellyfin: the server writes meta.json there and refuses to start if it can't.
ssh -t 192.168.8.50 'D=/var/lib/jellyfin/plugins/TunaBackIn_0.1.0.0 \
    && sudo install -d -o jellyfin -g jellyfin -m 755 "$D" \
    && sudo install -o jellyfin -g jellyfin -m 644 /tmp/Jellyfin.Plugin.TunaBackIn.dll "$D/" \
    && sudo systemctl restart jellyfin'
```

**Upgrading from the old "Music Resume" build:** remove `/var/lib/jellyfin/plugins/MusicResume_0.1.0.0/` first,
otherwise both copies load. Settings reset to defaults, and any pending offer is lost.

For Docker, use `<config volume>/data/plugins/TunaBackIn_0.1.0.0/` instead and restart the container.

Check the install:

1. Dashboard → Plugins should list **TunaBackIn** as *Active*.
2. Reload jellyfin-web (on the TV too).
3. Open Dashboard → Plugins → TunaBackIn → **Show test prompt**. If it says the client script is
   not loaded, the `index.html` injection didn't work on this server. Open an issue or check the server log.

## Settings

| Setting | Default | Meaning |
|---|---|---|
| Show prompt | on | jellyfin-web shows the prompt after a video ends |
| Resume automatically | off | resume without asking, a few seconds after the video ends |
| Music must have played within | 120 s | a video only arms a resume if music played this recently on that device |
| Offer expires after | 6 h | older offers are dropped |

## HTTP API

Every endpoint requires auth (`Authorization: MediaBrowser Token="<token>"`). A user token can only see
its own snapshots. An API key or an admin can see any device's snapshot.

| Method | Path | Result |
|---|---|---|
| `GET` | `/TunaBackIn/Snapshot?deviceId=…` | The saved snapshot (queue, index, position, state) |
| `GET` | `/TunaBackIn/Offer?deviceId=…` | `{Title, ItemCount, CurrentIndex}` when the prompt should show, else `204` |
| `POST` | `/TunaBackIn/Resume?deviceId=…` | Resume on that device. `204` sent, `404` nothing saved, `409` device not connected |
| `DELETE` | `/TunaBackIn/Offer?deviceId=…` | Dismiss the offer |
| `GET` | `/TunaBackIn/client.js` | The injected web script (no auth) |

`POST` works whatever state the snapshot is in, so you can resume the last music at any time.

Find a device id with `./spike/spike.py check` (shown as `device …`), or with `GET /Sessions`.
jellyfin-web device ids are base64 and can contain `+`, `/` or `=`, so URL-encode them in the query string.

### Home Assistant

```yaml
# configuration.yaml
rest_command:
  jellyfin_resume_music_tv:
    url: "http://192.168.8.50:8096/TunaBackIn/Resume?deviceId=<url-encoded tv device id>"
    method: POST
    headers:
      Authorization: !secret jellyfin_auth_header

# secrets.yaml
jellyfin_auth_header: 'MediaBrowser Token="<api key>"'
```

Call the `rest_command.jellyfin_resume_music_tv` action from a button or an automation.

## Spike

`spike/spike.py` (stdlib Python) checks the server-side assumptions against a live server without the plugin:
`check` inspects `/Sessions`, `snapshot` saves the queue, and `restore` sends it back as a PlayNow command.
It reads the API key from `JELLYFIN_API_KEY` or `~/.config/jellyfin-music-resume/api_key`.
