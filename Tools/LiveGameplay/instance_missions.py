"""Authored sequential mission actions for the four doors of Nano mission 842.

Only its saved first portal stage is seeded. Later stages are reached by normal
start/end packets, actual combat, and the server's map-departure failure branch.
"""


def augment(case, tasks, placements):
    if case['map'] not in (146, 147, 148):
        return False
    route = case['entry'] + case['exit']
    if [edge['warp'] for edge in route] != [311, 312, 313, 314, 315]:
        raise ValueError('Mission 842 must follow its complete authored door chain.')

    def at(kind, task, npc_type=0, map_id=0, **extra):
        result = dict(kind=kind, task=task, **extra)
        if npc_type:
            matches = [npc for _, npc in placements[npc_type] if npc.get('iMapNum', 0) == map_id]
            if len(matches) != 1:
                raise ValueError(f'Mission {task} needs exactly one NPC {npc_type} on map {map_id}.')
            npc = matches[0]
            result.update(npcType=npc_type, npcX=int(npc['iX']), npcY=int(npc['iY']), npcZ=int(npc['iZ']))
        return result

    def start(task):
        return at('start', task)

    def complete(task, npc_type, map_id):
        return at('complete', task, npc_type, map_id)

    actions = {
        311: [dict(kind='equip', task=5222, item=65, slot=49)],
        312: [complete(5222, 3321, 145), start(5223), complete(5223, 3322, 145),
              start(5224), complete(5224, 3323, 145), start(5225), complete(5225, 3324, 145), start(5226)],
        313: [complete(5227, 3328, 146), start(5228)],
        314: [complete(5233, 3330, 147), at('start', 5229, 3338, 147, escortType=3338),
              dict(kind='defeat', task=5229, npcType=3337, npcX=628329, npcY=787633, npcZ=2314),
              at('complete', 5229), start(5230)],
    }
    failures = {312: (5226, 5227), 313: (5228, 5233), 314: (5230, 5231), 315: (5231, 5222)}
    for edge in route:
        edge['beforeMission'] = actions.get(edge['warp'], [])
        old, outgoing = failures.get(edge['warp'], (0, 0))
        edge['afterMission'] = [at('failure', old, nextTask=outgoing), start(outgoing)] if old else []
        if old and tasks[old]['m_iFOutgoingTask'] != outgoing:
            raise ValueError(f'The authored failure edge of {old} changed.')
    case['sequentialMission'] = True
    # A level-36 melee weapon from the accepted XDT. The harness equips it
    # through the ordinary inventory packet before combat; no stat overrides.
    case['combatWeapon'] = dict(type=0, id=65, slot=49, quantity=1)
    case['sequentialTaskPrerequisites'] = list(case['tasks'])
    case['tasks'] = [5222]
    case['missionEvidence'] = 'Original 842 task chain and InstanceLeft=12 failure edges; normal packets and real escort/defeat objective; only saved task 5222 seeded.'
    return True
