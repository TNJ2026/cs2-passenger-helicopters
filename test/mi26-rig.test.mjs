import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=fileURLToPath(new URL('../',import.meta.url));
test('Mi-26 rig preserves source geometry, maps every face, and separates rotor axes',()=>{
 const dest=path.join(root,'assets_game_import/mi26-rig-v5');
 const rig=JSON.parse(fs.readFileSync(path.join(dest,'cityweaver-rig.json'),'utf8'));
 const settings=JSON.parse(fs.readFileSync(path.join(dest,'settings.json'),'utf8'));
 const counts={};
 for(const file of fs.readdirSync(dest).filter(n=>n.endsWith('.obj'))){
  const text=fs.readFileSync(path.join(dest,file),'utf8');
  const source=path.join(root,file.endsWith('04.obj')?'assets_game_import/special-materials-v3/CWMi26GlassV3':'assets_game_import/rotor-v4/CWMi26ParkedBodyV4',file);
  assert.equal(text,fs.readFileSync(source,'utf8'),'UV/geometry must remain byte-identical');
  assert(settings.createProceduralAnimationForPrefab.includes(file.slice(0,-4)));
  let group;
  for(const line of text.split(/\r?\n/)){
   if(line.startsWith('g '))group=line.slice(2).trim();
   if(line.startsWith('f ')){
    assert(rig.groups[group]);assert(!group.startsWith('rotor_b_'));
    const bone=rig.groups[group];counts[bone]=(counts[bone]??0)+1;
   }
  }
 }
 assert.equal(counts.MainRotor,4242);assert.equal(counts.TailRotor,2274);assert(counts.Body>50000);
 const main=rig.bones.find(b=>b.name==='MainRotor'),tail=rig.bones.find(b=>b.name==='TailRotor');
 for(const b of [main,tail]){assert.equal(b.type,'PoweredRotation');assert.equal(b.parent,0);assert(b.speed>0&&b.acceleration>0);}
 // Original hub origins and native axes, independent of prepared JSON.
 assert(Math.abs(main.position[1]-8.86779)<1e-5);assert(Math.abs(tail.position[2]+19.9166)<1e-5);
 const axis=b=>{const x=b.rotation_degrees[0]*Math.PI/180,z=b.rotation_degrees[2]*Math.PI/180;return [-Math.sin(z),Math.cos(x)*Math.cos(z),Math.sin(x)*Math.cos(z)];};
 assert(axis(main)[1]>.99);assert(axis(tail)[0]>.9999);assert(Math.abs(axis(tail)[1])<1e-6);
 assert.equal(settings.importSettings['#cwGlass.Surface'].materialTemplate,'DefaultGlass');
});
