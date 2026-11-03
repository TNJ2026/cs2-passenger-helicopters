import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {assetClosure} from '../package.mjs';
const root=fileURLToPath(new URL('../',import.meta.url));
const source=path.join(process.env.CSII_USERDATAPATH,'ImportedData');
const destination=path.join(root,'assets/native/ImportedData');
const name='CityWeaver_db45d3c0aa7642b690afaa3d19b3b429_0_Mi26RotorBlur07 Mesh';
const closure=assetClosure(source,[name]);
if(closure.external.length)throw Error('Unexpected external blur dependency');
const catalogPath=path.join(root,'assets/helicopter/publication-assets.json');
const catalog=JSON.parse(fs.readFileSync(catalogPath,'utf8'));
for(const file of closure.assets)for(const sourceFile of [file,file+'.cid']){
  const target=path.join(destination,path.relative(source,sourceFile));
  fs.mkdirSync(path.dirname(target),{recursive:true});
  fs.copyFileSync(sourceFile,target,fs.constants.COPYFILE_EXCL);
}
const render=closure.assets.find(f=>path.extname(f).toLowerCase()==='.prefab');
const cid=fs.readFileSync(render+'.cid','utf8').trim();
if(cid!=='108b8e1bfc994f74d963205a3e989ce9')throw Error('Wrong native blur identity');
catalog.prefabs.push({name,cid});
fs.writeFileSync(catalogPath,JSON.stringify(catalog,null,2)+'\n');
console.log(JSON.stringify({assets:closure.assets.length,files:closure.assets.length*2,render:name,cid}));
