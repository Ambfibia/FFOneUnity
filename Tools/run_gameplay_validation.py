"""Run the Unity gameplay/transport regression set in a closed editor."""
from pathlib import Path
import argparse
import subprocess

ROOT = Path(__file__).resolve().parents[1]
UNITY = Path(r'C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe')
if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('stage', nargs='?', choices=['prepare', 'tests', 'presentation-tests', 'build', 'content-build', 'content-only', 'instance-viewpoints', 'route-data', 'buff-content'], default='tests')
    parser.add_argument('--test-results', type=Path, help='Preserve a separate XML receipt for this acceptance run.')
    args = parser.parse_args()
    filters = ['LiveGameplayContractTests', 'NetworkProtocolTests', 'NetworkSessionTests',
               'NetworkShardSessionTests', 'GameplayInventoryTests', 'GameplayTradeTests',
               'GameplayTradeChatTests', 'GameplayChatTests', 'GameplayUiStateTests',
               'NetworkWorldContractTests', 'GameplayHudBindingTests', 'GameplayServiceTests', 'GameplayMailSocialTests', 'GameplayTravelTests', 'SliderTests', 'GameplayVehicleTests', 'GameplayInstanceAcceptanceTests', 'RecallPointContentTests', 'MissionFailureContractTests', 'NpcSkillProtocolTests']
    filters += ['WeaponCombatProtocolTests', 'BuffEffectsTests', 'CombatBuffContentTests']
    if args.stage == 'presentation-tests':
        filters = ['AvatarMaterialTests', 'PlayerAnimatorTests', 'NetworkWorldPresentationTests',
                   'PresentationTests', 'CharacterCreationViewTests', 'GameplayNanoHudTests',
                   'NativeUiViewTests', 'GameplayUiViewTests', 'FrontendLayoutTests']
    elif args.stage == 'tests':
        filters += ['AvatarMaterialTests', 'PlayerAnimatorTests', 'NetworkWorldPresentationTests',
                    'PresentationTests', 'CharacterCreationViewTests', 'GameplayNanoHudTests',
                    'NativeUiViewTests', 'GameplayUiViewTests', 'FrontendLayoutTests', 'TutorialEffectsTests']
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = subprocess.SW_HIDE
    command = [str(UNITY), '-batchmode', '-projectPath', str(ROOT)]
    if args.stage in ('tests', 'presentation-tests'):
        command += ['-runTests', '-testPlatform', 'EditMode', '-testFilter', ';'.join('FFOne.Tests.' + f for f in filters),
                    '-testResults', str(args.test_results.resolve() if args.test_results else ROOT / ('Logs/LiveGameplayPresentationTests.xml' if args.stage == 'presentation-tests' else 'Logs/LiveGameplayEditorTests.xml'))]
    else:
        if args.stage == 'buff-content':
            method = 'FFOne.Editor.CombatBuffContentPublication.Publish'
        elif args.stage == 'instance-viewpoints':
            method = 'FFOne.Editor.InstanceSceneViewpoints.Export'
        elif args.stage == 'route-data':
            method = 'FFOne.Editor.InstanceRoutesPublication.Publish'
        else:
            method = 'FFOne.Editor.NativeUiValidationBuild.' + {'prepare': 'PrepareTravelContent', 'content-build': 'BuildWithUpdatedContent', 'content-only': 'BuildContent', 'build': 'BuildWithCurrentContent'}[args.stage]
        command += ['-executeMethod', method, '-quit']
    log = 'LiveGameplayPresentationTests' if args.stage == 'presentation-tests' else 'LiveGameplayEditorTests' if args.stage == 'tests' else 'LiveGameplay' + args.stage.replace('-', '').title()
    command += ['-logFile', str(ROOT / ('Logs/' + log + '.log'))]
    result = subprocess.run(command, cwd=ROOT, startupinfo=startup,
                            creationflags=subprocess.CREATE_NO_WINDOW,
                            timeout=14400 if args.stage in ('content-build', 'content-only') else 1800, check=False)
    print('Unity gameplay ' + args.stage + ' exit:', result.returncode, flush=True)
    raise SystemExit(result.returncode)
