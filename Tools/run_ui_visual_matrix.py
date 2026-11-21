"""Capture and verify the shipped Unity UI in isolated offline profiles."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import socket
import struct
import subprocess


ROOT = Path(__file__).resolve().parents[1]
RESOLUTIONS = ("1280x720", "1920x1080", "1920x1200", "2560x1080")
SUITES = {
    "frontend": (
        "--native-ui-smoke", "NATIVE UI", {
            "login", "login-edit-account", "login-add-server", "login-invalid-server",
            "registration", "settings-graphics", "settings-game-ui", "settings-social",
            "settings-controls", "settings-resolution", "settings-text-locale",
            "loading-resources", "loading-background-only", "selection", "selection-occupied",
            "delete-character", "generated-name", "custom-name", "appearance", "appearance-girl",
            "selection-after-appearance", "quit",
        },
    ),
    "gameplay": (
        "--gameplay-ui-smoke", "GAMEPLAY UI", {
            "hud", "hud-exp-enabled", "nanocom", "game-guide", "inventory",
            "inventory-delete", "nanos", "nanos-extended", "mail-guide", "mail-guide-empty",
            "mail-empty", "mail-read", "mail-compose", "journal", "journal-ben",
            "journal-offer", "journal-turn-in", "journal-completed",
            "map-world", "map-zoom", "contacts", "buddy-list", "buddy-add", "quick-chat",
            "emote-menu", "chat-echo", "cash-mall", "trade-invitation", "trade-active",
            "trade-chat-echo", "mentor-warning", "mentor-selection", "mentor-confirm-ben",
            "resurrection", "croc-pot", "croc-confirm",
        },
    ),
}


def artifact_hashes(player):
    data = player.parent / f"{player.stem}_Data"
    paths = [player, data / "Managed/FFOne.Runtime.dll"]
    paths += [data / "StreamingAssets/aa" / name for name in ("settings.json", "catalog.bin", "catalog.hash")]
    return {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def prepare_profile(profile, endpoint, locale):
    profile.mkdir()
    addresses = [endpoint, "127.0.0.1:1", "127.0.0.1:2", "127.0.0.1:3"]
    write_json(profile / "login-profiles.json", {
        "schema": "ffone.login-profiles.v1",
        "selected": 0,
        "servers": [{"address": address, "api": None} for address in addresses],
        "accounts": [
            {"server": endpoint, "username": username}
            for username in ("VisualExisting", "VisualSecond", "VisualThird", "VisualFourth")
        ],
    })
    write_json(profile / "unity-options.json", {
        "schema": "ffone.unity-options.v1",
        "text_locale": locale,
        "voice_locale": locale,
    })


def capture_set(player, folder, size, locale, suite_name):
    flag, marker, expected = SUITES[suite_name]
    folder.mkdir(parents=True)
    log = folder / "player.log"
    profile = folder / "profiles"
    captures = folder / "captures"
    width, height = map(int, size.split("x"))
    # Reserve a loopback endpoint for the Settings and saved-profile path. The
    # smoke only opens views, so the socket has no game session or test account.
    with socket.socket() as reserved:
        reserved.bind(("127.0.0.1", 0))
        endpoint = f"127.0.0.1:{reserved.getsockname()[1]}"
        prepare_profile(profile, endpoint, locale)
        env = dict(os.environ, FFONE_DEV_PROFILES=str(profile), FFONE_DEV_SERVER=endpoint)
        command = [
            str(player), "-batchmode", "-force-vulkan", "-noaudio", "-screen-fullscreen", "0",
            "-screen-width", str(width), "-screen-height", str(height), flag,
            "--ui-locale", locale, "--ui-output-dir", str(captures), "-logFile", str(log),
        ]
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = subprocess.SW_HIDE
        try:
            result = subprocess.run(command, cwd=ROOT, env=env, startupinfo=startup,
                                    creationflags=subprocess.CREATE_NO_WINDOW, timeout=260)
            error = "" if result.returncode == 0 else f"Unity exited {result.returncode}"
        except subprocess.TimeoutExpired:
            error = "Unity timed out"

    log_text = log.read_text(encoding="utf-8-sig", errors="replace") if log.exists() else ""
    names = set(re.findall(rf"^{marker} CAPTURE: ([\w-]+)$", log_text, re.MULTILINE))
    invalid = []
    for name in sorted(names | expected):
        png = captures / f"{name}.png"
        if png.is_file():
            with png.open("rb") as stream:
                header = stream.read(24)
            if header[:8] == b"\x89PNG\r\n\x1a\n" and len(header) == 24 and struct.unpack(">II", header[16:24]) == (width, height):
                continue
        invalid.append(name)
    errors = [error] if error else []
    if f"UI MATRIX LOCALE: {locale} at {size}" not in log_text:
        errors.append("Missing locale/viewport verification")
    if f"{marker} PLAYER SMOKE PASSED" not in log_text:
        errors.append("Smoke did not complete")
    if expected - names:
        errors.append("Missing capture markers: " + ", ".join(sorted(expected - names)))
    if invalid:
        errors.append("Missing or invalid capture dimensions: " + ", ".join(invalid))
    return {
        "suite": suite_name,
        "locale": locale,
        "resolution": size,
        "passed": not errors,
        "log": str(log),
        "captures": sorted(names),
        "errors": errors,
    }


def resolution(value):
    if not re.fullmatch(r"[1-9]\d{2,3}x[1-9]\d{2,3}", value):
        raise argparse.ArgumentTypeError("resolution must be WIDTHxHEIGHT")
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--resolution", type=resolution, action="append")
    parser.add_argument("--locale", choices=("en", "ru"), action="append")
    parser.add_argument("--suite", choices=SUITES, action="append")
    args = parser.parse_args()
    player = args.player.resolve()
    output = args.output.resolve()
    output.mkdir(parents=True)
    receipt = {"player": str(player), "artifacts": artifact_hashes(player), "results": []}
    report = output / "validation.json"
    for size in args.resolution or RESOLUTIONS:
        for locale in args.locale or ("en", "ru"):
            for suite in args.suite or SUITES:
                result = capture_set(player, output / size / locale / suite, size, locale, suite)
                receipt["results"].append(result)
                write_json(report, receipt)
                status = "passed" if result["passed"] else "failed"
                print(f"{size} {locale} {suite}: {status}, {len(result['captures'])} captures", flush=True)
    receipt["artifactsUnchanged"] = receipt["artifacts"] == artifact_hashes(player)
    receipt["passed"] = receipt["artifactsUnchanged"] and all(result["passed"] for result in receipt["results"])
    write_json(report, receipt)
    return 0 if receipt["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
