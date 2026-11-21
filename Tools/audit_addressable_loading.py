"""Summarize a Unity Build Layout, including transitive bundle costs per address.

Bundle file sizes describe the dependency closure on disk, not resident asset RAM.
"""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROBES = (
    "ui/en/gameplay/loading/retrobution-load",
    "ui/en/launcher/login/login_screen_bg_16x10",
    "audio/music/00_main_theme",
    "audio/voice/en/computress/computress_tutorial_01",
    "NativeEditable/Index",
    "NativeData/characters/player/shared/player_rig_contract.json",
    "NativeData/data/character_creation/avatar_items.json",
)


def summarize(path):
    layout = json.loads(path.read_text(encoding="utf-8-sig"))
    rows = layout["references"]["RefIds"]
    refs = {row["rid"]: row["data"] for row in rows}
    bundles = {row["rid"]: row["data"] for row in rows if row["type"]["class"] == "BuildLayout/Bundle"}
    assets = [row["data"] for row in rows if row["type"]["class"] == "BuildLayout/ExplicitAsset"]

    def closure(start):
        visited, pending = set(), [start]
        while pending:
            rid = pending.pop()
            if rid in visited or rid not in bundles:
                continue
            visited.add(rid)
            pending.extend(dep["rid"] for dep in bundles[rid].get("Dependencies", []))
        return visited

    def cost(asset):
        bundle = bundles[asset["Bundle"]["rid"]]
        dependencies = closure(asset["Bundle"]["rid"])
        return {"address": asset["AddressableName"], "group": refs.get(bundle["Group"]["rid"], {}).get("Name"),
                "bundleBytes": bundle["FileSize"], "closureBundles": len(dependencies),
                "closureBytes": sum(bundles[rid]["FileSize"] for rid in dependencies)}

    probes = [asset for asset in assets if asset["AddressableName"] in PROBES]
    for prefix in ("NativeEditable/characters/player/items/shirt/", "audio/voice/en/computress/"):
        candidate = next((a for a in sorted(assets, key=lambda a: a["AddressableName"]) if a["AddressableName"].startswith(prefix) and
                          (a["AssetPath"].endswith(".prefab") if prefix.startswith("Native") else True)), None)
        if candidate:
            probes.append(candidate)
    address_hash = hashlib.sha256("\n".join(sorted(a["Guid"] + " " + a["AddressableName"] for a in assets)).encode()).hexdigest()
    return {
        "report": str(path), "buildStarted": layout["BuildStartTime"], "buildError": layout["BuildError"],
        "durationSeconds": layout["Duration"], "groups": len(layout["Groups"]),
        "bundles": len(bundles), "bundleBytes": sum(b["FileSize"] for b in bundles.values()),
        "explicitAssets": len(assets), "addressGuidSha256": address_hash,
        "duplicatedAssetCount": layout["DuplicatedAssetCount"],
        "largestBundles": [{"name": b["Name"], "bytes": b["FileSize"]} for b in
                           sorted(bundles.values(), key=lambda b: b["FileSize"], reverse=True)[:10]],
        "probes": sorted((cost(a) for a in probes), key=lambda a: a["address"]),
        "note": "Closure includes the requested bundle and unique transitive dependencies. Bytes are compressed files, not RAM.",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--layout", type=Path, default=ROOT / "Library/com.unity.addressables/buildlayout.json")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = summarize(args.layout)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
    if result["buildError"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
