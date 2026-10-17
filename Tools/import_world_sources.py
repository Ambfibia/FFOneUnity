"""Preflight and copy the accepted native map dependency closure without modifying sources."""
import argparse
import hashlib
import json
from pathlib import Path
from content_paths import resolve, files
import struct
import shutil


def guarded(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root):
        raise ValueError(f"Dependency escaped content root: {relative}")
    return resolve(path)


def paths(value):
    if isinstance(value, dict):
        for key, item in value.items():
            if key == "path" and isinstance(item, str):
                yield item
            else:
                yield from paths(item)
    elif isinstance(value, list):
        for item in value:
            yield from paths(item)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").digest()


def main():
    project = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=project.parent / "FFOneClient/assets/game")
    parser.add_argument("--copy", action="store_true")
    args = parser.parse_args()
    source = args.source.resolve()
    destination = (project / "Assets/Resources").resolve()
    registry = json.loads(guarded(source, "map/catalog.json").read_text(encoding="utf-8"))
    dependencies = {"map/catalog.json", *paths(registry)}
    for tile in registry["tiles"]:
        manifest = json.loads(guarded(source, tile["manifest"]["path"]).read_text(encoding="utf-8"))
        dependencies.update(file["path"] for file in manifest["files"])
    for relative in sorted(dependencies):
        if not relative.lower().endswith(".glb"):
            continue
        data = guarded(source, relative).read_bytes()
        if data[:4] != b"glTF":
            raise ValueError(f"Invalid GLB: {relative}")
        length, kind = struct.unpack_from("<II", data, 12)
        if kind != 0x4E4F534A:
            raise ValueError(f"GLB JSON missing: {relative}")
        document = json.loads(data[20:20 + length])
        def uris(value):
            if isinstance(value, dict):
                for key, item in value.items():
                    if key == "uri" and isinstance(item, str):
                        yield item
                    else:
                        yield from uris(item)
            elif isinstance(value, list):
                for item in value:
                    yield from uris(item)
        for uri in uris(document):
            if not uri.startswith("data:"):
                dependencies.add(guarded(source, Path(relative).parent / uri).relative_to(source).as_posix())
    fresh = []
    total_bytes = 0
    for relative in sorted(dependencies):
        original = guarded(source, relative)
        target = guarded(destination, relative)
        if not original.is_file():
            raise FileNotFoundError(original)
        total_bytes += original.stat().st_size
        if target.exists():
            if original.stat().st_size != target.stat().st_size or digest(original) != digest(target):
                raise ValueError(f"Local edit/import conflict; nothing copied: {relative}")
        else:
            fresh.append(relative)
    print(json.dumps({"source_files": len(dependencies), "source_bytes": total_bytes,
                      "new_files": len(fresh), "identical_files": len(dependencies) - len(fresh),
                      "catalog_sha256": digest(source / "map/catalog.json").hex()}, indent=2), flush=True)
    if args.copy:
        for index, relative in enumerate(fresh):
            target = guarded(destination, relative)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(guarded(source, relative), target)
            if (index + 1) % 2000 == 0:
                print(f"Copied {index + 1}/{len(fresh)}", flush=True)
        print(f"Copied {len(fresh)} files; Unity derived tiles still need WorldContentImport.", flush=True)


if __name__ == "__main__":
    main()
