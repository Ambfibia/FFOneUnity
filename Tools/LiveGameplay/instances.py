"""Plan instance acceptance from the production RustyFusion route tables.

The plan deliberately keeps map identity, shared geometry, missing authored routes,
and executable tests separate. Only seed() writes, and only to the runner's unique
ignored RustyFusion/target/unity-gameplay-* database.
"""
from collections import Counter, defaultdict
from pathlib import Path
import hashlib
import heapq
import json
import sqlite3
import instance_missions
from copy import deepcopy


def _read(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def plan(source_tables, destination, selected_maps=None):
    source_tables, destination = Path(source_tables), Path(destination)
    xdt = _read(source_tables / 'xdt.json')
    table = xdt['m_pInstanceTable']
    map_cells = {r['m_iInstanceNameID']: (r['m_iZoneX'], r['m_iZoneY']) for r in table['m_pInstanceData']}
    def contains(map_id, x, y):
        cell = map_cells.get(map_id)
        return map_id == 0 or cell is not None and x // 51200 == cell[0] and y // 51200 == cell[1]
    tasks = {r['m_iHTaskID']: r for r in xdt['m_pMissionTable']['m_pMissionData']}
    growth = {tasks[r['m_iNanoQuestTaskID']]['m_iHMissionID']: r['m_iLevel']
              for r in xdt['m_pAvatarTable']['m_pAvatarGrowData']
              if r['m_iNanoQuestTaskID'] in tasks}
    placements = defaultdict(list)
    for key, npc in _read(source_tables / 'NPCs.json')['NPCs'].items():
        if npc.get('bHidden', False) or not npc.get('bInteractable', True):
            continue
        placements[npc['iNPCType']].append((int(key), npc))
    edges = defaultdict(list)
    invalid_destinations = defaultdict(list)
    for warp in table['m_pWarpData']:
        if warp['m_iWarpNumber'] <= 0:
            continue
        if not contains(warp['m_iToMapNum'], warp['m_iToX'], warp['m_iToY']):
            invalid_destinations[warp['m_iToMapNum']].append(warp['m_iWarpNumber'])
            continue
        for key, npc in sorted(placements[warp['m_iNpcNumber']]):
            edge = dict(fromMap=npc.get('iMapNum', 0), map=warp['m_iToMapNum'],
                        warp=warp['m_iWarpNumber'], npcType=warp['m_iNpcNumber'],
                        # Match Rust deserialize_spawn_integer (truncate toward zero).
                        placement=key, npcX=int(npc['iX']), npcY=int(npc['iY']), npcZ=int(npc['iZ']),
                        x=warp['m_iToX'], y=warp['m_iToY'], z=warp['m_iToZ'],
                        instance=bool(warp['m_iIsInstance']), task=warp['m_iLimit_TaskID'],
                        minLevel=warp['m_iLimit_Level'], cost=warp['m_iCost'],
                        completedMission=warp.get('m_iLimit_CompletedMissionID', 0),
                        requiredType=warp['m_iLimit_ItemType'], requiredItem=warp['m_iLimit_ItemID'],
                        requiredCount=warp.get('m_iLimit_ItemCount', 0),
                        consumedType=warp['m_iLimit_UseItemType'], consumedItem=warp['m_iLimit_UseItemID'])
            edges[edge['fromMap']].append(edge)

    def routes(start):
        paths, scores, queue = {start: []}, {start: 0}, [(0, start)]
        while queue:
            score, current = heapq.heappop(queue)
            if score != scores[current]:
                continue
            for edge in edges[current]:
                # Favor ordinary authored portals over alternate consumable doors.
                weight = 100 + 10 * bool(edge['task']) + 20 * bool(edge['requiredItem']) + 30 * bool(edge['consumedItem'])
                new_score = score + weight
                target = edge['map']
                if new_score < scores.get(target, float('inf')):
                    scores[target] = new_score
                    paths[target] = paths[current] + [dict(edge)]
                    heapq.heappush(queue, (new_score, target))
        return paths

    from_world = routes(0)
    cases = []
    for row in sorted(table['m_pInstanceData'], key=lambda r: r['m_iInstanceNameID']):
        map_id = row['m_iInstanceNameID']
        if map_id == 0 or selected_maps is not None and map_id not in selected_maps:
            continue
        case = dict(map=map_id, cell=f"map_{row['m_iZoneX']:02d}_{row['m_iZoneY']:02d}",
                    account=f'unityinstance{map_id:03d}', name='Inst' + chr(65 + map_id // 26) + chr(65 + map_id % 26),
                    disposition='eligible', reason=None, entry=deepcopy(from_world.get(map_id, [])),
                    exit=deepcopy(routes(map_id).get(0, [])), level=36, tasks=[], items=[], completedMissions=[])
        case['invalidDestinationWarpIds'] = invalid_destinations[map_id]
        case['invalidDestinationWarps'] = [dict(warp=w['m_iWarpNumber'], npcType=w['m_iNpcNumber'],
                                              x=w['m_iToX'], y=w['m_iToY'], z=w['m_iToZ'],
                                              declaredCell=case['cell'])
                                          for w in table['m_pWarpData'] if w['m_iWarpNumber'] in invalid_destinations[map_id]]
        anchors = [w for w in table['m_pWarpData'] if w['m_iToMapNum'] == map_id and contains(map_id, w['m_iToX'], w['m_iToY'])]
        if not anchors:
            anchors = [w for w in table['m_pWarpData'] if w['m_iToMapNum'] > 0 and map_cells.get(w['m_iToMapNum']) == map_cells[map_id]
                       and contains(map_id, w['m_iToX'], w['m_iToY'])]
        case['scenePoint'] = dict(x=anchors[0]['m_iToX'], y=anchors[0]['m_iToY'], z=anchors[0]['m_iToZ']) if anchors else dict(
            x=row['m_iZoneX'] * 51200 + 25600, y=row['m_iZoneY'] * 51200 + 25600, z=0)
        if not case['entry']:
            incoming = [w for w in table['m_pWarpData'] if w['m_iToMapNum'] == map_id]
            reason = 'No placed NPC route from the overworld in original server tables.'
            if incoming and all(w['m_iWarpNumber'] in invalid_destinations[map_id] for w in incoming):
                reason = 'Original warp destination is outside the declared map cell; the production coverage guard correctly blocks it.'
            case.update(disposition='blocked', reason=reason,
                        incomingWarpIds=[w['m_iWarpNumber'] for w in incoming],
                        unplacedNpcTypes=sorted({w['m_iNpcNumber'] for w in incoming if not placements[w['m_iNpcNumber']]}))
            cases.append(case)
            continue
        all_edges = case['entry'] + case['exit']
        required_tasks = sorted({e['task'] for e in all_edges if e['task']})
        case['tasks'] = required_tasks
        sequential = instance_missions.augment(case, tasks, placements)
        case['taskPrerequisites'] = [dict(task=t, mission=tasks[t]['m_iHMissionID'],
                                          missionType=tasks[t]['m_iHMissionType']) for t in required_tasks if t in tasks]
        missions = [tasks[t]['m_iHMissionID'] for t in required_tasks if t in tasks]
        nano_levels = {growth[m] for m in missions if m in growth}
        normal = {m for m in missions if next(t for t in tasks.values() if t['m_iHMissionID'] == m)['m_iHMissionType'] == 3}
        guides = {m for m in missions if next(t for t in tasks.values() if t['m_iHMissionID'] == m)['m_iHMissionType'] == 1}
        nanos = {m for m in missions if next(t for t in tasks.values() if t['m_iHMissionID'] == m)['m_iHMissionType'] == 2}
        blockers = []
        if any(t not in tasks for t in required_tasks):
            blockers.append('Warp prerequisite references a missing task definition.')
        if len(set(missions)) != len(missions) and not sequential:
            blockers.append('Route requires multiple sequential stages of one mission; progression must be exercised separately.')
        if len(nanos) > 1 or len(normal) > 4 or len(guides) > 1:
            blockers.append('Route prerequisites do not fit a legitimate simultaneous mission journal.')
        if nano_levels:
            case['level'] = next(iter(nano_levels))
        min_level = max([e['minLevel'] for e in all_edges] + [tasks[t].get('m_iCTRReqLvMin', 0) for t in required_tasks if t in tasks] + [1])
        if case['level'] < min_level:
            blockers.append('Growth Nano stage and portal level prerequisites conflict.')
        case['completedMissions'] = sorted({e['completedMission'] for e in all_edges if e['completedMission']})
        inventory = {}
        for edge_index, edge in enumerate(all_edges):
            edge['requiredSlot'], edge['consumedSlot'] = 0, -1
            for kind in ('required', 'consumed'):
                item = edge[kind + 'Item']
                if not item:
                    continue
                item_type = edge[kind + 'Type']
                key = (item_type, item)
                if key not in inventory:
                    inventory[key] = dict(type=item_type, id=item, slot=len(inventory), quantity=0)
                entry = inventory[key]
                # The protocol harness re-enters once after validating the saved
                # fallback; the native run receives a fresh call to seed().
                entry['quantity'] = entry['quantity'] + (2 if edge_index < len(case['entry']) else 1) if kind == 'consumed' else max(entry['quantity'], edge['requiredCount'] or 1)
                edge[kind + 'Slot'] = entry['slot']
        case['items'] = list(inventory.values())
        if case.get('combatWeapon'):
            case['items'].append(case['combatWeapon'])
        case['reconnectPosition'] = {k: case['entry'][0]['npc' + k.upper()] for k in ('x', 'y', 'z')}
        case['exitMode'] = 'authored-npc-portal' if case['exit'] else 'saved-reconnect-to-pre-warp-overworld'
        if blockers:
            case.update(disposition='blocked', reason=' '.join(blockers))
        cases.append(case)
    result = dict(schema=1, source='Production RustyFusion XDT and persistent NPC placements, including documented instance-route repairs; ordinary account and access gates.',
                  sourceHashes={name: hashlib.sha256((source_tables / name).read_bytes()).hexdigest() for name in ('xdt.json', 'NPCs.json', 'mobs.json')},
                  maps=len(cases), geometryCells=len({c['cell'] for c in cases}),
                  unresolvedMapReferences=[dict(map=w['m_iToMapNum'], warp=w['m_iWarpNumber'], npcType=w['m_iNpcNumber'],
                                                x=w['m_iToX'], y=w['m_iToY'], z=w['m_iToZ'],
                                                reason='Warp references a map ID absent from original instance definitions.')
                                           for w in table['m_pWarpData'] if w['m_iToMapNum'] not in map_cells],
                  dispositions=dict(Counter(c['disposition'] for c in cases)), cases=cases)
    destination.write_text(json.dumps(result, indent=2), encoding='utf-8')
    return result


def seed(database, plan_path):
    """Seed legitimate saved prerequisites after normal account/character creation."""
    database = Path(database).resolve()
    if not any(p.name.startswith('unity-gameplay-') and p.parent.name == 'target' for p in database.parents):
        raise ValueError('Instance fixtures require the isolated unity-gameplay-* database.')
    data = _read(plan_path)
    with sqlite3.connect(database, timeout=20) as db:
        for case in data['cases']:
            if case['disposition'] != 'eligible':
                continue
            rows = db.execute('SELECT p.PlayerID,a.AccountLevel FROM Players p JOIN Accounts a ON a.AccountID=p.AccountID WHERE a.Login=?', (case['account'],)).fetchall()
            if len(rows) != 1 or rows[0][1] != 99:
                raise AssertionError('Instance acceptance must use one normal character: ' + case['account'])
            uid = rows[0][0]
            point = case['reconnectPosition']
            db.execute('UPDATE Players SET TutorialFlag=1,Level=?,HP=10000,Taros=1000000,XCoordinate=?,YCoordinate=?,ZCoordinate=? WHERE PlayerID=?',
                       (case['level'], point['x'], point['y'], point['z'], uid))
            db.execute('DELETE FROM RunningQuests WHERE PlayerID=?', (uid,))
            for task in case['tasks']:
                remaining = 0 if case.get('sequentialMission') else 999
                db.execute('INSERT INTO RunningQuests VALUES(?,?,?,?,?)', (uid, task, remaining, remaining, remaining))
            for item in case['items']:
                if item['type'] == 8:
                    db.execute('INSERT OR REPLACE INTO QuestItems VALUES(?,?,?,?)', (uid, item['slot'], item['id'], item['quantity']))
                else:
                    db.execute('INSERT OR REPLACE INTO Inventory VALUES(?,?,?,?,?,0)', (uid, 9 + item['slot'], item['id'], item['type'], item['quantity']))
            if case['completedMissions']:
                flags = bytearray(db.execute('SELECT Quests FROM Players WHERE PlayerID=?', (uid,)).fetchone()[0])
                for mission in case['completedMissions']:
                    index = mission - 1
                    flags[index // 8] |= 1 << (index % 8)
                db.execute('UPDATE Players SET Quests=? WHERE PlayerID=?', (bytes(flags), uid))
    return data


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_tables', type=Path)
    parser.add_argument('destination', type=Path)
    parser.add_argument('--maps', help='Optional comma-separated map IDs for a focused rerun.')
    args = parser.parse_args()
    result = plan(args.source_tables, args.destination,
                  {int(i) for i in args.maps.split(',')} if args.maps else None)
    print(json.dumps({k: v for k, v in result.items() if k not in ('cases', 'sourceHashes')}))
