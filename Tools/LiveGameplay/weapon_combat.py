"""Disposable weapon-family/GM fixtures; no production data or privileges change."""
WEAPONS = (706, 621, 612, 625, 781, 22)


def seed(database):
    from pathlib import Path
    import json
    import sqlite3
    database = Path(database).resolve()
    target = Path(__file__).resolve().parents[3] / 'RustyFusion' / 'target'
    if database.parent.parent != target.resolve() or not database.parent.name.startswith('unity-weapons-'):
        raise ValueError('Weapon fixture seeding requires an owned RustyFusion/target/unity-weapons-* database.')
    xdt = json.loads((database.parent / 'tabledata/xdt.json').read_text('utf-8'))
    max_hp = next(row['m_iMaxHP'] for row in xdt['m_pAvatarTable']['m_pAvatarGrowData'] if row['m_iLevel'] == 6)
    with sqlite3.connect(database, timeout=20) as db:
        rows = db.execute("SELECT PlayerID,FirstName FROM Players WHERE FirstName IN ('Weapa','Weapb','Weapgm') ORDER BY FirstName").fetchall()
        if len(rows) != 3:
            raise AssertionError('Expected three disposable weapon fixture players')
        accounts = db.execute("SELECT AccountLevel FROM Accounts WHERE Login IN ('unityweapa','unityweapb')").fetchall()
        if len(accounts) != 2 or any(level != 99 for level, in accounts):
            raise AssertionError('Weapon fixtures must keep normal account privilege 99')
        gm = db.execute("SELECT AccountID,AccountLevel FROM Accounts WHERE Login='unityweapgm'").fetchall()
        if len(gm) != 1 or gm[0][1] not in (30, 99):
            raise AssertionError('Expected one dedicated disposable GM fixture')
        db.execute('UPDATE Accounts SET AccountLevel=30 WHERE AccountID=?', (gm[0][0],))
        for i, (uid, _) in enumerate(rows):
            # Level 6 is the original minimum for accepted grenade item 22.
            db.execute('UPDATE Players SET TutorialFlag=1,Level=6,XCoordinate=?,YCoordinate=187177,ZCoordinate=-5500,HP=?,BatteryW=1000 WHERE PlayerID=?', (632032 + i * 100, max_hp, uid))
            db.execute('DELETE FROM Inventory WHERE PlayerID=? AND (Slot=0 OR Slot BETWEEN 29 AND 34)', (uid,))
            for slot, item in enumerate(WEAPONS, 20):
                db.execute('INSERT INTO Inventory(PlayerID,Slot,ID,Type,Opt,TimeLimit) VALUES(?,?,?,0,1,0) ON CONFLICT(PlayerID,Slot) DO UPDATE SET ID=excluded.ID,Type=0,Opt=1,TimeLimit=0', (uid, 9 + slot, item))
        return {login: level for login, level in db.execute("SELECT Login,AccountLevel FROM Accounts WHERE Login IN ('unityweapa','unityweapb','unityweapgm')")}


def add_mobs(document):
    """Same accepted mob type, six identities so each family gets a living target."""
    start = max(map(int, document['mobs'])) + 1
    for i in range(6):
        document['mobs'][str(start + i)] = {'iNPCType': 59, 'iX': 637032 + i * 2000, 'iY': 187177, 'iZ': -5500, 'iAngle': 0, 'iMapNum': 0}
