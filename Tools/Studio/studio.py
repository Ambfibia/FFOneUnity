"""Local Reconstruction Studio checks; no Unity/server launch or source-build writes."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tomllib
import time
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "Docs/Studio"
REPOS = ("FFOneUnity", "FFOneClient", "RustyFusion", "FusionForge", "FFOneWeb", "OpenFusion", "OpenFusionLauncher")


def git(root: Path, *args: str) -> str:
    return subprocess.check_output(["git", "-C", str(root), *args], text=True, encoding="utf-8", timeout=60)


def snapshot(max_seconds: float = 300) -> dict:
    started = time.monotonic()
    repos = {}
    for name in REPOS:
        path = ROOT.parent / name
        if not (path / ".git").exists():
            continue
        entries = git(path, "status", "--porcelain=v1", "-z", "--untracked-files=all").split("\0")
        repos[name] = {"head": git(path, "rev-parse", "HEAD").strip(),
                       "branch": git(path, "branch", "--show-current").strip(),
                       "status": [entry for entry in entries if entry]}
    protected = {}
    # Record all production assets' metadata; hash source/contracts/settings too.
    # This detects concurrent changes, but is not a backup or full content hash of huge assets.
    for directory in ("Assets", "Packages", "ProjectSettings"):
        pending = [ROOT / directory]
        while pending:
            current = pending.pop()
            if not current.exists():
                continue
            with os.scandir(current) as scan:
                for entry in scan:
                    if time.monotonic() - started > max_seconds:
                        raise TimeoutError(f"Snapshot incomplete after {max_seconds}s and {len(protected)} files; no pass or snapshot published")
                    if entry.is_symlink():
                        raise ValueError(f"Snapshot cannot guard symlink: {entry.path}")
                    if entry.is_dir(follow_symlinks=False):
                        pending.append(Path(entry.path))
                        continue
                    if not entry.is_file(follow_symlinks=False):
                        continue
                    path = Path(entry.path)
                    stat = entry.stat(follow_symlinks=False)
                    row = {"bytes": stat.st_size, "mtime_ns": stat.st_mtime_ns}
                    if path.suffix in {".cs", ".json", ".asmdef", ".shader", ".hlsl"} or directory != "Assets":
                        digest = hashlib.sha256()
                        with path.open("rb") as stream:
                            for block in iter(lambda: stream.read(1024 * 1024), b""):
                                if time.monotonic() - started > max_seconds:
                                    raise TimeoutError("Snapshot incomplete during hashing; no snapshot published")
                                digest.update(block)
                        row["sha256"] = digest.hexdigest()
                    protected[path.relative_to(ROOT).as_posix()] = row
                    if len(protected) % 10000 == 0:
                        print(f"Snapshot: {len(protected)} files, {time.monotonic()-started:.1f}s", file=sys.stderr, flush=True)
    return {"timestamp_utc": datetime.now(timezone.utc).isoformat(), "repos": repos, "protected": protected}


def _validate() -> dict:
    errors = []
    routes = json.loads((DATA / "orchestration.json").read_text(encoding="utf-8"))
    if routes.get("schema") != "ffone.studio.orchestration.v1":
        errors.append("unsupported orchestration schema")
    roles = {}
    for path in (ROOT / ".codex/agents").glob("*.toml"):
        role = tomllib.loads(path.read_text(encoding="utf-8"))
        for key in ("name", "description", "developer_instructions"):
            if not role.get(key):
                errors.append(f"{path.name}: missing {key}")
        if role.get("name") in roles:
            errors.append(f"duplicate role {role['name']}")
        roles[role.get("name")] = role
    skills = {}
    for path in (ROOT / ".agents/skills").glob("*/SKILL.md"):
        body = path.read_text(encoding="utf-8")
        if not body.startswith("---\n") or "\n---\n" not in body[4:]:
            errors.append(f"invalid skill frontmatter: {path}")
            continue
        try:
            fields = dict(line.split(":", 1) for line in body.split("---", 2)[1].strip().splitlines())
        except ValueError:
            errors.append(f"invalid skill frontmatter: {path}")
            continue
        name = fields.get("name", "").strip()
        if name != path.parent.name or not fields.get("description", "").strip():
            errors.append(f"invalid skill identity: {path}")
        skills[name] = str(path)
    if routes.get("integration_owner") not in roles:
        errors.append("unknown integration_owner")
    for mode, route in routes["modes"].items():
        for role in route["roles"]:
            if role not in roles:
                errors.append(f"{mode}: unknown role {role}")
        for skill in route["skills"]:
            if skill not in skills:
                errors.append(f"{mode}: unknown skill {skill}")
    sources = json.loads((DATA / "sources.json").read_text(encoding="utf-8"))["sources"]
    source_ids = {row["id"] for row in sources}
    if len(source_ids) != len(sources):
        errors.append("duplicate source IDs")
    for row in sources:
        if not (ROOT / row["path"]).resolve().exists():
            errors.append(f"missing source: {row['id']}")
    for filename in ("provenance.json", "preservation.json"):
        document = json.loads((DATA / filename).read_text(encoding="utf-8"))
        ids = set()
        for row in document["entries"]:
            if row["id"] in ids:
                errors.append(f"{filename}: duplicate {row['id']}")
            ids.add(row["id"])
            for source in row.get("sources", []):
                if source not in source_ids:
                    errors.append(f"{row['id']}: unknown source {source}")
            for evidence in row["evidence"]:
                if not (ROOT / evidence).resolve().exists():
                    errors.append(f"{row['id']}: evidence missing {evidence}")
            if filename == "preservation.json" and row["category"] not in {"PRESERVE", "RECONSTRUCT", "IMPROVE", "CANDIDATE", "DEPRECATED"}:
                errors.append(f"{row['id']}: invalid preservation category")
            if filename == "provenance.json" and row["status"] not in {"preserved", "implemented", "partial", "absent", "conflict", "planned"}:
                errors.append(f"{row['id']}: invalid provenance status")
            if row.get("confidence") not in {"observed", "derived", "historical", "unknown"}:
                errors.append(f"{row['id']}: invalid confidence")
    return {"passed": not errors, "roles": len(roles), "skills": len(skills), "sources": len(sources), "errors": errors}


def validate() -> dict:
    try:
        return _validate()
    except (OSError, ValueError, KeyError, TypeError) as error:
        return {"passed": False, "errors": [f"Invalid or incomplete Studio configuration: {error}"]}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("validate")
    snap = sub.add_parser("snapshot")
    snap.add_argument("--output", type=Path, required=True)
    snap.add_argument("--max-seconds", type=float, default=300)
    compare = sub.add_parser("compare")
    compare.add_argument("baseline", type=Path)
    compare.add_argument("--max-seconds", type=float, default=300)
    args = parser.parse_args()
    if args.command == "snapshot":
        if args.output.exists():
            parser.error("snapshot output already exists; choose a fresh file")
        data = snapshot(max_seconds=args.max_seconds)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("x", encoding="utf-8") as stream:
            json.dump(data, stream, indent=2)
        result = {"output": str(args.output), "protected_files": len(data["protected"]),
                  "repos": {name: {"head": row["head"], "branch": row["branch"], "dirty_entries": len(row["status"])} for name, row in data["repos"].items()}}
    elif args.command == "compare":
        before = json.loads(args.baseline.read_text(encoding="utf-8"))
        after = snapshot(max_seconds=args.max_seconds)
        changed = [key for key in before["protected"].keys() | after["protected"].keys()
                   if before["protected"].get(key) != after["protected"].get(key)]
        result = {"passed": not changed, "changed_protected_files": sorted(changed),
                  "note": "Metadata for all assets; content hashes for source/config. Not a backup."}
    else:
        result = validate()
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0 if result.get("passed", True) else 1


if __name__ == "__main__":
    sys.exit(main())
