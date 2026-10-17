"""Render the native character UI at Full HD and WUXGA without logging in."""
from pathlib import Path
import argparse
import hashlib
import json
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[1]
SCREENS = ("login", "selection", "selection-occupied", "delete-character",
           "generated-name", "custom-name", "appearance", "appearance-girl", "quit")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, default=ROOT / "Builds/Windows/FFOne.exe")
    parser.add_argument("--output", type=Path, default=ROOT / "Logs/FrontendFullHd")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    runtime = args.player.resolve().parent / (args.player.stem + "_Data/Managed/FFOne.Runtime.dll")
    runtime_hash = hashlib.sha256(runtime.read_bytes()).hexdigest()
    receipt = {"offline": True, "player": str(args.player.resolve()),
               "runtimeSha256": runtime_hash, "viewports": []}
    for width, height in ((1920, 1080), (1920, 1200)):
        folder = output / f"{width}x{height}"
        folder.mkdir(exist_ok=True)
        log = folder / "player.log"
        command = [str(args.player.resolve()), "-batchmode", "-force-vulkan", "-noaudio",
                   "-screen-fullscreen", "0", "-screen-width", str(width),
                   "-screen-height", str(height), "--native-ui-smoke",
                   "--ui-output-dir", str(folder), "-logFile", str(log)]
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = subprocess.SW_HIDE
        result = subprocess.run(command, cwd=ROOT, startupinfo=startup,
                                creationflags=subprocess.CREATE_NO_WINDOW, timeout=180)
        text = log.read_text(encoding="utf-8", errors="replace")
        if result.returncode or "NATIVE UI PLAYER SMOKE PASSED" not in text:
            raise RuntimeError(f"UI validation failed at {width}x{height}; see {log}")
        for screen in SCREENS:
            png = folder / f"{screen}.png"
            data = png.read_bytes()
            if data[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", data[16:24]) != (width, height):
                raise RuntimeError(f"Incorrect capture size: {png}")
        receipt["viewports"].append({"width": width, "height": height, "passed": True,
                                    "screens": list(SCREENS), "log": str(log)})
        print(f"Native UI passed: {width}x{height}, {len(SCREENS)} captures", flush=True)
    if hashlib.sha256(runtime.read_bytes()).hexdigest() != runtime_hash:
        raise RuntimeError("The runtime DLL changed during UI validation.")
    (output / "validation.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
