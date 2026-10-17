"""Exercise actual Addressable cell scenes in the built development player."""
from pathlib import Path
import json
import os
import re
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[1]
PLAYER = ROOT / 'Builds/WorldDistrict/Windows/FFOne.exe'
REPORT = ROOT / 'Tools/ContentMigration/world-district-player.json'
LOG = ROOT / 'Logs/WorldDistrictPlayer.log'


def main():
    profile = ROOT / 'Builds/WorldDistrict/SmokeProfiles' / uuid.uuid4().hex
    profile.mkdir(parents=True)
    raw = profile / 'result.json'
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = subprocess.SW_HIDE
    env = dict(os.environ, FFONE_DEV_SERVER='127.0.0.1:23000', FFONE_DEV_PROFILES=str(profile))
    process = subprocess.run([str(PLAYER), '-batchmode', '-force-vulkan', '-screen-width', '1280', '-screen-height', '720',
                              '--world-district-smoke', '--world-district-report', str(raw), '-logFile', str(LOG)],
                             cwd=PLAYER.parent, env=env, startupinfo=startup, creationflags=subprocess.CREATE_NO_WINDOW,
                             timeout=1800, check=False)
    result = json.loads(raw.read_text(encoding='utf-8-sig')) if raw.exists() else {'passed': False, 'error': 'Player report missing'}
    log = LOG.read_text(encoding='utf-8-sig', errors='replace')
    errors = re.findall(r'^.*(?:\w+Exception:|Failed to load AssetBundle|Unable to load content address).*$', log, re.M)
    baseline = 'ERROR: Shader Hidden/Universal Render Pipeline/DBufferClear shader is not supported on this GPU (none of subshaders/fallbacks are suitable)'
    engine_errors = re.findall(r'^ERROR:.*$', log, re.M)
    errors.extend(line for line in engine_errors if line != baseline)
    result.update(exitCode=process.returncode, exceptions=errors[:30], knownBaselineDiagnostics=[line for line in engine_errors if line == baseline],
                  screenshot=str(raw.with_suffix('.png')), log=str(LOG))
    result['passed'] = result['passed'] and process.returncode == 0 and not errors and 'WORLD DISTRICT PLAYER PASSED' in log
    REPORT.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result))
    raise SystemExit(0 if result['passed'] else 1)


if __name__ == '__main__':
    main()
