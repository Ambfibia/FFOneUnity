"""Recover the two additional Cheese ability clips from the matching Academy model."""
from pathlib import Path
import hashlib
import json
import struct
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
TOOL = ROOT.parent / "FusionForge/target/debug/fusionforge.exe"
DONOR = ROOT.parent / "builds/OG-academy/Nano_041.resourceFile"
OUT = ROOT / "Assets/FFOne/Editor/ActorMotionSources/AcademyCheeseSkills.glb"
EXPECTED = "0e4810aef59c9a21a8a63672585be0f3ca426d4904d708e91717ac8f50177082"
CLIPS = {"skill2": 1835128276, "skill3": 1848709271}

def write_motion_source(source):
    raw = source.read_bytes()
    size = struct.unpack_from("<I", raw, 12)[0]
    doc = json.loads(raw[20:20+size])
    buffer = raw[28+size:]
    animations = [row for row in doc["animations"] if row["name"] in CLIPS]
    assert {row["name"] for row in animations} == CLIPS.keys()
    accessors, views, binary = [], [], bytearray()
    remap, remap_view = {}, {}
    for anim in animations:
        for sampler in anim["samplers"]:
            for field in ("input", "output"):
                idx = sampler[field]
                if idx not in remap:
                    acc = doc["accessors"][idx].copy()
                    vi = acc["bufferView"]
                    if vi not in remap_view:
                        view = doc["bufferViews"][vi].copy()
                        off = view.get("byteOffset", 0)
                        binary.extend(b"\0" * (-len(binary) % 4))
                        view["byteOffset"] = len(binary)
                        binary.extend(buffer[off:off+view["byteLength"]])
                        remap_view[vi] = len(views)
                        views.append(view)
                    acc["bufferView"] = remap_view[vi]
                    remap[idx] = len(accessors)
                    accessors.append(acc)
                sampler[field] = remap[idx]
    nodes = [{key: value for key, value in node.items() if key in ("name","children","translation","rotation","scale")}
             for node in doc["nodes"]]
    output = {"asset":doc["asset"],"scene":doc["scene"],"scenes":doc["scenes"],"nodes":nodes,
              "animations":animations,"buffers":[{"byteLength":len(binary)}],"bufferViews":views,"accessors":accessors}
    encoded = json.dumps(output,ensure_ascii=False,separators=(",", ":")).encode("utf-8")
    encoded += b" " * (-len(encoded) % 4)
    binary.extend(b"\0" * (-len(binary) % 4))
    length = 28 + len(encoded) + len(binary)
    OUT.parent.mkdir(exist_ok=True)
    OUT.write_bytes(struct.pack("<4sIII4s",b"glTF",2,length,len(encoded),b"JSON")+encoded+
                    struct.pack("<I4s",len(binary),b"BIN\0")+binary)

if __name__ == "__main__":
    if hashlib.sha256(DONOR.read_bytes()).hexdigest() != EXPECTED:
        raise ValueError(f"Unexpected Academy Cheese container: {DONOR}")
    with tempfile.TemporaryDirectory(prefix="ffone-cheese-skills-") as temp:
        work = Path(temp)
        logical = work / "model.json"
        subprocess.run([TOOL,"export-logical-model-source",DONOR,"nano/nano_cheese.kfm",logical],check=True)
        source = json.loads(logical.read_bytes())
        if {a["name"]:a["pathId"] for a in source["animations"] if a["name"] in CLIPS and a["hasPoseCurves"]} != CLIPS:
            raise ValueError("Academy Cheese AnimationClip identity or curves changed")
        subprocess.run([TOOL,"publish-logical-model",logical,"characters",work/"native","--semantic-directory","nanos",
                        "--semantic-directory","nano_cheese"],check=True)
        write_motion_source(work/"native/characters/nanos/nano_cheese/nano_cheese.glb")
    print(OUT)
