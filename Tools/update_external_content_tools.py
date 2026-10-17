from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
changes={
'audit_environment_resources.py':[
 ("(root/'Assets/Generated/WorldModelTextures').glob('*.asset')","files(root/'Assets/Generated/WorldModelTextures','*.asset')"),
 ("(root/'Assets/Editor/NativePublications/objects').rglob('publication.json')","files(root/'Assets/Editor/NativePublications/objects','publication.json')"),
 ("p=root/a['Path'];text=payload(p)","p=resolve(root/a['Path']);text=payload(p)")],
'audit_environment_basis.py':[
 ("(root/'Assets/Resources/World/Tiles').glob('*/Content.asset')","files(root/'Assets/Resources/World/Tiles','Content.asset')")],
'validate_environment_basis.py':[
 ("local=root/'Assets/Resources'/(identity+'.glb')","local=resolve(root/'Assets/Resources'/(identity+'.glb'))")],
'import_tutorial_choreography.py':[
 ("DEST = ROOT / 'Assets/Resources/data/tutorial/cinematics.json'","DEST = resolve(ROOT / 'Assets/Resources/data/tutorial/cinematics.json')")],
'import_environment_hierarchy.py':[
 ("(directory / 'tile.json').read_text", "resolve(directory / 'tile.json').read_text"),
 ("(directory / 'scene.json').read_text", "resolve(directory / 'scene.json').read_text"),
 ("sorted(p.parent.name for p in (project / 'Assets/Resources/map/tiles').glob('*/tile.json'))","sorted(p.parent.parent.name for p in files(project / 'Assets/Resources/map/tiles','tile.json'))"),
 ("output = project / 'Assets/Editor/EnvironmentHierarchy'","output = resolve(project / 'Assets/Editor/EnvironmentHierarchy')")],
'import_world_sources.py':[
 ("    return path\n", "    return resolve(path)\n")],
}
for name,replacements in changes.items():
    p=ROOT/'Tools'/name;s=p.read_text(encoding='utf-8-sig')
    if 'from content_paths import resolve, files' not in s:
        marker='from pathlib import Path'
        s=s.replace(marker,marker+'\nfrom content_paths import resolve, files',1)
    for old,new in replacements:
        if old not in s and new not in s:raise ValueError(name+' missing '+old)
        s=s.replace(old,new)
    p.write_text(s,encoding='utf-8')
# The old deduplicator rewrites GUIDs and is not valid after a recorded GUID migration.
p=ROOT/'Tools/share_environment_resources.py';s=p.read_text(encoding='utf-8-sig')
s=s.replace('def main():\n','def main():\n    if (ROOT / "Tools/ContentMigration/layout.json").exists():\n        raise RuntimeError("This historical deduplicator predates the content layout. Use audit_environment_resources.py to review candidates; do not invalidate the migration GUID registry.")\n',1)
p.write_text(s,encoding='utf-8')
print('Updated standalone audit and source import paths; retired unsafe historical deduplication entrypoint')
