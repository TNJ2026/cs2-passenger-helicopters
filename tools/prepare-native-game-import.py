"""Prepare native-compatible names without changing the conversion archive."""
from pathlib import Path
import re, shutil, json
root=Path(__file__).resolve().parents[1]
out=root/'assets_game_import'
mapping=[]
for source in sorted((root/'assets_converted').glob('*/import/*/LOD0')):
    model='CW'+''.join(s.capitalize() for s in source.parent.name.split('_'))
    dest=out/model
    dest.mkdir(parents=True,exist_ok=True)
    names={p.stem:model+'Part'+p.stem.rsplit('_',1)[1] for p in source.glob('*.obj')}
    for file in source.iterdir():
        name=file.name
        for old,new in names.items():name=name.replace(old,new)
        target=dest/name
        if file.suffix in ('.obj','.mtl'):
            data=file.read_text(encoding='utf-8')
            for old,new in names.items():data=data.replace(old,new)
            target.write_text(data,encoding='utf-8')
        else:shutil.copy2(file,target)
    mapping.append({'source':str(source),'directory':str(dest),'names':names})
out.mkdir(exist_ok=True)
(out/'mapping.json').write_text(json.dumps(mapping,indent=2),encoding='utf-8')
print(json.dumps(mapping,indent=2))
