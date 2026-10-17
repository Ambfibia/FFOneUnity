"""Run actual player-capsule probes against Addressable scenes in Windows build."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('stage', choices=['before', 'after'])
    args = parser.parse_args()
    report = ROOT / f'Tools/ContentMigration/world-boundary-walk-{args.stage}.json'
    plan = ROOT / f'Tools/ContentMigration/world-boundary-probes-{args.stage}.json'
    profile = ROOT / 'Builds/WholeWorld/SmokeProfiles' / uuid.uuid4().hex
    profile.mkdir(parents=True)
    raw = profile/'boundary.json'
    log = ROOT/f'Logs/WorldBoundaryWalk-{args.stage}.log'
    player = ROOT/'Builds/WholeWorld/Windows/FFOne.exe'
    startup = subprocess.STARTUPINFO(); startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW; startup.wShowWindow = subprocess.SW_HIDE
    process = subprocess.run([str(player),'-batchmode','-force-vulkan','--whole-world-demo','--world-boundary-smoke',
        '--world-boundary-plan',str(plan),'--world-boundary-report',str(raw),'-logFile',str(log)],
        cwd=player.parent,env=dict(os.environ,FFONE_DEV_SERVER='127.0.0.1:23000',FFONE_DEV_PROFILES=str(profile)),
        startupinfo=startup,creationflags=subprocess.CREATE_NO_WINDOW,timeout=14400,check=False)
    result = json.loads(raw.read_text()) if raw.exists() else {'passed':False,'error':'No player report'}
    result.update(exitCode=process.returncode,log=str(log),plan=str(plan))
    result['passed'] = result['passed'] and process.returncode == 0
    report.write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps({k:v for k,v in result.items() if k!='observations'}))
    print('crossed',sum(o['crossed'] for o in result.get('observations',[])),'of',len(result.get('observations',[])))
    raise SystemExit(0 if result['passed'] else 1)


if __name__=='__main__':main()
