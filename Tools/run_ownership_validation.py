"""Run closed-editor checks for ownership moves, then build the current world."""
import argparse
from pathlib import Path
import subprocess
from run_whole_world_validation import ROOT, UNITY

parser=argparse.ArgumentParser();parser.add_argument('stage',choices=['tests','prepare','build']);args=parser.parse_args()
command=[str(UNITY),'-batchmode','-projectPath',str(ROOT),'-logFile',str(ROOT/f'Logs/ContentOwnership-{args.stage}.log')]
if args.stage=='tests':
    command+=['-runTests','-testPlatform','EditMode','-testFilter',
              'FFOne.Tests.ContentOwnershipTests;FFOne.Tests.ContentLayoutTests;FFOne.Tests.EnvironmentSharingTests;FFOne.Tests.NativeAssetPublisherTests;FFOne.Tests.WorldBoundaryRepairTests',
              '-testResults',str(ROOT/'Logs/ContentOwnershipTests.xml')]
else:
    command+=['-executeMethod','FFOne.Editor.'+('ContentLayoutAcceptance.Prepare' if args.stage=='prepare' else 'WholeWorldMigration.Build'),'-quit']
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=subprocess.SW_HIDE
print('Starting ownership',args.stage,flush=True)
result=subprocess.run(command,cwd=ROOT,startupinfo=startup,creationflags=subprocess.CREATE_NO_WINDOW,timeout=14400)
print('Ownership',args.stage,'exit',result.returncode,flush=True)
raise SystemExit(result.returncode)
