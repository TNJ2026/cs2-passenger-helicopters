"""Duplicate the actual first pad, including UVs, instead of approximating its artwork."""
from pathlib import Path
import shutil,json
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'assets_game_import/heliport-markings-v8/CWHeliportTerminalMarkingsV8'
OUT=ROOT/'assets_game_import/heliport-mirrored-v12/CWHeliportMirroredMarkingsV12'

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
    selected=[]; reflected=[]
    for group in groups.values():
        points=v[[k for i in group for k in ids[i]]];lo=points.min(0);hi=points.max(0)
        if lo[0]>=-21 and hi[0]<=7.29 and lo[2]>=2.70 and hi[2]<=50.73:
            selected.extend(group)
            if not (lo[0]>=-15.45 and hi[0]<=7.29 and lo[2]>=20.07 and hi[2]<=42.72): reflected.extend(group)
    assert len(selected)==758, len(selected) # pad plus approach, perimeter and dashed markings
    used=sorted({k for i in selected for k in ids[i]}); mapping={k:len(v)+i+1 for i,k in enumerate(used)}
    copied=v[used]+np.array([-29,0,0])
    reflected_vertices={k for i in reflected for k in ids[i]}
    pad_vertices={k for i in selected if i not in reflected for k in ids[i]}
    assert len(reflected)==48 and not reflected_vertices.intersection(pad_vertices)
    for j,k in enumerate(used):
        if k in reflected_vertices: copied[j,2]=2*31.39467335-v[k,2]
    assert np.allclose(copied[[j for j,k in enumerate(used) if k in pad_vertices]]-v[[k for k in used if k in pad_vertices]],[-29,0,0])
    assert copied[:,0].min()>-50 and copied[:,2].max()<62
    appended=['g second_pad_exact_copy']+['v '+' '.join(format(a,'.10g') for a in row) for row in copied]
    normals=np.array([[float(a) for a in l.split()[1:4]] for l in lines if l.startswith('vn ')])
    mirrored_normals=normals*np.array([1,1,-1])
    appended+=['vn '+' '.join(format(a,'.10g') for a in row) for row in mirrored_normals]
    for i in sorted(selected):
        corners=[]
        for c in faces[i]:
            parts=c.split('/');parts[0]=str(mapping[int(parts[0])-1])
            if i in reflected and len(parts)>2 and parts[2]: parts[2]=str(int(parts[2])+len(normals))
            corners.append('/'.join(parts))
        if i in reflected: corners.reverse()
        appended.append('f '+' '.join(corners))
    OUT.parent.mkdir(parents=True,exist_ok=True);shutil.copytree(SOURCE,OUT)
    (OUT/'CWHeliportTerminalPart01.obj').write_text('\n'.join(lines+appended)+'\n')
    for p in SOURCE.iterdir():
        if p.is_file() and p.suffix!='.obj':assert p.read_bytes()==(OUT/p.name).read_bytes()
    bounds=v[used].min(0),v[used].max(0)
    center=np.array([-4.081496695,.24,31.39467335])
    report={'includes_apron_lines':True,'copied_triangles':len(selected),'translation_m':[-29,0,0],
            'first_center':center.tolist(),'second_center':(center+[-29,0,0]).tolist(),
            'mirrored_surrounding_triangles':48,'mirror_axis_source_z':31.39467335,'pad_unchanged':True,'marking_y':.24}
    (OUT.parent/'validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
def prepare_ground():
    source=ROOT/'assets_game_import/heliport-double-v9/CWHeliportDoubleGroundV9'
    target=OUT.parent/'CWHeliportExtendedGroundV12'
    shutil.copytree(source,target)
    p=target/'CWHeliportTerminalPart14.obj'
    lines=p.read_text().splitlines()
    vertices=np.array([[float(a) for a in l.split()[1:4]] for l in lines if l.startswith('v ')])
    uvs=np.array([[float(a) for a in l.split()[1:3]] for l in lines if l.startswith('vt ')])
    vi=ti=0; result=[]
    for line in lines:
        if line.startswith('v '):
            row=vertices[vi].copy()
            if vi in (0,2):row[2]=62
            line='v '+' '.join(map(str,row));vi+=1
        elif line.startswith('vt '):
            row=uvs[ti].copy()
            if ti in (0,2):
                other=ti+1
                ratio=(62-vertices[other,2])/(vertices[ti,2]-vertices[other,2])
                row=uvs[other]+ratio*(uvs[ti]-uvs[other])
            line='vt '+' '.join(map(str,row));ti+=1
        result.append(line)
    p.write_text('\n'.join(result)+'\n')
if __name__=='__main__':prepare();prepare_ground()
