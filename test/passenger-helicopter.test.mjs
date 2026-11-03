import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, writeFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('helicopter routes require different road-connected airports and a working depot', async () => {
  const dir = await mkdtemp(path.join(tmpdir(), 'cityweaver-helicopter-'));
  try {
    const source = fileURLToPath(new URL('../Runtime/Transport/PassengerHelicopterRules.cs', import.meta.url));
    await writeFile(path.join(dir, 'Test.csproj'), `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="${source}"/></ItemGroup></Project>`);
    await writeFile(path.join(dir, 'Program.cs'), `using System; using System.Linq; using CityWeaver;
class Program {
  static void Check(string actual, string expected) { if(actual != expected) throw new Exception($"Expected {expected}, got {actual}"); }
  static void Main() {
    Check(PassengerHelicopterRules.ValidateAirports(new[]{1,2},new[]{true,true},true),null);
    Check(PassengerHelicopterRules.ValidateAirports(new[]{1,2,1},new[]{true,true,true},true),null);
    Check(PassengerHelicopterRules.ValidateAirports(new[]{1,1},new[]{true,true},true),"DISTINCT_AIRPORTS_REQUIRED");
    Check(PassengerHelicopterRules.ValidateAirports(new[]{1,2},new[]{true,false},true),"AIRPORT_REQUIRES_ROAD");
    Check(PassengerHelicopterRules.ValidateAirports(new[]{1,2},new[]{true,true},false),"HELICOPTER_DEPOT_REQUIRED");
    Check(PassengerHelicopterRules.ValidateAirports(new[]{1},new[]{true},true),"INVALID_ARGUMENT");
    Check(PassengerHelicopterRules.ValidateAirports(new[]{1,2},new[]{true},true),"INVALID_ARGUMENT");
    Check(PassengerHelicopterRules.ValidateAirports(Enumerable.Range(1,65).ToArray(),Enumerable.Repeat(true,65).ToArray(),true),"INVALID_ARGUMENT");
    Check(PassengerHelicopterRules.ValidateAirports(Enumerable.Range(1,64).ToArray(),Enumerable.Repeat(true,64).ToArray(),true),null);
    // Entity versions distinguish replacement airports from stale IDs.
    Check(PassengerHelicopterRules.ValidateAirports(new[]{(1,1),(1,2)},new[]{true,true},true),null);
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10},new[]{1},false,false),"HELICOPTER_FIRST_STOP_REQUIRES_AIRPORT");
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10},new[]{1},true,false),null);
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10,20},new[]{1,2},false,true),"HELICOPTER_FIRST_STOP_REQUIRES_AIRPORT");
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10,11},new[]{1,1},true,false),"HELICOPTER_SAME_AIRPORT_LEG");
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10,11,20},new[]{1,1,2},true,true),"HELICOPTER_SAME_AIRPORT_LEG");
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10,20,11},new[]{1,2,1},true,true),"HELICOPTER_SAME_AIRPORT_LEG");
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10,20,11},new[]{1,2,1},true,false),null);
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10,20,10},new[]{1,2,1},true,true),null);
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10,20,11,30},new[]{1,2,1,3},true,true),null);
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10,20},new[]{1,2},true,true),null);
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10,10},new[]{1,1},true,false),null);
    Check(PassengerHelicopterRules.ValidateRoute(new[]{10},new[]{1,2},true,true),"INVALID_ARGUMENT");
    if (PassengerHelicopterRules.PassengerCapacity != 60) throw new Exception("Wrong passenger capacity");
    Console.WriteLine("Helicopter route validation passed");
  }
}`);
    assert.match(execFileSync(process.env.CITYWEAVER_TEST_DOTNET || 'dotnet', ['run', '--project', path.join(dir, 'Test.csproj'), '-c', 'Release'], {encoding:'utf8', timeout:120000}), /Helicopter route validation passed/);
  } finally { await rm(dir, {recursive:true, force:true}); }
});
