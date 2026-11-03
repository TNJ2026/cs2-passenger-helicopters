import test from'node:test';import assert from'node:assert/strict';import fs from'node:fs';import os from'node:os';import path from'node:path';import{backupAndRemove,verifiedFile}from'../tools/remove-helicopter-model-assets.mjs';
test('model removal validates bounds, backs up every file before deleting and retains shared assets',async()=>{
 const dir=fs.mkdtempSync(path.join(os.tmpdir(),'cw-model-removal-'));const root=path.join(dir,'ImportedData'),stage=path.join(dir,'AssetImports');fs.mkdirSync(root);fs.mkdirSync(stage);
 try{
  const a=path.join(root,'a.Geometry'),b=path.join(root,'b.Geometry'),cid=a+'.cid',keep=path.join(root,'Bus.Texture');fs.writeFileSync(a,'model');fs.linkSync(a,b);fs.writeFileSync(cid,'asset-id');fs.writeFileSync(keep,'bus');
  const files=[a,b,cid].map(p=>({path:p,bytes:fs.statSync(p).size})),manifest={summary:{root,stage},files,protectedModelFiles:[keep]},archive=path.join(dir,'backup');
  assert.throws(()=>verifiedFile([root,stage],path.join(dir,'outside')));
  await assert.rejects(backupAndRemove(manifest,archive,()=>{throw Error('game running')}));assert(fs.existsSync(a));assert(!fs.existsSync(archive));
  const result=await backupAndRemove(manifest,archive,()=>{},backup=>{assert(fs.existsSync(a)&&fs.existsSync(b));assert(fs.existsSync(path.join(backup,'restore-manifest.json')));});
  assert.equal(result.deletedFiles,3);assert.equal(result.verifiedBackupPhysicalFiles,2);assert(!fs.existsSync(a));assert.equal(fs.readFileSync(keep,'utf8'),'bus');assert.equal(fs.readFileSync(path.join(archive,'ImportedData/a.Geometry'),'utf8'),'model');assert.equal(fs.readFileSync(path.join(archive,'ImportedData/a.Geometry.cid'),'utf8'),'asset-id');
  const missing={...manifest,files:[{path:keep,bytes:99}]};await assert.rejects(backupAndRemove(missing,path.join(dir,'bad-backup')));assert(fs.existsSync(keep));
 }finally{const absolute=path.resolve(dir);assert(absolute.startsWith(path.resolve(os.tmpdir())+path.sep));fs.rmSync(absolute,{recursive:true,force:true});}
});
