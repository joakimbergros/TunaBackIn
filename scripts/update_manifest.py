#!/usr/bin/env python3
"""Add a release to the Jellyfin plugin repository manifest (manifest.json).

Plugin metadata (guid, name, overview, description, owner, category, targetAbi,
changelog) comes from build.yaml; the version, download URL and checksum come
from the release being published. Re-running for an existing version replaces it.

  update_manifest.py --version 0.1.0.0 --zip path/to/plugin.zip --source-url URL
"""
import argparse
import datetime
import hashlib
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent


def read_build_yaml(path: pathlib.Path) -> dict:
    """Minimal reader for the flat build.yaml: `key: "value"` and folded `key: >` blocks."""
    data, key, block = {}, None, []
    for line in path.read_text().splitlines():
        if key and (line.startswith("  ") or not line.strip()):
            block.append(line.strip())
            continue
        if key:
            data[key] = " ".join(b for b in block if b)
            key, block = None, []
        m = re.match(r'^(\w+):\s*(.*)$', line)
        if not m:
            continue
        k, v = m.groups()
        if v == ">":
            key = k
        elif v:
            data[k] = v.strip().strip('"')
    if key:
        data[key] = " ".join(b for b in block if b)
    return data


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", required=True, help="four-part version, e.g. 0.1.0.0")
    ap.add_argument("--zip", required=True, type=pathlib.Path)
    ap.add_argument("--source-url", required=True)
    ap.add_argument("--manifest", type=pathlib.Path, default=ROOT / "manifest.json")
    ap.add_argument("--build-yaml", type=pathlib.Path, default=ROOT / "build.yaml")
    args = ap.parse_args()

    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", args.version):
        sys.exit(f"version must have four parts, got {args.version!r}")

    meta = read_build_yaml(args.build_yaml)
    manifest = json.loads(args.manifest.read_text()) if args.manifest.exists() else []
    plugin = next((p for p in manifest if p.get("guid") == meta["guid"]), None)
    if plugin is None:
        plugin = {"guid": meta["guid"], "versions": []}
        manifest.append(plugin)
    plugin.update({k: meta[k] for k in ("name", "overview", "description", "owner", "category")})

    entry = {
        "version": args.version,
        "changelog": meta.get("changelog", ""),
        "targetAbi": meta["targetAbi"],
        "sourceUrl": args.source_url,
        "checksum": hashlib.md5(args.zip.read_bytes()).hexdigest(),
        "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    versions = [v for v in plugin["versions"] if v["version"] != args.version] + [entry]
    plugin["versions"] = sorted(versions, key=lambda v: tuple(map(int, v["version"].split("."))), reverse=True)

    args.manifest.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"manifest: {plugin['name']} {args.version} -> {args.source_url} (md5 {entry['checksum']})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
