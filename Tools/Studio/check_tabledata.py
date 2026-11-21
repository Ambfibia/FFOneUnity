"""Compare the production server's XDT with the Unity publication source."""

import argparse
import hashlib
import json
from pathlib import Path


def read_document(path):
    data = path.read_bytes()
    document = json.loads(data.decode("utf-8-sig"))
    if not isinstance(document, dict) or not document.get("m_pInstanceTable"):
        raise ValueError(f"Invalid XDT document: {path}")
    return document, hashlib.sha256(data).hexdigest()


def main():
    unity_root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server-xdt", type=Path,
                        default=unity_root.parent / "RustyFusion/tabledata/xdt.json")
    parser.add_argument("--unity-xdt", type=Path,
                        default=unity_root / "Assets/FFOne/Data/Gameplay/Source/tables/xdt.json")
    args = parser.parse_args()

    server, server_hash = read_document(args.server_xdt)
    unity, unity_hash = read_document(args.unity_xdt)
    server_metadata = server.pop("_ffone", None)
    unity_metadata = unity.pop("_ffone", None)
    common = server.keys() & unity.keys()
    missing_in_unity = sorted(server.keys() - unity.keys())
    missing_in_server = sorted(unity.keys() - server.keys())
    unity_canonical = lambda section: json.dumps(section, sort_keys=True, separators=(",", ":"))
    changed = sorted(key for key in common if unity_canonical(server[key]) != unity_canonical(unity[key]))
    matches = not (missing_in_unity or missing_in_server or changed)
    print(json.dumps({
        "serverSha256": server_hash,
        "unitySha256": unity_hash,
        "gameplayMatches": matches,
        "publicationMetadataMatches": server_metadata == unity_metadata,
        "changedSections": changed,
        "missingInUnity": missing_in_unity,
        "missingInServer": missing_in_server,
    }, indent=2))
    return 0 if matches else 1


if __name__ == "__main__":
    raise SystemExit(main())
