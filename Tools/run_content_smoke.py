"""Validate the development player against its built local Addressables bundles."""
from pathlib import Path
import argparse
import json
import os
import re
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
PLAYER = ROOT / 'Builds/ContentLayout/Windows/FFOne.exe'
LOG = ROOT / 'Logs/ContentLayoutPlayerSmoke.log'
RESULT = ROOT / 'Logs/ContentLayoutPlayerSmoke.json'

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--player',type=Path,default=PLAYER);args=parser.parse_args()
    player=args.player.resolve()
    if not player.is_file():
        raise FileNotFoundError(player)
    profile = ROOT / 'Builds/ContentLayout/SmokeProfiles' / uuid.uuid4().hex
    profile.mkdir(parents=True)
    environment = dict(os.environ, FFONE_DEV_SERVER='127.0.0.1:23000', FFONE_DEV_PROFILES=str(profile))
    options = {}
    if os.name == 'nt':
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = subprocess.SW_HIDE
        options = dict(startupinfo=startup, creationflags=subprocess.CREATE_NO_WINDOW)
    started = time.monotonic()
    try:
        process = subprocess.run([str(player), '-batchmode', '-force-vulkan', '--content-layout-smoke',
                                  '-logFile', str(LOG)], cwd=player.parent, env=environment,
                                 timeout=240, check=False, **options)
        code = process.returncode
    except subprocess.TimeoutExpired:
        code = -1
    output = LOG.read_text(encoding='utf-8-sig', errors='replace') if LOG.exists() else ''
    errors = re.findall(r'^.*(?:\w+Exception:|Failed to load AssetBundle|Unable to load content address).*$', output, re.M)
    shader_diagnostic = 'ERROR: Shader Hidden/Universal Render Pipeline/DBufferClear shader is not supported on this GPU (none of subshaders/fallbacks are suitable)'
    engine_errors = re.findall(r'^ERROR:.*$', output, re.M)
    baseline_diagnostics = [line for line in engine_errors if line == shader_diagnostic]
    errors.extend(line for line in engine_errors if line != shader_diagnostic)
    result = dict(passed=code == 0 and 'CONTENT LAYOUT PLAYER SMOKE PASSED:' in output and not errors,
                  exitCode=code, seconds=round(time.monotonic()-started, 2), errors=errors[:20],
                  knownBaselineDiagnostics=baseline_diagnostics,
                  nanoModelsChecked='CONTENT NANO MODELS PASSED: 69 stable identities' in output,
                  player=str(player),log=str(LOG), profile=str(profile))
    RESULT.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result))
    raise SystemExit(0 if result['passed'] else 1)

if __name__ == '__main__':
    main()
