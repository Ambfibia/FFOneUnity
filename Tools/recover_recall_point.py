"""Recover the exact primary RXcom asset directly to editable Unity files.

The source has a MeshFilter but no Renderer. Preserve that distinction: the
unrendered Box is source geometry, not an invitation to add a visible cube.
No extraction/cache/project or intermediate JSON is created. --check is read-only;
an existing different destination (including an edited publication) is a conflict.
"""
import argparse
import copy
import hashlib
import re
import struct
from pathlib import Path

import UnityPy
import yaml
from UnityPy.enums.MeshTopology import MeshTopology
from UnityPy.helpers.MeshHelper import MeshHandler


SOURCE_SHA = '748e1ede36aafe2586c8c3e8e970a738b874a56c17b2bb5af38fc8757055663f'
ASSET = 'CustomAssetBundle-74b5d4666b65a47518510d30c66283ee'
ROOT = Path('Assets/FFOne/Content/Characters/NPCs/rxcom')
COMMON = {'m_ObjectHideFlags': 0, 'm_CorrespondingSourceObject': {'fileID': 0},
          'm_PrefabInstance': {'fileID': 0}, 'm_PrefabAsset': {'fileID': 0}}


class UnityDumper(yaml.SafeDumper):
    def ignore_aliases(self, data):
        return True


def guid(name):
    return hashlib.sha256(('ffone.primary-rxcom.20260821:rxcom/' + name).encode()).hexdigest()[:32]


def reference(file_id, name=None):
    return {'fileID': file_id, **({'guid': guid(name), 'type': 2} if name else {})}


def unity_yaml(records):
    result = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'
    for class_id, file_id, name, fields in records:
        result += f'--- !u!{class_id} &{file_id}\n'
        result += yaml.dump({name: fields}, Dumper=UnityDumper, sort_keys=False, allow_unicode=True, width=100000)
    return result.encode('utf-8')


def meta(name, prefab=False):
    importer = 'PrefabImporter' if prefab else 'NativeFormatImporter'
    return (f'fileFormatVersion: 2\nguid: {guid(name)}\n{importer}:\n'
            '  externalObjects: {}\n' + ('' if prefab else '  mainObjectFileID: ' + ('4300000' if name.endswith('.asset') else '7400000') + '\n') +
            '  userData: \n  assetBundleName: \n  assetBundleVariant: \n').encode()


def mesh_asset(obj):
    # UnityPy's old (<3.5) process() assumes a VertexData member absent in 2.5.
    # Invoke its existing old-mesh decoder directly, using the actual source version.
    version = tuple(int(n) for n in re.match(r'(\d+)\.(\d+)\.(\d+)', obj.assets_file.unity_version).groups()) + (0,)
    mesh = obj.read()
    decoded = MeshHandler(mesh, version=version)
    decoded.version = version
    decoded.copy_from_mesh()
    decoded.decompress_compressed_mesh()
    for sub in mesh.m_SubMeshes:
        if sub.isTriStrip != 2:
            raise ValueError('Unexpected source Box topology')
        sub.topology = MeshTopology.TriangleStrip
    triangles = decoded.get_triangles()
    if decoded.m_VertexCount != 24 or len(triangles) != 1 or len(triangles[0]) != 12:
        raise ValueError('Unexpected source Box geometry')
    vertices = b''.join(struct.pack('<6f', *p, *n) for p, n in zip(decoded.m_Vertices, decoded.m_Normals))
    indices = b''.join(struct.pack('<3H', *tri) for tri in triangles[0])
    bounds = obj.read_typetree()['m_LocalAABB']
    channels = [{'stream': 0, 'offset': 0, 'format': 0, 'dimension': 0} for _ in range(14)]
    channels[0] = {'stream': 0, 'offset': 0, 'format': 0, 'dimension': 3}
    channels[1] = {'stream': 0, 'offset': 12, 'format': 0, 'dimension': 3}
    empty_float = {'m_NumItems': 0, 'm_Range': 0, 'm_Start': 0, 'm_Data': '', 'm_BitSize': 0}
    empty_int = {'m_NumItems': 0, 'm_Data': '', 'm_BitSize': 0}
    compressed = {name: copy.deepcopy(empty_float if name in ('m_Vertices', 'm_UV', 'm_Normals', 'm_Tangents', 'm_FloatColors') else empty_int)
                  for name in ('m_Vertices', 'm_UV', 'm_Normals', 'm_Tangents', 'm_Weights', 'm_NormalSigns', 'm_TangentSigns', 'm_FloatColors', 'm_BoneIndices', 'm_Triangles')}
    compressed['m_UVInfo'] = 0
    body = {**COMMON, 'm_Name': 'Box', 'serializedVersion': 12,
            'm_SubMeshes': [{'serializedVersion': 2, 'firstByte': 0, 'indexCount': 36, 'topology': 0, 'baseVertex': 0,
                             'firstVertex': 0, 'vertexCount': 24, 'localAABB': bounds}],
            'm_Shapes': {'vertices': [], 'shapes': [], 'channels': [], 'fullWeights': []},
            'm_BindPose': [], 'm_BoneNameHashes': '', 'm_RootBoneNameHash': 0, 'm_BonesAABB': [],
            'm_VariableBoneCountWeights': {'m_Data': ''}, 'm_MeshCompression': 0, 'm_IsReadable': 1,
            'm_KeepVertices': 1, 'm_KeepIndices': 1, 'm_IndexFormat': 0, 'm_IndexBuffer': indices.hex(),
            'm_VertexData': {'serializedVersion': 3, 'm_VertexCount': 24, 'm_Channels': channels,
                             'm_DataSize': len(vertices), '_typelessdata': vertices.hex()},
            'm_CompressedMesh': compressed, 'm_LocalAABB': bounds, 'm_MeshUsageFlags': 0,
            'm_CookingOptions': 30, 'm_BakedConvexCollisionMesh': '', 'm_BakedTriangleCollisionMesh': '',
            'm_MeshMetrics[0]': 1, 'm_MeshMetrics[1]': 1, 'm_MeshOptimizationFlags': 1,
            'm_StreamData': {'serializedVersion': 2, 'offset': 0, 'size': 0, 'path': ''}}
    return unity_yaml([(43, 4300000, 'Mesh', body)])


def animation_asset(source):
    body = {**COMMON, 'm_Name': source['m_Name'], 'serializedVersion': 7, 'm_Legacy': 1,
            'm_Compressed': int(source['m_UseCompression']), 'm_UseHighQualityCurve': 1}
    stop = 0.0
    for field in ('m_RotationCurves', 'm_PositionCurves', 'm_ScaleCurves', 'm_FloatCurves'):
        curves = copy.deepcopy(source[field])
        for entry in curves:
            curve = entry['curve']
            curve['serializedVersion'] = 2
            curve['m_RotationOrder'] = 4
            for key in curve['m_Curve']:
                key['serializedVersion'] = 3
                key['tangentMode'] = key['weightedMode'] = 0
                value = key['value']
                key['inWeight'] = key['outWeight'] = ({axis: 1 / 3 for axis in value} if isinstance(value, dict) else 1 / 3)
                stop = max(stop, key['time'])
        body[field] = curves
    # Keep the source packed quaternion payload/times/slopes byte-for-byte.
    # Unity's AnimationClip serializer owns their interpretation; do not bake/resample.
    packed = copy.deepcopy(source['m_CompressedRotationCurves'])
    for entry in packed:
        for field in ('m_Times', 'm_Values', 'm_Slopes'):
            entry[field]['m_Data'] = bytes(entry[field]['m_Data']).hex()
    body['m_CompressedRotationCurves'] = packed
    body.update({'m_EulerCurves': [], 'm_PPtrCurves': [], 'm_SampleRate': source['m_SampleRate'],
                 'm_WrapMode': 0, 'm_Bounds': {'m_Center': {'x': 0, 'y': 0, 'z': 0}, 'm_Extent': {'x': 0, 'y': 0, 'z': 0}},
                 'm_ClipBindingConstant': {'genericBindings': [], 'pptrCurveMapping': []},
                 'm_AnimationClipSettings': {'serializedVersion': 2, 'm_AdditiveReferencePoseClip': reference(0),
                    'm_AdditiveReferencePoseTime': 0, 'm_StartTime': 0, 'm_StopTime': stop,
                    'm_OrientationOffsetY': 0, 'm_Level': 0, 'm_CycleOffset': 0,
                    'm_HasAdditiveReferencePose': 0, 'm_LoopTime': 0, 'm_LoopBlend': 0,
                    'm_LoopBlendOrientation': 0, 'm_LoopBlendPositionY': 0, 'm_LoopBlendPositionXZ': 0,
                    'm_KeepOriginalOrientation': 0, 'm_KeepOriginalPositionY': 1, 'm_KeepOriginalPositionXZ': 0,
                    'm_HeightFromFeet': 0, 'm_Mirror': 0},
                 'm_EditorCurves': [], 'm_EulerEditorCurves': [], 'm_HasGenericRootTransform': 0,
                 'm_HasMotionFloatCurves': 0,
                 'm_Events': [{**event, 'objectReferenceParameter': reference(0), 'floatParameter': 0, 'intParameter': 0}
                              for event in source['m_Events']]})
    return unity_yaml([(74, 7400000, 'AnimationClip', body)])


def prepare(bundle):
    if hashlib.sha256(bundle.read_bytes()).hexdigest() != SOURCE_SHA:
        raise ValueError('Not the accepted primary Freearea_shared.resourceFile')
    env = UnityPy.load(str(bundle))
    objects = {o.path_id: o for o in env.objects if o.assets_file.name == ASSET}
    specs = ((4900, 3373, 'RXcom'), (4901, 3374, 'Box01'), (4744, 3375, 'Box01 NonAccum'), (4743, 3376, 'Box'))
    records = []
    for owner, transform, name in specs:
        go, trs = objects[owner].read_typetree(), objects[transform].read_typetree()
        if go['m_Name'] != name or trs['m_GameObject']['m_PathID'] != owner:
            raise ValueError('Source ownership changed')
        if any(kind not in (4, 111, 33) or ptr['m_FileID'] != 0 for kind, ptr in go['m_Component']):
            raise ValueError('Source RXcom now has a Renderer/unsupported component')
        components = [{'component': reference(ptr['m_PathID'])} for _, ptr in go['m_Component']]
        records.append((1, owner, 'GameObject', {**COMMON, 'serializedVersion': 6,
            'm_Component': components, 'm_Layer': go['m_Layer'], 'm_Name': name, 'm_TagString': 'Untagged',
            'm_Icon': reference(0), 'm_NavMeshLayer': 0, 'm_StaticEditorFlags': 0, 'm_IsActive': go['m_IsActive']}))
        records.append((4, transform, 'Transform', {**COMMON, 'm_GameObject': reference(owner), 'serializedVersion': 2,
            **{key: trs[key] for key in ('m_LocalRotation', 'm_LocalPosition', 'm_LocalScale')},
            'm_ConstrainProportionsScale': 0, 'm_Children': [reference(ptr['m_PathID']) for ptr in trs['m_Children']],
            'm_Father': reference(trs['m_Father']['m_PathID']), 'm_LocalEulerAnglesHint': {'x': 0, 'y': 0, 'z': 0}}))
    animation = objects[7018].read_typetree()
    records.append((111, 7018, 'Animation', {**COMMON, 'm_GameObject': reference(4900), 'm_Enabled': int(animation['m_Enabled']),
        'serializedVersion': 3, 'm_Animation': reference(7400000, 'nif-default.anim'),
        'm_Animations': [reference(7400000, 'nif-default.anim'), reference(7400000, 'stand1.anim')],
        'm_WrapMode': animation['m_WrapMode'], 'm_PlayAutomatically': int(animation['m_PlayAutomatically']),
        'm_AnimatePhysics': int(animation['m_AnimatePhysics']), 'm_CullingType': 0}))
    records.append((33, 6751, 'MeshFilter', {**COMMON, 'm_GameObject': reference(4743), 'm_Mesh': reference(4300000, 'Box.asset')}))
    outputs = {ROOT / 'Prefabs/rxcom/model.prefab': unity_yaml(records),
               ROOT / 'Models/rxcom/Box.asset': mesh_asset(objects[571])}
    for path_id in (1051, 1052):
        clip = objects[path_id].read_typetree()
        outputs[ROOT / ('Animation/rxcom/' + clip['m_Name'] + '.anim')] = animation_asset(clip)
    for path in list(outputs):
        outputs[Path(str(path) + '.meta')] = meta(path.name, path.suffix == '.prefab')
    return outputs


def main():
    project = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bundle', type=Path, default=project.parent / 'builds/retrobution-20260821/Freearea_shared.resourceFile')
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    outputs = prepare(args.bundle)
    conflicts = [str(path) for path, data in outputs.items() if (project / path).exists() and (project / path).read_bytes() != data]
    if conflicts:
        raise ValueError('Existing different files; no files written: ' + ', '.join(conflicts))
    if not args.check:
        for path, data in outputs.items():
            target = project / path
            if not target.exists():
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
    print(f'RXcom: {len(outputs)} final files validated; ' + ('no writes' if args.check else 'published/preserved') +
          '; 4 original nodes, 24 source vertices, 12 triangles, no Renderer, 2 source clips')
    print('Runtime prefab address: ffone/' + guid('model.prefab'))


if __name__ == '__main__':
    main()
