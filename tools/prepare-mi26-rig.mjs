import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
const root=process.cwd(),dest=path.join(root,'assets_game_import/mi26-rig-v5');
if(fs.existsSync(dest))throw Error('Output exists; preserve prepared assets.');
const metadata=JSON.parse(fs.readFileSync('assets_converted/mil-mi26tc-native/metadata/native/base/vehicle/helicopter/mil_mi26tc/mil_mi26tc.mdl.json','utf8'));
const identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
function mul(a,b){return Array.from({length:16},(_,i)=>{const r=i%4,c=Math.floor(i/4);return [0,1,2,3].reduce((s,k)=>s+a[k*4+r]*b[c*4+k],0);});}
const pivots={};
function walk(n,parent){if(!n||Array.isArray(n))return;const t=mul(parent,n.transf??identity);if(['rotor_top','rotor_bck'].includes(n.name)){
 // Source +X forward/+Z up -> native Unity +Z forward/+Y up.
 const axis=[-t[1],t[2],t[0]],length=Math.hypot(...axis);const [x,y,z]=axis.map(v=>v/length);
 pivots[n.name]={position:[-t[13],t[14],t[12]],rotation_degrees:[Math.atan2(z,y)*180/Math.PI,0,Math.asin(-x)*180/Math.PI]};
 }for(const c of n.children??[])walk(c,t);}
walk(metadata.lods[0].node,identity);
assert.equal(Object.keys(pivots).length,2);
const bones=[{name:'Body',parent:-1,position:[0,0,0],type:'None'},
 {name:'MainRotor',parent:0,...pivots.rotor_top,type:'PoweredRotation',speed:10,acceleration:1},
 {name:'TailRotor',parent:0,...pivots.rotor_bck,type:'PoweredRotation',speed:20,acceleration:2}];
const counts={},groups={},files=[];
fs.mkdirSync(dest,{recursive:true});
for(const dir of ['assets_game_import/rotor-v4/CWMi26ParkedBodyV4','assets_game_import/special-materials-v3/CWMi26GlassV3']){
 for(const entry of fs.readdirSync(dir)){if(entry==='settings.json')continue;const source=path.join(dir,entry);if(!fs.statSync(source).isFile())continue;
 if(entry.endsWith('.obj')){
  let group;for(const line of fs.readFileSync(source,'utf8').split(/\r?\n/)){
   if(line.startsWith('g ')){group=line.slice(2).trim();groups[group]=group==='rotor_top'?'MainRotor':group==='rotor_bck'?'TailRotor':'Body';}
   if(line.startsWith('f ')){assert(group,'All faces need authored groups');assert(!['rotor_b_top','rotor_b_bck'].includes(group),'No simultaneous blur geometry');counts[group]=(counts[group]??0)+1;}
  }files.push(entry.slice(0,-4));
 }fs.copyFileSync(source,path.join(dest,entry));
 }
}
assert(counts.rotor_top>4000&&counts.rotor_bck>2000);
const settings=JSON.parse(fs.readFileSync('assets_game_import/special-materials-v3/CWMi26GlassV3/settings.json','utf8'));
settings.createProceduralAnimationForPrefab=files;
fs.writeFileSync(path.join(dest,'settings.json'),JSON.stringify(settings,null,2));
fs.writeFileSync(path.join(dest,'cityweaver-rig.json'),JSON.stringify({bones,groups},null,2));
const report={bones,counts,files,triangles:Object.values(counts).reduce((a,b)=>a+b,0),limitations:['Real rotating blades; source static blur discs excluded. No automatic runtime blur-disc switching.','Native import and flight verification pending.'],source:'Original authored rotor transforms, converted to Unity axes; source mesh UVs and materials retained.'};
fs.writeFileSync(path.join(dest,'preparation.json'),JSON.stringify(report,null,2));
console.log(JSON.stringify(report,null,2));
