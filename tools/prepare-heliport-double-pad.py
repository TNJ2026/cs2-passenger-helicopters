"""Build the reference's second apron bay at the existing depot spawn position."""
from pathlib import Path
import shutil, json, math
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'assets_game_import/heliport-double-v9'

def prepare():
    OUT.mkdir(exist_ok=False)
    src=ROOT/'assets_game_import/heliport-clearance-v7'
    ground=OUT/'CWHeliportDoubleGroundV9'
    site=OUT/'CWHeliportDoubleSiteV9'
    shutil.copytree(src/'CWHeliportTerminalPBRSection2Clearance',ground)
    shutil.copytree(src/'CWHeliportSiteV7Clearance',site)
    # Extend the existing concrete plane, not a second overlapping sheet.
    p=ground/'CWHeliportTerminalPart14.obj'
    lines=p.read_text().splitlines()
    verts=[list(map(float,l.split()[1:])) for l in lines if l.startswith('v ')]
    uvs=[list(map(float,l.split()[1:])) for l in lines if l.startswith('vt ')]
    vi=ti=0; result=[]
    for line in lines:
        if line.startswith('v '):
            v=verts[vi][:]
            if vi in (2,3):v[0]=-50
            line='v '+' '.join(map(str,v));vi+=1
        elif line.startswith('vt '):
            uv=uvs[ti][:]
            if ti in (2,3):
                right=ti-2; ratio=(-50-verts[right][0])/(verts[ti][0]-verts[right][0])
                uv=[uvs[right][j]+ratio*(uv[j]-uvs[right][j]) for j in range(2)]
            line='vt '+' '.join(map(str,uv));ti+=1
        result.append(line)
    p.write_text('\n'.join(result)+'\n')
    # Match the existing operational depot point (-32,26); first pad is (-3,26).
    # New geometry uses flat textures, preserving all source image/UV content.
    for name,color in [('White',(225,224,215,255)),('Yellow',(232,184,38,255))]:
        vertices=[]; faces=[]
        def quad(points):
            n=len(vertices); vertices.extend([(x,.24,z) for x,z in points])
            faces.extend([(n,n+2,n+1),(n,n+3,n+2)])
        def ring(r0,r1,a,b):
            quad([(-32+r0*math.cos(a),26+r0*math.sin(a)),(-32+r1*math.cos(a),26+r1*math.sin(a)),
                  (-32+r1*math.cos(b),26+r1*math.sin(b)),(-32+r0*math.cos(b),26+r0*math.sin(b))])
        def rect(x0,z0,x1,z1):quad([(-32+x0,26+z0),(-32+x1,26+z0),(-32+x1,26+z1),(-32+x0,26+z1)])
        if name=='White':
            for i in range(128):ring(10.65,10.95,i*math.tau/128,(i+1)*math.tau/128)
            rect(-3,-3,-2,3);rect(2,-3,3,3);rect(-2,-.5,2,.5)
        else:
            for i in range(24):
                for j in range(3):ring(9.75,10.0,(i+j*.6/3)*math.tau/24,(i+(j+1)*.6/3)*math.tau/24)
            rect(-.1,-23,.1,-12)
        stem='CWHeliportSecondPad'+name
        obj=[f'mtllib {stem}.mtl',f'usemtl {stem}']
        obj+=['v '+' '.join(map(str,v)) for v in vertices]
        obj+=['vt .5 .5','vn 0 1 0']
        obj+=['f '+' '.join(f'{k+1}/1/1' for k in face) for face in faces]
        (site/(stem+'.obj')).write_text('\n'.join(obj)+'\n')
        (site/(stem+'.mtl')).write_text(f'newmtl {stem}\nKd 1 1 1\nmap_Kd {stem}_BaseColorMap.png\n')
        Image.new('RGBA',(32,32),color).save(site/(stem+'_BaseColorMap.png'))
        for kind in ('NormalMap','MaskMap'):
            shutil.copy2(site/('CWHeliportSiteParkingWhite_'+kind+'.png'),site/(stem+'_'+kind+'.png'))
        # Bounds, triangle winding, and no zero-area faces.
        for a,b,c in faces:
            a,b,c=vertices[a],vertices[b],vertices[c]
            assert (b[2]-a[2])*(c[0]-a[0])-(b[0]-a[0])*(c[2]-a[2])>0
        assert all(-50<=x<=-21.05 and 2.728<=z<=50.729 for x,y,z in vertices)
    report={'reference':'assets/heliport-native/rs.png','pad_centers_source_xz':[[-3,26],[-32,26]],
            'center_distance_m':29,'roles':['passenger boarding','depot spawn'],
            'concrete_y_m':.2,'markings_y_m':.24,'requires_dll_update':False,
            'note':'Second pad is depot spawn, not a second passenger stop. Live verification required.'}
    (OUT/'validation.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report,indent=2))

if __name__=='__main__':prepare()
