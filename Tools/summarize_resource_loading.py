"""Collect compact evidence from the built layout and actual client validation."""
import argparse
import hashlib
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

from audit_addressable_loading import summarize

ROOT = Path(__file__).resolve().parents[1]
LOGS = ROOT / "Logs/ResourceLoading"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def metrics(receipt):
    result = []
    for run in receipt["runs"]:
        timings = "\n".join(run["timings"]).replace(",", ".")
        startup = re.search(r"STARTUP READY: elapsed=([0-9.]+)", timings)
        selection = re.search(r"rendered-selection=([0-9.]+)", timings)
        frames = re.findall(r"RECOVERY FRAME: ([0-9.]+)s", timings)
        result.append({"phase": run["phase"], "passed": run["passed"],
                       "startupSeconds": float(startup[1]) if startup else None,
                       "selectionSeconds": float(selection[1]) if selection else None,
                       "largestLoggedFrameSeconds": max(map(float, frames), default=None),
                       "syncWaits": run["syncWaits"]})
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "Tools/ResourceLoading/acceptance.json")
    args = parser.parse_args()
    before = read(LOGS / "before.json")
    layout = ROOT / "Library/com.unity.addressables/buildlayout.json"
    after = summarize(layout)
    raw = read(layout)
    entries = [row["data"] for row in raw["references"]["RefIds"] if row["type"]["class"] == "BuildLayout/ExplicitAsset"]
    old = set((LOGS / "addresses-before.txt").read_text().splitlines())
    new = {entry["Guid"] + " " + entry["AddressableName"] for entry in entries}
    missing = sorted(old - new)
    tests = ET.parse(LOGS / "EditorTests.xml").getroot()
    lifecycle = read(LOGS / "optimized-lifecycle/validation.json")
    request_log = (LOGS / "request-smoke.log").read_text(encoding="utf-8-sig", errors="replace")
    request_passed = "CONTENT REQUEST SMOKE PASSED" in request_log and "Exception:" not in request_log
    baseline = read(LOGS / "baseline-lifecycle/validation.json")
    offline = read(LOGS / "optimized-ui/validation.json")
    content_smoke = read(LOGS / "content-smoke.json")
    sources = read(LOGS / "source-hashes.json")
    changed_sources = [path for path, digest in sources.items() if hashlib.sha256((ROOT / path).read_bytes()).hexdigest() != digest]
    player = Path(lifecycle["player"])
    bundles_path = player.parent / (player.stem + "_Data/StreamingAssets/aa")
    expected_bundles = {row["data"]["Name"] for row in raw["references"]["RefIds"] if row["type"]["class"] == "BuildLayout/Bundle"}
    player_bundles = {path.name: path.stat().st_size for path in bundles_path.rglob("*.bundle")}
    missing_bundles = sorted(expected_bundles - player_bundles.keys())
    extra_bundles = sorted(player_bundles.keys() - expected_bundles)
    probes = {probe["address"]: probe for probe in before["probes"]}
    comparison = []
    for probe in after["probes"]:
        if probe["address"] in probes:
            previous = probes[probe["address"]]
            comparison.append({"address": probe["address"], "beforeBytes": previous["closureBytes"],
                               "afterBytes": probe["closureBytes"], "afterGroup": probe["group"],
                               "beforeBundles": previous["closureBundles"], "afterBundles": probe["closureBundles"]})
    keys = ("groups", "bundles", "bundleBytes", "explicitAssets", "duplicatedAssetCount", "addressGuidSha256")
    result = {
        "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "before": {key: before[key] for key in keys}, "after": {key: after[key] for key in keys},
        "largestBundlesAfter": after["largestBundles"], "dependencyClosureComparison": comparison,
        "addressPreservation": {"beforeCount": len(old), "afterCount": len(new), "missing": missing,
                                "addedCount": len(new - old)},
        "editorTests": {key: tests.attrib.get(key) for key in ("total", "passed", "failed", "skipped", "result")},
        "requestSmokePassed": request_passed,
        "offlineUi": {"resolutions": [{"width": item["width"], "height": item["height"], "passed": item["passed"]} for item in offline["viewports"]]},
        "contentSmoke": {key: content_smoke[key] for key in ("passed", "exitCode", "errors", "knownBaselineDiagnostics", "nanoModelsChecked")},
        "sourceConsistency": {"files": len(sources), "changedAfterPlayerCompilation": changed_sources,
                              "compilation": read(LOGS / "source-compilation.json")},
        "catalogBytes": {"before": read(LOGS / "catalog-before.json")["playerCatalogBytes"],
                         "after": (bundles_path / "catalog.bin").stat().st_size},
        "playerBundleInventory": {"expected": len(expected_bundles), "actual": len(player_bundles),
                                  "bytes": sum(player_bundles.values()), "missing": missing_bundles, "extra": extra_bundles},
        "baselineLifecycle": metrics(baseline), "optimizedLifecycle": metrics(lifecycle),
        "optimizedPlayerHashes": {Path(key).name: value for key, value in lifecycle["hashes"].items()},
        "limitations": ["Dependency bytes measure compressed bundle files, not asset RAM or physical disk reads.",
                        "Measurements use new player processes on one Windows machine; OS file and shader caches were not cleared.",
                        "The optimized player includes concurrent working-tree music changes; timing differences do not isolate this patch.",
                        "The lifecycle probe covers tutorial entry/reentry, not a complete traversal of every world cell.",
                        "Avatar/model cache remains session-scoped; mobile memory budgets need separate validation."]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
    if (missing or tests.attrib.get("failed") != "0" or not lifecycle.get("passed") or
            after["buildError"] or not request_passed or changed_sources or missing_bundles or extra_bundles or
            not content_smoke["passed"] or not content_smoke["nanoModelsChecked"] or any(not item["passed"] for item in offline["viewports"])):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
