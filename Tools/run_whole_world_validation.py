"""Run sequential Unity acceptance stages in a closed project (Windows)."""
from pathlib import Path
import argparse
import subprocess

ROOT = Path(__file__).resolve().parents[1]
UNITY = Path(r'C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('stage', choices=['seam-plan', 'seam-apply', 'tests', 'build', 'player-build', 'boundary-apply', 'boundary-scan', 'boundary-tests'])
    args = parser.parse_args()
    methods = {'seam-plan': 'WholeWorldSeams.Plan', 'seam-apply': 'WholeWorldSeams.Apply',
               'build': 'WholeWorldMigration.Build', 'player-build': 'WholeWorldMigration.BuildPlayerOnly',
               'boundary-apply':'WorldBoundaryInvestigation.ApplyReviewedRepairs', 'boundary-scan':'WorldBoundaryInvestigation.ScanAfter'}
    log = ROOT / 'Logs' / f'WholeWorld-{args.stage}.log'
    command = [str(UNITY), '-batchmode', '-projectPath', str(ROOT), '-logFile', str(log)]
    if args.stage in ('tests', 'boundary-tests'):
        command += ['-runTests', '-testPlatform', 'EditMode', '-testFilter',
                    'FFOne.Tests.WorldCellAuthoringTests;FFOne.Tests.WorldSourceBindingsTests;FFOne.Tests.WorldContentTests;FFOne.Tests.NetworkWorldContractTests' + (';FFOne.Tests.WorldBoundaryRepairTests' if args.stage=='boundary-tests' else ''),
                    '-testResults', str(ROOT / ('Logs/WorldBoundaryEditorTests.xml' if args.stage=='boundary-tests' else 'Logs/WholeWorldEditorTests.xml'))]
    else:
        command += ['-executeMethod', 'FFOne.Editor.' + methods[args.stage], '-quit']
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = subprocess.SW_HIDE
    print(f'Starting {args.stage}: {log}', flush=True)
    run = subprocess.run(command, cwd=ROOT, startupinfo=startup, creationflags=subprocess.CREATE_NO_WINDOW, timeout=14400, check=False)
    print(f'{args.stage} exit code: {run.returncode}', flush=True)
    raise SystemExit(run.returncode)


if __name__ == '__main__':
    main()
