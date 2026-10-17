"""Build pair-specific source evidence and physical probes; never edit assets."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPORTS = ROOT / 'Tools/ContentMigration'
SOURCE = ROOT.parent / 'FFOneClient/assets/game/map/tiles'


def main():
    surfaces = json.loads((REPORTS / 'world-boundary-surfaces.json').read_text())
    previous = json.loads((REPORTS / 'whole-world-seam-audit.json').read_text())
    # Dedicated instance staging cells verified against original TableData#7.
    # Mixed overworld/instance cells (e.g. 02_04, 07_08) are deliberately excluded.
    isolated = {'map_06_14', 'map_12_15', 'map_15_09'} | {
        f'map_{x:02}_{z:02}' for x, z in [(12,13),(12,14),(13,13),(13,14),(13,15),
        (14,12),(14,13),(14,14),(14,15),(15,12),(15,13),(15,14),(15,15)]}
    edges, probes = [], []
    for edge in previous['adjacentEdges']:
        a, b = edge['a'], edge['b']
        if a not in surfaces or b not in surfaces:
            continue
        ax, az = map(int, a.split('_')[1:]); bx, bz = map(int, b.split('_')[1:])
        vertical = ax != bx
        sa, sb = ('right', 'left') if vertical else ('top', 'bottom')
        av = {(round(v[1],3), round(v[3],3)):v[2] for v in surfaces[a]['boundary']}
        bv = {(round(v[1],3), round(v[3],3)):v[2] for v in surfaces[b]['boundary']}
        gaps = [{'x': p[0], 'z':p[1], 'aY':av[p], 'bY':bv[p], 'gap':abs(av[p]-bv[p])}
                for p in sorted(av.keys() & bv.keys()) if abs(av[p]-bv[p]) > .5]
        if not gaps:
            continue
        scenes = [json.loads((SOURCE / id / 'scene.json').read_text()) for id in (a,b)]
        kind = 'source-terrain-crack'
        reason = 'Pair-specific mismatch in accepted terrain; inspect physical crossings and nearby placements before repair.'
        if a in isolated or b in isolated:
            kind = 'instance-separation'; reason = 'Original TableData instance/warp destinations; not a pedestrian adjacency.'
        elif {a,b} == {'map_11_06','map_11_07'}:
            kind = 'authored-blocker'; reason = 'Original collision_512 wall spans x5610..6163, z3582.72..3585.96, y-355..197.'
        elif any(len(s['visuals']) <= 1 and all(('water' in v['name'].lower() or 'poison' in v['name'].lower()) for v in s['visuals']) for s in scenes):
            # This is supporting evidence, not sufficient to erase a real crack.
            kind = 'background-boundary'; reason = 'At least one side has only background terrain or water; no claimed walking connection.'
        row = dict(a=a,b=b,kind=kind,reason=reason,maximumGap=max(g['gap'] for g in gaps),samples=gaps)
        edges.append(row)
        if kind in ('instance-separation',):
            continue
        pairs = list(zip(surfaces[a]['edges'][sa],surfaces[b]['edges'][sb]))
        candidates = [i for i,(u,v) in enumerate(pairs) if u['terrain'] is not None and v['terrain'] is not None and abs(u['terrain']-v['terrain'])>.5]
        # Include a control away from the crack and a spread of actual mismatches.
        chosen = {128}
        if candidates:
            chosen.update(candidates[round((len(candidates)-1)*fraction)] for fraction in (0,.25,.5,.75,1))
        for i in sorted(chosen):
            along = max(1., min(511., pairs[i][0]['along']))
            point = [max(ax,bx)*512,0,az*512+along] if vertical else [ax*512+along,0,max(az,bz)*512]
            for sign in (-1,1):
                probes.append({'id':f'{a}/{b}@{along:g}:{sign:+}', 'a':a,'b':b,'expected':'observe',
                               'center':point,'direction':[sign if vertical else 0,0,0 if vertical else sign]})
    (REPORTS/'world-boundary-classification.json').write_text(json.dumps({'edges':edges},indent=2)+'\n')
    (REPORTS/'world-boundary-probes-before.json').write_text(json.dumps({'probes':probes},indent=2)+'\n')
    print('True pair edges:',len(edges),'physical probes:',len(probes))
    for e in edges: print(e['a'],e['b'],e['kind'],round(e['maximumGap'],3),len(e['samples']))


if __name__ == '__main__':
    main()
