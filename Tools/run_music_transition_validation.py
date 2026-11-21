"""Check real audio playback, creation/world entry and a cold occupied roster.

Run against an isolated local RustyFusion server. This creates one disposable
Recovery account; existing player accounts are not used.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, default=ROOT / "Builds/MusicTransitions/Windows/FFOne.exe")
    parser.add_argument("--output", type=Path, default=ROOT / "Logs/MusicTransitions")
    parser.add_argument("--server-port", type=int, default=24440)
    args = parser.parse_args()
    player, output = args.player.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    runtime = player.parent / (player.stem + "_Data/Managed/FFOne.Runtime.dll")
    digest = hashlib.sha256(runtime.read_bytes()).hexdigest()
    results = {"player": str(player), "runtimeSha256": digest, "audioEnabled": True, "runs": []}
    account = None
    for phase in ("audio", "creation-world", "cold-occupied-roster"):
        if hashlib.sha256(runtime.read_bytes()).hexdigest() != digest:
            raise RuntimeError("Player changed before validation completed.")
        folder = output / phase
        folder.mkdir(exist_ok=True)
        log = folder / "player.log"
        command = [str(player), "-batchmode", "-force-vulkan", "-screen-fullscreen", "0",
                   "-screen-width", "1920", "-screen-height", "1080", "-logFile", str(log)]
        if phase == "audio":
            command += ["--retrobution-audio-smoke"]
            marker = "RETROBUTION AUDIO SMOKE PASSED"
        else:
            command += ["--frontend-recovery-smoke", "--recovery-port", str(args.server_port)]
            if phase == "creation-world":
                command += ["--recovery-overworld"]
            if account:
                command += ["--recovery-existing-account", account]
            marker = "FRONTEND RECOVERY SMOKE PASSED"
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = subprocess.SW_HIDE
        result = subprocess.run(command, cwd=ROOT, startupinfo=startup,
                                creationflags=subprocess.CREATE_NO_WINDOW, timeout=600,
                                env=dict(os.environ, FFONE_DEV_PROFILES=str(output / "profiles"),
                                         FFONE_DEV_SERVER=f"127.0.0.1:{args.server_port}"))
        text = log.read_text(encoding="utf-8-sig", errors="replace")
        errors = re.findall(r"^.*(?:\w+Exception:|Failed to load AssetBundle|Unable to load content address).*$", text, re.M)
        passed = result.returncode == 0 and marker in text and not errors
        results["runs"].append({"phase": phase, "passed": passed, "exitCode": result.returncode,
                                "errors": errors, "log": str(log)})
        (output / "validation.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
        print(f"{phase}: {'PASSED' if passed else 'FAILED'}", flush=True)
        if not passed:
            raise RuntimeError(f"Music/loading validation failed: {log}")
        if phase != "audio":
            captures = player.parent / "Logs/FrontendRecovery"
            if captures.exists():
                shutil.copytree(captures, folder / "captures", dirs_exist_ok=True)
        if phase == "creation-world":
            match = re.search(r"^FRONTEND RECOVERY ACCOUNT: (Recovery\d+)\s*$", text, re.M)
            if not match:
                raise RuntimeError("The disposable validation account was not reported.")
            account = match.group(1)
    if hashlib.sha256(runtime.read_bytes()).hexdigest() != digest:
        raise RuntimeError("Player changed during validation.")
    results["passed"] = True
    (output / "validation.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
