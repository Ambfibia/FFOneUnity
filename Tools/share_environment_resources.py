"""Move published environment resources into a shared pool and redirect exact duplicates.

Run while Unity auto-refresh is suspended, then refresh and run EnvironmentPublicationUpgrade.
Only byte-equivalent serialized content (apart from the display name) is merged.
The operation journal is resumable; it contains paths/hashes, not copies of assets.
"""
from pathlib import Path
import collections
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[1]
POOL = 'Assets/Resources/Environment/Shared'
JOURNAL = ROOT / 'Logs/EnvironmentSharingMigration.json'
GUID = re.compile(r'(?<!^)\bguid: ([0-9a-f]{32})', re.M)
NAME = re.compile(r'^  m_Name:.*(?:\n    .*?)*\n', re.M)

def path(value):
    resolved = (ROOT / value).resolve()
    if not resolved.is_relative_to(ROOT / 'Assets'):
        raise ValueError(f'Not a project asset: {value}')
    return resolved

def guid(value):
    return re.search(r'^guid: (\w+)', path(value + '.meta').read_text(), re.M)[1]

def payload(value):
    return NAME.sub('  m_Name: <identity>\n', path(value).read_text(encoding='utf-8'), count=1)

def digest(value):
    return hashlib.sha256(value.encode()).hexdigest()

def save(plan):
    JOURNAL.write_text(json.dumps(plan, indent=2), encoding='utf-8')

def references(characters_only=False):
    extensions = {'.prefab', '.unity', '.asset', '.mat', '.anim', '.controller', '.overrideController', '.playable', '.meta'}
    for p in (ROOT / 'Assets').rglob('*'):
        if characters_only and '/characters/' not in p.as_posix().lower():
            continue
        if p.suffix not in extensions or not p.is_file():
            continue
        with p.open('rb') as stream:
            head = stream.read(300)
        # Numeric mesh/texture buffers have no references to another Mesh/Material.
        if b'--- !u!43 ' in head or b'--- !u!28 ' in head:
            continue
        if not (head.startswith(b'%YAML') or p.suffix == '.meta'):
            continue
        yield p

def main():
    if any((ROOT / ("Tools/ContentMigration/layout.json" + suffix)).exists() for suffix in ("", ".gz")):
        raise RuntimeError("This historical deduplicator predates the content layout. Use audit_environment_resources.py to review candidates; do not invalidate the migration GUID registry.")
    previous = json.loads(JOURNAL.read_text()) if JOURNAL.exists() else None
    if previous and previous['phase'] != 'complete':
        plan = previous
    else:
        resources = {}
        manifests = []
        for p in (ROOT / 'Assets/Editor/NativePublications/objects').rglob('publication.json'):
            m = json.loads(p.read_text())
            if not m['SourcePath'].startswith('Assets/Resources/objects/'):
                raise ValueError('Non-environment source')
            manifests.append(p.relative_to(ROOT).as_posix())
            for entry in m['Assets']:
                if entry['Type'] not in ('UnityEngine.Mesh', 'UnityEngine.Material'):
                    continue
                if entry['Guid'] in resources:
                    continue
                if not entry['Path'].startswith('Assets/Resources/Environment/') or guid(entry['Path']) != entry['Guid']:
                    raise ValueError(f'Unexpected resource ownership: {entry}')
                resources[entry['Guid']] = dict(entry, hash=digest(payload(entry['Path'])))
                if len(resources) % 1000 == 0: print(f'Hashed {len(resources)} resources', flush=True)
        groups = collections.defaultdict(list)
        for resource in resources.values():
            groups[(resource['Type'], resource['hash'])].append(resource)
        rows = []
        for (kind, key), group in groups.items():
            group.sort(key=lambda r: (not r['Path'].startswith(POOL + '/'), r['Path']))
            canonical = group[0]
            label = re.sub('[^a-zA-Z0-9_-]', '_', canonical['Name'])[:48].strip('_') or 'resource'
            target = canonical['Path'] if canonical['Path'].startswith(POOL + '/') else POOL + ('/Meshes/' if kind.endswith('.Mesh') else '/Materials/') + label + '_' + key + ('.asset' if kind.endswith('.Mesh') else '.mat')
            if target != canonical['Path'] and (path(target).exists() or path(target + '.meta').exists()):
                raise ValueError(f'Occupied shared destination: {target}')
            for resource in group:
                rows.append(dict(source=resource['Path'], oldGuid=resource['Guid'], target=target, guid=canonical['Guid'], hash=key, type=kind))
        aliases = {r['oldGuid']: r['guid'] for r in rows if r['oldGuid'] != r['guid']}
        # Do not alter character assets even if an unexpected cross-domain reference exists.
        print("Checking character references", flush=True)
        for p in references(characters_only=True):
            if '/characters/' in p.as_posix().lower():
                if set(GUID.findall(p.read_text(encoding='utf-8'))).intersection(aliases):
                    raise ValueError(f'Character depends on an environment duplicate: {p}')
        plan = dict(phase='prepared', resources=rows, manifests=manifests, rewritten=[])
        save(plan)
    aliases = {r['oldGuid']: r['guid'] for r in plan['resources'] if r['oldGuid'] != r['guid']}
    lookup = {r['oldGuid']: r for r in plan['resources']}
    moved = 0
    for row in plan['resources']:
        if row['oldGuid'] != row['guid'] or row['source'] == row['target']:
            continue
        old, new = path(row['source']), path(row['target'])
        new.parent.mkdir(parents=True, exist_ok=True)
        if old.exists():
            if digest(payload(row['source'])) != row['hash']:
                raise ValueError(f'Resource changed during migration: {old}')
            if new.exists():
                raise ValueError(f'Destination exists: {new}')
            old.rename(new)
        if path(row['source'] + '.meta').exists():
            path(row['source'] + '.meta').rename(path(row['target'] + '.meta'))
        if guid(row['target']) != row['guid']:
            raise ValueError(f'Canonical GUID changed: {new}')
        moved += 1
    plan['phase'] = 'moved'; save(plan)
    print(f'Moved {moved} shared assets; redirecting references', flush=True)
    rewritten = set(plan['rewritten'])
    for p in references():
        raw = p.read_bytes()
        text = raw.decode('utf-8')
        if not set(GUID.findall(text)).intersection(aliases):
            continue
        replacement = GUID.sub(lambda m: 'guid: ' + aliases.get(m[1], m[1]), text)
        if replacement != text:
            p.write_bytes(replacement.encode('utf-8'))
            rewritten.add(p.relative_to(ROOT).as_posix())
    for value in plan['manifests']:
        p = path(value); m = json.loads(p.read_text())
        changed = False
        for entry in m['Assets']:
            row = lookup.get(entry['Guid'])
            if row and (entry['Path'] != row['target'] or entry['Guid'] != row['guid']):
                entry['Path'], entry['Guid'] = row['target'], row['guid']; changed = True
        if changed:
            p.write_text(json.dumps(m, indent=2), encoding='utf-8')
    print(f'Redirected {len(rewritten)} files; verifying references', flush=True)
    plan['rewritten'] = sorted(rewritten); plan['phase'] = 'redirected'; save(plan)
    # Re-read everything before deleting: no remaining serialized reference may use a retired GUID.
    for p in references():
        if set(GUID.findall(p.read_text(encoding='utf-8'))).intersection(aliases):
            raise ValueError(f'Unresolved duplicate reference: {p}')
    deleted = collections.Counter()
    for row in plan['resources']:
        if row['oldGuid'] == row['guid']:
            continue
        old = path(row['source'])
        if old.exists():
            if digest(payload(row['source'])) != row['hash'] or guid(row['source']) != row['oldGuid']:
                raise ValueError(f'Duplicate changed before deletion: {old}')
            if digest(payload(row['target'])) != row['hash'] or guid(row['target']) != row['guid']:
                raise ValueError(f'Canonical changed before deletion: {row["target"]}')
            old.unlink()
        path(row['source'] + '.meta').unlink(missing_ok=True)
        deleted[row['type']] += 1
    plan['phase'] = 'complete'; plan['summary'] = dict(sharedResources=len({r['guid'] for r in plan['resources']}), removedCopies=dict(deleted), rewrittenFiles=len(rewritten), manifests=len(plan['manifests']))
    save(plan)
    print(json.dumps(plan['summary'], indent=2))

if __name__ == '__main__':
    main()
