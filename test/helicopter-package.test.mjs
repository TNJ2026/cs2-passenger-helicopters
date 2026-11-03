import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {assetClosure,inside,rawId,references} from '../package.mjs';

test('updating a consolidated runtime preserves its directory and rejects duplicate libraries or a fresh code-only install',()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'helicopter-update-'));
  try {
    const installer=fileURLToPath(new URL('../Install.ps1',import.meta.url));
    fs.copyFileSync(installer,path.join(dir,'Install.ps1'));
    const game=path.join(dir,'Game');fs.mkdirSync(game);
    const source=path.join(dir,'Mods/CityWeaver/CityWeaver.dll');fs.mkdirSync(path.dirname(source),{recursive:true});fs.writeFileSync(source,'test core');
    const installed=path.join(game,'Mods/PassengerHelicopters/CityWeaver.dll');fs.mkdirSync(path.dirname(installed),{recursive:true});fs.writeFileSync(installed,'test core');
    const manifest={name:'Passenger Helicopters',version:'1.0.1',files:[{path:'Mods/CityWeaver/CityWeaver.dll',kind:'standalone',bytes:9,sha256:crypto.createHash('sha256').update('test core').digest('hex')}]};
    const save=()=>fs.writeFileSync(path.join(dir,'manifest.json'),JSON.stringify(manifest));save();
    fs.writeFileSync(path.join(dir,'package-complete.json'),'{}');
    const quote=s=>`'${s.replaceAll("'","''")}'`;
    const verify=target=>JSON.parse(execFileSync('powershell.exe',['-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-Command',
      `& ${quote(path.join(dir,'Install.ps1'))} -UserDataPath ${quote(target)} -VerifyOnly | ConvertTo-Json -Compress`],{encoding:'utf8',stdio:'pipe'}));
    assert.equal(verify(game).changes_needed,0);
    manifest.version=JSON.parse(fs.readFileSync(fileURLToPath(new URL('../package.json',import.meta.url)),'utf8')).version;save();
    assert.equal(verify(game).changes_needed,0);
    assert.equal(fs.existsSync(path.join(game,'Mods/CityWeaver')),false);
    manifest.code_only=true;save();assert.throws(()=>verify(game));
    fs.writeFileSync(path.join(game,'Mods/PassengerHelicopters/PassengerHelicopters.dll'),'entry');fs.mkdirSync(path.join(game,'ImportedData'));
    assert.equal(verify(game).changes_needed,0);
    const fresh=path.join(dir,'Fresh');fs.mkdirSync(fresh);assert.throws(()=>verify(fresh));assert.deepEqual(fs.readdirSync(fresh),[]);
    const duplicate=path.join(game,'Mods/CityWeaver/CityWeaver.dll');fs.mkdirSync(path.dirname(duplicate),{recursive:true});fs.writeFileSync(duplicate,'duplicate');assert.throws(()=>verify(game));
    assert.equal(fs.readFileSync(installed,'utf8'),'test core');
  } finally {assert(inside(os.tmpdir(),dir));fs.rmSync(dir,{recursive:true,force:true});}
});

test('standalone asset closure follows native CID and binary surface references while excluding unrelated history',()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'helicopter-package-'));
  try {
    const ids=['1','2','3','4','5'].map(s=>s.repeat(32));
    const put=(name,id,data)=>{const p=path.join(dir,name);fs.writeFileSync(p,data);fs.writeFileSync(p+'.cid',id);return p;};
    const bytes=Buffer.from(Array.from({length:16},(_,i)=>i*11));
    assert.equal(rawId(bytes),[...bytes].map(v=>v.toString(16).padStart(2,'0').split('').reverse().join('')).join(''));
    put('Airport.Prefab',ids[0],`{"mesh":$fstrref:"CID:${ids[1]}","native":$fstrref:"CID:${'f'.repeat(32)}"}`);
    put('Mesh.Prefab',ids[1],`{"material":$fstrref:"CID:${ids[2]}"}`);
    const surface=Buffer.concat([Buffer.from('_BaseColorMap'),Buffer.from([255]),Buffer.alloc(16,0x44)]);
    put('Material.Surface',ids[2],surface);put('Base.Texture',ids[3],Buffer.from('optimized texture'));
    put('UnrelatedBus.Prefab',ids[4],Buffer.from('bus'));
    const found=assetClosure(dir,['Airport']);
    assert.deepEqual(found.assets.map(p=>path.basename(p)).sort(),['Airport.Prefab','Base.Texture','Material.Surface','Mesh.Prefab']);
    assert.deepEqual(found.external,['f'.repeat(32)]);
    assert.deepEqual(references('material.Surface',surface),[ids[3]]);
    assert.equal(inside(dir,dir),false);assert.equal(inside(dir,dir+'-other/file'),false);
    assert.throws(()=>assetClosure(dir,['Missing']),/exactly one/);
    put('Duplicate.Prefab',ids[0],Buffer.from('different asset'));
    assert.throws(()=>assetClosure(dir,['Airport']),/Duplicate asset CID/);
  } finally {assert(inside(os.tmpdir(),dir));fs.rmSync(dir,{recursive:true,force:true});}
});

test('package verification is read-only and rejects corrupt files before installation',()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'helicopter-install-'));
  try {
    const installer=fileURLToPath(new URL('../Install.ps1',import.meta.url));
    fs.copyFileSync(installer,path.join(dir,'Install.ps1'));
    const game=path.join(dir,'Game');fs.mkdirSync(game);
    const file=path.join(dir,'Mods/PassengerHelicopters/PassengerHelicopters.dll');fs.mkdirSync(path.dirname(file),{recursive:true});fs.writeFileSync(file,'test code');
    fs.writeFileSync(path.join(dir,'package-complete.json'),'{}');
    fs.writeFileSync(path.join(dir,'manifest.json'),JSON.stringify({name:'Passenger Helicopters',version:'1.0.0',files:[{path:'Mods/PassengerHelicopters/PassengerHelicopters.dll',kind:'standalone',bytes:9,sha256:crypto.createHash('sha256').update('test code').digest('hex')}]}));
    const args=['-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',path.join(dir,'Install.ps1'),'-UserDataPath',game,'-VerifyOnly'];
    assert.match(execFileSync('powershell.exe',args,{encoding:'utf8'}),/True/);
    assert.deepEqual(fs.readdirSync(game),[]);
    fs.writeFileSync(file,'tampered!');
    assert.throws(()=>execFileSync('powershell.exe',args,{encoding:'utf8',stdio:'pipe'}));
    assert.deepEqual(fs.readdirSync(game),[]);
  } finally {assert(inside(os.tmpdir(),dir));fs.rmSync(dir,{recursive:true,force:true});}
});
