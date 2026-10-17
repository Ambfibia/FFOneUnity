from pathlib import Path
import re
from update_content_consumers import end_call,split_args
ROOT=Path(__file__).resolve().parents[1]
count=0
for folder in ['Assets/FFOne/Editor','Assets/FFOne/Tests']:
    for p in (ROOT/folder).rglob('*.cs'):
        s=p.read_text(encoding='utf-8-sig');before=s
        if 'using ContentPaths = ' not in s:continue
        pattern=re.compile(r'(?<![\w.])(?:File\.[A-Za-z]+|new FileStream|new FileInfo|Directory\.(?:CreateDirectory|Delete))\(')
        for m in reversed(list(pattern.finditer(s))):
            end=end_call(s,m.end()-1);args=split_args(s[m.end():end])
            for i in range(min(len(args),2 if m[0].startswith('File.Copy') else 1)):
                args[i]=args[i].replace('ContentPaths.Resolve(', 'ContentPaths.FilePath(',1)
            s=s[:m.end()]+','.join(args)+s[end:]
        if s!=before:p.write_text(s,encoding='utf-8');count+=1
print('Long-path safe IO in',count,'files')
