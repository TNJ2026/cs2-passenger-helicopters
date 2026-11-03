import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {inside} from './restore-helicopter-model-assets.mjs';

// Matches TextureAsset.Save(mipBias) in the installed game's native asset library.
// Keep existing mip pixels verbatim: no resampling, recolouring or normal-map conversion.
export function limitTextureMips(input,maxDimension=1024){
  if(!Number.isInteger(maxDimension)||maxDimension<1)throw Error('Invalid maximum dimension');
  if(input.length<18||input.readUInt16LE(0)!==5)throw Error('Expected native texture v5');
  let width=input.readUInt16LE(2),height=input.readUInt16LE(4),mips=input[8];
  if(!width||!height||!mips||input.readUInt16LE(6)!==1||input[10]!==2||input.readInt32LE(14)!==-1)
    throw Error('Expected uncompressed native 2D mip payload');
  if(mips>Math.floor(Math.log2(Math.max(width,height)))+1)throw Error('Invalid mip count');
  if(![4,8].includes(input[9])){
    if(Math.max(width,height)<=maxDimension)return {data:input,dropped:0};
    throw Error('Unsupported graphics format: '+input[9]);
  }
  let expected=18,w=width,h=height;
  for(let i=0;i<mips;i++){
    if(i&&w===1&&h===1&&i!==mips-1)throw Error('Repeated terminal mip');
    expected+=w*h*4;w=Math.max(1,Math.floor(w/2));h=Math.max(1,Math.floor(h/2));
  }
  if(expected!==input.length)throw Error('Native texture payload length mismatch');
  let offset=18,dropped=0;
  while(Math.max(width,height)>maxDimension){
    if(mips<=1)throw Error('No existing lower mip is available');
    offset+=width*height*4;width=Math.max(1,Math.floor(width/2));height=Math.max(1,Math.floor(height/2));mips--;dropped++;
  }
  if(!dropped)return {data:input,dropped:0};
  const header=Buffer.from(input.subarray(0,18));
  header.writeUInt16LE(width,2);header.writeUInt16LE(height,4);header[8]=mips;
  return {data:Buffer.concat([header,input.subarray(offset)]),dropped,width,height,mips};
}
const digest=data=>crypto.createHash('sha256').update(data).digest('hex');
function closed(){execFileSync('powershell.exe',['-NoProfile','-NonInteractive','-Command',
  'if(Get-Process Cities2 -ErrorAction SilentlyContinue){exit 42}; exit 0'],{stdio:'pipe'});}
async function main(){
  const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
  const journal=JSON.parse(fs.readFileSync(path.join(root,'artifacts/helicopter-model-restore-20261030.json')));
  if(journal.state!=='completed')throw Error('Restore is not complete');
  const destination=path.resolve('C:/Users/sc/AppData/LocalLow/Colossal Order/Cities Skylines II/ImportedData');
  if(path.resolve(journal.summary.destination)!==destination)throw Error('Unexpected game root');
  const staging=path.join(root,'artifacts/helicopter-texture-budget-20261030');
  const apply=process.argv.includes('--apply');
  const stage=apply||process.argv.includes('--stage');
  if(apply)closed();
  if(stage)fs.mkdirSync(staging,{recursive:true});
  const files=[],staged=new Map();let before=0,after=0;
  for(const entry of journal.restored.filter(file=>/\.Texture$/i.test(file.path))){
    const target=path.resolve(entry.path);
    if(!inside(destination,target))throw Error('Texture escaped game root');
    for(let parent=target;parent!==destination;parent=path.dirname(parent))
      if(fs.lstatSync(parent).isSymbolicLink())throw Error('Linked target');
    const original=fs.readFileSync(target);
    if(digest(original)!==entry.sha256)throw Error('Restored texture changed: '+target);
    const result=limitTextureMips(original);
    before+=original.length;after+=result.data.length;
    if(!result.dropped)continue;
    const sha256=digest(result.data),stagedFile=path.join(staging,sha256+'.Texture');
    if(stage&&!staged.has(sha256)){
      if(fs.existsSync(stagedFile)){
        if(digest(fs.readFileSync(stagedFile))!==sha256)throw Error('Staged texture checksum mismatch');
      }else fs.writeFileSync(stagedFile,result.data,{flag:'wx'});
      staged.set(sha256,stagedFile);
    }
    files.push({path:target,original_sha256:entry.sha256,sha256,stage:stagedFile,
      bytes:result.data.length,width:result.width,height:result.height,dropped_mips:result.dropped});
  }
  const report={max_dimension:1024,files:files.length,before_bytes:before,after_bytes:after,
    source_models_untouched:true,method:'Native precomputed mip suffix, same format and asset CID',entries:files};
  if(stage)fs.writeFileSync(path.join(staging,'plan.json'),JSON.stringify(report,null,2));
  if(!apply){console.log(JSON.stringify({...report,entries:undefined,staged:stage}));return;}
  closed();
  // Each original remains in the checksum-verified restore backup. Atomic replacement
  // breaks restored hard links and never changes a protected bus/road/shared texture.
  for(const file of files){
    closed();
    if(digest(fs.readFileSync(file.path))!==file.original_sha256)throw Error('Texture changed during staging');
    const temporary=file.path+'.cw-memory-tmp';
    fs.copyFileSync(file.stage,temporary,fs.constants.COPYFILE_EXCL);
    fs.renameSync(temporary,file.path);
    if(digest(fs.readFileSync(file.path))!==file.sha256)throw Error('Installed texture checksum mismatch');
  }
  fs.writeFileSync(path.join(staging,'completed.json'),JSON.stringify({...report,completed_at:new Date().toISOString()},null,2));
  console.log(JSON.stringify({...report,entries:undefined,state:'completed'}));
}
if(process.argv[1]&&import.meta.url===pathToFileURL(path.resolve(process.argv[1])).href)await main();
