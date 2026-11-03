import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';

// Reuse the authored TF3 blurred rotors, including their alpha and hub geometry.
// No generated replacement art and no changes to the existing source assets.
const root=fileURLToPath(new URL('../',import.meta.url));
const source=path.join(root,'assets_game_import/special-materials-v3/CWMi26BodyV3');
const dest=path.join(root,'assets_game_import/mi26-rotor-blur-v1');
if(fs.existsSync(dest))throw Error('Prepared blur assets already exist; do not overwrite.');
const rig=JSON.parse(fs.readFileSync(path.join(root,'assets_game_import/mi26-rig-v6-small/cityweaver-rig.json'),'utf8'));
rig.bones=rig.bones.map(b=>({...b,name:b.name==='Body'?'Body':b.name+'Blur'}));
rig.groups={rotor_b_top:'MainRotorBlur',rotor_b_bck:'TailRotorBlur'};
// The texture is an integrated motion image: rotating it rapidly recreates
// aliasing. The rendering controller supplies a slower visible angular phase.
for(const b of rig.bones)if(b.type==='PoweredRotation'){b.speed=.1;b.acceleration=1;}
const settings=JSON.parse(fs.readFileSync(path.join(root,'assets_game_import/mi26-rig-v6-small/settings.json'),'utf8'));
settings.createProceduralAnimationForPrefab=[];
settings.assetMapping={...settings.assetMapping};delete settings.assetMapping.CWMilMi26tcPart04;
const counts={};
fs.mkdirSync(dest,{recursive:true});
for(const part of ['07','08']) {
  const from='CWMilMi26tcPart'+part,to='Mi26RotorBlur'+part;
  const original=fs.readFileSync(path.join(source,from+'.obj'),'utf8');
  let faces=0;
  const obj=original.split(/\r?\n/).map(line=>{
    if(line.startsWith('f '))faces++;
    if(line.startsWith('v '))return 'v '+line.trim().split(/\s+/).slice(1).map(v=>Number(v)*2/3).join(' ');
    return line.replaceAll(from,to);
  }).join('\n');
  assert(faces>0);counts[to]=faces;
  fs.writeFileSync(path.join(dest,to+'.obj'),obj);
  fs.writeFileSync(path.join(dest,to+'.mtl'),fs.readFileSync(path.join(source,from+'.mtl'),'utf8').replaceAll(from,to));
  for(const suffix of ['_BaseColorMap.png','_NormalMap.png','_MaskMap.png'])fs.copyFileSync(path.join(source,from+suffix),path.join(dest,to+suffix));
  settings.assetMapping[to]=['#cwGlass.Surface'];
  settings.createProceduralAnimationForPrefab.push(to);
}
fs.writeFileSync(path.join(dest,'settings.json'),JSON.stringify(settings,null,2));
fs.writeFileSync(path.join(dest,'cityweaver-rig.json'),JSON.stringify(rig,null,2));
fs.writeFileSync(path.join(dest,'preparation.json'),JSON.stringify({source:path.relative(root,source),scale:2/3,counts,texture_alpha:'Original RGBA retained; DefaultGlass, no alpha clip',controller:'Per-visible-vehicle flight phase, aircraft speed, rotor momentum and bounded angular motion'},null,2));
console.log(JSON.stringify({dest,counts}));
