"""Recover eight exact primary objects omitted by the accepted native catalogue.

FusionForge owns object decoding and PNG export. Audio copies the original Ogg
payload unchanged. Existing different output bytes are always a conflict.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import subprocess
from content_paths import resolve

ROOT = Path(__file__).resolve().parents[1]
FORGE = ROOT.parent / "FusionForge"
SOURCE = ROOT.parent / "builds/retrobution-20260821"
BUNDLE = SOURCE / "Tutorial.resourceFile"
SHA = "729e9aad557709d4a75937b7478b6784b82f202c000d13127d1074213991f916"
ASSET = "CustomAssetBundle-b4f543c102ded400fbc6f1da25d9679a"
ROWS = [(7268, "spawn_cs_run1_1"), (7269, "spawn_cs_run1_2"), (7270, "spawn_cs_skill0"),
        (7271, "spawn_cs_melee2_1"), (7272, "spawn_cs_melee2_2"), (7273, "spawn_cs_melee2_3")]


def forge(*args):
    return json.loads(subprocess.check_output([str(FORGE / "target/debug/fusionforge.exe"), *map(str, args)], cwd=FORGE, text=True))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    if hashlib.sha256(BUNDLE.read_bytes()).hexdigest() != SHA:
        raise RuntimeError("The immutable primary source hash differs")
    outputs = []
    for pid, name in ROWS:
        result = forge("dump-object-evidence", "primary", SOURCE, "Tutorial.resourceFile", pid,
                       "--serialized-asset", ASSET, "--type", "AudioClip", "--out", "-", "--allow-unresolved-pointers")
        value = result["object"]["value"]
        if value["m_Name"] != name:
            raise RuntimeError("Source AudioClip owner differs")
        data = base64.b64decode(value["audio data"]["base64"])
        if not data.startswith(b"OggS"):
            raise RuntimeError("The source audio is not an Ogg payload")
        relative = "audio/sfx/creatures/spawn_cs/" + name.removeprefix("spawn_cs_") + ".ogg"
        outputs.append((pid, name, relative, data))
    for pid, name in [(7253, "npc_numbuhtwo_face_talk"), (7254, "npc_numbuhtwo_face_talksad")]:
        result = forge("export-exact-texture", BUNDLE, pid, "-")
        # The exporter supplies final pixels with the established PNG origin.
        data = base64.b64decode(result["payload"]["dataUrl"].split(",", 1)[1])
        relative = "characters/npcs/npc_numbuhtwo_future/textures/" + name + ".png"
        outputs.append((pid, name, relative, data))
    prefix = ROOT / "Assets/Resources"
    for _, _, path, data in outputs:
        target = resolve(prefix / path)
        if target.exists() and target.read_bytes() != data:
            raise RuntimeError("Refusing a distinct authored output: " + str(target))
    if args.apply:
        for _, _, path, data in outputs:
            target = resolve(prefix / path)
            target.parent.mkdir(parents=True, exist_ok=True)
            if not target.exists():
                target.write_bytes(data)
    report = {"sourceRole": "primary", "source": str(BUNDLE), "sourceSha256": SHA, "serializedAsset": ASSET,
              "applied": args.apply, "outputs": [{"pathId": pid, "name": name, "path": str(resolve(prefix / path)),
                 "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)} for pid, name, path, data in outputs]}
    out = ROOT / "Logs/CinematicFollowup/recovered-presentation.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"{'Recovered' if args.apply else 'Verified'} {len(outputs)} exact primary objects")


if __name__ == "__main__":
    main()
