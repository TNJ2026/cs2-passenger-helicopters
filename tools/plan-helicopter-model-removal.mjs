import fs from'node:fs';import path from'node:path';import crypto from'node:crypto';
const game='C:/Users/sc/AppData/LocalLow/Colossal Order/Cities Skylines II',root=path.resolve(game,'ImportedData'),stage=path.resolve(game,'ModsData/CityWeaver/AssetImports');
const files=[];function walk(p){for(const e of fs.readdirSync(p,{withFileTypes:true})){const f=path.join(p,e.name),s=fs.lstatSync(f);if(s.isSymbolicLink())throw Error('Linked path: '+f);if(s.isDirectory())walk(f);else if(s.isFile())files.push({path:f,bytes:s.size,ino:s.ino,dev:s.dev});}}walk(root);
const assets=files.filter(f=>!f.path.toLowerCase().endsWith('.cid')).map(f=>{const cid=f.path+'.cid';return{...f,id:fs.existsSync(cid)?fs.readFileSync(cid,'utf8').trim():null,ext:path.extname(f.path).toLowerCase(),refs:[]};});
const byId=new Map(assets.filter(a=>/^[a-f0-9]{32}$/i.test(a.id)).map(a=>[a.id,a]));const rawId=b=>[...b].map(v=>v.toString(16).padStart(2,'0').split('').reverse().join('')).join('');
function operation(file){let rel=path.relative(root,file).split(path.sep);return rel[0]==='CityWeaver'?rel[1]:rel[0].replace(/^CityWeaver_/,'');}
const model=/CWHeli(?:pad|port)|CWMilMi26|CWMi26|CWHangar|MilMi26|Mi26|ImportedPassengerHeliport|CityWeaver Helicopter Transport/i,protectedName=/BusW|BusRig|RoadFont|RoadMarking|Speed100|Speed160|Speed200|Projector/i;
const selected=new Set(),primaryOps=new Set(),protectedOps=new Set();
for(const a of assets){let text=a.ext==='.prefab'?fs.readFileSync(a.path,'utf8'):'';if(model.test(path.basename(a.path))||model.test(text))primaryOps.add(operation(a.path));if(protectedName.test(path.basename(a.path))||protectedName.test(text))protectedOps.add(operation(a.path));
 if(a.ext==='.prefab')a.refs=[...text.matchAll(/CID:([a-f0-9]{32})/gi)].map(m=>m[1]).filter(id=>byId.has(id));
 if(a.ext==='.surface'){const b=fs.readFileSync(a.path);for(const property of['_BaseColorMap','_ControlMask','_MaskMap','_NormalMap','_EmissiveColorMap']){const p=Buffer.from(property),i=b.indexOf(p),start=i+p.length;if(i>=0&&b[start]===255&&start+17<=b.length){const id=rawId(b.subarray(start+1,start+17));if(byId.has(id)){a.refs.push(id);if(property==='_BaseColorMap')a.base=id;}}}}
 // Neutral-base hue processing was used for the Mi-26; BusW uses a neutral
 // paint mask without RouteBase. Protect bus/model groups independently above.
 if(/^RouteBase_\d+\.Texture$/i.test(path.basename(a.path)))primaryOps.add(operation(a.path));
}
for(const a of assets)if(primaryOps.has(operation(a.path))&&!protectedOps.has(operation(a.path)))selected.add(a.id);
// Reuse the byte audit to identify content-addressed helicopter textures.
const audit=JSON.parse(fs.readFileSync('artifacts/imported-storage-duplicates.json')),selectedHashes=new Set(audit.hashes.filter(f=>primaryOps.has(operation(f.path))&&!protectedOps.has(operation(f.path))).map(f=>f.sha256));
for(const a of assets.filter(a=>a.path.includes(path.sep+'RouteColorCache'+path.sep)&&a.ext==='.texture')){const h=crypto.createHash('sha256').update(fs.readFileSync(a.path)).digest('hex');if(selectedHashes.has(h))selected.add(a.id);}
let changed=true;while(changed){changed=false;for(const a of assets){if(selected.has(a.id)||protectedOps.has(operation(a.path)))continue;const target=a.ext==='.surface'?a.base&&selected.has(a.base):a.ext==='.prefab'&&a.refs.length>0&&a.refs.every(id=>selected.has(id));if(target){selected.add(a.id);changed=true;}}}
function closure(ids){const result=new Set(ids),queue=[...ids];while(queue.length){const id=queue.pop();for(const ref of byId.get(id)?.refs||[])if(!result.has(ref)){result.add(ref);queue.push(ref);}}return result;}
const candidate=closure(selected),kept=closure(assets.filter(a=>(a.ext==='.prefab'||a.ext==='.surface')&&!selected.has(a.id)).map(a=>a.id));
const remove=assets.filter(a=>candidate.has(a.id)&&!kept.has(a.id));const assetPaths=new Set(remove.flatMap(a=>[a.path,a.path+'.cid']));
const selectedFiles=files.filter(f=>assetPaths.has(f.path));
const importOps=new Set(remove.filter(a=>a.ext==='.prefab'||a.ext==='.geometry').map(a=>operation(a.path)));
const stageFiles=[];if(fs.existsSync(stage))for(const op of importOps){const dir=path.join(stage,op);if(!/^[a-f0-9]{32}$/i.test(op)||!fs.existsSync(dir))continue;const visit=p=>{for(const e of fs.readdirSync(p,{withFileTypes:true})){const f=path.join(p,e.name),s=fs.lstatSync(f);if(s.isSymbolicLink())throw Error('Linked staged project');if(s.isDirectory())visit(f);else if(s.isFile())stageFiles.push({path:f,bytes:s.size,ino:s.ino,dev:s.dev});}};visit(dir);}
const all=[...selectedFiles,...stageFiles],seen=new Set();let physicalBytes=0;for(const f of all){const k=f.dev+':'+f.ino;if(!seen.has(k)){seen.add(k);physicalBytes+=f.bytes;}}
const summary={at:new Date().toISOString(),root,stage,assets:remove.length,files:all.length,logicalBytes:all.reduce((s,f)=>s+f.bytes,0),physicalBytes,importedModelOperations:importOps.size,protectedOperations:[...protectedOps],sharedAssetsKept:[...candidate].filter(id=>kept.has(id)).length,remainingTargetModelAssets:assets.filter(a=>model.test(path.basename(a.path))&&!candidate.has(a.id)).map(a=>a.path)};
const manifest={summary,assets:remove.map(a=>({id:a.id,path:a.path,refs:a.refs})),files:all,protectedModelFiles:assets.filter(a=>protectedOps.has(operation(a.path))).map(a=>a.path)};
fs.writeFileSync('artifacts/helicopter-model-removal-plan.json',JSON.stringify(manifest,null,2));console.log(JSON.stringify({...summary,logicalGiB:summary.logicalBytes/2**30,physicalGiB:physicalBytes/2**30,protectedOperations:protectedOps.size,remainingTargetModelAssets:summary.remainingTargetModelAssets.slice(0,10)},null,2));
