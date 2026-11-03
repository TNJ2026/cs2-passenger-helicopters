import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {verifyCatalog,validatePayloadPaths,publicationAccess} from '../publication.mjs';

test('publication access must be explicit and supports private staging and public releases',()=>{
  for(const access of ['Private','Public'])assert.equal(publicationAccess(`<AccessLevel Value="${access}" />`),access);
  for(const xml of ['', '<AccessLevel Value="" />','<AccessLevel Value="Unknown" />','<AccessLevel Value="public" />'])assert.throws(()=>publicationAccess(xml),/Invalid publication access/);
});

const root=fileURLToPath(new URL('../',import.meta.url));
test('subscription ownership requires the original name and CID; foreign mods and copied display names stay excluded',()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'helicopter-subscription-'));
  try {
    const catalog=JSON.parse(fs.readFileSync(path.join(root,'assets/helicopter/publication-assets.json'),'utf8'));
    const source=fs.readFileSync(path.join(root,'Runtime/Transport/BundledHelicopterAssets.cs'),'utf8');
    const pairs=[...source.matchAll(/\{ "([^"]+)", "([a-f0-9]{32})" \}/g)].map(m=>({name:m[1],cid:m[2]}));
    assert.deepEqual(pairs,catalog.prefabs);
    const files=['BundledHelicopterAssets.cs','HelicopterAssetOwnership.cs'].map(n=>path.join(root,'Runtime/Transport',n));
    fs.writeFileSync(path.join(dir,'Test.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${files.map(p=>`<Compile Include="${p}"/>`).join('')}</ItemGroup></Project>`);
    fs.writeFileSync(path.join(dir,'Program.cs'),`
using System;using CityWeaver;using Game.Prefabs;
namespace Game.Prefabs {
 class PrefabBase {public bool isSubscribedMod;public string name;public PrefabAsset asset;}
 class PrefabAsset {public Id id=new Id();} class Id {public GuidValue guid;}
 struct GuidValue {public string value;public static implicit operator GuidValue(string s)=>new GuidValue{value=s};public override string ToString()=>value??new string('0',32);}
}
class Program {
 static void Check(bool v,string m){if(!v)throw new Exception(m);}
 static PrefabBase Asset(string name,string cid,bool subscribed=true)=>new PrefabBase{name=name,isSubscribedMod=subscribed,asset=new PrefabAsset{id=new Id{guid=cid}}};
 static void Main(){
  string[] names={${pairs.map(p=>JSON.stringify(p.name)).join(',')}};
  string[] ids={${pairs.map(p=>JSON.stringify(p.cid)).join(',')}};
  for(int i=0;i<names.Length;i++) {
   Check(!HelicopterAssetOwnership.RejectForeignSubscription(Asset(names[i],ids[i])),"own subscribed asset excluded");
   Check(!HelicopterAssetOwnership.RejectForeignSubscription(Asset(names[i],ids[i].ToUpperInvariant())),"CID case changed ownership");
   Check(HelicopterAssetOwnership.RejectForeignSubscription(Asset(names[i],"ffffffffffffffffffffffffffffffff")),"same-name foreign resource accepted");
   Check(HelicopterAssetOwnership.RejectForeignSubscription(Asset("Another mod",ids[i])),"foreign name accepted");
  }
  Check(HelicopterAssetOwnership.RejectForeignSubscription(Asset(names[0],null)),"missing subscribed ID accepted");
  Check(HelicopterAssetOwnership.RejectForeignSubscription(new PrefabBase{name=names[0],isSubscribedMod=true}),"missing subscribed asset accepted");
  Check(!HelicopterAssetOwnership.RejectForeignSubscription(Asset("Local old asset",null,false)),"legacy local behavior changed");
  Console.WriteLine("Native subscription identity guard passed");
 }
}`);
    assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Test.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Native subscription identity guard passed/);
  } finally {assert(dir.startsWith(os.tmpdir()+path.sep));fs.rmSync(dir,{recursive:true,force:true});}
});

test('publication payload rejects bridge code, local installers, debug payload, hidden files and traversal',()=>{
  validatePayloadPaths([{path:'CityWeaver.dll'},{path:'TransportFever3Helicopters.Runtime.dll'},{path:'PassengerHelicopters.dll'},{path:'ImportedData/CityWeaver/model/Body.Prefab'},{path:'ImportedData/CityWeaver/model/Body.Prefab.cid'}]);
  for(const p of ['CityWeaverMCP.dll','Install.ps1','PassengerHelicopters.pdb','node_modules/a.dll','Saves/test.cok','../CityWeaver.dll','ImportedData/../file.Prefab','C:/file.Prefab','ImportedData/.hidden/Body.Prefab','ImportedData/a/Bridge.dll'])assert.throws(()=>validatePayloadPaths([{path:p}]),/Unsafe|Unexpected/);
  assert.throws(()=>validatePayloadPaths([{path:'CityWeaver.dll'},{path:'cityweaver.dll'}]),/Duplicate/);
});

test('publication fails before upload for missing or changed native resource IDs and unresolved references',()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'helicopter-publish-cid-'));
  try {
    const file=path.join(dir,'Airport.Prefab');fs.writeFileSync(file,JSON.stringify({name:'Airport'}));fs.writeFileSync(file+'.cid','1'.repeat(32));
    const closure={assets:[file],external:[]},catalog={prefabs:[{name:'Airport',cid:'1'.repeat(32)}],external_native_asset_cids:[]};
    verifyCatalog(dir,closure,catalog);
    fs.writeFileSync(file+'.cid','2'.repeat(32));assert.throws(()=>verifyCatalog(dir,closure,catalog),/CID mismatch/);
    fs.writeFileSync(file+'.cid','1'.repeat(32));
    assert.throws(()=>verifyCatalog(dir,{...closure,external:['3'.repeat(32)]},catalog),/Unresolved/);
    assert.throws(()=>verifyCatalog(dir,{assets:[],external:[]},catalog),/CID mismatch/);
  } finally {assert(dir.startsWith(os.tmpdir()+path.sep));fs.rmSync(dir,{recursive:true,force:true});}
});

test('publication validates serialized dependency names and CIDs despite content-hash filenames',()=>{
  const dir=fs.mkdtempSync(path.join(os.tmpdir(),'helicopter-marker-cid-'));
  try {
    const file=path.join(dir,'CityWeaver Helicopter Boarding Stop_ae80ee649df020b197ac48f49624c112.Prefab');
    fs.writeFileSync(file,'{"name":"CityWeaver Helicopter Boarding Stop","components":{"m_Mesh":$fstrref:"CID:11111111111111111111111111111111"}}');
    fs.writeFileSync(file+'.cid','e2592fa67f8e2465d7d32de27b2ab067');
    const closure={assets:[file],external:[]};
    const catalog={prefabs:[{name:'CityWeaver Helicopter Boarding Stop',cid:'e2592fa67f8e2465d7d32de27b2ab067'}],external_native_asset_cids:[]};
    verifyCatalog(dir,closure,catalog);
    fs.writeFileSync(file,JSON.stringify({name:'Another mod marker'}));
    assert.throws(()=>verifyCatalog(dir,closure,catalog),/CID mismatch/);
  } finally {assert(dir.startsWith(os.tmpdir()+path.sep));fs.rmSync(dir,{recursive:true,force:true});}
});
