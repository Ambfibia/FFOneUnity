#!/usr/bin/env python3
"""Build the Russian renderer comparison from a reviewed receipt and fresh raw data.

No discovery promotes validation captures into performance runs. A receipt names
every selected raw report and frame; --validate-only creates no final artifact.
Run from FFOneUnity with the bundled Python. Requires numpy, Pillow, reportlab,
matplotlib (for authoring); SSIM is calculated locally with a 7x7 uniform window.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timedelta
import hashlib
import html
import json
import math
import re
import statistics
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image as PillowImage


PROJECT = Path(__file__).resolve().parents[1]
SITES = ("pool", "station", "campfire")
SITE_NAMES = {"pool": "Бассейн", "station": "Городская станция", "campfire": "Костёр"}
SITE_ANCHORS = {"pool": (-6374.3188, -56.4746, 668.2039), "station": (-3750.5798, -56.9, 4484.4097),
                "campfire": (-1788.0410, -44.6575, 6782.5977)}
ENGINES = ("Rust", "Unity")
MODES = ("shared", "baseline")
FONT = Path("C:/Windows/Fonts/arial.ttf")
BOLD = Path("C:/Windows/Fonts/arialbd.ttf")


def read_json(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError(f"Expected JSON object: {path}")
    return value


def value(objects: list[dict], *keys: str, default=None):
    for obj in objects:
        for key in keys:
            current: Any = obj
            for part in key.split("."):
                current = current.get(part) if isinstance(current, dict) else None
            if current is not None:
                return current
    return default


def safe(root: Path, relative: str) -> Path:
    path = Path(relative)
    resolved = (path if path.is_absolute() else root / path).resolve()
    if not resolved.is_relative_to(root.resolve()):
        raise ValueError(f"Dataset reference escapes the new raw-results root: {relative}")
    return resolved


def site_name(name: str) -> str:
    name = name.lower()
    return "station" if name in ("city", "city-station", "city_station") else name


def tile_ids(items) -> tuple:
    if not isinstance(items, list):
        return ()
    result = []
    for item in items:
        if isinstance(item, (list, tuple)) and len(item) == 2:
            result.append((int(item[0]), int(item[1])))
        elif isinstance(item, str):
            match = re.fullmatch(r"(?:map_|tile_)?(\d+)_(\d+)", item)
            if not match:
                raise ValueError("Unknown tile identity: " + item)
            result.append((int(match[1]), int(match[2])))
        else:
            raise ValueError("Unknown tile identity: " + repr(item))
    if len(set(result)) != len(result):
        raise ValueError("Repeated resident tile identity")
    return tuple(sorted(result))


def camera(report: dict) -> dict:
    objects = [report]
    return {
        "position": value(objects, "camera.position", "cameraPosition"),
        "forward": value(objects, "camera.forward", "cameraForward"),
        "viewport": value(objects, "viewport", default=[report.get("width"), report.get("height")]),
        "fov": value(objects, "camera.fovDegrees", "fov", "verticalFovDegrees"),
        "near": value(objects, "camera.near", "near"),
        "far": value(objects, "camera.far", "far"),
        "tiles": tile_ids(report.get("residentTileIds")),
        "placements": report.get("visualPlacements"),
    }


def matching_views(left: dict, right: dict) -> list[str]:
    problems = []
    a, b = camera(left), camera(right)
    for key, tolerance in (("position", .002), ("forward", .0001)):
        if a[key] is None or b[key] is None:
            problems.append("missing camera " + key)
        elif len(a[key]) != 3 or len(b[key]) != 3 or np.max(np.abs(np.array(a[key]) - np.array(b[key]))) > tolerance:
            problems.append("camera " + key + " differs")
    for key in ("viewport", "tiles", "placements"):
        if a[key] != b[key] or a[key] in (None, (), []):
            problems.append(key + " differs or missing")
    for key in ("fov", "near", "far"):
        if a[key] is None or b[key] is None or abs(float(a[key]) - float(b[key])) > .00001:
            problems.append(key + " differs or missing")
    return problems


@dataclass
class Run:
    engine: str
    site: str
    mode: str
    repeat: int
    path: Path
    report: dict
    metadata: dict
    process: dict
    receipt: dict
    kind: str = "performance"
    stats: dict = field(default_factory=dict)
    total_ready: float | None = None
    working: float | None = None
    private: float | None = None
    transition: dict | None = None
    reasons: list[str] = field(default_factory=list)

    @property
    def key(self):
        return self.site, self.engine, self.mode


class Dataset:
    def __init__(self, root: Path, receipt_path: Path):
        self.root = root.resolve()
        self.receipt_path = receipt_path.resolve()
        self.receipt = read_json(receipt_path)
        self.runs: list[Run] = []
        self.pairs: list[dict] = []
        self.problems: list[str] = []
        self.settings = self.receipt.get("settings", {})
        self.conditions = self.receipt.get("conditions", {})
        self.environment = self.receipt.get("environment", {})
        evidence = self.root / "Rust/environment-evidence.json"
        if evidence.exists():
            self.environment = {**read_json(evidence), **self.environment}
        self._load_runs()
        self._gate_views()
        self._load_pairs()

    def optional_json(self, name: str | None, default: Path) -> dict:
        path = safe(self.root, name) if name else default
        return read_json(path) if path.exists() else {}

    def _load_runs(self):
        records = list(self.receipt.get("runs", [])) + [{**r, "kind": "transition"} for r in self.receipt.get("transitions", [])]
        for record in records:
            try:
                path = safe(self.root, record["report"])
                report = read_json(path)
                for key in ("report", "process", "metadata", "transition"):
                    if record.get(key + "Sha256") and hashlib.sha256(safe(self.root, record[key]).read_bytes()).hexdigest() != record[key + "Sha256"].lower():
                        raise ValueError(key + " hash differs from reviewed receipt")
                metadata = self.optional_json(record.get("metadata"), path.parent / "run-metadata.json")
                process = self.optional_json(record.get("process"), path.parent / "process.json")
                objects = [record, metadata, process, report]
                engine = str(value(objects, "engine", default=path.relative_to(self.root).parts[0])).split()[0]
                site = site_name(str(value(objects, "site", default=path.parent.name.split("-")[0])))
                mode = value(objects, "mode", default="shared" if report.get("sharing") else "baseline")
                kind = record.get("kind", record.get("purpose", "transition" if "transition" in path.parent.name else "performance"))
                run = Run(engine, site, mode, int(value(objects, "repeat", default=1 if kind == "transition" else 0)), path, report, metadata, process, record, kind)
                self._validate_run(run)
                self.runs.append(run)
            except (KeyError, ValueError, OSError) as error:
                self.problems.append(f"Receipt run cannot be read: {record.get('report')}: {error}")

    def _validate_run(self, run: Run):
        objects = [run.receipt, run.metadata, run.process, run.report]
        flags = objects + [self.conditions]
        name = run.path.parent.name.lower()
        if run.engine not in ENGINES or run.site not in SITES or run.mode not in MODES or run.repeat < 1:
            run.reasons.append("unknown engine/site/mode/repeat")
        if value(flags, "perfAccepted", "performanceAccepted", "accepted", default=False) is not True:
            run.reasons.append("performance not explicitly accepted")
        for obj in objects:
            if obj.get("perfAccepted") is False or obj.get("performanceAccepted") is False or obj.get("accepted") is False:
                run.reasons.append("explicit rejected performance flag")
            if obj.get("validationOnly") is True or obj.get("frozen") is True or obj.get("animated") is False:
                run.reasons.append("validation/frozen run")
            # Rust's runner records requestedPoseSeconds=0 even for ordinary
            # fixed-simulation runs. Only an actual pose flag/command freezes it.
            if obj.get("fixedPoseSeconds") is not None:
                run.reasons.append("fixed pose run")
            if obj.get("compilationOverlap") is True or obj.get("compilationActive") is True:
                run.reasons.append("compilation overlapped measurement")
            if obj.get("overlappingCompilers"):
                run.reasons.append("recorded active compilation overlap")
            if obj.get("grassDiagnostic"):
                run.reasons.append("grass diagnostic run changes the measured scene")
            if obj.get("exitCode") not in (None, 0):
                run.reasons.append("process did not finish successfully")
            if obj.get("mode") is not None and obj["mode"] != run.mode:
                run.reasons.append("mode metadata differs")
            arguments = obj.get("arguments", obj.get("commandLine", ""))
            arguments = " ".join(map(str, arguments)) if isinstance(arguments, list) else str(arguments)
            if "--freeze" in arguments or "--pose-seconds" in arguments:
                run.reasons.append("frozen command-line options")
        if "validation" in name or "frozen" in name or "freeze" in name or re.search(r"(?:pose|frozen)-t[01]", name):
            run.reasons.append("validation/frozen folder")
        explicit_idle = value(flags, "compilationOverlap", default=None) is False
        sampled_idle = any("overlappingCompilers" in obj and not obj["overlappingCompilers"] for obj in objects)
        if not explicit_idle and not sampled_idle:
            run.reasons.append("no evidence of idle compilation")
        if "sharing" in run.report and bool(run.report["sharing"]) != (run.mode == "shared"):
            run.reasons.append("sharing flag differs from selected mode")
        samples = run.report.get("frameMs", [])
        if not isinstance(samples, list) or len(samples) != 360 or run.report.get("frames") != 360:
            run.reasons.append("requires exactly 360 measured intervals")
        elif any(not isinstance(x, (int, float)) or not math.isfinite(x) or x <= 0 for x in samples):
            run.reasons.append("invalid frame intervals")
        else:
            data = sorted(samples)
            run.stats = {"meanMs": statistics.mean(data), "medianMs": data[len(data) // 2],
                         "p95Ms": data[int(len(data) * .95)], "p99Ms": data[int(len(data) * .99)], "maxMs": max(data)}
            expected = {"meanMs": ("meanMs",), "medianMs": ("medianMs", "p50Ms"), "p95Ms": ("p95Ms",), "p99Ms": ("p99Ms",)}
            for key, aliases in expected.items():
                raw = value([run.report], *aliases)
                if raw is None or abs(float(raw) - run.stats[key]) > max(.00001, run.stats[key] * .00001):
                    run.reasons.append(key + " does not agree with raw intervals")
        controls = objects + [self.settings]
        expected_backend = self.settings.get("graphicsApi")
        actual_backend = value(objects, "graphicsApi", "backendRequested", "backend")
        if expected_backend and str(actual_backend).lower() != str(expected_backend).lower():
            run.reasons.append("graphics API differs from the selected comparison")
        step = value(controls, "simulationNominalStepSeconds", "simulationStepSeconds", "fixedStepSeconds", "simulationDeltaSeconds", "fixedDeltaSeconds")
        if step is None or abs(float(step) - 1 / 60) > 1e-7:
            run.reasons.append("simulation step is not confirmed at 1/60")
        if value(controls, "warmupSeconds") != 3:
            run.reasons.append("warmup is not 3 seconds")
        if run.engine == "Rust" and run.report.get("fixedSimulation") is not True:
            run.reasons.append("Rust fixed simulation is not confirmed")
        if run.engine == "Rust":
            elapsed = run.report.get("simulationElapsedSeconds")
            started = run.report.get("simulationCaptureStartSeconds")
            if not isinstance(elapsed, (int, float)) or not isinstance(started, (int, float)) or not math.isfinite(elapsed - started) or abs(started) > 1e-6 or abs(elapsed - started - 6) > 1e-6:
                run.reasons.append("Rust measured simulation must advance from zero through six seconds")
        if run.engine == "Unity":
            elapsed = run.report.get("fixedSimulationSeconds")
            if not isinstance(elapsed, (int, float)) or not math.isfinite(elapsed) or abs(elapsed - 6) > 1e-5:
                run.reasons.append("Unity measured simulation must advance from zero through six seconds")
        view = camera(run.report)
        if view["viewport"] != [1280, 720] or view["far"] != 340:
            run.reasons.append("viewport/far-plane contract differs")
        if view["fov"] is None or abs(float(view["fov"]) - 45) > .00001 or view["near"] is None or abs(float(view["near"]) - .1) > .00001:
            run.reasons.append("FOV/near-plane contract differs")
        if run.report.get("msaaSamples", 1) != 1 or run.report.get("msaa", 0) != 0 or run.report.get("vsync", 0) != 0:
            run.reasons.append("MSAA/VSync contract differs")
        if not view["tiles"] or not isinstance(view["placements"], int):
            run.reasons.append("missing tile/placement counts")
        if run.report.get("behaviourErrors"):
            run.reasons.append("runtime behaviour errors")
        for key in ("authoredColliderLoading", "authoredColliderBlocked", "authoredColliderMissingStatus"):
            if run.report.get(key, 0) != 0:
                run.reasons.append("authored collider admission incomplete: " + key)
        if run.kind == "performance" and run.site in SITE_ANCHORS:
            anchor = run.report.get("anchor")
            expected = self.receipt.get("siteAnchors", {}).get(run.site, SITE_ANCHORS[run.site]) if hasattr(self, "receipt") else SITE_ANCHORS[run.site]
            if anchor is None or len(anchor) != 3 or np.max(np.abs(np.array(anchor) - np.array(expected))) > .002:
                run.reasons.append("site anchor differs or missing")
        run.total_ready = value(objects + [run.process], "totalReadySeconds")
        if run.total_ready is None and run.report.get("preAppLoadSeconds") is not None:
            run.total_ready = run.report.get("readySeconds", 0) + run.report["preAppLoadSeconds"]
        # Never compare readySeconds alone across the engines: those clocks start at different stages.
        if run.total_ready is not None:
            run.total_ready = float(run.total_ready)
            if not math.isfinite(run.total_ready) or run.total_ready < float(run.report.get("readySeconds", 0)):
                run.reasons.append("invalid totalReadySeconds")
        process_fields = [run.receipt, run.process, run.metadata, run.report]
        run.working = value(process_fields, "peakWorkingSetBytes", "processPeakWorkingSetBytes", "processWorkingSetBytes")
        run.private = value(process_fields, "peakPrivateBytes", "peakPrivateMemoryBytes", "processPeakPrivateBytes", "processPrivateBytes")
        transition_path = run.receipt.get("transition")
        candidate = safe(self.root, transition_path) if transition_path else run.path.parent / "transition.json"
        if candidate.exists():
            run.transition = read_json(candidate)
        elif isinstance(run.report.get("transition"), dict):
            run.transition = run.report["transition"]
        if run.kind == "transition":
            if not run.transition:
                run.reasons.append("missing transition journal")
            else:
                delta = run.transition.get("delta")
                frames = run.transition.get("frames", [])
                ready = run.transition.get("readySeconds")
                if not isinstance(delta, list) or len(delta) != 3 or any(not isinstance(d, (int, float)) or not math.isfinite(d) for d in delta) or abs(math.sqrt(sum(d*d for d in delta)) - 512) > .01:
                    run.reasons.append("transition distance must be 512")
                if not isinstance(ready, (int, float)) or not math.isfinite(ready) or ready <= 0:
                    run.reasons.append("invalid transition readiness time")
                if not isinstance(frames, list) or not frames or any(not isinstance(f, dict) or not isinstance(f.get("frameMs"), (int, float)) or not math.isfinite(f["frameMs"]) or f["frameMs"] <= 0 for f in frames):
                    run.reasons.append("transition journal lacks valid frame intervals")
        run.reasons = list(dict.fromkeys(run.reasons))

    def _gate_views(self):
        active = [r for r in self.runs if not r.reasons]
        for kind, site in [(k, s) for k in ("performance", "transition") for s in SITES]:
            site_runs = [r for r in active if r.site == site and r.kind == kind]
            if not site_runs:
                continue
            reference = site_runs[0]
            for run in site_runs[1:]:
                differences = matching_views(reference.report, run.report)
                if reference.engine != run.engine:
                    rust, unity = (reference, run) if reference.engine == "Rust" else (run, reference)
                    if rust.report.get("authoredColliderEntities") is not None and unity.report.get("colliders") is not None:
                        if rust.report["authoredColliderEntities"] != unity.report["colliders"]:
                            differences.append("authored object collider placements differ (terrain excluded)")
                if differences:
                    reason = "view/coverage gate: " + "; ".join(differences)
                    run.reasons.append(reason)
                    if reason not in reference.reasons:
                        reference.reasons.append(reason)
        if self.conditions.get("orderReversed") is not True:
            self.problems.append("Reversed repeat order is not confirmed in the receipt")
        # Confirm mode reversal with process timestamps, including Unity's older
        # runner which records completion plus the elapsed process duration.
        for engine in ENGINES:
            for site in SITES:
                ordered = defaultdict(dict)
                for run in active:
                    if run.engine != engine or run.site != site or run.kind != "performance":
                        continue
                    objects = [run.metadata, run.process]
                    start = value(objects, "startedUtc")
                    finish = value(objects, "finishedUtc", "capturedAt")
                    duration = value(objects, "processElapsedSeconds")
                    try:
                        stamp = datetime.fromisoformat((start or finish).replace("Z", "+00:00"))
                        if not start:
                            stamp -= timedelta(seconds=float(duration))
                        ordered[run.repeat][run.mode] = stamp
                    except (AttributeError, TypeError, ValueError):
                        self.problems.append(f"Missing process ordering timestamps: {run.path.relative_to(self.root)}")
                signs = [m["shared"] < m["baseline"] for m in ordered.values() if set(m) == set(MODES)]
                if len(signs) >= 2 and len(set(signs)) < 2:
                    self.problems.append(f"Recorded shared/baseline order was not reversed: {engine}/{site}")

    def _load_pairs(self):
        for record in self.receipt.get("visualPairs", []):
            pair = dict(record)
            pair["site"] = site_name(str(pair.get("site", "")))
            pair["reasons"] = []
            try:
                if pair.get("accepted") is not True:
                    pair["reasons"].append("visual pair not explicitly accepted")
                for engine in ("rust", "unity"):
                    for key in (engine, engine + "Report"):
                        if pair.get(key + "Sha256") and hashlib.sha256(safe(self.root, pair[key]).read_bytes()).hexdigest() != pair[key + "Sha256"].lower():
                            pair["reasons"].append(key + " hash differs from reviewed receipt")
                    pair[engine] = safe(self.root, pair[engine])
                    pair[engine + "Report"] = read_json(safe(self.root, pair[engine + "Report"]))
                pair["reasons"].extend(matching_views(pair["rustReport"], pair["unityReport"]))
                if pair["site"] not in SITES or float(pair.get("poseSeconds", -1)) not in (0, 1):
                    pair["reasons"].append("unknown site or pose")
                else:
                    target = float(pair["poseSeconds"])
                    rust = pair["rustReport"]
                    unity = pair["unityReport"]
                    for engine, report, key, frozen in (("Rust", rust, "frozenPoseSeconds", rust.get("frozen") is True),
                                                        ("Unity", unity, "fixedPoseSeconds", unity.get("animated") is False)):
                        actual = report.get(key)
                        if not frozen or not isinstance(actual, (int, float)) or not math.isfinite(actual) or abs(actual - target) > 1e-5:
                            pair["reasons"].append(engine + " raw report does not confirm requested frozen time")
                with PillowImage.open(pair["rust"]) as image:
                    a = np.asarray(image.convert("RGB"), dtype=np.float64)
                with PillowImage.open(pair["unity"]) as image:
                    b = np.asarray(image.convert("RGB"), dtype=np.float64)
                if a.shape != b.shape or a.shape[:2] != (720, 1280):
                    pair["reasons"].append("visual resolution differs")
                else:
                    difference = np.abs(a - b)
                    pair["metrics"] = {"mae": float(difference.mean()), "ssim": ssim(a, b),
                                       "percentAbove5": float(np.any(difference > 5, axis=2).mean() * 100),
                                       "maxDifference": int(difference.max())}
            except (KeyError, ValueError, OSError) as error:
                pair["reasons"].append(str(error))
            self.pairs.append(pair)

    @property
    def accepted(self):
        return [run for run in self.runs if not run.reasons and run.kind == "performance"]

    @property
    def accepted_transitions(self):
        return [run for run in self.runs if not run.reasons and run.kind == "transition"]

    def groups(self):
        groups = defaultdict(list)
        for run in self.accepted:
            groups[run.key].append(run)
        return groups

    def status(self):
        groups = self.groups()
        missing = []
        for site in SITES:
            for engine in ENGINES:
                for mode in MODES:
                    repeats = [run.repeat for run in groups[site, engine, mode]]
                    if len(repeats) < 2 or len(set(repeats)) != len(repeats):
                        missing.append({"site": site, "engine": engine, "mode": mode, "repeats": repeats})
        return {"acceptedPerformanceRuns": len(self.accepted), "acceptedTransitions": len(self.accepted_transitions), "excluded": [
            {"report": str(r.path.relative_to(self.root)), "reasons": r.reasons} for r in self.runs if r.reasons],
            "missingCells": missing, "problems": self.problems,
            "visualPairs": [{"site": p["site"], "poseSeconds": p.get("poseSeconds"), "reasons": p["reasons"], "metrics": p.get("metrics")}
                            for p in self.pairs]}


def box_mean(data: np.ndarray, window=7):
    radius = window // 2
    padded = np.pad(data, ((radius, radius), (radius, radius), (0, 0)), mode="reflect")
    integral = np.pad(padded, ((1, 0), (1, 0), (0, 0))).cumsum(0).cumsum(1)
    return (integral[window:, window:] - integral[:-window, window:] - integral[window:, :-window] + integral[:-window, :-window]) / window ** 2


def ssim(a: np.ndarray, b: np.ndarray) -> float:
    # Same standard constants/data range as RGB SSIM with a uniform7x7 window,
    # unbiased local covariance and an excluded3pixel boundary. No image resizing.
    ma, mb = box_mean(a), box_mean(b)
    scale = 49 / 48
    va = np.maximum((box_mean(a * a) - ma * ma) * scale, 0)
    vb = np.maximum((box_mean(b * b) - mb * mb) * scale, 0)
    covariance = (box_mean(a * b) - ma * mb) * scale
    c1, c2 = (.01 * 255) ** 2, (.03 * 255) ** 2
    score = ((2 * ma * mb + c1) * (2 * covariance + c2)) / ((ma * ma + mb * mb + c1) * (va + vb + c2))
    return float(score[3:-3, 3:-3].mean())


def aggregate(runs: list[Run]):
    if not runs:
        return {}
    result = {key: statistics.mean(r.stats[key] for r in runs) for key in runs[0].stats}
    result["fps"] = 1000 / result["meanMs"]
    result["n"] = len(runs)
    for key in ("total_ready", "working", "private"):
        fields = [getattr(r, key) for r in runs]
        result[key] = statistics.mean(fields) if all(x is not None for x in fields) else None
    return result


def fmt(number, digits=2):
    if number is None:
        return "нет данных"
    return f"{number:,.{digits}f}".replace(",", " ").replace(".", ",")


def build(dataset: Dataset, output: Path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib import font_manager
    from reportlab.lib import colors
    from reportlab.lib.enums import TA_LEFT
    from reportlab.lib.pagesizes import A4
    from reportlab.lib.styles import ParagraphStyle
    from reportlab.lib.units import mm
    from reportlab.pdfbase import pdfmetrics
    from reportlab.pdfbase.ttfonts import TTFont
    from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, PageBreak, Table, TableStyle, Image, KeepTogether

    status = dataset.status()
    if status["missingCells"] or status["problems"]:
        raise ValueError("Dataset is incomplete or lacks required acceptance evidence; use --validate-only")
    output.parent.mkdir(parents=True, exist_ok=True)
    assets = output.parent / "assets"
    assets.mkdir(parents=True, exist_ok=True)
    pdfmetrics.registerFont(TTFont("ReportArial", str(FONT)))
    pdfmetrics.registerFont(TTFont("ReportArialBold", str(BOLD)))
    font_manager.fontManager.addfont(str(FONT))
    font_manager.fontManager.addfont(str(BOLD))
    pdfmetrics.registerFontFamily("ReportArial", normal="ReportArial", bold="ReportArialBold", italic="ReportArial", boldItalic="ReportArialBold")
    ink, navy, blue, pale = colors.HexColor("#23364B"), colors.HexColor("#17304C"), colors.HexColor("#197CA0"), colors.HexColor("#EAF1F6")
    page_width, page_height = A4
    content_width = page_width - 36 * mm
    styles = {
        "title": ParagraphStyle("title", fontName="ReportArialBold", fontSize=25, leading=29, textColor=navy, spaceAfter=12),
        "h": ParagraphStyle("h", fontName="ReportArialBold", fontSize=17, leading=21, textColor=navy, spaceAfter=10),
        "sub": ParagraphStyle("sub", fontName="ReportArialBold", fontSize=11, leading=15, textColor=blue, spaceBefore=8, spaceAfter=6),
        "body": ParagraphStyle("body", fontName="ReportArial", fontSize=9.7, leading=14, textColor=ink, spaceAfter=7),
        "small": ParagraphStyle("small", fontName="ReportArial", fontSize=8.1, leading=11, textColor=ink, spaceAfter=5),
        "cell": ParagraphStyle("cell", fontName="ReportArial", fontSize=8.1, leading=10.5, textColor=ink),
        "head": ParagraphStyle("head", fontName="ReportArialBold", fontSize=8, leading=10.5, textColor=colors.white),
    }
    story = []
    def paragraph(text, style="body"):
        return Paragraph(text, styles[style])
    def add(text, style="body"):
        story.append(paragraph(text, style))
    def table(rows, widths=None):
        converted = [[paragraph(html.escape(str(cell)).replace("\n", "<br/>"), "head" if row == 0 else "cell") for cell in line]
                     for row, line in enumerate(rows)]
        item = Table(converted, colWidths=widths, repeatRows=1, hAlign="LEFT")
        item.setStyle(TableStyle([
            ("BACKGROUND", (0, 0), (-1, 0), navy), ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, pale]),
            ("VALIGN", (0, 0), (-1, -1), "TOP"), ("LEFTPADDING", (0, 0), (-1, -1), 6),
            ("RIGHTPADDING", (0, 0), (-1, -1), 6), ("TOPPADDING", (0, 0), (-1, -1), 6),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 6), ("LINEBELOW", (0, 0), (-1, 0), .5, navy)]))
        story.append(item); story.append(Spacer(1, 7))
    def page(title):
        story.append(PageBreak()); add(title, "h")
    def picture(path, width=content_width):
        with PillowImage.open(path) as source:
            height = width * source.height / source.width
        story.append(Image(str(path), width=width, height=height))
        story.append(Spacer(1, 7))
    groups = dataset.groups()
    summary = {key: aggregate(runs) for key, runs in groups.items()}
    ratios = [summary[s, "Rust", "shared"]["fps"] / summary[s, "Unity", "shared"]["fps"] for s in SITES]
    if all(r > 1.05 for r in ratios):
        verdict = "Rust: выше FPS в трёх снятых сценах"
    elif all(r < 1 / 1.05 for r in ratios):
        verdict = "Unity: выше FPS в трёх снятых сценах"
    else:
        verdict = "Преимущество по FPS зависит от снятой сцены"
    visual_ok = [p for p in dataset.pairs if not p["reasons"]]
    visual_different = any(p["metrics"]["maxDifference"] > 0 for p in visual_ok)
    visual_findings = dataset.receipt.get("visualFindings", [])
    significant_mismatch = any(isinstance(f, dict) and f.get("severity") == "significant" and not f.get("resolved", False)
                               for f in visual_findings)
    add("FFOne: Unity и Rust", "title")
    add("Основной мир, ресурсы и загрузка чанков<br/>12 октября 2026", "sub")
    add(verdict, "h")
    ratio = math.prod(ratios) ** (1 / len(ratios))
    add("Геометрическое среднее отношения FPS Rust / Unity в режиме shared: <b>" + fmt(ratio, 2) + "x</b>. "
        "Это результат конкретных текущих сборок FFOne на указанном оборудовании, в трёх точках мира. "
        "Он не определяет победителя для всех игр, карт и видеокарт.")
    if significant_mismatch:
        add("В проверенных кадрах остаются существенные визуальные расхождения. Вывод по FPS условен: "
            "он сравнивает фактически отображённые сцены и пока не выбирает лучший рендер при одинаковом внешнем виде. "
            "Замечания ручной проверки приведены в методике.")
    elif not visual_ok:
        add("Визуальная эквивалентность не подтверждена: нет принятых пар кадров. Вердикт по скорости относится к измеренному набору и не означает одинаковую картинку.")
    elif visual_different:
        add("RGB в принятых парах кадров различается. Сам факт различия пикселей не определяет его важность: "
            "это могут быть округление, частицы или статические детали. Полное визуальное совпадение не заявляется; "
            "ручные замечания и страницы сравнения позволяют оценить конкретные расхождения.")
    else:
        add("В представленных парах кадров пиксели совпали. Это проверка выбранных камер и моментов времени; она не подтверждает все анимации и эффекты во всём мире.")
    scope = dataset.receipt.get("scope", {})
    table([["Объём мира", "Принятые измерения", "Условия кэша"],
           [f"{scope.get('worldTiles',170)} тайлов\n{scope.get('grassInstances',709789):,} экземпляров травы".replace(",", " "),
            f"{len(dataset.accepted)} прогонов\n360 интервалов в каждом", "Новый процесс\nФайловый кэш ОС не сбрасывался"]],
          [content_width * .35, content_width * .32, content_width * .33])
    plt.rcParams.update({"font.family": "Arial", "font.size": 10, "axes.spines.top": False, "axes.spines.right": False})
    fig, ax = plt.subplots(figsize=(8, 3.6), layout="constrained")
    x = np.arange(3)
    for engine, offset, color in (("Rust", -.18, "#C56A3B"), ("Unity", .18, "#197CA0")):
        means = [summary[s, engine, "shared"]["fps"] for s in SITES]
        raw = [[1000 / r.stats["meanMs"] for r in groups[s, engine, "shared"]] for s in SITES]
        errors = np.array([[m - min(v) for m, v in zip(means, raw)], [max(v) - m for m, v in zip(means, raw)]])
        bars = ax.bar(x + offset, means, .34, label=engine, color=color, yerr=np.maximum(errors, 0), capsize=3)
        ax.bar_label(bars, labels=[fmt(v, 1) for v in means], padding=5, fontsize=9)
    ax.set_xticks(x, [SITE_NAMES[s] for s in SITES]); ax.set_ylabel("FPS, shared"); ax.legend(frameon=False)
    ax.set_ylim(0, max(summary[s,e,"shared"]["fps"] for s in SITES for e in ENGINES) * 1.22)
    ax.grid(axis="y", alpha=.18); ax.set_axisbelow(True)
    chart = assets / "shared-fps.png"; fig.savefig(chart, dpi=200); plt.close(fig); picture(chart)
    add("Полоски показывают минимум и максимум повторных прогонов, а не доверительный интервал.", "small")

    page("Методика и границы сравнения")
    environment = dataset.environment
    cpu = environment.get("cpu", {})
    cpu_name = cpu.get("Name", "не указано") if isinstance(cpu, dict) else str(cpu)
    gpu = environment.get("gpu", environment.get("adapter", "не указано"))
    ram = environment.get("ram", {})
    ram_bytes = ram.get("TotalPhysicalMemory") if isinstance(ram, dict) else None
    add("<b>Оборудование:</b> " + html.escape(cpu_name.strip()) + "; " + html.escape(str(gpu)) +
        ("; ОЗУ " + fmt(ram_bytes / 2**30, 1) + " ГиБ." if ram_bytes else "."))
    add("Обе сборки запускаются по очереди на одном адаптере. Повторы меняют порядок baseline/shared. "
        "VSync выключен, окно 1280x720, вертикальный FOV 45°, near 0,1, far 340, без MSAA и карт теней. "
        "До записи интервалов выполняется прогрев 3 секунды. Симуляция идёт шагом 1/60 секунды; "
        "рендер не ограничивается 60 FPS.")
    add("Времена измеряются в новых процессах, но файловый кэш Windows не сбрасывался. Это не замер холодного диска. "
        "Проверки кадров с остановленным временем, validation-прогоны, отклонённые результаты и прогоны с компиляцией "
        "исключены из таблиц производительности.")
    if dataset.settings.get("graphicsApi"):
        add("<b>Общий графический API:</b> " + html.escape(dataset.settings["graphicsApi"]) + ".")
    for finding in dataset.receipt.get("runtimeFindings", []):
        add(html.escape(str(finding)), "small")
    if visual_findings:
        add("Ручная проверка внешнего вида", "sub")
        for finding in visual_findings:
            if isinstance(finding, str):
                add(html.escape(finding))
            elif isinstance(finding, dict):
                site = site_name(str(finding.get("site", "")))
                label = SITE_NAMES.get(site, "Общая проверка")
                severity = {"significant": "существенное", "minor": "небольшое", "informational": "наблюдение"}.get(finding.get("severity"), "наблюдение")
                state = "; исправлено" if finding.get("resolved") else ""
                description = str(finding.get("description", finding.get("finding", "")))
                add("<b>" + html.escape(label + " / " + severity + state) + ":</b> " + html.escape(description))
        add("Замечания отражают ручной просмотр выбранных кадров. MAE, SSIM и максимальная разница RGB "
            "служат дополнительными измерениями и сами по себе не подтверждают совпадение статических деталей.", "small")
    add("Камера и охват", "sub")
    rows = [["Сцена", "Позиция камеры (native XYZ)", "Тайлы / визуальные размещения"]]
    for site in SITES:
        raw = groups[site,"Rust","shared"][0].report; view = camera(raw)
        rows.append([SITE_NAMES[site], " / ".join(fmt(p,3) for p in view["position"]),
                     f"{len(view['tiles'])} / {view['placements']}\n" + ", ".join(f"{x:02d}_{z:02d}" for x,z in view["tiles"])])
    table(rows, [content_width*.21,content_width*.42,content_width*.37])
    add("Внутри каждой сцены проверены одинаковые тайлы и число визуальных размещений, параметры камеры и viewport. "
        "Допуск округления позиции - 0,002 единицы мира, направления - 0,0001 по компоненте. "
        "Число размещений не является числом draw calls.")
    add("Перенос и ресурсы", "sub")
    add("Импорт охватывает основной мир целиком: 170 тайлов и 709 789 экземпляров травы. map_01_01 сохраняется в WorldMap. "
        "Перенесены высоты Gray16/32767, сдвиги вершин, текстуры и mip-цепочки terrain, положения объектов, "
        "анимации, движущиеся части и опубликованные эффекты. Ресурсы объединяются при совпадении содержимого; "
        "геометрия, плотность травы и размещения сохраняются.")
    gap = scope.get("missingTargetRecords", 105)
    add(f"У map_01_01 в принятом исходнике есть {gap} записей без targets. Этот исходный пробел унаследован обоими "
        "вариантами; он не считается успешно восстановленной анимацией. У остальных принятых записей используется "
        "современный контракт привязок. Terrain wavingGrass в текущем Rust представлен статичными перекрёстными "
        "плоскостями; Unity воспроизводит этот вариант.")
    add("FPS проверяется в трёх точках, а переход чанка - по указанному маршруту. Импорт всего мира и выбранные замеры "
        "не заменяют обход каждого тайла и всех состояний игрового процесса.")

    page("Время кадра и эффект оптимизаций")
    add("FPS = 1000 / среднее время кадра. Для каждой строки сначала рассчитываются статистики отдельных прогонов, "
        "затем усредняются между повторами. p95 и p99 здесь - средние перцентилей прогонов; "
        "интервалы разных запусков не объединяются в один псевдонезависимый ряд.")
    rows = [["Сцена / рендер", "Режим / N", "FPS", "Среднее, мс", "Медиана, мс", "p95, мс", "p99, мс"]]
    for site in SITES:
        for engine in ENGINES:
            for mode in MODES:
                row = summary[site,engine,mode]
                rows.append([SITE_NAMES[site]+"\n"+engine, mode+" / "+str(row["n"]), fmt(row["fps"],1),
                             *[fmt(row[k]) for k in ("meanMs","medianMs","p95Ms","p99Ms")]])
    table(rows, [content_width*.24,content_width*.16]+[content_width*.12]*5)
    add("Изменение shared относительно baseline", "sub")
    rows = [["Сцена", "Rust: FPS / p95", "Unity: FPS / p95"]]
    for site in SITES:
        cells = [SITE_NAMES[site]]
        for engine in ENGINES:
            base, shared = summary[site,engine,"baseline"], summary[site,engine,"shared"]
            cells.append(f"{fmt((shared['fps']/base['fps']-1)*100,1)}% / {fmt((shared['p95Ms']/base['p95Ms']-1)*100,1)}%")
        rows.append(cells)
    table(rows,[content_width*.34,content_width*.33,content_width*.33])
    add("Рост FPS полезен; снижение p95 полезно. Unity baseline дополнительно отключает SRP batching и "
        "создаёт отдельные материалы размещений. Rust baseline отключает своё объединение ресурсов. "
        "Эти режимы показывают эффект оптимизаций каждой реализации и не являются одинаковыми переключателями.")

    page("Старт, память процесса и переход чанка")
    add("Используется totalReadySeconds: Rust складывает preAppLoadSeconds и readySeconds; "
        "его отсчёт начинается внутри main перед загрузкой содержимого. Unity считает от Process.StartTime "
        "до готовности мира. Самый ранний запуск процесса Rust этим таймером не покрыт, поэтому границы старта "
        "не строго идентичны. readySeconds отдельно не сравнивается между движками. "
        "При отсутствии общего totalReadySeconds поле оставлено пустым и сравнение старта не делается. "
        "Полное время жизни процесса включает прогрев, запись 360 кадров и завершение, поэтому не заменяет время загрузки.")
    add("В строках ниже значения shared / baseline показаны в таком порядке; усредняются одинаково между повторами.", "small")
    rows = [["Сцена / рендер", "Режимы", "Готовность, с", "Working set, МиБ", "Private bytes, МиБ"]]
    for site in SITES:
        for engine in ENGINES:
            rows_by_mode = [summary[site,engine,mode] for mode in MODES]
            ready = " / ".join(fmt(row["total_ready"]) for row in rows_by_mode)
            working = " / ".join(fmt(row["working"]/2**20 if row["working"] is not None else None,1) for row in rows_by_mode)
            private = " / ".join(fmt(row["private"]/2**20 if row["private"] is not None else None,1) for row in rows_by_mode)
            rows.append([SITE_NAMES[site]+"\n"+engine,"shared / baseline",ready,working,private])
    table(rows,[content_width*.25,content_width*.15,content_width*.18,content_width*.21,content_width*.21])
    add("Working set - физически резидентные страницы процесса; private bytes - его приватная виртуальная память. "
        "Это CPU/process-метрики, <b>не VRAM</b>. При наличии process.json используются пики за время жизни процесса; "
        "иначе указана память в момент записи отчёта. Резидентные image CPU bytes и Unity allocated bytes "
        "имеют другие определения и не подменяют эти столбцы.", "small")
    collider_notes = []
    for site in SITES:
        rust = groups[site,"Rust","shared"][0].report; unity = groups[site,"Unity","shared"][0].report
        authored = rust.get("authoredColliderEntities")
        if authored is not None:
            collider_notes.append(SITE_NAMES[site] + ": объекты Rust/Unity " + str(authored) + "/" + str(unity.get("colliders", "нет данных")) +
                                  "; terrain Rust " + str(rust.get("terrainHeightmapColliders", "нет данных")))
    if collider_notes:
        add("<b>Коллайдеры:</b> " + "; ".join(collider_notes) + ". Число Unity colliders и Rust authoredColliderEntities "
            "считает размещения коллайдеров объектов, без terrain. Rust terrainHeightmapColliders показан отдельно; "
            "его внутреннее представление отличается от Unity MeshCollider.", "small")
    transitions = [(r,t) for r in dataset.accepted_transitions if (t:=r.transition)] + [(r,t) for r in dataset.accepted if (t:=r.transition)]
    add("Переход на 512 единиц", "sub")
    if transitions:
        rows = [["Сцена / рендер / режим", "Готовность, с", "p95 кадра, мс", "Максимум, мс", "Интервалов"]]
        for run,t in transitions:
            delta = t.get("delta", t.get("offset"))
            frames = t.get("frames", t.get("frameMs", []))
            samples = [float(f.get("frameMs",f.get("ms",0))) if isinstance(f,dict) else float(f) for f in frames]
            samples = sorted(s for s in samples if s>0)
            if delta and abs(math.sqrt(sum(float(d)**2 for d in delta))-512)>.01:
                continue
            rows.append([SITE_NAMES[run.site]+" / "+run.engine+" / "+run.mode,fmt(t.get("readySeconds",t.get("totalReadySeconds"))),
                         fmt(samples[int(len(samples)*.95)] if samples else None),fmt(max(samples) if samples else None),str(len(samples))])
        table(rows,[content_width*.36,content_width*.16,content_width*.17,content_width*.17,content_width*.14])
        add("Спайки относятся к переходу и показываются отдельно от FPS после прогрева. При коротком переходе p95 "
            "нестабилен; максимум и число интервалов приведены рядом.", "small")
        if any(t.get("frames", [{}])[-1].get("admitting", 0) != 0 for _, t in transitions if t.get("frames")):
            add("Некоторые журналы заканчиваются до кадра полной готовности. Его задержка могла бы изменить максимум "
                "и p95; время готовности включает весь переход.", "small")
    else:
        add("Принятого журнала перехода нет. По этому набору нельзя выбрать лучший загрузчик чанков.")

    for site in SITES:
        pairs = sorted([p for p in visual_ok if p["site"]==site],key=lambda p:p["poseSeconds"])
        if not pairs:
            continue
        page("Кадры: " + SITE_NAMES[site])
        add("Одинаковые камера, охват и время симуляции. MAE измеряется по RGB в диапазоне 0-255; "
            "доля различий - пиксели, где хотя бы один канал отличается больше чем на 5. "
            "SSIM рассчитан на исходном разрешении, окно 7x7, без подгонки и смещения изображений.","small")
        heatmaps = []
        for pair in pairs[:2]:
            pose = pair["poseSeconds"]; metrics = pair["metrics"]
            add(f"t = {fmt(pose,0)} с: MAE {fmt(metrics['mae'],2)}, SSIM {fmt(metrics['ssim'],4)}, "
                f"различий &gt;5/255: {fmt(metrics['percentAbove5'],2)}%", "sub")
            with PillowImage.open(pair["rust"]) as image: left=image.convert("RGB")
            with PillowImage.open(pair["unity"]) as image: right=image.convert("RGB")
            composite=PillowImage.new("RGB",(left.width*2,left.height),(255,255,255)); composite.paste(left,(0,0)); composite.paste(right,(left.width,0))
            pair_path=assets/f"{site}-pose{pose}-pair.png"; composite.save(pair_path); picture(pair_path)
            add("Rust слева / Unity справа", "small")
            diff=np.abs(np.asarray(left,dtype=float)-np.asarray(right,dtype=float)).mean(axis=2)
            fig, ax=plt.subplots(figsize=(8,4.5),layout="constrained")
            heat=ax.imshow(diff,cmap="inferno",vmin=0,vmax=255); ax.set_axis_off()
            fig.colorbar(heat,ax=ax,shrink=.8,label="Средняя |ΔRGB|, 0-255")
            heat_path=assets/f"{site}-pose{pose}-heatmap.png"; fig.savefig(heat_path,dpi=160); plt.close(fig); heatmaps.append(heat_path)
        if len(heatmaps)==2:
            with PillowImage.open(heatmaps[0]) as image: a=image.convert("RGB")
            with PillowImage.open(heatmaps[1]) as image: b=image.convert("RGB")
            combined=PillowImage.new("RGB",(a.width+b.width,max(a.height,b.height)),"white");combined.paste(a,(0,0));combined.paste(b,(a.width,0))
            heat_path=assets/f"{site}-heatmaps.png";combined.save(heat_path);picture(heat_path)
            add("Карты разницы t=0 и t=1; одинаковая шкала 0-255.","small")
        else:
            picture(heatmaps[0],content_width*.75)
        add("Разница RGB включает динамические частицы с недетерминированным состоянием. Поэтому она не отделяет "
            "ошибки материала от случайного расположения частиц. Видимые расхождения требуют проверки по "
            "неподвижным деталям и состояниям эффекта; показатели не доказывают полное совпадение.","small")

    receipt_hash=hashlib.sha256(dataset.receipt_path.read_bytes()).hexdigest()
    add("Источник чисел: выбранные report.json, process.json и run-metadata.json в Builds/WorldBenchmark/Rust и Unity; "
        "перечень закреплён dataset receipt. SHA256 receipt: "+receipt_hash+".","small")
    def footer(canvas,doc):
        canvas.saveState();canvas.setStrokeColor(pale);canvas.line(18*mm,17*mm,page_width-18*mm,17*mm)
        canvas.setFillColor(ink);canvas.setFont("ReportArial",7.5)
        canvas.drawString(18*mm,12*mm,"FFOne / сравнение текущих сборок / 12.10.2026")
        canvas.drawRightString(page_width-18*mm,12*mm,str(doc.page));canvas.restoreState()
    doc=SimpleDocTemplate(str(output),pagesize=A4,rightMargin=18*mm,leftMargin=18*mm,topMargin=17*mm,bottomMargin=24*mm,
                          title="FFOne: сравнение Unity и Rust",author="FFOne renderer comparison")
    doc.build(story,onFirstPage=footer,onLaterPages=footer)
    (assets/"analysis.json").write_text(json.dumps({"receiptSha256":receipt_hash,"status":status,"summary":[
        {"site":s,"engine":e,"mode":m,**v} for (s,e,m),v in summary.items()]},ensure_ascii=False,indent=2),encoding="utf-8")
    return output


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root",type=Path,default=PROJECT/"Builds/WorldBenchmark")
    parser.add_argument("--receipt",type=Path)
    parser.add_argument("--output",type=Path,default=PROJECT/"Reports/2026-10-12-renderer-comparison.pdf")
    parser.add_argument("--validate-only",action="store_true",help="Inspect receipt and raw data without authoring a PDF.")
    args=parser.parse_args()
    receipt=args.receipt or args.root/"dataset-receipt.json"
    if not receipt.exists():
        parser.error("A reviewed dataset receipt is required: "+str(receipt))
    dataset=Dataset(args.root,receipt)
    if args.validate_only:
        print(json.dumps(dataset.status(),ensure_ascii=False,indent=2));return
    try:
        result=build(dataset,args.output.resolve())
    except ModuleNotFoundError as error:
        parser.error("Missing report dependency: "+str(error)+". Install matplotlib into the bundled Python before authoring.")
    print(str(result))


if __name__=="__main__":
    main()
