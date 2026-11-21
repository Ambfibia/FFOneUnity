"""Exercise startup, creation, world entry and a fresh-process occupied roster.

Requires the isolated local development server on 24300. Creates one disposable
Recovery account through the client; never changes an existing player's data.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import socket
import sqlite3
import subprocess
import time

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def accounts(database):
    with sqlite3.connect(database.as_uri() + "?mode=ro", uri=True) as connection:
        return {row[0] for row in connection.execute("SELECT Login FROM Accounts WHERE Login LIKE 'Recovery%'")}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, default=ROOT / "Builds/Windows/FFOne.exe")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--port", type=int, default=24300, help="Login port of the local validation shard.")
    parser.add_argument("--database", type=Path, default=ROOT.parent / "RustyFusion/target/ffone-unity/database.db")
    parser.add_argument("--tutorial-cinematics", action="store_true", help="Play and capture all ten finite tutorial scenes.")
    parser.add_argument("--overworld", action="store_true", help="Also validate the first ordinary world frame on the disposable account.")
    parser.add_argument("--native-screen", action="store_true", help="Capture the ordinary player framebuffer.")
    parser.add_argument("--audio", action="store_true", help="Enable the real audio device and verify audible cinematic SFX at the listener.")
    parser.add_argument("--input-actions", action="store_true", help="Verify rebound keyboard and gamepad movement in the ordinary world.")
    parser.add_argument("--quick-roster", action="store_true", help="Skip the introductory ship scenes after their first frame.")
    args = parser.parse_args()
    if args.input_actions and not (args.overworld and args.native_screen):
        parser.error("--input-actions requires --overworld and --native-screen")
    if args.quick_roster and args.tutorial_cinematics:
        parser.error("--quick-roster and --tutorial-cinematics validate different scenarios")
    player, output = args.player.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    database = args.database.resolve()
    with socket.create_connection(("127.0.0.1", args.port), timeout=2):
        pass
    before_accounts = accounts(database)
    data = player.parent / (player.stem + "_Data")
    content = data / "StreamingAssets/aa"
    frozen = {str(p): digest(p) for p in [data / "Managed/FFOne.Runtime.dll", content / "settings.json", *content.glob("catalog*")] if p.is_file()}
    receipt = {"player": str(player), "hashes": frozen, "runs": []}
    account = None
    for phase in ("creation-world", "cold-occupied-roster"):
        folder = output / phase
        folder.mkdir(exist_ok=True)
        log = folder / "player.log"
        command = [str(player), "-batchmode", "-force-vulkan", "-noaudio", "-screen-fullscreen", "0",
                   "-screen-width", "1920", "-screen-height", "1080", "--frontend-recovery-smoke",
                   "--content-loading-profile", "--recovery-port", str(args.port), "-logFile", str(log)]
        if args.native_screen:
            command.remove("-batchmode")
            command += ["--recovery-native-screen"]
        if args.audio:
            command.remove("-noaudio")
            command += ["--recovery-audio"]
        if account:
            command += ["--recovery-existing-account", account]
        if phase == "creation-world":
            if args.quick_roster:
                command += ["--recovery-quick-roster"]
            if args.tutorial_cinematics:
                command += ["--tutorial-cinematic-smoke"]
            if args.overworld:
                command += ["--recovery-overworld"]
            if args.input_actions:
                command += ["--recovery-input-actions"]
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        # Hidden validation uses the independent camera/UI render targets.
        startup.wShowWindow = 1 if args.native_screen else subprocess.SW_HIDE  # SW_SHOWNORMAL
        started = time.monotonic()
        process = subprocess.run(command, cwd=ROOT, startupinfo=startup, creationflags=subprocess.CREATE_NO_WINDOW,
                                 env=dict(os.environ, FFONE_DEV_PROFILES=str(output / "profiles")), timeout=600)
        text = log.read_text(encoding="utf-8-sig", errors="replace")
        screenshots = player.parent / "Logs/FrontendRecovery"
        if screenshots.exists():
            captures = folder / "captures"
            captures.mkdir(exist_ok=True)
            names = set(re.findall(r"^FRONTEND RECOVERY CAPTURE: ([^;]+);", text, re.M))
            for name in sorted(names):
                shutil.copy2(screenshots / (name + ".png"), captures)
            if (screenshots / "result.txt").is_file():
                shutil.copy2(screenshots / "result.txt", captures)
        errors = re.findall(r"^.*(?:\w+Exception:|Failed to load AssetBundle|Unable to load content address).*$", text, re.M)
        missing_animations = re.findall(r"^Tutorial animation .+ missing on .+$", text, re.M)
        audible_sfx = re.findall(r"^TUTORIAL AUDIBLE SFX PASSED: (.+)$", text, re.M)
        cinematic_scenes = re.findall(r"^TUTORIAL CINEMATIC PASSED: (\w+);", text, re.M)
        subtitle_scenes = re.findall(r"^TUTORIAL VOICE SUBTITLES PASSED: (\w+)$", text, re.M)
        cinematic_hud = "TUTORIAL HUD SMOKE PASSED" in text
        cinematic_motion = "TUTORIAL MOTION SMOKE PASSED" in text
        cinematic_environment = re.findall(r"^TUTORIAL ENVIRONMENT PASSED: (.+)$", text, re.M)
        cinematic_bridge = "TUTORIAL BRIDGE SUPPORT PASSED" in text
        waits = re.findall(r"^CONTENT SYNC WAIT: (.+)$", text, re.M)
        blocking_appearances = [address for address, stack in re.findall(
            r"^CONTENT SYNC WAIT: ([^\n]+)\n((?:[^\n]+\n)*)", text, re.M)
            if "NativeHnpcAvatar.cs" in stack or address.endswith("/CSCharButtonNormal")]
        passed = process.returncode == 0 and "FRONTEND RECOVERY SMOKE PASSED" in text and not errors and not blocking_appearances
        if phase == "creation-world" and args.tutorial_cinematics:
            passed = passed and len(set(cinematic_scenes)) == 10 and cinematic_hud and cinematic_motion and cinematic_bridge and bool(cinematic_environment) and not missing_animations
            # NanoPowerA2 is the five-second music-only transition.
            passed = passed and set(subtitle_scenes) == set(cinematic_scenes) - {"NanoPowerA2"}
            if args.audio:
                passed = passed and bool(audible_sfx)
        if phase == "creation-world" and args.overworld:
            passed = passed and "FRONTEND RECOVERY CAPTURE: overworld-entry;" in text and "FRONTEND RECOVERY HNPC PASSED:" in text and "CELL SOURCE BINDINGS PASSED:" in text
        native_input = "NATIVE GAMEPLAY INPUT PASSED:" in text
        if phase == "creation-world" and args.input_actions:
            passed = passed and native_input
        run = {"phase": phase, "passed": passed, "exitCode": process.returncode,
               "seconds": round(time.monotonic() - started, 3), "errors": errors,
               "cinematicScenes": cinematic_scenes, "tutorialHudPassed": cinematic_hud,
               "voiceSubtitleScenes": subtitle_scenes,
               "tutorialMotionPassed": cinematic_motion,
               "tutorialBridgePassed": cinematic_bridge,
               "tutorialEnvironment": cinematic_environment, "nativeInputPassed": native_input,
               "missingAnimations": missing_animations,
               "audibleSfx": audible_sfx,
               "syncWaits": waits, "blockingAppearanceWaits": blocking_appearances,
               "timings": re.findall(r"^FRONTEND (?:STARTUP READY|RECOVERY TIMING|RECOVERY FRAME): .+$", text, re.M)}
        receipt["runs"].append(run)
        (output / "validation.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        print(f"{phase}: {'PASSED' if passed else 'FAILED'}, {run['seconds']}s", flush=True)
        if not passed:
            raise RuntimeError(f"Resource loading validation failed: {log}")
        if phase == "creation-world":
            created = accounts(database) - before_accounts
            reported = re.findall(r"^FRONTEND RECOVERY ACCOUNT: (.+)$", text, re.M)
            matching = [name for name in created if len(reported) == 1 and name.casefold() == reported[0].strip().casefold()]
            if len(matching) != 1:
                raise RuntimeError("Could not identify the disposable account reported by this client.")
            account = matching[0]
    if any(digest(Path(path)) != value for path, value in frozen.items()):
        raise RuntimeError("The player assembly or Addressables catalog changed during validation.")
    receipt["passed"] = True
    (output / "validation.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
