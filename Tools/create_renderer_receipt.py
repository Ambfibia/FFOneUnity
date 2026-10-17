#!/usr/bin/env python3
"""Create a pinned receipt from explicit measurement selections; never scan folders.

Selection JSON uses the report schema: settings, conditions, environment, scope,
siteAnchors, runs, transitions, visualPairs. Each run must explicitly specify
engine/site/mode/repeat/report. Each visual pair specifies site/poseSeconds,
rust/unity PNG paths and rustReport/unityReport JSON paths. Paths are relative to
--root. No acceptance flags are added: reviewers select them explicitly and the
report validator verifies raw evidence independently. This command authors no PDF.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from build_renderer_report import Dataset, PROJECT, read_json, safe


def pin_file(root, name):
    path = safe(root, name)
    if not path.is_file():
        raise ValueError("Selected evidence is missing: " + name)
    return path.relative_to(root.resolve()).as_posix(), hashlib.sha256(path.read_bytes()).hexdigest()


def make_receipt(root, selection):
    receipt = dict(selection)
    receipt["schema"] = "ffone.renderer-dataset.v1"
    for section in ("runs", "transitions"):
        records = []
        for selected in selection.get(section, []):
            record = dict(selected)
            for key in ("engine", "site", "mode", "repeat", "report"):
                if key not in record:
                    raise ValueError(f"{section} record lacks explicit {key}: {selected}")
            record["report"], record["reportSha256"] = pin_file(root, record["report"])
            parent = Path(record["report"]).parent
            for key, default in (("process", "process.json"), ("metadata", "run-metadata.json"), ("transition", "transition.json")):
                relative = record.get(key, (parent / default).as_posix())
                if key in record or safe(root, relative).is_file():
                    record[key], record[key + "Sha256"] = pin_file(root, relative)
            records.append(record)
        receipt[section] = records
    pairs = []
    for selected in selection.get("visualPairs", []):
        pair = dict(selected)
        for key in ("rust", "unity", "rustReport", "unityReport"):
            pair[key], pair[key + "Sha256"] = pin_file(root, pair[key])
        pairs.append(pair)
    receipt["visualPairs"] = pairs
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=PROJECT / "Builds/WorldBenchmark")
    parser.add_argument("--selection", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    output = args.output or args.root / "dataset-receipt.json"
    receipt = make_receipt(args.root, read_json(args.selection))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    status = Dataset(args.root, output).status()
    print(json.dumps(status, ensure_ascii=False, indent=2))
    if status["missingCells"] or status["problems"] or status["excluded"]:
        raise SystemExit(2)


if __name__ == "__main__":
    main()
