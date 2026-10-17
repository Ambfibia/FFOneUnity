"""Final boundary evidence, classifications and honest walking acceptance."""
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from content_paths import resolve

ROOT=Path(__file__).resolve().parents[1]
REPORTS=ROOT/'Tools/ContentMigration'


def read(name):
    return json.loads((REPORTS/f'world-boundary-{name}.json').read_text(encoding='utf-8-sig'))


def main():
    classification=read('classification');before=read('surfaces');after=read('surfaces-after')
    applied=read('applied');plan=read('repair-plan');walk_before=read('walk-before');walk_after=read('walk-after')
    pairs={}
    for o in walk_after['observations']:pairs.setdefault((o['a'],o['b']),[]).append(o)
    barriers={
        ('map_02_04','map_02_05'):'Original Map_02_04 collider#20018, collision_512; capsule contacted it in built player.',
        ('map_02_11','map_03_11'):'Original Map_03_11 colliders#1536/#1539, collision_512; capsule contacted them.',
        ('map_07_08','map_08_08'):'Original Map_07_08 collider#12548, collision_512; capsule contacted it.',
        ('map_10_07','map_11_07'):'Original Map_10_07 collider#121, collision_512; capsule contacted it.',
        ('map_11_06','map_11_07'):'Original Map_11_06 collider#38 covers the full boundary; capsule confirms blocking.',
        ('map_11_11','map_12_11'):'Original Map_11_11 perimeter colliders and steep relief; no tested pedestrian crossing.',
    }
    fixed={(p['x'],p['z']) for p in plan['points']}
    for edge in classification['edges']:
        pair=(edge['a'],edge['b']);observations=pairs.get(pair,[])
        edge['walking']={'tested':len(observations),'crossed':sum(o['crossed'] for o in observations)}
        edge['fixedCoordinates']=[g for g in edge['samples'] if (g['x'],g['z']) in fixed]
        if pair in barriers:edge.update(kind='authored-blocker',reason=barriers[pair])
        elif pair==('map_00_01','map_01_01'):
            edge.update(kind='covered-by-poison-surface',reason='All 12 capsule probes use the continuous ffPoison collider at Y=-120, above the ground mismatch; not a land route.')
        elif edge['fixedCoordinates']:
            edge.update(kind='repaired-source-crack' if len(edge['fixedCoordinates'])==len(edge['samples']) else 'mixed-repaired-and-exterior-relief',
                        reason='Only explicitly reviewed coordinates welded. Other source relief retained; see per-coordinate records and probe outcomes.')
        elif edge['kind'] not in ('instance-separation','background-boundary'):
            edge.update(kind='retained-source-relief',reason='Original/accepted source geometry has the mismatch. Tested route is a steep, submerged or perimeter area; no evidence sufficient to authorize a new pedestrian connection. Intent is not inferred solely from failed walking.')
        # True pairwise post-repair gaps; avoid multi-cell corner contamination.
        a={(round(v[1],3),round(v[3],3)):v[2] for v in after[edge['a']]['boundary']}
        b={(round(v[1],3),round(v[3],3)):v[2] for v in after[edge['b']]['boundary']}
        edge['remainingMaximumGap']=max(abs(a[p]-b[p]) for p in a.keys()&b.keys())
    hashes_ok=all(hashlib.sha256((ROOT/f'Assets/FFOne/Worlds/Overworld/Cells/{id}/{id}.unity').read_bytes()).hexdigest()==v['sceneSha256'] for id,v in applied.items())
    unchanged=all(before[id]['sceneHash']==after[id]['sceneHash'] for id in before if id not in applied)
    source_ok=all(hashlib.sha256(resolve(v['originalMesh']).read_bytes()).hexdigest()==v['originalHash'] for v in applied.values())
    tests=ET.parse(ROOT/'Logs/WorldBoundaryEditorTests.xml').getroot().attrib
    previous={o['id']:o for o in walk_before['observations']}
    improvements=[o['id'] for o in walk_after['observations'] if o['crossed'] and not previous[o['id']]['crossed']]
    regressions=[o['id'] for o in walk_after['observations'] if not o['crossed'] and previous[o['id']]['crossed']]
    closed=[]
    for p in plan['points']:
        heights=[v[2] for t in after.values() for v in t['boundary'] if abs(v[1]-p['x'])<.001 and abs(v[3]-p['z'])<.001]
        closed.append(dict(p,afterGap=max(heights)-min(heights)))
    report={'passed':walk_after['passed'] and tests['failed']=='0' and hashes_ok and unchanged and source_ok and not regressions and all(p['afterGap']<.0001 for p in closed),
        'scope':'Remaining >0.5 unit terrain discrepancies from the 170-cell migration; pair-specific review and built-player capsule probes.',
        'pairsReviewed':len(classification['edges']),'affectedCellsInspected':len(before),'cellsChanged':len(applied),'verticesChanged':sum(v['verticesAdjusted'] for v in applied.values()),
        'sourceHeightScaleTransformOrBoundaryShiftErrors':0,'geometryRepairs':closed,'acceptedSceneHashesMatch':hashes_ok,'otherInspectedScenesUnchanged':unchanged,'sharedMeshesUnchanged':source_ok,
        'editorTests':{k:tests.get(k) for k in ('total','passed','failed','result')},
        'walking':{'passed':walk_after['passed'],'probes':len(walk_after['observations']),'beforeCrossed':sum(o['crossed'] for o in walk_before['observations']),'afterCrossed':sum(o['crossed'] for o in walk_after['observations']),
                   'improved':improvements,'regressions':regressions,'capsule':{'height':1.8,'radius':.45,'stepOffset':.4,'slopeLimitDegrees':50,'routeLength':12},'originalBlockersRetained':True},
        'edges':classification['edges'],
        'sourceEvidence':{'build':'retrobution-20260821','terrainParity':{'cells':69,'heightSamplesCompared':1148229,'heightMismatches':0,'transformsCompared':138,'transformMismatches':0,'scaleMismatches':0,'ownerReferenceMismatches':0,'matchingVertexShifts':3070},'mainSha256':'01b544976b2d54355507cf30fe6dfada2b476b92b209a3d47c1499669ed9b4ef',
            'tableDataSha256':'bd8767c2d316f60a4b38de320794701b2391d7b40412020bdc1d17e88c7b7212',
            'xdtRawObjectSha256':'2c9db47141ffa8f6033978356b142088643d412fc2e703c1f686afe0bc6f002a',
            'xdtObject':'TableData.resourceFile / CustomAssetBundle-1dca92eecee4742d985b799d8226666d / MonoBehaviour#7',
            'loader':'Assembly - CSharp - first pass.dll DongLoader PreloadDong MoveNext 0x0600045a and PreloadDongLocal 0x06000460 set Terrain neighbors; no height edits in these complete methods.',
            'replay':'FusionForge/target/debug/fusionforge.exe dump-object-evidence retrobution <build> <container> <pathId> --type TerrainData --out - --allow-unresolved-pointers'},
        'interiorSourceFindings':[
            {'cell':'map_01_01','status':'unrepaired-interior-export-omission','validShiftsAbsent':386,'columns':[33,109],'rows':[26,72],'boundaryRecords':0,'container':'Tutorial.resourceFile','terrainDataPathId':1903,'containerSha256':'729e9aad557709d4a75937b7478b6784b82f202c000d13127d1074213991f916','rawObjectSha256':'74f62c76c29149e201379b5afbfa61ed204938ac1765e2641774f83eada62926'},
            {'cell':'map_02_12','status':'unsupported-interior-source-flags','count':7,'flags':11,'boundaryRecords':0,'coordinatesColumnRow':[[69,43],[69,44],[73,53],[73,54],[74,56],[74,57],[75,68]],'terrainDataPathId':55,'rawObjectSha256':'c0a8eb7b824054c42bfc76eb6f16a48d75efcc77ebb940e0dd1b8d79cafa3967','reason':'Opposite X directions simultaneously set; original exporter rejects this mask.'}],
        'limitations':['Failed capsule crossing alone does not prove designer intent. Dedicated instance destinations and original collision walls have stronger evidence.',
            'Instance teleport gameplay, swimming/poison damage, AI pathfinding and every possible freeform route were not validated by the walking probes.',
            'Retained-source-relief and background-boundary entries are preserved uncertain/nonpedestrian relief, not silently certified as correctly designed.',
            'Native terrain engine implementation in the original legacy client was not decompiled; serialized terrain and bounded managed loading methods were inspected.'],
        'researchScratch':{'path':str(ROOT.parent/'FusionForge/work/sessions/inspect/session_18de4fbcf54d71e8'),'bytes':605128244,'cleanup':'Automatic approval review rejected recursive deletion with blocked by policy; not retried.'}}
    (REPORTS/'world-boundary-acceptance.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k not in ('edges','sourceEvidence')},ensure_ascii=False))
    raise SystemExit(0 if report['passed'] else 1)


if __name__=='__main__':main()
