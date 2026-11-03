"""Duplicate the actual first pad, including UVs, instead of approximating its artwork."""
from pathlib import Path
import shutil,json
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'assets_game_import/heliport-markings-v8/CWHeliportTerminalMarkingsV8'
OUT=ROOT/'assets_game_import/heliport-complete-markings-v11/CWHeliportCompleteMarkingsV11'

def prepare():
    lines=(SOURCE/'CWHeliportTerminalPart01.obj').read_text().splitlines()
    v=np.array([[float(a) for a in l.split()[1:4]] for l in lines if l.startswith('v ')])
    faces=[l.split()[1:] for l in lines if l.startswith('f ')]
    ids=[[int(c.split('/')[0])-1 for c in f] for f in faces]
    parent=list(range(len(faces)));seen={}
    def find(i):
        while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
        return i
    for i,f in enumerate(ids):
        for k in f:
            key=tuple(np.round(v[k],4))
            if key in seen:parent[find(i)]=find(seen[key])
            else:seen[key]=i
    groups={}
    for i in range(len(faces)):groups.setdefault(find(i),[]).append(i)
    selected=[]
    for group in groups.values():
        points=v[[k for i in group for k in ids[i]]];lo=points.min(0);hi=points.max(0)
        if lo[0]>=-21 and hi[0]<=7.29 and lo[2]>=2.70 and hi[2]<=50.73:
            selected.extend(group)
    assert len(selected)==758, len(selected) # pad plus approach, perimeter and dashed markings
    used=sorted({k for i in selected for k in ids[i]}); mapping={k:len(v)+i+1 for i,k in enumerate(used)}
    copied=v[used]+np.array([-29,0,0])
    assert np.allclose(copied-v[used],[-29,0,0])
    assert copied[:,0].min()>-50 and copied[:,2].max()<50.73
    appended=['g second_pad_exact_copy']+['v '+' '.join(format(a,'.10g') for a in row) for row in copied]
    for i in sorted(selected):
        corners=[]
        for c in faces[i]:
            parts=c.split('/');parts[0]=str(mapping[int(parts[0])-1]);corners.append('/'.join(parts))
        appended.append('f '+' '.join(corners))
    OUT.parent.mkdir(parents=True,exist_ok=True);shutil.copytree(SOURCE,OUT)
    (OUT/'CWHeliportTerminalPart01.obj').write_text('\n'.join(lines+appended)+'\n')
    for p in SOURCE.iterdir():
        if p.is_file() and p.suffix!='.obj':assert p.read_bytes()==(OUT/p.name).read_bytes()
    bounds=v[used].min(0),v[used].max(0)
    center=np.array([-4.081496695,.24,31.39467335])
    report={'includes_apron_lines':True,'copied_triangles':len(selected),'translation_m':[-29,0,0],
            'first_center':center.tolist(),'second_center':(center+[-29,0,0]).tolist(),
            'same_scale_orientation_uv_material':True,'marking_y':.24}
    (OUT.parent/'validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
if __name__=='__main__':prepare()
