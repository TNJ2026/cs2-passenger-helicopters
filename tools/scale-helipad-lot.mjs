import fs from 'node:fs';import path from 'node:path';
const sx=32/28.954733848571777,sz=32/32.11713409423828;
const sources=['pbr-v2-uv/CWHelipadPBRSection1','special-materials-v3/CWHelipadBodyV3','special-materials-v3/CWHelipadLightV3','reference-v5/CWHelipadMarkingsV5'];
const report=[];
for(const [i,source] of sources.entries()){
 const src=path.join('assets_game_import',source),dest=`assets_game_import/helipad-lot32/Part${i}`;
 if(fs.existsSync(dest))throw Error('Output exists');fs.cpSync(src,dest,{recursive:true});
 for(const name of fs.readdirSync(dest).filter(n=>n.endsWith('.obj'))){
  const text=fs.readFileSync(path.join(src,name),'utf8');
  fs.writeFileSync(path.join(dest,name),text.split(/\r?\n/).map(l=>{
   if(l.startsWith('v ')){const v=l.trim().split(/\s+/).slice(1).map(Number);v[0]*=sx;v[2]*=sz;return 'v '+v.join(' ');}
   if(l.startsWith('vn ')){const v=l.trim().split(/\s+/).slice(1).map(Number);v[0]/=sx;v[2]/=sz;const n=Math.hypot(...v);return 'vn '+v.map(x=>x/n).join(' ');}
   return l;
  }).join('\n'));
 }
 report.push({src,dest,sx,sz});
}
fs.writeFileSync('assets_game_import/helipad-lot32/preparation.json',JSON.stringify(report,null,2));console.log({sx,sz});
