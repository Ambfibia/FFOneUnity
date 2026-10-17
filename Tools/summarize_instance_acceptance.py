"""Combine independent instance partitions, requiring exact original-map coverage."""
import argparse
from collections import Counter
import hashlib
import json
import re
from pathlib import Path
from datetime import datetime, timezone
from run_instance_validation import source_hashes

ROOT = Path(__file__).resolve().parents[1]


def require_complete_partition(report):
    """A successful row list cannot erase a failure in the owning runner."""
    if report.get('error') or not report.get('passed') or not report.get('serverStopped'):
        raise ValueError('A partition runner failed or did not confirm shutdown: ' + str(report.get('error', report.get('directory'))))
    if not report.get('instanceNetwork', {}).get('passed') or not report.get('instancePlayer', {}).get('passed'):
        raise ValueError('Both partition network and native reports must pass.')
    if report.get('contentErrors') or report.get('contentComplete') is not True:
        raise ValueError('A partition has incomplete native content.')


def summarize(paths, destination, recoveries=()):
    base_count = len(paths)
    recoveries = tuple(recoveries)
    paths = list(paths) + list(recoveries)
    reports = [json.loads(Path(path).read_text('utf-8')) for path in paths]
    tables = ROOT.parent / 'RustyFusion/tabledata'
    xdt = json.loads((tables / 'xdt.json').read_text('utf-8'))
    expected = {row['m_iInstanceNameID'] for row in xdt['m_pInstanceTable']['m_pInstanceData']} - {0}
    expected_cells = {row['m_iInstanceNameID']: f"map_{row['m_iZoneX']:02d}_{row['m_iZoneY']:02d}"
                      for row in xdt['m_pInstanceTable']['m_pInstanceData']}
    table_hashes = {name: hashlib.sha256((tables / name).read_bytes()).hexdigest() for name in ('xdt.json', 'NPCs.json', 'mobs.json')}
    network, player, sources, captures, map_captures, map_evidence = {}, {}, {}, {}, {}, {}
    recovered, completed_evidence = [], []
    identities = set()
    content_identities = set()
    binaries = {key: set() for key in ('serverBinarySha256', 'harnessAssemblySha256', 'playerExecutableSha256')}
    for report_index, report in enumerate(reports):
        recovery = report_index >= base_count
        # Historical focused recoveries retain their explicit failed evidence.
        # A fresh full rerun requires every owning partition to finish cleanly.
        if not recoveries:
            require_complete_partition(report)
        if report['instancePlan']['sourceHashes'] != table_hashes:
            raise ValueError('A partition uses different original source tables.')
        before, after = report.get('playerAssemblySha256'), report.get('playerAssemblySha256After')
        # Older failed runs retained their before hash but exited before writing
        # the after hash/manifest. Complete that observation from the unchanged
        # build and sources, retaining the time and the original failed report.
        if before and (not after or 'sourceHashes' not in report):
            executable = Path(report['playerExecutable'])
            assembly = executable.parent / (executable.stem + '_Data') / 'Managed/FFOne.Runtime.dll'
            current = hashlib.sha256(assembly.read_bytes()).hexdigest()
            manifest = source_hashes()
            started = (Path(report['directory']) / 'instance-plan.json').stat().st_mtime
            if current != before or any((ROOT.parent / path).stat().st_mtime > started for path in manifest):
                raise ValueError('Cannot complete failed-run evidence after a build or source change.')
            report['playerAssemblySha256After'] = after = current
            report['sourceHashes'] = manifest
            log = (Path(report['directory']) / 'instance-player.log').read_text('utf-8', errors='replace')
            report['playerExceptions'] = re.findall(r'^\w+(?:\.\w+)*Exception:.*$', log, re.MULTILINE)
            completed_evidence.append(dict(report=str(Path(paths[report_index]).resolve()),
                observedAt=datetime.now(timezone.utc).isoformat(), assemblySha256=current,
                sourceModificationTimesPrecedePlan=True))
        if not before or before != after:
            raise ValueError('A partition lacks a stable player assembly identity.')
        identities.add(before)
        content_before, content_after = report.get('playerContentCatalogHashes'), report.get('playerContentCatalogHashesAfter')
        if not content_before or content_before != content_after:
            raise ValueError('A partition lacks stable Addressables catalog evidence.')
        content_identities.add(json.dumps(content_before, sort_keys=True))
        for key, values in binaries.items():
            values.add(report[key])
        for section, rows in (('instanceNetwork', network), ('instancePlayer', player)):
            for row in report[section]['results']:
                map_id = row.get('map')
                if map_id is None:
                    raise ValueError(f'Duplicate or unidentifiable {section} row: {map_id}.')
                if row.get('cell') != expected_cells.get(map_id):
                    raise ValueError(f'Partition cell disagrees with original map {map_id}.')
                if map_id in rows:
                    if not recovery:
                        raise ValueError(f'Duplicate {section} map: {map_id}.')
                    if section == 'instanceNetwork' and rows[map_id]['status'] == row['status']:
                        continue
                    if rows[map_id]['status'] != 'failed' or row['status'] != 'passed':
                        raise ValueError('Recovery may replace only a failed native row with a passing row.')
                    recovered.append(dict(map=map_id, previousFailure=rows[map_id],
                                          recoveryReport=str(Path(paths[report_index]).resolve())))
                rows[map_id] = row
                if section == 'instancePlayer' and row['status'] == 'passed':
                    name = f"instance-{map_id}" + ('-scene-only' if row['acceptance'].startswith('scene-load-') else '') + '.png'
                    capture = Path(report['playerProfile']) / name
                    if not capture.is_file():
                        raise ValueError('Accepted map lacks its rendered capture: ' + str(capture))
                    map_captures[str(map_id)] = str(capture)
                    plan = json.loads((Path(report['directory']) / 'instance-plan.json').read_text('utf-8'))
                    case = next(c for c in plan['cases'] if c['map'] == map_id)
                    map_evidence[str(map_id)] = dict(report=str(Path(paths[report_index]).resolve()),
                        playerPlanSha256=report['playerPlanSha256'],
                        combatFixture=report.get('combatFixture', {'mode': 'original-hostile-spawns'}),
                        scenePoint=case['scenePoint'], sceneViewpoint=case.get('sceneViewpoint'))
        for path, digest in report['sourceHashes'].items():
            if path in sources and sources[path] != digest:
                raise ValueError('Partition sources changed: ' + path)
            sources[path] = digest
        for capture in sorted(Path(report['playerProfile']).glob('instance-*.png')):
            captures[str(capture)] = hashlib.sha256(capture.read_bytes()).hexdigest()
    if len(identities) != 1:
        raise ValueError('Partitions must use the same player assembly.')
    if len(content_identities) != 1:
        raise ValueError('Partitions must use the same built Addressables catalog.')
    if any(len(values) != 1 for values in binaries.values()):
        raise ValueError('Partitions must use identical server, harness and player executables.')
    if set(network) != expected or set(player) != expected:
        raise ValueError('Partition rows do not cover every original instance map exactly once.')
    for map_id in expected:
        eligible = network[map_id]['status'] == 'passed'
        if network[map_id]['status'] not in ('passed', 'blocked'):
            raise ValueError('Network route failed: ' + str(map_id))
        native = player[map_id]
        if native['status'] != 'passed':
            raise ValueError('Native scene failed: ' + str(map_id))
        if eligible != (native['acceptance'] == 'native-portals-loading-scene-colliders-exit-reconnect'):
            raise ValueError('Network and native route claims disagree: ' + str(map_id))
        if not eligible and native.get('portalRoute') != 'unverified':
            raise ValueError('A scene-only row must preserve its unverified portal boundary.')
    eligible = sum(row['status'] == 'passed' for row in network.values())
    cells = {row['cell'] for row in player.values()}
    errors = {str(map_id): row['contentErrors'] for map_id, row in player.items() if row.get('contentErrors')}
    result = dict(
        passed=all(r['instanceNetwork']['passed'] and r['serverStopped'] for r in reports),
        serverStopped=all(r['serverStopped'] for r in reports),
        partitionReports=[str(Path(path).resolve()) for path in paths],
        partitionReportHashes={str(Path(path).resolve()): hashlib.sha256(Path(path).read_bytes()).hexdigest() for path in paths},
        directories=[r['directory'] for r in reports],
        playerAssemblySha256=next(iter(identities)),
        playerContentCatalogHashes=json.loads(next(iter(content_identities))),
        **{key: next(iter(values)) for key, values in binaries.items()},
        sourceHashes=sources, sourceTableHashes=table_hashes,
        instancePlan=dict(maps=len(expected), geometryCells=len(cells),
                          dispositions=dict(eligible=eligible, blocked=len(expected) - eligible),
                          unresolvedMapReferences=reports[0]['instancePlan']['unresolvedMapReferences']),
        instanceNetwork=dict(passed=True, coverageComplete=eligible == len(expected), maps=len(expected),
                             eligible=eligible, blocked=len(expected) - eligible,
                             results=[network[m] for m in sorted(expected)]),
        instancePlayer=dict(passed=True, maps=len(expected), mapsReported=len(player),
                            routeCoverageComplete=eligible == len(expected),
                            acceptanceKinds=dict(Counter(r['acceptance'] for r in player.values())),
                            results=[player[m] for m in sorted(expected)]),
        contentComplete=not errors, contentErrors=errors, captureHashes=captures,
        mapCaptures=map_captures, mapEvidence=map_evidence,
        supersededFailures=recovered, postRunEvidenceCompletions=completed_evidence,
        playerExceptions=[e for r in reports for e in r.get('playerExceptions', [])],
        serverScriptErrors=[e for r in reports for e in r.get('serverScriptErrors', [])],
        serverPanics=[e for r in reports for e in r.get('serverPanics', [])])
    result['passed'] &= not errors and not any(result[k] for k in ('playerExceptions', 'serverScriptErrors', 'serverPanics'))
    Path(destination).write_text(json.dumps(result, indent=2), encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reports', nargs='+', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'Logs/InstanceValidationResult.json')
    parser.add_argument('--recoveries', nargs='*', type=Path, default=[], help='Passing focused reruns which explicitly replace failed native rows.')
    args = parser.parse_args()
    result = summarize(args.reports, args.output, args.recoveries)
    print(json.dumps({key: result[key] for key in ('passed', 'serverStopped', 'contentComplete', 'instancePlan')}))
    raise SystemExit(0 if result['passed'] else 1)
