"""Combine the recorded district checks; does not run Unity or modify assets."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPORTS = ROOT / "Tools" / "ContentMigration"


def read(name):
    return json.loads((REPORTS / f"world-district-{name}.json").read_text(encoding="utf-8-sig"))


def main():
    scenes, tests, build, player, addressables, seams, editor = (
        read(name) for name in ("scenes", "tests", "build", "player", "addressables", "seam-audit", "editor-cycle")
    )
    passed = all((
        len(scenes["cells"]) == 9,
        scenes["reimportPreservesSceneBytes"],
        tests["result"] == "Passed" and int(tests["failed"]) == 0,
        build["result"] == "Succeeded" and build["errors"] == 0,
        player["passed"] and player["exitCode"] == 0 and not player["exceptions"],
        addressables["DuplicatedAssetCount"] == 0 and not addressables["BuildError"],
        seams["maximumGapBefore"] == 0,
        editor["passed"],
    ))
    report = {
        "passed": passed,
        "scope": "EntryDistrict: nine editable environment cells and local Addressables streaming",
        "cells": [row["id"] for row in scenes["cells"]],
        "tests": {key: tests[key] for key in ("result", "total", "passed", "failed")},
        "build": build,
        "player": player,
        "addressables": addressables,
        "terrainSeams": seams,
        "editorCycle": editor,
        "limitations": [
            "The remaining 161 world cells use the legacy loader.",
            "Changed source import requires staging and manual review; no automatic three-way merge.",
            "Network gameplay, AI navigation and original disabled trigger volumes are not certified by these checks.",
            "Player peakAllocatedBytes samples Unity CPU allocations, not total process or GPU memory.",
            "Existing build warnings and the baseline DBufferClear shader diagnostic remain recorded.",
        ],
    }
    output = REPORTS / "world-district-acceptance.json"
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"District acceptance: {'PASS' if passed else 'FAIL'} ({output})")
    raise SystemExit(0 if passed else 1)


if __name__ == "__main__":
    main()
