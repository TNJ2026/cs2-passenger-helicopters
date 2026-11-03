"""Offline literal-table/blob converter. Never executes source Lua/scripts.
Outputs right-handed Y-up OBJ (source X-forward becomes OBJ -Z), PNG,
and lossless JSON source hierarchy/animation tables. Original assets untouched.
"""
import json, re, hashlib, shutil
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'assets_converted'
TOKEN = re.compile(r'\s+|--[^\n]*|"(?:\\.|[^"\\])*"|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?|[A-Za-z_][\w]*|[{}=,;\[\]]')

def parse(path):
    s = path.read_text(encoding='utf-8-sig')
    s = s[s.index('return') + 6:]
    s = re.sub(r'_\(("(?:\\.|[^"\\])*")\)',r'\1',s)
    ts = [m.group() for m in TOKEN.finditer(s) if not m.group().isspace() and not m.group().startswith('--')]
    i = 0
    def val():
        nonlocal i
        t = ts[i]; i += 1
        if t == '{':
            d = {}; a = []
            while ts[i] != '}':
                if i + 1 < len(ts) and ts[i+1] == '=':
                    k=ts[i]; i+=2; d[k]=val()
                elif ts[i] == '[':
                    i+=1; k=val(); assert ts[i:i+2]==[']','=']; i+=2; d[str(k)]=val()
                else: a.append(val())
                if ts[i] in (',',';'): i+=1
            i+=1
            if d and a: d['_array']=a
            return d if d else a
        if t.startswith('"'): return json.loads(t)
        if t in ('true','false','nil'): return {'true':True,'false':False,'nil':None}[t]
        return float(t) if any(c in t for c in '.eE') else int(t)
    return val()

def dump(path, value):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')

def resolve(base, ref, package):
    p = package/'native/base'/ref[3:] if ref.startswith('::/') else package/'native/base'/ref.lstrip('/') if ref.startswith('/') else base/ref
    p=p.resolve()
    if not p.is_relative_to(package.resolve()) or not p.is_file(): raise ValueError(f'Missing/outside reference: {ref}')
    return p

def values(node,key):
    if isinstance(node,dict):
        for k,v in node.items():
            if k==key: yield v
            yield from values(v,key)
    elif isinstance(node,list):
        for v in node: yield from values(v,key)

# Proper rotation, determinant +1: source X forward, Z up -> OBJ -Z forward,Y up.
# Source +X forward/+Y left/+Z up -> RH OBJ -Z forward/-X left/+Y up.
# This must be a proper rotation: reflecting here without changing winding
# inverts every face relative to its authored normals.
C=np.array([[0,-1,0],[0,0,1],[-1,0,0]],float)
assert np.isclose(np.linalg.det(C),1)

def mesh(path):
    d=parse(path); b=Path(str(path)+'.blob').read_bytes(); streams={}
    for k,v in d['vertexAttr'].items():
        n=int(v['count']); off=int(v['offset']); comp=int(v['numComp'])
        assert n%4==0 and off+n<=len(b)
        streams[k]=np.frombuffer(b,dtype='<f4',count=n//4,offset=off).reshape(-1,comp).astype(float)
        if k in ('position','uv0'): assert np.isfinite(streams[k]).all(), (path,k)
    subs=[]
    for sub in d['subMeshes']:
        idx={}
        for k,v in sub['indices'].items():
            n=int(v['count']);off=int(v['offset'])
            # All supplied native index streams are byte ranges of little-endian uint32.
            width=4
            assert off+n<=len(b) and n%width==0
            a=np.frombuffer(b,dtype='<u2' if width==2 else '<u4',count=n//width,offset=off).astype(int)
            assert len(a)%3==0 and a.max()<len(streams[k]), (path,k,a.max(),len(streams[k]))
            idx[k]=a.reshape(-1,3)
        assert len({len(a) for a in idx.values()})==1
        subs.append(idx)
    return streams,subs

def convert(package):
    target=OUT/package.name; target.mkdir(parents=True,exist_ok=True)
    report={'package':package.name,'models':[],'textures':0,'animations':0,'warnings':[]}
    cache={}
    for dds in sorted(package.rglob('*.dds')):
        dest=target/'textures'/dds.relative_to(package).with_suffix('.png')
        dest.parent.mkdir(parents=True,exist_ok=True)
        with Image.open(dds) as im:
            im.load(); im.save(dest)
            cache[dds.resolve()]=dest
        report['textures']+=1
    for kind in ('mdl','mtl','msh','ani'):
        for src in sorted(package.rglob('*.'+kind)):
            dump(target/'metadata'/src.relative_to(package).with_suffix('.'+kind+'.json'),parse(src))
            if kind=='ani': report['animations']+=1
    for mdl in sorted(package.rglob('*.mdl')):
        data=parse(mdl)
        for li,lod in enumerate(data['lods']):
            dest=target/'import'/mdl.stem/f'LOD{li}';dest.mkdir(parents=True,exist_ok=True)
            mats={}; groups=[]; total=0; removed=0; bounds=[]
            def walk(node,parent):
                nonlocal total,removed
                if not isinstance(node,dict): return
                transform=parent@np.array(node.get('transf',np.eye(4).flatten(order='F')),float).reshape(4,4,order='F')
                if 'mesh' in node:
                    mp=resolve(mdl.parent,node['mesh'],package); streams,subs=mesh(mp)
                    pos=streams['position']@transform[:3,:3].T+transform[:3,3]
                    pos=pos@C.T
                    nr=streams['normal']@np.linalg.inv(transform[:3,:3]);nr=nr@C.T
                    lengths=np.linalg.norm(nr,axis=1)
                    invalid_normals=not (np.isfinite(nr).all() and (lengths>1e-10).all())
                    if not invalid_normals:nr/=lengths[:,None]
                    for si,idx in enumerate(subs):
                        matref=node['materials'][si]; material=resolve(mdl.parent,matref,package)
                        mats.setdefault(material,len(mats)+1)
                        p=pos[idx['position']]; good=np.linalg.norm(np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]),axis=1)>1e-6
                        removed+=int((~good).sum());idx={k:a[good] for k,a in idx.items()}
                        if not len(idx['position']):continue
                        normals=nr
                        if invalid_normals:
                            p=pos[idx['position']]; normals=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]);normals/=np.linalg.norm(normals,axis=1)[:,None]
                            if np.linalg.det(transform[:3,:3])<0:normals=-normals
                            idx['normal']=np.repeat(np.arange(len(normals))[:,None],3,axis=1)
                            report['warnings'].append('Rebuilt invalid source normals: '+str(mp.relative_to(package)))
                        groups.append((mats[material],node.get('name','part'),pos,normals,streams['uv0'],idx,np.linalg.det(transform[:3,:3])<0))
                        total+=len(idx['position']);bounds.append(pos[idx['position']].reshape(-1,3))
                for child in node.get('children',[]):walk(child,transform)
            walk(lod['node'],np.eye(4))
            # One file per material follows the native asset pipeline's texture naming.
            for material,mi in mats.items():
                name=f'{mdl.stem}_{mi:02d}';md=parse(material)
                textures=[]
                for ref in values(md,'fileName'):
                    src=resolve(material.parent,ref,package)
                    if src.suffix.lower()=='.dds':textures.append(src)
                params=md.get('params',{})
                color=[]
                for key,value in params.items():
                    if 'albedo' in key or 'diffuse' in key: color+=list(values(value,'fileName'))
                mtl=[f'newmtl {name}','Kd 1 1 1']
                if color:
                    src=resolve(material.parent,color[0],package);png=dest/(name+'_BaseColorMap.png')
                    shutil.copy2(cache[src],png);mtl+=['map_Kd '+png.name]
                normal=list(values(params.get('map_normal',{}),'fileName'))
                if normal:
                    src=resolve(material.parent,normal[0],package);png=dest/(name+'_NormalMap.png')
                    shutil.copy2(cache[src],png);mtl+=['map_Bump '+png.name]
                (dest/(name+'.mtl')).write_text('\n'.join(mtl)+'\n',encoding='utf-8')
                with (dest/(name+'.obj')).open('w',encoding='utf-8') as f:
                    f.write(f'# Y-up metres; native hierarchy baked; animation metadata retained separately\nmtllib {name}.mtl\nusemtl {name}\n')
                    offset=[0,0,0]
                    for mid,group,pos,nr,uv,idx,reverse in groups:
                        if mid!=mi:continue
                        f.write('g '+re.sub(r'[^\w-]','_',group)+'\n')
                        for tag,arr in [('v',pos),('vt',uv),('vn',nr)]:
                            for row in arr:f.write(tag+' '+' '.join(format(float(v),'.9g') for v in row)+'\n')
                        for pi,ti,ni in zip(idx['position'],idx['uv0'],idx['normal']):
                            order=[0,2,1] if reverse else [0,1,2]
                            f.write('f '+' '.join(f'{pi[j]+offset[0]+1}/{ti[j]+offset[1]+1}/{ni[j]+offset[2]+1}' for j in order)+'\n')
                        offset=[offset[0]+len(pos),offset[1]+len(uv),offset[2]+len(nr)]
            allp=np.concatenate(bounds)
            files=list(dest.iterdir());size=sum(p.stat().st_size for p in files)
            assert len(files)<=256 and size<=512*1024**2
            report['models'].append({'model':mdl.stem,'lod':li,'triangles':total,'degenerate_triangles_removed':removed,'materials':len(mats),'bounds':[allp.min(axis=0).tolist(),allp.max(axis=0).tolist()],'directory':str(dest.relative_to(OUT)),'bytes':size})
            print(package.name,mdl.stem,li,total,flush=True)
    report['warnings']+=['OBJ is static; source node hierarchy and animation tables are preserved as JSON, not game-ready animation bindings.','All DDS mip0 images converted without resizing; packed material maps retained in textures and require target-shader channel mapping.','Only base color and source normal maps are linked in import folders; native shader effects/scripts are not portable.']
    dump(target/'conversion-report.json',report)
    return report

if __name__=='__main__':
    OUT.mkdir(exist_ok=True)
    results=[convert(p) for p in sorted((ROOT/'assets').iterdir()) if p.is_dir()]
    dump(OUT/'conversion-report.json',results)
    print('DONE',sum(len(r['models']) for r in results),'model LODs')
