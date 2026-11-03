import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath,pathToFileURL} from 'node:url';

const moduleRoot=path.dirname(fileURLToPath(import.meta.url));
const repository=moduleRoot;
export function inside(root,file) {
  const relative=path.relative(root,file);
  return relative!==''&&!relative.startsWith('..')&&!path.isAbsolute(relative);
}
export function rawId(bytes) {
  return [...bytes].map(v=>v.toString(16).padStart(2,'0').split('').reverse().join('')).join('');
}
export function references(file,bytes) {
  if(path.extname(file).toLowerCase()==='.prefab')
    return [...bytes.toString('utf8').matchAll(/CID:([a-f0-9]{32})/gi)].map(m=>m[1].toLowerCase());
  if(path.extname(file).toLowerCase()!=='.surface')return [];
  const result=[];
  for(const property of ['_BaseColorMap','_ControlMask','_MaskMap','_NormalMap','_EmissiveColorMap']) {
    const key=Buffer.from(property),offset=bytes.indexOf(key),start=offset+key.length;
    if(offset>=0&&bytes[start]===255&&start+17<=bytes.length)result.push(rawId(bytes.subarray(start+1,start+17)));
  }
  return result;
}
function walk(root) {
  const result=[];
  const visit=directory=>{for(const entry of fs.readdirSync(directory,{withFileTypes:true})) {
    const file=path.join(directory,entry.name),stat=fs.lstatSync(file);
    if(stat.isSymbolicLink())throw Error('Linked asset path: '+file);
    if(stat.isDirectory())visit(file);else if(stat.isFile())result.push(file);
  }};
  visit(root);return result;
}
export function assetClosure(root,names) {
  const all=walk(root),byId=new Map();
  for(const cid of all.filter(p=>p.endsWith('.cid'))) {
    const id=fs.readFileSync(cid,'utf8').trim().toLowerCase(),file=cid.slice(0,-4);
    if(!/^[a-f0-9]{32}$/.test(id)||!fs.existsSync(file))continue;
    if(byId.has(id)&&byId.get(id)!==file)throw Error('Duplicate asset CID: '+id);
    byId.set(id,file);
  }
  const queue=names.map(name=>{
    const found=all.filter(p=>path.basename(p)===name+'.Prefab');
    if(found.length!==1)throw Error('Expected exactly one source prefab: '+name);
    return found[0];
  });
  const selected=new Set(),external=new Set();
  while(queue.length) {
    const file=queue.pop();if(selected.has(file))continue;
    if(!inside(root,file)||!fs.existsSync(file+'.cid'))throw Error('Invalid native asset: '+file);
    selected.add(file);
    if(!/\.(prefab|surface)$/i.test(file))continue;
    for(const id of references(file,fs.readFileSync(file))) {
      if(byId.has(id))queue.push(byId.get(id));else external.add(id);
    }
  }
  return {assets:[...selected].sort(),external:[...external].sort()};
}
async function sha(file) {
  const hash=crypto.createHash('sha256');for await(const chunk of fs.createReadStream(file))hash.update(chunk);
  return hash.digest('hex');
}
function nativeFiles(name) {
  return [name+'.dll',name+'.pdb',name+'_win_x86_64.dll',name+'_win_x86_64.pdb',name+'_mac_x86_64.bundle',name+'_linux_x86_64.so'];
}
export function rootPrefabNames() {
  const defaults=JSON.parse(fs.readFileSync(path.join(repository,'assets/vehicle-defaults/mi26.json')));
  const footprint=fs.readFileSync(path.join(moduleRoot,'Runtime/Transport/ImportedHeliportFootprintSystem.cs'),'utf8');
  return [...new Set([
    'CityWeaver_850b7b8e4f8b4a679fa2623a7c3dd3e0','CityWeaver_7e149f3fb9574d47adfac7977c83d900',
    'CityWeaver_4a7fc21e03ce4e449bc02ae964c6ffe2',...defaults.parts.map(p=>p.render_prefab),
    ...[...footprint.matchAll(/"(CityWeaver_[^"]+ Mesh)"/g)].map(m=>m[1])
  ])];
}
export async function packageMod({userData,staged,output,includeMcp=false}) {
  if(includeMcp)throw Error('The MCP adapter is a separate project and is not bundled with this mod.');
  const root=path.join(userData,'ImportedData');
  if(!fs.existsSync(root))throw Error('ImportedData source missing: '+root);
  if(fs.existsSync(output))throw Error('Package output already exists; choose a new output directory.');
  const names=rootPrefabNames();
  const closure=assetClosure(root,names),records=[];
  const add=(source,relative,kind)=>records.push({source,path:relative.replaceAll('\\','/'),kind});
  for(const file of closure.assets)for(const source of [file,file+'.cid'])add(source,path.join('ImportedData',path.relative(root,source)),'asset');
  for(const name of ['CityWeaver','TransportFever3Helicopters.Runtime','PassengerHelicopters'])for(const file of nativeFiles(name))add(path.join(staged,name,file),path.join('Mods',name,file),'standalone');
  for(const record of records) {
    if(!fs.statSync(record.source).isFile())throw Error('Source file missing: '+record.source);
    record.bytes=fs.statSync(record.source).size;record.sha256=await sha(record.source);
  }
  fs.mkdirSync(output,{recursive:true});
  const hashes=new Map();
  for(const record of records) {
    const destination=path.resolve(output,record.path);
    if(!inside(output,destination))throw Error('Package path escaped output.');
    fs.mkdirSync(path.dirname(destination),{recursive:true});
    // Copy the first content instance so package files never alias live game
    // files. Package-internal hard links reduce repeated material texture data.
    if(hashes.has(record.sha256))fs.linkSync(hashes.get(record.sha256),destination);
    else {fs.copyFileSync(record.source,destination,fs.constants.COPYFILE_EXCL);hashes.set(record.sha256,destination);}
    if(await sha(destination)!==record.sha256)throw Error('Package verification failed: '+record.path);
  }
  const manifest={name:'Passenger Helicopters',display_name:'Transport Fever 3 Helicopters',display_name_zh:'狂热运输3直升机客运',model_source_game:'Transport Fever 3',version:JSON.parse(fs.readFileSync(path.join(moduleRoot,'package.json'),'utf8')).version,created_at:new Date().toISOString(),
    runtime_assembly:'TransportFever3Helicopters.Runtime',compatibility_assembly:'CityWeaver',bootstrap_assembly:'PassengerHelicopters',mcp_required:false,
    source_models:['assets/helicopter/heliport','assets/helicopter/helipad','assets/helicopter/mil_mi26tc'],
    root_prefabs:names,asset_count:closure.assets.length,external_native_asset_cids:closure.external,
    files:records.map(({source,...record})=>record)};
  fs.writeFileSync(path.join(output,'manifest.json'),JSON.stringify(manifest,null,2));
  for(const name of ['Install.ps1','README.md','README.en.md','PLAYER-GUIDE.zh-CN.md','PLAYER-GUIDE.en.md','CREDITS.md','CHANGELOG.md','PUBLICATION.md'])fs.copyFileSync(path.join(moduleRoot,name),path.join(output,name));
  fs.writeFileSync(path.join(output,'package-complete.json'),JSON.stringify({files:records.length,asset_count:closure.assets.length,logical_bytes:records.reduce((n,r)=>n+r.bytes,0),unique_bytes:[...hashes.keys()].reduce((n,key)=>n+records.find(r=>r.sha256===key).bytes,0)},null,2));
  return {output,asset_count:closure.assets.length,files:records.length,mcp_required:false};
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href) {
  const argument=(name,fallback)=>{const i=process.argv.indexOf(name);return i<0?fallback:process.argv[i+1];};
  const userData=path.resolve(argument('--user-data',path.join(repository,'assets/native')));
  const staged=path.resolve(argument('--staged',path.join(repository,'artifacts/staged')));
  const output=path.resolve(argument('--output',path.join(repository,'artifacts/packages/PassengerHelicopters-1.1.6')));
  console.log(JSON.stringify(await packageMod({userData,staged,output,includeMcp:process.argv.includes('--with-mcp')}),null,2));
}

