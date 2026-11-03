"""Record source animation pivots/axes; does not claim a playable rig."""
import json,importlib.util
from pathlib import Path
import numpy as np
root=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('conv',Path(__file__).with_name('convert-native-assets.py'))
conv=importlib.util.module_from_spec(spec);spec.loader.exec_module(conv)
package=root/'assets/mil-mi26tc-native'
mdl=next(package.rglob('*.mdl'));data=conv.parse(mdl)
u=np.diag([1,1,-1])@conv.C
records=[]
def walk(node,parent):
    world=parent@np.array(node.get('transf',np.eye(4).flatten(order='F'))).reshape(4,4,order='F')
    if node.get('animations'):
        record={'group':node.get('name'),'pivot_unity_m':(u@world[:3,3]).tolist(),'animations':[]}
        for event,animation in node['animations'].items():
            ref=animation.get('params',{}).get('id')
            if not ref:continue
            source=conv.resolve(mdl.parent,ref,package)
            value=conv.parse(source)
            record['animations'].append({'event':event,'source':str(source.relative_to(package)),
                'duration_ms':max(value.get('times',[0])),
                'first_transform':value.get('transfs',[None])[0],
                'last_transform':value.get('transfs',[None])[-1]})
        records.append(record)
    for child in node.get('children',[]):walk(child,world)
walk(data['lods'][0]['node'],np.eye(4))
result={'source':str(mdl),'status':'source audit only; no game animation configured',
 'notes':['Rigid importer currently supports None/RollingTire/SteeringTire only.',
          'Native PropellerRotation responds to movement along its axis, unsuitable for helicopter hover spin.',
          'Native vehicle PoweredRotation supports time rotation but requires oriented bones and speed/acceleration settings.',
          'Existing door controller rotates around Y only; source door tracks include other axes and translations.',
          'Solid and blurred rotor groups represent alternate states and must not render simultaneously as opaque geometry.'],
 'animated_nodes':records}
out=root/'artifacts/native-import-20261027/mi26-animation-source-audit.json'
out.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print('Recorded',len(records),'animated nodes:',out)
