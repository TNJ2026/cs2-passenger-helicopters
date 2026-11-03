import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {assetClosure,inside,rootPrefabNames} from './package.mjs';

const moduleRoot=path.dirname(fileURLToPath(import.meta.url));
const repository=moduleRoot;
const docs=['README.md','README.en.md','PLAYER-GUIDE.zh-CN.md','PLAYER-GUIDE.en.md','CREDITS.md','CHANGELOG.md','PUBLICATION.md'];
const artwork=['Thumbnail.jpg','Screenshot-airport.jpg','Screenshot-helipad.jpg','Screenshot-menu.jpg','Screenshot-line.jpg'];
const catalogFile=path.join(repository,'assets/helicopter/publication-assets.json');
export async function sha256(file) {
  const hash=crypto.createHash('sha256');for await(const bytes of fs.createReadStream(file))hash.update(bytes);
  return hash.digest('hex');
}
export function verifyCatalog(root,closure,catalog) {
  // Serialized dependency files can carry a content-hash suffix; their native
  // prefab name, rather than the filename, is the identity used at runtime.
  const prefabs=closure.assets.filter(p=>path.extname(p).toLowerCase()==='.prefab').map(file=>{
    // Native prefab bodies contain $fstrref tokens and are not strict JSON.
    // The root name is a JSON string in the header preceding components.
    const header=fs.readFileSync(file,'utf8').split(/"components"\s*:/u,1)[0];
    const name=header.match(/"name"\s*:\s*("(?:\\.|[^"\\])*")/u)?.[1];
    if(!name)throw Error('Native prefab name missing: '+file);
    return {file,name:JSON.parse(name)};
  });
  for(const {name,cid} of catalog.prefabs) {
    const matches=prefabs.filter(p=>p.name===name&&fs.readFileSync(p.file+'.cid','utf8').trim().toLowerCase()===cid.toLowerCase());
    if(matches.length!==1)throw Error('Bundled prefab CID mismatch: '+name);
  }
  const allowed=new Set(catalog.external_native_asset_cids);
  for(const id of closure.external)if(!allowed.has(id))throw Error('Unresolved asset reference: '+id);
  for(const file of closure.assets)if(!inside(root,file))throw Error('Asset outside source root');
}
export function validatePayloadPaths(records) {
  const seen=new Set();
  for(const record of records) {
    const p=record.path.replaceAll('\\','/');
    if(path.posix.isAbsolute(p)||p.split('/').some(s=>s==='..'||s===''||s.startsWith('.'))||/^[A-Za-z]:/.test(p))throw Error('Unsafe payload path: '+p);
    if(seen.has(p.toLowerCase()))throw Error('Duplicate payload path: '+p);
    seen.add(p.toLowerCase());
    if(p.startsWith('ImportedData/')) {
      if(!/\.(Prefab|Surface|Texture|Geometry|cid)$/i.test(p))throw Error('Unexpected asset extension: '+p);
    } else if(!['CityWeaver.dll','TransportFever3Helicopters.Runtime.dll','PassengerHelicopters.dll','CityWeaver_win_x86_64.dll','TransportFever3Helicopters.Runtime_win_x86_64.dll','PassengerHelicopters_win_x86_64.dll',...docs].includes(p))throw Error('Unexpected publication payload file: '+p);
  }
}
export function publicationAccess(xml) {
  const access=xml.match(/<AccessLevel\s+Value="([^"]+)"\s*\//u)?.[1];
  if(!['Private','Public'].includes(access))throw Error('Invalid publication access level');
  return access;
}
function metadataVersion(xml) {
  const version=xml.match(/<ModVersion\s+Value="([^"]+)"/u)?.[1];
  if(!/^\d+\.\d+\.\d+$/.test(version??''))throw Error('Invalid publication version');
  publicationAccess(xml);
  return version;
}
export async function preparePublication({userData,staged,output,publishConfig}) {
  if(fs.existsSync(output))throw Error('Publication output exists; choose a fresh directory.');
  const configPath=path.resolve(publishConfig??process.env.CSII_PUBLISH_CONFIG??path.join(moduleRoot,'Properties/PublishConfiguration.xml'));
  if(!fs.existsSync(configPath))throw Error('Local publication configuration missing; pass --publish-config or set CSII_PUBLISH_CONFIG.');
  const xml=fs.readFileSync(configPath,'utf8');
  const version=metadataVersion(xml),access=publicationAccess(xml);
  const root=path.join(userData,'ImportedData'),sourceRoots=rootPrefabNames(),closure=assetClosure(root,sourceRoots);
  const catalog=JSON.parse(fs.readFileSync(catalogFile,'utf8'));
  verifyCatalog(root,closure,catalog);
  const records=[];
  for(const file of closure.assets)for(const source of [file,file+'.cid'])records.push({source,path:'ImportedData/'+path.relative(root,source).replaceAll('\\','/'),kind:'asset'});
  for(const name of ['CityWeaver','TransportFever3Helicopters.Runtime','PassengerHelicopters'])for(const suffix of ['.dll','_win_x86_64.dll'])records.push({source:path.join(staged,name,name+suffix),path:name+suffix,kind:'code'});
  for(const name of docs)records.push({source:path.join(moduleRoot,name),path:name,kind:'documentation'});
  validatePayloadPaths(records);
  // Validate all input before creating an output directory. Never modify live assets.
  for(const record of records) {
    const stat=fs.lstatSync(record.source);if(!stat.isFile()||stat.isSymbolicLink())throw Error('Expected regular source file');
    record.bytes=stat.size;record.sha256=await sha256(record.source);
  }
  for(const name of artwork)if(!fs.statSync(path.join(moduleRoot,'Properties',name)).isFile())throw Error('Missing publication artwork: '+name);
  const payload=path.join(output,'content');
  fs.mkdirSync(payload,{recursive:true});
  const hashes=new Map();
  for(const record of records) {
    const destination=path.resolve(payload,record.path);
    if(!inside(payload,destination))throw Error('Payload escaped output');
    fs.mkdirSync(path.dirname(destination),{recursive:true});
    if(hashes.has(record.sha256))fs.linkSync(hashes.get(record.sha256),destination);
    else {fs.copyFileSync(record.source,destination,fs.constants.COPYFILE_EXCL);hashes.set(record.sha256,destination);}
    if(await sha256(destination)!==record.sha256)throw Error('Publication copy verification failed');
  }
  const gallery=[];fs.mkdirSync(path.join(output,'gallery'));
  for(const name of artwork) {
    const destination=path.join(output,'gallery',name);fs.copyFileSync(path.join(moduleRoot,'Properties',name),destination);
    gallery.push({path:'gallery/'+name,bytes:fs.statSync(destination).size,sha256:await sha256(destination)});
  }
  // The upload script starts in this directory; use portable relative artwork paths.
  const publishXml=xml.replaceAll('../Properties/','gallery/');
  fs.writeFileSync(path.join(output,'PublishConfiguration.xml'),publishXml);
  fs.copyFileSync(path.join(moduleRoot,'PUBLICATION.md'),path.join(output,'PUBLICATION.md'));
  fs.copyFileSync(path.join(moduleRoot,'Properties/Artwork.json'),path.join(output,'gallery/Artwork.json'));
  const manifest={schema_version:1,name:'Transport Fever 3 Helicopters',display_name_zh:'狂热运输3直升机客运',version,platform:'Windows',game_version:'1.6.2*',access,mod_id:xml.match(/<ModId\s+Value="([^"]*)"/u)?.[1]||null,
    delivery:'single-mod-native-code-and-loose-assets',runtime_assembly:'TransportFever3Helicopters.Runtime',runtime_assembly_version:version+'.0',compatibility_assembly:'CityWeaver',compatibility_assembly_version:'1.36.100.0',bootstrap_assembly:'PassengerHelicopters',mcp_required:false,mcp_included:false,install_script_required:false,
    asset_count:closure.assets.length,root_prefabs:catalog.prefabs.filter(p=>sourceRoots.includes(p.name)),owned_prefabs:catalog.prefabs,external_native_asset_cids:closure.external,files:records.map(({source,...record})=>record),gallery,
    validation:{package_hashes_verified:true,game_loading:'pending',clean_environment:'pending',native_subscription:'pending',fresh_building_placement:'pending',return_to_depot:'pending',save_reload:'pending'},created_at:new Date().toISOString()};
  fs.writeFileSync(path.join(output,'publication-manifest.json'),JSON.stringify(manifest,null,2));
  return {output,version,asset_count:manifest.asset_count,files:manifest.files.length,logical_bytes:records.reduce((n,r)=>n+r.bytes,0),mcp_included:false,native_subscription_verified:false};
}
export async function verifyPublication(output) {
  const manifest=JSON.parse(fs.readFileSync(path.join(output,'publication-manifest.json'),'utf8'));
  if(manifest.schema_version!==1||!['Private','Public'].includes(manifest.access)||manifest.mcp_included||manifest.mcp_required||manifest.install_script_required)throw Error('Unexpected publication manifest');
  validatePayloadPaths(manifest.files);
  const payload=path.join(output,'content');
  for(const file of manifest.files) {
    const destination=path.resolve(payload,file.path);
    if(!inside(payload,destination)||fs.statSync(destination).size!==file.bytes||await sha256(destination)!==file.sha256)throw Error('Publication checksum mismatch: '+file.path);
  }
  // Check for extras as well as missing files, including hidden or nested binaries.
  const actual=[];const walk=dir=>{for(const entry of fs.readdirSync(dir,{withFileTypes:true})) {
    const file=path.join(dir,entry.name);if(fs.lstatSync(file).isSymbolicLink())throw Error('Linked publication file');
    if(entry.isDirectory())walk(file);else actual.push(path.relative(payload,file).replaceAll('\\','/'));
  }};walk(payload);
  if(actual.length!==manifest.files.length||actual.some(p=>!manifest.files.some(r=>r.path===p)))throw Error('Unexpected extra publication files');
  for(const item of manifest.gallery) {
    const destination=path.resolve(output,item.path);
    if(!inside(output,destination)||fs.statSync(destination).size!==item.bytes||await sha256(destination)!==item.sha256)throw Error('Artwork checksum mismatch');
  }
  const closure=assetClosure(path.join(payload,'ImportedData'),manifest.root_prefabs.map(p=>p.name));
  verifyCatalog(path.join(payload,'ImportedData'),closure,{prefabs:manifest.owned_prefabs??manifest.root_prefabs,external_native_asset_cids:manifest.external_native_asset_cids});
  if(closure.assets.length!==manifest.asset_count)throw Error('Publication asset count mismatch');
  const xml=fs.readFileSync(path.join(output,'PublishConfiguration.xml'),'utf8');
  if(metadataVersion(xml)!==manifest.version)throw Error('Publication version mismatch');
  if(publicationAccess(xml)!==manifest.access)throw Error('Publication access mismatch');
  for(const field of [...xml.matchAll(/<(?:Thumbnail|Screenshot)\s+Value="([^"]+)"/g)]) {
    const p=path.resolve(output,field[1]);if(!inside(output,p)||!fs.statSync(p).isFile())throw Error('Publication artwork path missing');
  }
  return {verified:true,version:manifest.version,assets:manifest.asset_count,files:manifest.files.length,native_subscription:manifest.validation.native_subscription};
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href) {
  const argument=(name,fallback)=>{const i=process.argv.indexOf(name);return i<0?fallback:process.argv[i+1];};
  const output=path.resolve(argument('--output',path.join(repository,'artifacts/publishing/TransportFever3Helicopters-1.1.0')));
  if(process.argv.includes('--verify'))console.log(JSON.stringify(await verifyPublication(output),null,2));
  else console.log(JSON.stringify(await preparePublication({output,staged:path.resolve(argument('--staged',path.join(repository,'artifacts/staged'))),userData:path.resolve(argument('--user-data',path.join(repository,'assets/native'))),publishConfig:argument('--publish-config',process.env.CSII_PUBLISH_CONFIG)}),null,2));
}

