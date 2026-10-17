"""Inventory every authored task; static rows are not recorded as runtime passes."""
from collections import Counter, defaultdict
from pathlib import Path
import argparse
import hashlib
import json


def audit(tables, destination):
    tables = Path(tables)
    read = lambda name: json.loads((tables / name).read_text(encoding='utf-8'))
    xdt = read('xdt.json')
    rows = [row for row in xdt['m_pMissionTable']['m_pMissionData'] if row['m_iHTaskID'] > 0]
    tasks = {row['m_iHTaskID']: row for row in rows}
    npcs = {row['m_iNpcNumber']: row for row in xdt['m_pNpcTable']['m_pNpcData']}
    strings = xdt['m_pMissionTable']['m_pMissionStringData']
    placements = defaultdict(set)
    for row in read('NPCs.json')['NPCs'].values():
        placements[row['iNPCType']].add(row.get('iMapNum', 0))
    mobs = read('mobs.json')
    for row in list(mobs['mobs'].values()) + list(mobs['groups'].values()):
        placements[row['iNPCType']].add(row.get('iMapNum', 0))
        for follower in row.get('aFollowers', []):
            placements[follower['iNPCType']].add(row.get('iMapNum', 0))
    transitions, dialogue_issues, task_rows = [], [], []
    phase_counts = Counter()
    for row in rows:
        task = row['m_iHTaskID']
        for field, kind in [('m_iSUOutgoingTask', 'success'), ('m_iFOutgoingTask', 'failure')]:
            target = row[field]
            if target > 0 and target not in tasks:
                transitions.append(dict(task=task, edge=kind, target=target, status='missing-source-task'))
            elif target > 0 and tasks[target]['m_iHMissionID'] != row['m_iHMissionID']:
                transitions.append(dict(task=task, edge=kind, target=target, status='cross-mission-source-edge'))
        dialogue = []
        for phase, message_id in [('ST', 'm_iSTMessageTextID'), ('SU', 'm_iSUMessagetextID'), ('F', 'm_iFMessageTextID')]:
            for channel, text_field, speaker_field in [('bubble', 'm_i' + phase + 'DialogBubble', 'm_i' + phase + 'DialogBubbleNPCID'),
                                                       ('nanocom', message_id, 'm_i' + phase + 'MessageSendNPC')]:
                text, speaker = row[text_field], row[speaker_field]
                admitted = text > 0 and speaker > 0 and (channel == 'bubble' or row['m_i' + phase + 'MessageType'] & 2 != 0)
                if not admitted:
                    continue
                phase_counts[phase + ':' + channel] += 1
                valid = text < len(strings) and len(strings[text].get('m_pstrNameString', '')) > 1 and speaker in npcs
                value = dict(phase=phase, channel=channel, string=text, npc=speaker, validSource=valid)
                dialogue.append(value)
                if not valid:
                    dialogue_issues.append(dict(task=task, **value))
        owner = row['m_iHNPCID']
        target = row['m_iHTerminatorNPCID']
        escort = row['m_iCSUDEFNPCID']
        policy = 'source-type-0-explicit-server-validated-completion' if row['m_iHTaskType'] == 0 else 'explicit-npc-interaction'
        if row['m_iHTaskType'] in (2, 6) or target == 0 and (any(row['m_iCSUNumToKill']) or any(row['m_iCSUItemNumNeeded'])):
            policy = 'automatic-reward-confirmation' if row['m_iSUReward'] > 0 else 'automatic-objective-request'
        task_rows.append(dict(task=task, mission=row['m_iHMissionID'], type=row['m_iHTaskType'], completionPolicy=policy,
            map=row['m_iRequireInstanceID'], level=row['m_iCTRReqLvMin'], giver=owner, terminator=target, escort=escort,
            sourceNpcTypesPresent=all(npc == 0 or npc in npcs for npc in (owner, target, escort)),
            placedMaps={str(npc): sorted(placements[npc]) for npc in (owner, target, escort) if npc},
            success=row['m_iSUOutgoingTask'], failure=row['m_iFOutgoingTask'], timer=row['m_iCSUCheckTimer'],
            reward=row['m_iSUReward'], dialogue=dialogue, runtimeAcceptance='not-inferred-from-static-audit'))
    asymmetric = [row['m_iHTaskID'] for row in rows if any(row['m_iFItemID']) and
                  dict((i, n) for i, n in zip(row['m_iFItemID'], row['m_iFItemNumNeeded']) if i) !=
                  dict((i, n) for i, n in zip(row['m_iSUItem'], row['m_iSUInstancename']) if i)]
    result = dict(schema=1, evidence='Production XDT and placements; static inventory only, not exhaustive runtime acceptance.',
        sourceHashes={name: hashlib.sha256((tables / name).read_bytes()).hexdigest() for name in ('xdt.json', 'NPCs.json', 'mobs.json')},
        tasks=len(rows), uniqueTasks=len(tasks), missions=len({row['m_iHMissionID'] for row in rows if row['m_iHMissionID'] > 0}),
        types=dict(Counter(row['m_iHTaskType'] for row in rows)), completionPolicies=dict(Counter(row['completionPolicy'] for row in task_rows)),
        checkTimed=sum(row['m_iCSUCheckTimer'] > 0 for row in rows), grantTimed=sum(row['m_iSTGrantTimer'] > 0 for row in rows),
        grantOnlyTimerTasks=[row['m_iHTaskID'] for row in rows if row['m_iSTGrantTimer'] > 0 and row['m_iCSUCheckTimer'] == 0],
        outgoingGiverEdges=[dict(task=row['m_iHTaskID'], edge=kind, target=row[field], giver=tasks[row[field]]['m_iHNPCID'])
            for row in rows for field, kind in [('m_iSUOutgoingTask', 'success'), ('m_iFOutgoingTask', 'failure')]
            if row[field] in tasks and tasks[row[field]]['m_iHNPCID'] > 0],
        escortRows=sum(row['m_iCSUDEFNPCID'] > 0 for row in rows), dialogueCounts=dict(phase_counts),
        failureItemDeltasDifferFromSuccess=asymmetric, transitionIssues=transitions, dialogueIssues=dialogue_issues,
        activeScriptRows=[row['m_iHTaskID'] for row in rows if row.get('m_pstrSTScript') not in ('', '0', None)],
        unsupportedTaskTypes=[row['m_iHTaskID'] for row in rows if row['m_iHTaskType'] not in range(1, 8)], cases=task_rows)
    destination = Path(destination); destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('tables', type=Path); parser.add_argument('destination', type=Path)
    args = parser.parse_args(); result = audit(args.tables, args.destination)
    print(json.dumps({key: value for key, value in result.items() if key not in ('cases', 'sourceHashes')}, ensure_ascii=True))
