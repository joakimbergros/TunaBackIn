#!/usr/bin/env python3
"""Feasibility spike for the TunaBackIn plugin (stdlib only).

Checks, for sessions currently playing audio:
  1. NowPlayingQueue holds the full queue (>1 item), not null.
  2. SupportsRemoteControl is true and the session accepts a play command.

Usage:
  export JELLYFIN_API_KEY=...            # or put it in ~/.config/jellyfin-music-resume/api_key
  ./spike.py check                       # read-only: inspect /Sessions
  ./spike.py snapshot                    # save queue + position of the audio session to snapshot.json
  ./spike.py restore                     # send PlayNow with the saved queue/index/position
"""
import json
import os
import pathlib
import sys
import urllib.parse
import urllib.request

SERVER = os.environ.get("JELLYFIN_URL", "http://192.168.8.50:8096")
SNAPSHOT = pathlib.Path(__file__).with_name("snapshot.json")
KEY_FILE = pathlib.Path.home() / ".config/jellyfin-music-resume/api_key"


def api_key() -> str:
    key = os.environ.get("JELLYFIN_API_KEY") or (KEY_FILE.read_text().strip() if KEY_FILE.exists() else "")
    if not key:
        sys.exit(f"No API key: set JELLYFIN_API_KEY or write it to {KEY_FILE}")
    return key


def call(method: str, path: str, query: dict | None = None):
    url = SERVER + path + ("?" + urllib.parse.urlencode(query) if query else "")
    req = urllib.request.Request(url, method=method, headers={"Authorization": f'MediaBrowser Token="{api_key()}"'})
    with urllib.request.urlopen(req, timeout=10) as resp:
        body = resp.read()
        return resp.status, (json.loads(body) if body else None)


def audio_sessions() -> list[dict]:
    _, sessions = call("GET", "/Sessions")
    return [s for s in sessions if (s.get("NowPlayingItem") or {}).get("MediaType") == "Audio"]


def current_index(s: dict) -> int | None:
    queue = s.get("NowPlayingQueue") or []
    ps = s.get("PlayState") or {}
    pid = ps.get("PlaylistItemId")
    if pid:
        for i, q in enumerate(queue):
            if q.get("PlaylistItemId") == pid:
                return i
    item_id = s["NowPlayingItem"]["Id"]  # fallback: ambiguous if a track repeats in the queue
    return next((i for i, q in enumerate(queue) if q.get("Id") == item_id), None)


def check() -> int:
    sessions = audio_sessions()
    if not sessions:
        print("No session is playing audio. Start music on the TV and rerun.")
        return 1
    ok = True
    for s in sessions:
        queue = s.get("NowPlayingQueue")
        ps = s.get("PlayState") or {}
        print(f"\n== {s.get('Client')} / {s.get('DeviceName')} (device {s.get('DeviceId')}, user {s.get('UserName')})")
        print(f"   now playing     : {s['NowPlayingItem'].get('Name')}")
        print(f"   queue length    : {len(queue) if queue is not None else 'null'}")
        print(f"   current index   : {current_index(s)}  (PlayState.PlaylistItemId={ps.get('PlaylistItemId')!r})")
        print(f"   position ticks  : {ps.get('PositionTicks')}")
        print(f"   shuffle/repeat  : {ps.get('ShuffleMode')} / {ps.get('RepeatMode')}")
        print(f"   remote control  : {s.get('SupportsRemoteControl')}")
        print(f"   supported cmds  : {', '.join(s.get('SupportedCommands') or []) or '(none)'}")
        c1 = queue is not None and len(queue) > 1
        c2 = bool(s.get("SupportsRemoteControl"))
        print(f"   CHECK 1 queue>1 : {'PASS' if c1 else 'FAIL'}")
        print(f"   CHECK 2 remote  : {'PASS' if c2 else 'FAIL'} (play-command acceptance: use snapshot + restore)")
        ok &= c1 and c2
    return 0 if ok else 2


def snapshot() -> int:
    sessions = audio_sessions()
    if len(sessions) != 1:
        print(f"Expected exactly one audio session, found {len(sessions)}.")
        return 1
    s = sessions[0]
    ps = s.get("PlayState") or {}
    snap = {
        "sessionDeviceId": s["DeviceId"],
        "itemIds": [q["Id"] for q in s.get("NowPlayingQueue") or []],
        "startIndex": current_index(s),
        "positionTicks": ps.get("PositionTicks") or 0,
        "shuffle": ps.get("ShuffleMode"),
        "repeat": ps.get("RepeatMode"),
    }
    SNAPSHOT.write_text(json.dumps(snap, indent=2))
    print(f"Saved {len(snap['itemIds'])} items, index {snap['startIndex']}, "
          f"{snap['positionTicks'] / 1e7:.0f}s into the track -> {SNAPSHOT}")
    return 0


def restore() -> int:
    snap = json.loads(SNAPSHOT.read_text())
    _, sessions = call("GET", "/Sessions")
    target = next((s for s in sessions if s.get("DeviceId") == snap["sessionDeviceId"]), None)
    if not target:
        print("The device that saved the snapshot has no active session (is the client open?).")
        return 1
    status, _ = call("POST", f"/Sessions/{target['Id']}/Playing", {
        "playCommand": "PlayNow",
        "itemIds": ",".join(snap["itemIds"]),
        "startIndex": snap["startIndex"] or 0,
        "startPositionTicks": snap["positionTicks"],
    })
    print(f"PlayNow sent -> HTTP {status}. Check that the TV resumed the right track at about the right position.")
    return 0


if __name__ == "__main__":
    cmds = {"check": check, "snapshot": snapshot, "restore": restore}
    if len(sys.argv) != 2 or sys.argv[1] not in cmds:
        sys.exit(__doc__)
    sys.exit(cmds[sys.argv[1]]())
