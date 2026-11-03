import fs from 'node:fs';
import path from 'node:path';
const sources=['heliport-clearance-v7/CWHeliportPBRSection1Clearance','heliport-hangar-animated-v1','heliport-hangar-animated-v1-glass','heliport-clearance-v7/CWHeliportSiteV7Clearance','heliport-complete-markings-v11/CWHeliportCompleteMarkingsV11','heliport-double-v9/CWHeliportDoubleGroundV9'];
const scale=104/102,report=[];
for(const [index,source] of sources.entries()){
 const src=path.join('assets_game_import',source),dest=path.join('assets_game_import/heliport-width104',`Part${index}`);
 if(fs.existsSync(dest))throw Error('Output already exists: '+dest);
 fs.cpSync(src,dest,{recursive:true});let vertices=0;
 for(const name of fs.readdirSync(dest)){
  if(!name.endsWith('.obj'))continue;
  const text=fs.readFileSync(path.join(src,name),'utf8');
  fs.writeFileSync(path.join(dest,name),text.split(/\r?\n/).map(line=>{
   if(line.startsWith('v ')){const v=line.trim().split(/\s+/).slice(1).map(Number);v[2]*=scale;vertices++;return 'v '+v.join(' ');}
   if(line.startsWith('vn ')){const v=line.trim().split(/\s+/).slice(1).map(Number);v[2]/=scale;const n=Math.hypot(...v);return 'vn '+v.map(x=>x/n).join(' ');}
   return line;
  }).join('\n'));
 }
 const rigFile=path.join(dest,'cityweaver-rig.json');
 if(fs.existsSync(rigFile)){const rig=JSON.parse(fs.readFileSync(rigFile));for(const bone of rig.bones)bone.position[2]*=scale;fs.writeFileSync(rigFile,JSON.stringify(rig,null,2));}
 report.push({index,source:src,dest,scale,vertices});
}
fs.writeFileSync('assets_game_import/heliport-width104/preparation.json',JSON.stringify(report,null,2));
console.log(JSON.stringify(report));
