"""Publish the accepted, engine-independent choreography as Unity runtime data.

Reads only the named native choreography constants. This is a strict syntax adapter,
not a source decompiler; unsupported expressions fail before any output is written.
The retained source contracts are in FFOneClient's tutorial_runtime/choreography.
"""
import json
import re
from pathlib import Path
from content_paths import resolve, files

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parent / 'FFOneClient/crates/ffone-client/src/tutorial_runtime/choreography'
DEST = resolve(ROOT / 'Assets/Resources/data/tutorial/cinematics.json')
TOKEN = re.compile(r'\s+|//[^\n]*|"(?:\\.|[^"\\])*"|[A-Za-z_][A-Za-z_0-9]*(?:::[A-Za-z_][A-Za-z_0-9]*)*|\d[\d_]*(?:\.\d[\d_]*)?|[\[\](){},:;&=\-]')

class Reader:
    def __init__(self, source):
        self.tokens = []
        pos = 0
        while pos < len(source):
            match = TOKEN.match(source, pos)
            if not match:
                raise ValueError('Unsupported syntax: ' + source[pos:pos+100])
            token = match.group(); pos = match.end()
            if not token.isspace() and not token.startswith('//'): self.tokens.append(token)
        self.i = 0

    def take(self, expected=None):
        token = self.tokens[self.i]; self.i += 1
        if expected is not None and token != expected: raise ValueError((expected, token, self.tokens[self.i:self.i+10]))
        return token

    def peek(self): return self.tokens[self.i] if self.i < len(self.tokens) else None

    def value(self):
        token = self.take()
        if token == '&': return self.value()
        if token == '-': return -self.value()
        if token.startswith('"'): return json.loads(token)
        if token[0].isdigit(): return float(token.replace('_','')) if '.' in token else int(token.replace('_',''))
        if token in ('true','false'): return token == 'true'
        if token == 'None': return None
        if token == '[':
            result=[]
            while self.peek() != ']':
                result.append(self.value())
                if self.peek() == ',': self.take(',')
                else: break
            self.take(']'); return result
        if self.peek() == '(':
            self.take('('); args=[]
            while self.peek() != ')':
                args.append(self.value())
                if self.peek() == ',': self.take(',')
                else: break
            self.take(')')
            return {'call': token, 'args': args}
        if self.peek() == '{':
            self.take('{'); result={'type':token.split('::')[-1]}
            while self.peek() != '}':
                name=self.take(); self.take(':'); result[name]=self.value()
                if self.peek() == ',': self.take(',')
                else: break
            self.take('}'); return result
        return {'ref':token}

constants={}
for path in sorted(SOURCE.glob('constants_*.rs')):
    source=path.read_text(encoding='utf-8')
    matches=list(re.finditer(r'(?:pub(?:\(super\))?\s+)?const\s+(\w+)\s*:[^=]+=',source))
    for index,match in enumerate(matches):
        # Every declaration ends with a top-level semicolon; the parser consumes
        # one value and checks it, without relying on the next declaration.
        reader=Reader(source[match.end():matches[index+1].start() if index+1<len(matches) else len(source)])
        constants[match[1]]=reader.value(); reader.take(';')

def resolve(value):
    if isinstance(value,list): return [resolve(v) for v in value]
    if not isinstance(value,dict): return value
    if 'ref' in value:
        name=value['ref']
        if name in constants: return resolve(constants[name])
        if name == 'ClientVec3::ZERO': return [0,0,0]
        if name == 'ClientVec3::UP': return [0,1,0]
        if '::' not in name: raise ValueError('Unknown constant '+name)
        return {'type':name.split('::')[-1]}
    if 'call' not in value:
        return {k:resolve(v) for k,v in value.items() if k not in ('source_line','source','coroutine')}
    name=value['call']; a=resolve(value['args'])
    if name=='Some': return a[0]
    if name=='ClientVec3::new': return a
    if name=='LegacyServerPosition::new': return [a[0],a[2],a[1]]
    if name=='at': return {'time':a[0],'action':a[2]}
    if name=='skip': return a[1]
    if name=='spawn': return dict(id=a[0],npc_type=a[1],position=[a[2],a[4],a[3]],helper_angle_degrees=a[5],initial_animation=a[6],force_update=a[7])
    if name=='effect': return dict(effect_id=a[0],position={'type':'Client','args':[[a[1],a[2],a[3]]]},scale=a[4],tracked=a[5])
    if name=='oriented': return dict(type='OrientedOffset',origin=a[0],world_offset=a[1],orientation=a[2],local_offset=a[3])
    if name=='skip_contract': return dict(scene_specific=a[0],common_cleanup=resolve(constants['COMMON_SKIP_CLEANUP']),final_fade=a[1])
    if '::' in name: return {'type':name.split('::')[-1],'args':a}
    raise ValueError('Unknown function '+name)

scenes=resolve(constants['TUTORIAL_SCENE_CHOREOGRAPHIES'])
assert len(scenes)==10, 'Ten finite source coroutines; InfectionD is a gameplay transition.'
for scene in scenes:
    times=[a['time'] for a in scene['actions']]
    assert times==sorted(times), scene['scene']
result=json.dumps({'schema':'ffone.unity-tutorial-cinematics.v1','scenes':scenes},ensure_ascii=False,separators=(',',':'))+'\n'
DEST.parent.mkdir(parents=True,exist_ok=True)
if DEST.exists() and DEST.read_text(encoding='utf-8') != result:
    raise SystemExit('Existing cinematic data differs; review before replacing it.')
DEST.write_text(result,encoding='utf-8')
print(f'{len(scenes)} scenes, {sum(len(s["actions"]) for s in scenes)} timed actions, {len(result)} bytes')
