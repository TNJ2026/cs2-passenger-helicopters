import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('both locales cover all named assets and route errors; actual tooltip uses language-resolved IDs',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'heli-localization-'));
 try {
  const sources=['Runtime/HelicopterLocale.cs','Runtime/Transport/PassengerHelicopterRouteTooltipSystem.cs'].map(p=>fileURLToPath(new URL('../'+p,import.meta.url)));
  await writeFile(path.join(dir,'Test.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${sources.map(p=>`<Compile Include="${p}"/>`).join('')}</ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using System.Collections.Generic;using System.Linq;using System.Text.RegularExpressions;using CityWeaver;
namespace Colossal {
 public interface IDictionaryEntryError {}
 public interface IDictionarySource {IEnumerable<KeyValuePair<string,string>> ReadEntries(IList<IDictionaryEntryError> errors,Dictionary<string,int> counts);void Unload();}
}
namespace Game.UI.Localization {
 public struct LocalizedString {public string id;public static LocalizedString Id(string id)=>new LocalizedString{id=id};}
}
namespace Game.UI.Tooltip {
 public class StringTooltip {public string path;public Game.UI.Localization.LocalizedString value;}
 public class FakeWorld {public object Source;public T GetExistingSystemManaged<T>() where T:class=>Source as T;}
 public class TooltipSystemBase {
  public FakeWorld World=new FakeWorld();public StringTooltip LastTooltip;public int Additions;
  protected virtual void OnUpdate(){} protected void AddMouseTooltip(StringTooltip t){LastTooltip=t;Additions++;}
 }
}
namespace CityWeaver {
 internal static class ImportedHelipadMenuSystem {public const string Airport="CityWeaver_850b7b8e4f8b4a679fa2623a7c3dd3e0",Station="CityWeaver Imported Helipad Station";}
 public class PassengerHelicopterRouteToolPolicySystem {public string LastError;}
}
class Probe:PassengerHelicopterRouteTooltipSystem {public void Tick()=>OnUpdate();}
class Program {
 static void Check(bool v,string m){if(!v)throw new Exception(m);}
 static Dictionary<string,string> Entries(bool zh)=>new HelicopterLocale(zh).ReadEntries(null,new Dictionary<string,int>()).ToDictionary(p=>p.Key,p=>p.Value);
 static void Main(){
  var en=Entries(false);var zh=Entries(true);
  Check(en.Keys.OrderBy(k=>k).SequenceEqual(zh.Keys.OrderBy(k=>k)),"locale key coverage differs");
  foreach(var pair in en) {
   Check(!string.IsNullOrWhiteSpace(pair.Value)&&!string.IsNullOrWhiteSpace(zh[pair.Key]),"empty translation");
   Check(!Regex.IsMatch(pair.Value,@"[\p{IsCJKUnifiedIdeographs}]"),"Chinese leaked into English locale");
   Check(Regex.IsMatch(zh[pair.Key],@"[\p{IsCJKUnifiedIdeographs}]")||pair.Value=="Mil Mi-26TC","Chinese translation missing");
   if(pair.Key.StartsWith("Assets.NAME["))Check(en.ContainsKey(pair.Key.Replace("Assets.NAME[","Assets.DESCRIPTION[")),"named asset lacks a description: "+pair.Key);
  }
  Check(en["CityWeaver.RouteName[CityWeaver Helicopter Line]"].Contains("{NUMBER}")&&zh["CityWeaver.RouteName[CityWeaver Helicopter Line]"].Contains("{NUMBER}"),"route-number placeholder changed");
  var source=new PassengerHelicopterRouteToolPolicySystem();var p=new Probe();p.World.Source=source;
  var errors=new[]{"HELICOPTER_FIRST_STOP_REQUIRES_AIRPORT","HELICOPTER_SAME_AIRPORT_LEG","HELICOPTER_TOOL_API_UNAVAILABLE","FUTURE_ERROR"};
  foreach(var error in errors) {
   source.LastError=error;p.Tick();var key=p.LastTooltip.value.id;
   Check(key==HelicopterLocale.RouteErrorKey(error)&&en.ContainsKey(key)&&zh.ContainsKey(key),"tooltip is not localized");
   var current=zh;var text=current[key];current=en;
   Check(current[p.LastTooltip.value.id]!=text,"changing language leaves Chinese-only tooltip");
  }
  int before=p.Additions;source.LastError=null;p.Tick();source.LastError="INCOMPATIBLE_TRANSPORT_STOP";p.Tick();
  Check(p.Additions==before,"native/absent errors must retain existing tooltip behavior");
  // PrefabUISystem.GetTitleAndDescription selects these keys for UIAssetCategoryPrefab.
  const string category="CityWeaver Helicopter Transport";
  foreach(var entries in new[]{en,zh}) {
   Check(entries["SubServices.NAME["+category+"]"]==entries["Assets.NAME["+category+"]"],"native tab title missing");
   Check(entries["Assets.SUB_SERVICE_DESCRIPTION["+category+"]"]==entries["Assets.DESCRIPTION["+category+"]"],"native tab description missing");
  }
  Check(en.Count==24,"expected complete named asset and route tooltip coverage");
  Console.WriteLine("Bilingual localization and route-tooltip resolution passed: "+en.Count+" entries per locale");
 }
}
`);
  assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Test.csproj'),'-c','Release'],{encoding:'utf8',timeout:120000}),/Bilingual localization and route-tooltip resolution passed: 24/);
 } finally {await rm(dir,{recursive:true,force:true});}
});
