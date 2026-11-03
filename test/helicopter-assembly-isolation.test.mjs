import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('built standalone assemblies retain legacy component identities and contain no MCP service dependency',async()=>{
  const dir=await mkdtemp(path.join(tmpdir(),'helicopter-assembly-'));
  try {
    const root=fileURLToPath(new URL('../',import.meta.url));
    await writeFile(path.join(dir,'Audit.csproj'),'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>');
    await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using System.IO;using System.Linq;using System.Reflection.Metadata;using System.Reflection.PortableExecutable;using System.Collections.Generic;using System.Security.Cryptography;
class Program {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static string Full(MetadataReader m,TypeDefinitionHandle h){var t=m.GetTypeDefinition(h);var p=t.GetDeclaringType();return p.IsNil?m.GetString(t.Namespace)+"."+m.GetString(t.Name):Full(m,p)+"+"+m.GetString(t.Name);}
 static void Main(string[] args){
  var root=args[0];using var rs=File.OpenRead(Path.Combine(root,"artifacts/staged/CityWeaver/CityWeaver.dll"));using var rp=new PEReader(rs);var r=rp.GetMetadataReader();
  using var hs=File.OpenRead(Path.Combine(root,"artifacts/staged/TransportFever3Helicopters.Runtime/TransportFever3Helicopters.Runtime.dll"));using var hp=new PEReader(hs);var h=hp.GetMetadataReader();
  using var ls=File.OpenRead(Path.Combine(root,"artifacts/staged/PassengerHelicopters/PassengerHelicopters.dll"));using var lp=new PEReader(ls);var l=lp.GetMetadataReader();
  var rt=r.TypeDefinitions.ToDictionary(h=>Full(r,h),h=>r.GetTypeDefinition(h));
  var ht=h.TypeDefinitions.ToDictionary(x=>Full(h,x),x=>h.GetTypeDefinition(x));
  Check(r.GetString(r.GetAssemblyDefinition().Name)=="CityWeaver","Serialized prefab assembly identity changed");
  Check(h.GetString(h.GetAssemblyDefinition().Name)=="TransportFever3Helicopters.Runtime","Dedicated transport runtime missing");
  Check(l.GetString(l.GetAssemblyDefinition().Name)=="PassengerHelicopters","Missing standalone entry assembly");
  Check(!r.AssemblyReferences.Select(x=>r.GetString(r.GetAssemblyReference(x).Name)).Contains("TransportFever3Helicopters.Runtime"),"Compatibility layer depends on active runtime");
  Check(h.AssemblyReferences.Select(x=>h.GetString(h.GetAssemblyReference(x).Name)).Contains("CityWeaver"),"Runtime does not consume the compatibility identities");
  Check(l.AssemblyReferences.Select(x=>l.GetString(l.GetAssemblyReference(x).Name)).Contains("TransportFever3Helicopters.Runtime"),"Bootstrap does not load the isolated runtime");
  foreach(var m in new[]{r,h,l})Check(!m.AssemblyReferences.Select(x=>m.GetString(m.GetAssemblyReference(x).Name)).Contains("CityWeaverMCP"),"Standalone requires the MCP bridge");
  foreach(var name in new[]{"ImportedPassengerHeliport","PassengerHelicopterRoadAccess","VehicleDoorProperties","HelipadLotVersion","HelicopterAirportWidth","VehicleModelPersistenceSystem","VehicleModelSaveData","SavedRoadSpeed"}) {
   Check(rt.ContainsKey("CityWeaver."+name),"Legacy serialized type missing from compatibility assembly: "+name);
  }
  Check(!rt.ContainsKey("CityWeaver.HelicopterRuntime")&&!rt.ContainsKey("CityWeaver.PassengerHelicopterSystem"),"Active transport logic leaked into compatibility assembly");
  Check(ht.ContainsKey("CityWeaver.PassengerHelicopterSystem"),"Transport simulation was not extracted");
  Check(ht.Keys.Any(n=>n.EndsWith(".Mi26ModelSystem")),"Standalone model loader missing");
  Check(ht.ContainsKey("CityWeaver.HelicopterModels.HelicopterNativeMotionBlurSystem"),"Native motion blur diagnostics missing");
  Check(ht.ContainsKey("CityWeaver.HelicopterModels.HelicopterRotorExposureSystem"),"Angular real-blade renderer missing");
  Check(ht.ContainsKey("CityWeaver.HelicopterModels.HelicopterRotorSpeedRules"),"Display-only rotor power rules missing");
  Check(!ht.ContainsKey("CityWeaver.HelicopterModels.HelicopterRotorVisualSystem")&&!ht.ContainsKey("CityWeaver.HelicopterModels.HelicopterRotorVisualRules"),"Retired synthetic rotor controls leaked into native mode");
  Check(!ht.ContainsKey("CityWeaver.GameQueryService")&&!ht.ContainsKey("CityWeaver.LocalQueryBridge"),"MCP code leaked into transport runtime");
  Check(!rt.ContainsKey("CityWeaver.Mod"),"Compatibility library must not create another mod entry");
  Check(r.ManifestResources.Count==0,"Compatibility assembly must not contain active runtime resources");
  Check(h.ManifestResources.Count==7,"Independent runtime must contain five icons, the Mi-26 configuration and angular shader");
  Check(h.ManifestResources.Select(x=>h.GetString(h.GetManifestResource(x).Name)).Contains("CityWeaver.Rendering.RotorAngular.bundle"),"Angular shader bundle missing");
  var resource=h.ManifestResources.Select(x=>h.GetManifestResource(x)).Single(x=>h.GetString(x.Name)=="CityWeaver.Rendering.RotorAngular.bundle");
  var payload=hp.GetSectionData(hp.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress).GetReader((int)resource.Offset,(int)(hp.GetSectionData(hp.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress).Length-resource.Offset));
  byte[] embedded=payload.ReadBytes(payload.ReadInt32());
  Check(SHA256.HashData(embedded).SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"assets/rendering/rotor-angular-windows.bundle")))),"Staged assembly embeds a stale angular shader");
  var oldFile=Path.Combine(Environment.GetEnvironmentVariable("CSII_LOCALMODSPATH")??"","CityWeaver","CityWeaver.dll");
  if(File.Exists(oldFile)){
   using var os=File.OpenRead(oldFile);using var op=new PEReader(os);var old=op.GetMetadataReader();var previous=old.TypeDefinitions.ToDictionary(h=>Full(old,h),h=>old.GetTypeDefinition(h));
   foreach(var name in new[]{"ImportedPassengerHeliport","PassengerHelicopterRoadAccess","VehicleDoorProperties","HelipadLotVersion","HelicopterAirportWidth","SavedRoadSpeed"}){
    if(!previous.TryGetValue("CityWeaver."+name,out var before))continue;
    var a=before.GetFields().Select(h=>old.GetString(old.GetFieldDefinition(h).Name)).OrderBy(x=>x).ToArray();
    var after=rt["CityWeaver."+name].GetFields().Select(h=>r.GetString(r.GetFieldDefinition(h).Name)).OrderBy(x=>x).ToArray();
    Check(a.SequenceEqual(after),"Legacy serialized fields changed: "+name);
   }
  }
  Console.WriteLine("Standalone assembly isolation and legacy type audit passed");
 }
}
`);
    assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Audit.csproj'),'-c','Release','--',root],{encoding:'utf8',timeout:120000}),/Standalone assembly isolation and legacy type audit passed/);
  } finally {await rm(dir,{recursive:true,force:true});}
});
