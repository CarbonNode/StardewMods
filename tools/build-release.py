#!/usr/bin/env python3
"""Build the Nexus release zips for Shared Day (main file) and Many Farms (optional file).

Usage: python3 build-release.py <mods-dir> <out-dir> [--nexus-id N]

<mods-dir> holds the RELEASED mod folders exactly as the pack ships them (SharedDay/, ManyFarms/;
e.g. a copy of a farm host's Mods folder). Only the files a player needs are taken:
manifest.json, the two DLLs, and Many Farms' assets/. Never config.json, backups/ or logs:
a host's config carries its farm password hash, tracker token and join address.
--nexus-id adds "Nexus:<id>" to each manifest's UpdateKeys so SMAPI reports updates.
"""
import json, sys, zipfile, hashlib
from pathlib import Path

MODS = {
    "SharedDay": ["manifest.json", "SharedDay.dll", "SharedDay.Core.dll"],
    "ManyFarms": ["manifest.json", "ManyFarms.dll", "ManyFarms.Core.dll", "assets/"],
}
FORBIDDEN = ("config.json", "backups", ".log", ".bak")

def main():
    args = sys.argv[1:]
    nexus_id = None
    if "--nexus-id" in args:
        i = args.index("--nexus-id"); nexus_id = args[i + 1]; del args[i:i + 2]
    src, out = Path(args[0]), Path(args[1]); out.mkdir(parents=True, exist_ok=True)
    for mod, wanted in MODS.items():
        root = src / mod
        files = []
        for w in wanted:
            p = root / w
            files += sorted(f for f in p.rglob("*") if f.is_file()) if w.endswith("/") else [p]
        manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8-sig"))
        if nexus_id:
            keys = [k for k in manifest.get("UpdateKeys", []) if not k.startswith("Nexus:")]
            manifest["UpdateKeys"] = keys + [f"Nexus:{nexus_id}"]
        name = {"SharedDay": "Shared Day", "ManyFarms": "Many Farms"}[mod]
        zpath = out / f"{name} {manifest['Version']}.zip"
        with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
            for f in files:
                rel = f.relative_to(root).as_posix()
                if any(x in rel for x in FORBIDDEN):
                    sys.exit(f"refusing to ship {mod}/{rel}")
                if rel == "manifest.json":
                    z.writestr(f"{mod}/manifest.json", json.dumps(manifest, indent=2) + "\n")
                else:
                    z.write(f, f"{mod}/{rel}")
        h = hashlib.sha256(zpath.read_bytes()).hexdigest()[:16]
        print(f"{zpath.name}: {len(files)} files, {zpath.stat().st_size} bytes, sha256 {h}")

if __name__ == "__main__":
    main()
