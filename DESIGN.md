# TunaBackIn: design

Target: Jellyfin **12.4.0** (`Jellyfin.Controller`/`Jellyfin.Model` 12.4.0, net10.0).
Clients: jellyfin-web in the browser and on the TV (Plasma Bigscreen).

## Flow (per device)

1. **Capture**: on every audio `PlaybackStopped` (plus the latest `PlaybackProgress` for position),
   snapshot the full queue from `SessionInfo.NowPlayingQueue`: item IDs, current index (via
   `PlaylistItemId`), `PositionTicks`, shuffle/repeat. Keep only the latest snapshot, keyed
   `userId:deviceId`, stored as JSON in the plugin data dir.
2. **Arm**: when a video starts on the same device and music played within the last 2 min (configurable),
   mark the snapshot *pending*. The queue is cached from progress reports, because some reports omit it.
3. **Offer**: when that video stops, the pending snapshot becomes an *offer*. It shows once the device has been
   idle for 4 s. If the next episode autoplays, it goes back to *pending*. Offers expire after 6 h.
4. **Prompt**: an injected jellyfin-web script picks up the offer and shows
   `Continue music streaming of "X"` with Resume / Dismiss buttons (they must be focusable for
   TV-remote navigation). X = album name if the queue is one album, else the current track's album
   plus "+N more".
5. **Resume**: the button calls `POST /TunaBackIn/Resume?deviceId=` →
   `ISessionManager.SendPlayCommand(PlayNow, ItemIds, StartIndex, StartPositionTicks)` on that session.
   Dismiss calls `DELETE /TunaBackIn/Offer?deviceId=`.

## Web injection

The plugin rewrites `index.html` responses in memory (ASP.NET middleware registered from the plugin)
to add `<script src="/TunaBackIn/client.js">`. It doesn't write to the web dir (root-owned on the
Fedora RPM) and has no dependency on third-party plugins. The script gets the offer by polling
`GET /TunaBackIn/Offer?deviceId=` every few seconds, using `ApiClient.deviceId()` and the client's auth token.

Verified on 12.4.0: `window.ApiClient` (`accessToken`, `deviceId`, `getUrl`) exists in jellyfin-web, and `index.html`
is served brotli-compressed with an ETag, which is why the middleware removes `Accept-Encoding` and the conditional
headers from the request. Event and manager names compile against 12.4.0. Not verified until deployed: whether a
plugin's `IStartupFilter` is applied. Use the config page's "Show test prompt" button to check.

Event order isn't guaranteed (a video's PlaybackStart can be processed before the music's PlaybackStopped).
Audio events up to 5 s after a video start count as the old music stopping.

## Endpoints (auth required, own sessions only)

- `GET /TunaBackIn/Snapshot?deviceId=`: inspect the snapshot
- `GET /TunaBackIn/Offer?deviceId=`: pending offer or 204
- `POST /TunaBackIn/Resume?deviceId=`: resume
- `DELETE /TunaBackIn/Offer?deviceId=`: dismiss

`deviceId` goes in the query string, not the path: jellyfin-web ids are `btoa(...)` output and can contain `/`,
and ASP.NET leaves `%2F` undecoded in route values.

Also usable from Home Assistant via `rest_command`.

## Config

- Show resume prompt (default ON)
- Auto-resume without prompting (default OFF)

## Out of scope for v1

- Resuming on a different device (the store key leaves room for user-level later)
- Native clients (would need a `DisplayMessage` popup without a button, or auto-resume)
