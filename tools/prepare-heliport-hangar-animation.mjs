import fs from 'node:fs';
import path from 'node:path';
const glass=process.argv.includes('--glass');
const source='assets_game_import/heliport-clearance-v7/CWHeliportPBRSection'+(glass?'3':'2')+'Clearance';
const dest='assets_game_import/heliport-hangar-animated-v1'+(glass?'-glass':'');
fs.mkdirSync(dest,{recursive:true});
const groups={};let fixedTriangles=0,doorTriangles=0;
for(const file of fs.readdirSync(source)){
 if(file==='settings.json')continue;
 const name=file.replaceAll('CWHeliportPart','CWHangarAnimatedPart');
 if(!file.endsWith('.obj')&&!file.endsWith('.mtl')){fs.copyFileSync(path.join(source,file),path.join(dest,name));continue;}
 let lines=fs.readFileSync(path.join(source,file),'utf8').replaceAll('CWHeliportPart','CWHangarAnimatedPart').split(/\r?\n/);
 if(file.endsWith('.obj')){
  const vertices=[];let group='';const out=[];
  for(const line of lines){
   if(line.startsWith('v '))vertices.push(line.split(/\s+/).slice(1).map(Number));
   if(line.startsWith('g ')){group=line.slice(2).trim();groups[group]=/^heliport_hangar_door_(\d+)(?:_glass)?$/.test(group)?'HangarDoor'+Number(group.match(/door_(\d+)/)[1]):'HangarRoot';}
   if(group==='heliport_hangar_door_1'&&line.startsWith('f ')){
    // Door 1 also owns a full-width fixed track: keep its triangles on the root.
    const p=line.slice(2).trim().split(/\s+/).map(t=>vertices[Number(t.split('/')[0])-1]);
    if(p.some(v=>v[0]>-18.28)){
     out.push('g hangar_fixed_track',line,'g '+group);groups.hangar_fixed_track='HangarRoot';fixedTriangles++;continue;
    }
   }
   if(line.startsWith('f ')&&/^heliport_hangar_door_/.test(group))doorTriangles++;
   out.push(line);
  }
  lines=out;
 }
 fs.writeFileSync(path.join(dest,name),lines.join('\n'));
}
const bones=[{name:'HangarRoot',parent:-1,position:[0,0,0],type:'None'}];
for(let i=1;i<=16;i++)bones.push({name:'HangarDoor'+i,parent:0,position:[i<=8?-20.312+2*(i-1):11.688-2*(16-i),0,.27],type:'None'});
fs.writeFileSync(path.join(dest,'cityweaver-rig.json'),JSON.stringify({bones,groups},null,2));
const settings=JSON.parse(fs.readFileSync(path.join(source,'settings.json'),'utf8'));
settings.createProceduralAnimationForPrefab=fs.readdirSync(dest).filter(f=>f.endsWith('.obj')).map(f=>path.basename(f,'.obj'));
fs.writeFileSync(path.join(dest,'settings.json'),JSON.stringify(settings,null,2));
if((!glass&&!fixedTriangles)||!doorTriangles)throw Error('Door/track separation failed');
fs.writeFileSync(dest+'-validation.json',JSON.stringify({source,dest,bones:bones.length,groups:Object.keys(groups).length,fixedTriangles,doorTriangles},null,2));
console.log({dest,bones:bones.length,fixedTriangles,doorTriangles});
