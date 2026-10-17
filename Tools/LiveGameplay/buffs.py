"""Disposable buff acceptance fixtures. XDT identities/values remain unmodified."""
import json
import sqlite3

PASSIVES = [(2, 4, 1), (4, 11, 13), (5, 14, 7), (6, 16, 3), (8, 22, 5),
            (9, 26, 14), (10, 29, 4), (11, 31, 18), (18, 54, 16), (19, 57, 6), (33, 99, 15)]
ACTIVE = [(1, 1, 10), (12, 70, 11), (14, 41, 8), (24, 34, 19), (17, 51, 0)]
ITEMS = [122, 124, 125, 126, 127, 137, 138, 146, 147, 148, 150, 151, 152, 123, 145, 149, 119, 130, 134, 135, 136, 167, 168]
EFFECTS = [366, 368, 370, 376, 411, 412, 429, 431, 434, 435, 436, 441, 447, 714, 805]


def plan(tables, destination):
    xdt = json.loads((tables / 'xdt.json').read_text('utf-8'))
    skills = {r['m_iSkillNumber']: r for r in xdt['m_pSkillTable']['m_pSkillData']}
    result = dict(passives=[dict(nano=n, skill=s, buff=b, value=skills[s]['m_iValueA'][0], boostedValue=skills[s]['m_iValueA'][3],
                                group=skills[s]['m_iTargetType'] == 3) for n, s, b in PASSIVES],
                  active=[dict(nano=n, skill=s, buff=b) for n, s, b in ACTIVE],
                  items=[dict(slot=i, item=n, skill=next(r['m_iLinkSkill'] for r in xdt['m_pGeneralItemTable']['m_pItemData']
                                                       if r['m_iItemNumber'] == n), supported=i < 16) for i, n in enumerate(ITEMS)],
                  effects=EFFECTS, unresolvedSkills=[5, 69, 87, 148, 150, 189],
                  unresolvedConsumers=[130, 134, 167], unsupportedConsumers=[135, 136, 168],
                  originalBehaviorScope='Implemented authored semantics; unresolved Dash/healing/capacity/token/Nanocom behavior is explicitly excluded.')
    destination.write_text(json.dumps(result, indent=2), encoding='utf-8')
    return result


def seed(database):
    nanos = {n: s for n, s, _ in PASSIVES + ACTIVE}
    nanos.update({3: 8, 7: 20})
    with sqlite3.connect(database, timeout=20) as db:
        rows = db.execute("SELECT PlayerID,FirstName FROM Players WHERE FirstName IN ('Buffa','Buffb','Buffd') ORDER BY FirstName").fetchall()
        accounts = db.execute("SELECT AccountLevel FROM Accounts WHERE Login IN ('unitybuffa','unitybuffb','unitybuffd')").fetchall()
        if len(rows) != 3 or len(accounts) != 3 or any(level != 99 for level, in accounts):
            raise AssertionError('Buff acceptance requires three normal isolated accounts')
        for uid, name in rows:
            db.execute('UPDATE Players SET TutorialFlag=1,Level=36,XCoordinate=?,YCoordinate=187177,ZCoordinate=-5500,HP=?,BatteryW=1000,BatteryN=1000,Nano1=1,Nano2=8,Nano3=3 WHERE PlayerID=?',
                       (632032, 2000 if name != 'Buffd' else 500, uid))
            if name == 'Buffd': db.execute('UPDATE Players SET Level=1 WHERE PlayerID=?', (uid,))
            db.execute('DELETE FROM Nanos WHERE PlayerID=?', (uid,))
            for nano, skill in nanos.items():
                db.execute('INSERT INTO Nanos(PlayerID,ID,Skill,Stamina) VALUES(?,?,?,150)', (uid, nano, skill))
            for slot, item in enumerate(ITEMS):
                db.execute('INSERT INTO Inventory(PlayerID,Slot,ID,Type,Opt,TimeLimit) VALUES(?,?,?,7,3,0) ON CONFLICT(PlayerID,Slot) DO UPDATE SET ID=excluded.ID,Type=7,Opt=3,TimeLimit=0', (uid, 9 + slot, item))


def place_mobs(document):
    # Preserve all authored production placements. These additional source type59
    # and type1 actors have distinct identities and original server stats/AI.
    start = max(map(int, document['mobs'])) + 1
    for i in range(5):
        document['mobs'][str(start + i)] = {'iNPCType': 59, 'iX': 632932 + i * 100,
            'iY': 187177, 'iZ': -5500, 'iAngle': 0, 'iMapNum': 0}
    document['mobs'][str(start + 5)] = {'iNPCType': 3337, 'iX': 635132,
        'iY': 187177, 'iZ': -5500, 'iAngle': 0, 'iMapNum': 0}
