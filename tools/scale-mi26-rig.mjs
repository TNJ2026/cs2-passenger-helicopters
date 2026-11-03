import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
const src='assets_game_import/mi26-rig-v5',dest='assets_game_import/mi26-rig-v6-small',factor=2/3;
if(fs.existsSync(dest))throw Error('Output exists');
fs.cpSync(src,dest,{recursive:true});
let vertices=0;
for(const file of fs.readdirSync(src).filter(f=>f.endsWith('.obj'))){
 const lines=fs.readFileSync(path.join(src,file),'utf8').split(/\r?\n/);
 const scaled=lines.map(line=>{
  if(!line.startsWith('v '))return line;
  const p=line.trim().split(/\s+/).slice(1).map(Number);assert.equal(p.length,3);assert(p.every(Number.isFinite));vertices++;
  return 'v '+p.map(v=>v*factor).join(' ');
 });
 assert.equal(lines.filter(l=>l.startsWith('f ')).length,scaled.filter(l=>l.startsWith('f ')).length);
 fs.writeFileSync(path.join(dest,file),scaled.join('\n'));
}
const rig=JSON.parse(fs.readFileSync(path.join(src,'cityweaver-rig.json'),'utf8'));
for(const b of rig.bones)b.position=b.position.map(v=>v*factor);
fs.writeFileSync(path.join(dest,'cityweaver-rig.json'),JSON.stringify(rig,null,2));
fs.writeFileSync(path.join(dest,'preparation.json'),JSON.stringify({source:src,scale:factor,vertices,bones:rig.bones,unchanged:'UVs, normals, triangles, materials, rotation axes and animation speed'},null,2));
console.log(JSON.stringify({dest,factor,vertices}));
