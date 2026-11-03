import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {assetClosure,rootPrefabNames} from '../package.mjs';
const root=fileURLToPath(new URL('../',import.meta.url));

test('real-blade modes ship the original rigid rotor mesh and exclude the synthetic disk resource graph',()=>{
 const names=rootPrefabNames();
 assert(names.every(n=>!n.includes('Mi26RotorBlur')));
 const closure=assetClosure(path.join(root,'assets/native/ImportedData'),names);
 assert.equal(closure.external.length,0);
 assert.equal(closure.assets.length,392);
 assert(closure.assets.every(f=>!f.includes('db45d3c0aa7642b690afaa3d19b3b429')&&!f.includes('Mi26RotorBlur')));
 const relative='ImportedData/CityWeaver/4a7fc21e03ce4e449bc02ae964c6ffe2/CityWeaver_4a7fc21e03ce4e449bc02ae964c6ffe2_0_CWMilMi26tcPart01 Mesh.Prefab';
 const render=fs.readFileSync(path.join(root,'assets/native',relative));
 // Stable pre-disk render identity/content, independently captured by the audit.
 assert.equal(crypto.createHash('sha256').update(render).digest('hex'),'6849b16f84bb941b125d7a75a9e7f28f39bb6c7530605777e83b7743825bcdf0');
 const rig=JSON.parse(fs.readFileSync(path.join(root,'assets_game_import/mi26-rig-v6-small/cityweaver-rig.json')));
 assert.deepEqual(rig.bones.map(b=>b.name),['Body','MainRotor','TailRotor']);
 assert.deepEqual(rig.bones.slice(1).map(b=>[b.type,b.speed,b.acceleration]),[['PoweredRotation',10,1],['PoweredRotation',20,2]]);
});
