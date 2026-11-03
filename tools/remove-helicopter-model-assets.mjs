import fs from'node:fs';import path from'node:path';import crypto from'node:crypto';import{execFileSync}from'node:child_process';import{pathToFileURL}from'node:url';
const sha=async file=>{const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(file,{highWaterMark:4*1024*1024}))h.update(b);return h.digest('hex');};
function inside(root,file){const rel=path.relative(root,file);return rel!==''&&rel!=='..'&&!rel.startsWith('..'+path.sep)&&!path.isAbsolute(rel);}
export function verifiedFile(roots,file){file=path.resolve(file);const root=roots.find(r=>inside(r,file));if(!root)throw Error('Target outside explicit removal roots');for(let p=file;;p=path.dirname(p)){if(fs.lstatSync(p).isSymbolicLink())throw Error('Linked path refused');if(p.toLowerCase()===root.toLowerCase())break;}if(!fs.statSync(file).isFile())throw Error('Not a regular file');return{file,root};}
export async function backupAndRemove(manifest,archiveRoot,guard=()=>{},beforeRemove=()=>{},progress=()=>{}){
 const roots=[manifest.summary.root,manifest.summary.stage].map(r=>path.resolve(r));archiveRoot=path.resolve(archiveRoot);
 if(roots.some(r=>r===archiveRoot||inside(r,archiveRoot)||inside(archiveRoot,r))||fs.existsSync(archiveRoot))throw Error('Unsafe/existing backup directory');
 guard();const sourceFiles=manifest.files.map(f=>({...f,...verifiedFile(roots,f.path)}));
 for(const f of sourceFiles)if(fs.statSync(f.file).size!==f.bytes)throw Error('File changed since removal plan');
 fs.mkdirSync(archiveRoot,{recursive:true});const copies=new Map(),records=[];
 // No source removals until the entire recoverable backup is verified.
 for(let i=0;i<sourceFiles.length;i++){
  if(i%100===0)guard();const f=sourceFiles[i],s=fs.statSync(f.file,{bigint:true}),identity=s.dev+':'+s.ino,backup=path.join(archiveRoot,roots.indexOf(f.root)===0?'ImportedData':'AssetImports',path.relative(f.root,f.file));
  if(!inside(archiveRoot,backup))throw Error('Backup path escaped');fs.mkdirSync(path.dirname(backup),{recursive:true});let hash;
  if(copies.has(identity)){const prior=copies.get(identity);fs.linkSync(prior.backup,backup);hash=prior.sha256;}
  else{hash=await sha(f.file);fs.copyFileSync(f.file,backup,fs.constants.COPYFILE_EXCL);if(await sha(backup)!==hash)throw Error('Backup checksum mismatch');copies.set(identity,{backup,sha256:hash});}
  records.push({source:f.file,backup,bytes:f.bytes,sha256:hash,dev:s.dev.toString(),ino:s.ino.toString(),mtime:s.mtimeNs.toString()});
  if((i+1)%300===0)progress({phase:'backup',files:i+1,total:sourceFiles.length});
 }
 const recordPath=path.join(archiveRoot,'restore-manifest.json');fs.writeFileSync(recordPath,JSON.stringify({summary:manifest.summary,files:records},null,2));
 guard();await beforeRemove(archiveRoot);guard();let deleted=0;const dirs=new Set();
 for(const r of records){if(deleted%100===0)guard();verifiedFile(roots,r.source);const s=fs.statSync(r.source,{bigint:true});if(s.dev.toString()!==r.dev||s.ino.toString()!==r.ino||s.size!==BigInt(r.bytes)||s.mtimeNs.toString()!==r.mtime)throw Error('Source changed after backup; stopped');fs.unlinkSync(r.source);fs.appendFileSync(path.join(archiveRoot,'removed.jsonl'),JSON.stringify({source:r.source,sha256:r.sha256})+'\n');dirs.add(path.dirname(r.source));deleted++;if(deleted%500===0)progress({phase:'remove',files:deleted,total:records.length});}
 // Remove only now-empty directories, never recursive trees or a root itself.
 const parents=new Set();for(const dir of dirs){for(let p=dir;roots.some(r=>inside(r,p));p=path.dirname(p))parents.add(p);}
 for(const dir of [...parents].sort((a,b)=>b.length-a.length))if(fs.existsSync(dir)){if(fs.lstatSync(dir).isSymbolicLink())throw Error('Directory became a link');if(fs.readdirSync(dir).length===0)fs.rmdirSync(dir);}
 for(const file of manifest.protectedModelFiles||[])if(!fs.existsSync(file))throw Error('Protected model asset missing');
 for(const r of records)if(fs.existsSync(r.source))throw Error('Removal incomplete');
 const result={deletedFiles:deleted,backup:archiveRoot,verifiedBackupPhysicalFiles:copies.size,logicalBytes:records.reduce((s,r)=>s+r.bytes,0)};fs.writeFileSync(path.join(archiveRoot,'completed.json'),JSON.stringify(result,null,2));return result;
}
function closed(){execFileSync('powershell.exe',['-NoProfile','-NonInteractive','-Command','if(Get-Process Cities2 -ErrorAction SilentlyContinue){exit 42}; exit 0'],{stdio:'pipe'});}
async function main(){
 const manifest=JSON.parse(fs.readFileSync('artifacts/helicopter-model-removal-plan.json'));
 const game=path.resolve('C:/Users/sc/AppData/LocalLow/Colossal Order/Cities Skylines II');
 if(path.resolve(manifest.summary.root)!==path.join(game,'ImportedData')||path.resolve(manifest.summary.stage)!==path.join(game,'ModsData/CityWeaver/AssetImports'))throw Error('Unexpected game roots');
 if(!process.argv.includes('--apply')){console.log(JSON.stringify(manifest.summary));return;}
 const deployment=JSON.parse(fs.readFileSync('artifacts/deployment-1.36.92.json'));if(deployment.version!=='1.36.92.0')throw Error('Compatibility patch must be deployed first');
 closed();const archive=path.resolve('artifacts/backups/helicopter-models-before-removal-'+new Date().toISOString().replace(/[^0-9]/g,''));const workspace=path.resolve('.');if(!inside(workspace,archive))throw Error('Backup outside workspace');
 const result=await backupAndRemove(manifest,archive,closed,async backup=>{
  const policy=path.join(game,'ModsData/CityWeaver/HelicopterModels.json');
  fs.writeFileSync(path.join(backup,'policy-before.json'),JSON.stringify({existed:fs.existsSync(policy),content:fs.existsSync(policy)?fs.readFileSync(policy,'utf8'):null},null,2));
  fs.writeFileSync(policy,JSON.stringify({imported_models_enabled:false},null,2));
 },r=>console.log(JSON.stringify(r)));
 fs.writeFileSync('artifacts/helicopter-model-removal-result.json',JSON.stringify(result,null,2));console.log(JSON.stringify(result));
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href)await main();
