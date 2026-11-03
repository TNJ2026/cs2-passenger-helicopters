"""Reconstruct reference markings as mesh paint; source package lacks layout decals."""
from pathlib import Path
import json, math, shutil
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'assets_game_import/reference-v5/CWHelipadMarkingsV5'

def prepare():
    OUT.mkdir(parents=True, exist_ok=False)
    source = ROOT/'assets_game_import/pbr-v2-uv/CWHelipadPBRSection1/CWHelipadPart04.obj'
    lines = source.read_text().splitlines()
    v = np.array([[float(x) for x in s.split()[1:]] for s in lines if s.startswith('v ')])
    f = np.array([[int(x.split('/')[0])-1 for x in s.split()[1:]] for s in lines if s.startswith('f ')])
    t = v[f]
    flat = t[(np.ptp(t[:,:,1],axis=1)<0.001) & (t[:,:,1].mean(axis=1)>4.9)]
    low, high = flat.min(axis=(0,1)), flat.max(axis=(0,1))
    cx, cz = ((low+high)/2)[[0,2]]
    y = high[1]+0.015
    radius = (high[0]-low[0])/2
    assert 9.7 < radius < 9.9 and 4.99 < y < 5.03
    report = {'source':str(source.relative_to(ROOT)), 'center':[cx,float(y),cz], 'radius':radius,
              'method':'Reference-based mesh paint; dimensions inferred from screenshot, not original layout data.'}
    for name, color in [('White',(230,231,219,255)),('Yellow',(242,168,16,255))]:
        part = 'CWHelipadPaint'+name
        vertices=[]; faces=[]
        def quad(points):
            start=len(vertices)
            vertices.extend([(cx+x,y,cz+z) for x,z in points])
            # Clockwise in XZ gives upward Y normals.
            faces.extend([(start,start+2,start+1),(start,start+3,start+2)])
        def ring(inner,outer,start=0,end=2*math.pi,steps=160):
            for a,b in zip(np.linspace(start,end,steps+1)[:-1],np.linspace(start,end,steps+1)[1:]):
                quad([(inner*math.cos(a),inner*math.sin(a)),(outer*math.cos(a),outer*math.sin(a)),
                      (outer*math.cos(b),outer*math.sin(b)),(inner*math.cos(b),inner*math.sin(b))])
        def rect(x0,z0,x1,z1): quad([(x0,z0),(x1,z0),(x1,z1),(x0,z1)])
        if name=='White':
            ring(radius*.91,radius*.955)
            rect(-2.5,-2.7,-1.55,2.7);rect(1.55,-2.7,2.5,2.7);rect(-1.55,-.48,1.55,.48)
        else:
            ring(radius*.835,radius*.865)
            for i in range(24):
                a=i*2*math.pi/24
                ring(radius*.755,radius*.78,a,a+2*math.pi/24*.52,5)
        # Check every triangle is finite, nondegenerate and facing up.
        triangles=np.array(vertices)[np.array(faces)]
        normals=np.cross(triangles[:,1]-triangles[:,0],triangles[:,2]-triangles[:,0])
        assert np.isfinite(triangles).all() and (normals[:,1]>0).all()
        obj=[f'mtllib {part}.mtl',f'usemtl {part}']
        obj += ['v '+' '.join(format(float(x),'.9g') for x in row) for row in vertices]
        obj += ['vt 0.5 0.5','vn 0 1 0']
        obj += ['f '+' '.join(f'{i+1}/1/1' for i in face) for face in faces]
        (OUT/(part+'.obj')).write_text('\n'.join(obj)+'\n')
        (OUT/(part+'.mtl')).write_text(f'newmtl {part}\nKd 1 1 1\nmap_Kd {part}_BaseColorMap.png\n')
        for suffix,value in [('BaseColorMap',color),('MaskMap',(0,0,0,20)),('NormalMap',(128,128,255,255))]:
            Image.new('RGBA',(32,32),value).save(OUT/(part+'_'+suffix+'.png'))
        report[name]={'triangles':len(faces),'color':color}
    shutil.copy2(ROOT/'assets_game_import/pbr-v2-uv/CWHelipadPBRSection1/settings.json',OUT/'settings.json')
    (OUT.parent/'validation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report,indent=2))

if __name__=='__main__': prepare()
