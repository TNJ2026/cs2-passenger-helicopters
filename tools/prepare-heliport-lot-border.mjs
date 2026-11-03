import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=fileURLToPath(new URL('../',import.meta.url));
const source=path.join(root,'assets_game_import/heliport-double-v9/CWHeliportDoubleGroundV9');
const output=path.join(root,'assets_game_import/heliport-lot-104/CWHeliportLotBorder104');
fs.mkdirSync(output,{recursive:true});
const name='CWHeliportLotBorder104';
for(const suffix of ['BaseColorMap','NormalMap','MaskMap'])fs.copyFileSync(path.join(source,`CWHeliportTerminalPart14_${suffix}.png`),path.join(output,`${name}_${suffix}.png`));
fs.copyFileSync(path.join(source,'settings.json'),path.join(output,'settings.json'));
// Source Z [-50,52] becomes local X [-51,51]. Add only the outside metre.
const vertices=[],faces=[];
for(const [lo,hi] of [[-51,-50],[52,53]]){
 const n=vertices.length;
 vertices.push([-50,.2,lo],[-50,.2,hi],[62,.2,hi],[62,.2,lo]);
 faces.push([n+1,n+2,n+3],[n+1,n+3,n+4]);
}
const lines=[`mtllib ${name}.mtl`,`usemtl ${name}`,'g lot_border'];
lines.push(...vertices.map(v=>'v '+v.join(' ')));
// Original concrete texture density: approximately one repeat per six metres.
lines.push(...vertices.map(([x,y,z])=>`vt ${z/6} ${-x/6}`),'vn 0 1 0');
lines.push(...faces.map(f=>'f '+f.map(i=>`${i}/${i}/1`).join(' ')));
fs.writeFileSync(path.join(output,`${name}.obj`),lines.join('\n')+'\n');
fs.writeFileSync(path.join(output,`${name}.mtl`),`newmtl ${name}\nKd 1 1 1\nmap_Kd ${name}_BaseColorMap.png\nmap_Bump ${name}_NormalMap.png\n`);
console.log(output);
