"""Import original map object ownership, without exporting geometry or touching source builds.

Only native world-map tiles are accepted. Tutorial maps use a different source route.
The final editor metadata keeps exact source IDs and local Transform chains; proximity
and material/name similarity never establish ownership.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path
from content_paths import resolve, files

import UnityPy


def read_tile(project, build, tile):
    directory = project / 'Assets/Resources/map/tiles' / tile
    native = json.loads(resolve(directory / 'tile.json').read_text(encoding='utf-8'))
    if not native['sourceScene'].startswith('world/maps/'):
        return None
    scene = json.loads(resolve(directory / 'scene.json').read_text(encoding='utf-8'))
    bundle = build / (tile.replace('map_', 'Map_') + '.unity3d')
    env = UnityPy.load(str(bundle))
    source = 'BuildPlayer-' + tile.replace('map_', 'Map_')
    objects = {o.path_id: o for o in env.objects if o.assets_file.name == source}
    transforms = {i: o.read_typetree() for i, o in objects.items() if o.type.name == 'Transform'}
    owners = {d['m_GameObject']['m_PathID']: i for i, d in transforms.items()}
    records = {}
    for role, key in [('visual', 'visuals'), ('collision', 'colliders')]:
        for record in scene[key]:
            match = re.search(r'\[([^#]+)#(\d+) ' + role + r'\]$', record['name'])
            if not match or match[1] != source:
                continue
            owner = int(match[2])
            if owner not in owners:
                raise ValueError(f'{tile}: missing original Transform for {owner}')
            records.setdefault(owner, []).append((role, record))

    def chain(i, stop=0):
        result, seen = [], set()
        while i != stop:
            if not i or i in seen or i not in transforms:
                raise ValueError(f'{tile}: broken ancestry {i} -> {stop}')
            seen.add(i)
            d = transforms[i]
            result.append({'position': [d['m_LocalPosition'][a] for a in 'xyz'],
                           'rotation': [d['m_LocalRotation'][a] for a in 'xyzw'],
                           'scale': [d['m_LocalScale'][a] for a in 'xyz']})
            father = d['m_Father']
            if father['m_FileID'] != 0:
                raise ValueError('External Transform parent is unsupported')
            i = father['m_PathID']
        return list(reversed(result))

    def descendants(i):
        yield i
        for child in transforms[i]['m_Children']:
            if child['m_FileID'] != 0:
                raise ValueError('External Transform child is unsupported')
            yield from descendants(child['m_PathID'])

    groups = []
    # Shipped maps have scene -> organizational container -> placed object.
    # Accept only the named map root and pure containers; never group a whole tile.
    roots = [i for i, d in transforms.items() if d['m_Father']['m_PathID'] == 0 and
             objects[d['m_GameObject']['m_PathID']].read_typetree().get('m_Name', '').lower() == tile]
    if len(roots) != 1:
        raise ValueError(f'{tile}: expected one named map root, got {len(roots)}')
    for container_ref in transforms[roots[0]]['m_Children']:
        container = transforms[container_ref['m_PathID']]
        owner = container['m_GameObject']['m_PathID']
        components = objects[owner].read_typetree()['m_Component']
        if any(kind not in (4, 114) for kind, _ in components) or owner in records or len(container['m_Children']) < 2:
            continue
        for reference in container['m_Children']:
            root = reference['m_PathID']
            parts = []
            for i in descendants(root):
                owner = transforms[i]['m_GameObject']['m_PathID']
                for role, record in records.get(owner, []):
                    parts.append({'sourceNode': f'{source}#{owner}', 'role': role,
                                  'name': record['name'], 'chain': chain(i, root)})
            if len(parts) < 2 or not any(p['role'] == 'visual' for p in parts):
                continue
            if len(parts) > 256:
                raise ValueError(f'{tile}: ambiguous object with {len(parts)} parts')
            names = [p['name'].split(' [')[0].split('-')[0] for p in parts if p['role'] == 'visual']
            name = max(dict.fromkeys(names), key=names.count)
            groups.append({'sourceRoot': f"{source}#{transforms[root]['m_GameObject']['m_PathID']}",
                           'name': name, 'rootChain': chain(root), 'parts': parts})
    return {'version': 1, 'tile': tile, 'sourceContainer': bundle.name,
            'sourceSha256': hashlib.sha256(bundle.read_bytes()).hexdigest(), 'groups': groups}


def main():
    project = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build', type=Path, default=project.parent / 'builds/retrobution-20260821')
    parser.add_argument('--tiles', nargs='+')
    args = parser.parse_args()
    tiles = args.tiles or sorted(p.parent.parent.name for p in files(project / 'Assets/Resources/map/tiles','tile.json'))
    output = resolve(project / 'Assets/Editor/EnvironmentHierarchy')
    output.mkdir(parents=True, exist_ok=True)
    total = 0
    for tile in tiles:
        document = read_tile(project, args.build, tile)
        if document is None:
            continue
        target = output / (tile + '.json')
        text = json.dumps(document, ensure_ascii=False, separators=(',', ':')) + '\n'
        if not target.exists() or target.read_text(encoding='utf-8') != text:
            target.write_text(text, encoding='utf-8')
        total += len(document['groups'])
        print(f"{tile}: {len(document['groups'])} original objects", flush=True)
    print(f'Total: {total}', flush=True)


if __name__ == '__main__':
    main()
