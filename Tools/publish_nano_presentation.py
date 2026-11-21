"""Publish accepted Nano animation cues and their exact audio routes into an editable Unity resource."""
from collections import defaultdict
from pathlib import Path
import json
import re
import struct

ROOT = Path(__file__).resolve().parents[1]
GAME = ROOT.parent / 'FFOneClient/assets/game'
OUTPUT = ROOT / 'Assets/FFOne/Data/Gameplay/Resources/WorldNanoPresentation.json'


def gltf(path):
    with path.open('rb') as stream:
        magic, version, _, length, chunk = struct.unpack('<4sIIII', stream.read(20))
        if magic != b'glTF' or version != 2 or chunk != 0x4E4F534A:
            raise ValueError(f'Invalid GLB header in {path}')
        return json.loads(stream.read(length))


def takes(payload):
    name = re.sub(r'\.(wav|ogg|mp3)$', '', payload.strip(), flags=re.I)
    match = re.search(r'\(RAND:(\d+)-(\d+)\)', name, re.I)
    if not match:
        return [name.casefold()]
    return [choice for take in range(int(match[1]), int(match[2]) + 1)
            for choice in takes(name[:match.start()] + str(take) + name[match.end():])]


def publish():
    tables = json.loads((GAME / 'data/tables/xdt.json').read_text(encoding='utf-8'))
    audio = next(e['value']['m_pAudioData'] for e in tables['_ffone']['tables'] if e['name'] == 'native_asset_routes')
    # The two Academy characters share a historical directory, while their true
    # recording names identify which model owns each line.
    def voice_identity(row):
        return 'nano_holonano' if row['trueName'].casefold().startswith('unstable nano_nan') else row['owner']
    sounds = defaultdict(list)
    voice_takes = defaultdict(list)
    voice_directories = {}
    dismiss = defaultdict(list)
    for row in audio:
        name = row['trueName'].casefold()
        sounds[name].append(row)
        if row['category'] != 'voice':
            continue
        identity = voice_identity(row)
        match = re.search(r'_(nan[a-z0-9]+)$', name)
        if match:
            voice_takes[identity, match[1]].append(row)
        if re.search(r'_nandismiss\d+$', name):
            dismiss[identity].append(Path(row['path']).stem)
            voice_directories[identity] = Path(row['path']).parent.as_posix()
    models = {}
    unresolved = set()
    for path in sorted((GAME / 'characters/nanos').glob('*/*.glb')):
        owner = path.parent.name.casefold()
        if owner in models:
            raise ValueError(f'Multiple models for {owner}')
        clips = {}
        source_clips = gltf(path)['animations']
        if owner == 'nano_cheese':
            supplement = ROOT / 'Assets/FFOne/Editor/ActorMotionSources/AcademyCheeseSkills.glb'
            source_clips += gltf(supplement)['animations']
        for clip in source_clips:
            cues = []
            for event in clip['extras']['nonTrs']['events']:
                fn = event['functionName']
                if fn not in ('sound', 'particle', 'tag_p', 'end', 'emote_face_on', 'emote_face_off'):
                    continue
                payload = event['stringParameter']
                cue = dict(time=event['time'], function=fn, payload=payload)
                if fn == 'sound':
                    voices, effects = [], []
                    voice_owners = set()
                    authored = payload
                    if owner == 'nano_deedee' and authored.casefold() == 'deedee_nanpwrway0(rand:1-3)(rand:1-1).wav':
                        authored = 'DeeDee_NanPwrWay0(RAND:1-3).wav'
                    if owner == 'nano_coco' and authored.casefold() == 'coco_nandismiss.wav':
                        authored = 'Coco_NanDismiss0(RAND:1-3).wav'
                    for title in takes(authored):
                        matches = sounds[title]
                        named = [r for r in matches if r['category'] == 'voice']
                        owned = [r for r in named if r['owner'] == owner]
                        if not owned:
                            take = re.search(r'_(nan[a-z0-9]+)$', title)
                            if take:
                                owned = voice_takes[owner, take[1]]
                        # An authored event may explicitly name another actor's recording.
                        if not owned and len(named) == 1:
                            owned = named
                        if owned:
                            voices.extend(Path(r['path']).stem for r in owned)
                            voice_owners.update(Path(r['path']).parent.as_posix() for r in owned)
                        else:
                            effects.extend(Path(r['path']).with_suffix('').as_posix() for r in matches if r['category'] == 'sfx')
                    if voices:
                        if len(voice_owners) != 1:
                            raise ValueError(f'Ambiguous recording owner for {payload}: {voice_owners}')
                        cue.update(voiceOwner=voice_owners.pop(), voices=list(dict.fromkeys(voices)))
                    if effects:
                        cue['sfx'] = list(dict.fromkeys(effects))
                    if not voices and not effects:
                        unresolved.add((owner, payload))
                cues.append(cue)
            if cues:
                clips[clip['name']] = sorted(cues, key=lambda cue: cue['time'])
        models[owner] = dict(voiceOwner=voice_directories.get(owner, owner), dismiss=sorted(dismiss[owner]), clips=clips)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    # One cue per line keeps event timing and its routes together in source review.
    lines = ['{"models": {']
    for index, (model, data) in enumerate(models.items()):
        lines.append('  ' + json.dumps(model) + ': {"voiceOwner": ' + json.dumps(data['voiceOwner']) +
                     ', "dismiss": ' + json.dumps(data['dismiss']) + ', "clips": {')
        for i, (name, cues) in enumerate(data['clips'].items()):
            lines.append('    ' + json.dumps(name) + ': [')
            lines.extend('      ' + json.dumps(c, ensure_ascii=False) + (',' if j + 1 < len(cues) else '')
                         for j, c in enumerate(cues))
            lines.append('    ]' + (',' if i + 1 < len(data['clips']) else ''))
        lines.append('  }}' + (',' if index + 1 < len(models) else ''))
    OUTPUT.write_text('\n'.join(lines + ['}}', '']), encoding='utf-8')
    assert json.loads(OUTPUT.read_text(encoding='utf-8'))['models'] == models
    print(f'{len(models)} models. Unresolved model/cue pairs: {len(unresolved)}')
    for model, cue in sorted(unresolved):
        print(model, cue)


if __name__ == '__main__':
    publish()
