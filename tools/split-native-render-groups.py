"""Split a static import into <=8-material modules, preserving common origin.
The modules must later be composed as distinct renders, not merged into one mesh.
"""
from pathlib import Path
import json,shutil,sys
root=Path(__file__).resolve().parents[1]
source=root/'assets_game_import/diagnostics'/sys.argv[1]
dest=root/'assets_game_import/modules'/source.name
dest.mkdir(parents=True,exist_ok=False)
objects=sorted(source.glob('*.obj'));groups=[]
for i in range(0,len(objects),8):
    folder=dest/(source.name+'Section'+str(i//8+1))
    folder.mkdir()
    files=[]
    for obj in objects[i:i+8]:
        for p in source.glob(obj.stem+'*'):
            shutil.copy2(p,folder/p.name);files.append(p.name)
    if (source/'settings.json').exists():shutil.copy2(source/'settings.json',folder/'settings.json')
    groups.append({'directory':str(folder),'material_parts':len(objects[i:i+8]),'offset':[0,0,0],'rotation':[0,0,0],'scale':[1,1,1],'files':files})
result={'source':str(source),'status':'prepared only; game import pending','assembly':'Keep separate RenderPrefabs at their common origin; never merge the 17 surfaces into one render mesh.','groups':groups}
(dest/'assembly.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps({'parts':len(objects),'module_sizes':[x['material_parts'] for x in groups],'directory':str(dest)}))
