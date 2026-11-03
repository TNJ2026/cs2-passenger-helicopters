import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';

test('live duplicate airport paths repair into one connected tree and pending courses cannot multiply',async()=>{
 const dir=await mkdtemp(path.join(tmpdir(),'cw-heliport-footpath-'));
 try{
  const sources=['HeliportPedestrianTopology','HeliportNetCreationGate'].map(n=>fileURLToPath(new URL(`../Runtime/Transport/${n}.cs`,import.meta.url)));
  const fixture=fileURLToPath(new URL('./fixtures/heliport-pedestrian-duplicate.json',import.meta.url));
  await writeFile(path.join(dir,'Tests.csproj'),`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup>${sources.map(s=>`<Compile Include="${s}"/>`).join('')}</ItemGroup></Project>`);
  await writeFile(path.join(dir,'Program.cs'),String.raw`
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Text.Json;using CityWeaver;
using T=CityWeaver.HeliportPedestrianTopology;
class Fixture{public T.Node[] Nodes;public T.Edge[] Edges;}
class Program{
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 static T.Point P(float x,float y,float z)=>new T.Point(x,y,z);
 static void Main(string[] args){
  var f=JsonSerializer.Deserialize<Fixture>(File.ReadAllText(args[0]),new JsonSerializerOptions{IncludeFields=true});
  var plan=T.Analyze(f.Nodes,f.Edges);
  Check(plan.RemovedEdges.SetEquals(new[]{726776,892702}),"Only the two repeated 52 m edges may be removed");
  Check(plan.Nodes.Count(n=>n.Key!=n.Value)==4,"Only the four copied endpoints may be merged");
  Check(plan.Nodes[677845]==677845&&plan.Nodes[677846]==677846&&plan.Nodes[684478]==684478,"Keep road entrance and original junctions");
  var edges=f.Edges.Where(e=>!plan.RemovedEdges.Contains(e.Id)).Select(e=>new T.Edge{Id=e.Id,Start=plan.Nodes[e.Start],End=plan.Nodes[e.End],Curve=e.Curve}).ToArray();
  var nodes=f.Nodes.Where(n=>plan.Nodes[n.Id]==n.Id).ToArray();
  Check(edges.Length==5&&nodes.Length==6,"Five paths form the intended six-node tree");
  var reached=new HashSet<int>{677845};
  for(int i=0;i<nodes.Length;i++)foreach(var e in edges){if(reached.Contains(e.Start))reached.Add(e.End);if(reached.Contains(e.End))reached.Add(e.Start);}
  Check(nodes.All(n=>reached.Contains(n.Id)),"Both boarding branches must reach the external road entrance");
  Check(!T.Analyze(nodes,edges).Changed,"Reloading an already repaired graph must be a no-op");
  // Reverse-direction duplicates are equivalent; distinct curved/raised paths are not.
  var a=new T.Node{Id=1,Position=P(0,0,0),External=true};var b=new T.Node{Id=2,Position=P(10,0,0)};
  var straight=new T.Edge{Id=10,Start=1,End=2,Curve=new[]{P(0,0,0),P(3,0,0),P(7,0,0),P(10,0,0)}};
  var reverse=new T.Edge{Id=11,Start=2,End=1,Curve=straight.Curve.Reverse().ToArray()};
  var curved=new T.Edge{Id=12,Start=1,End=2,Curve=new[]{P(0,0,0),P(3,0,2),P(7,0,2),P(10,0,0)}};
  Check(T.Analyze(new[]{a,b},new[]{straight,reverse,curved}).RemovedEdges.SetEquals(new[]{11}),"Never delete a separate curved path");
  var upperA=new T.Node{Id=3,Position=P(0,4,0)};var upperB=new T.Node{Id=4,Position=P(10,4,0)};
  var upper=new T.Edge{Id=13,Start=3,End=4,Curve=straight.Curve.Select(p=>P(p.X,p.Y+4,p.Z)).ToArray()};
  Check(!T.Analyze(new[]{a,b,upperA,upperB},new[]{straight,upper}).Changed,"Grade-separated paths remain separate");
  Check(!T.Analyze(new[]{a,new T.Node{Id=5,Position=a.Position,External=true}},Array.Empty<T.Edge>()).Changed,"Distinct external road anchors remain intact");
  // Native definitions can disappear long before the world edge is observable.
  var gate=new HeliportNetCreationGate<int,object>();var first=new object();var second=new object();
  Check(gate.TrySubmit(1,first),"Initial native course submission");
  for(int frame=0;frame<1000;frame++){gate.Observe(1,second);Check(!gate.TrySubmit(1,first),"Waiting frames must never resubmit an unresolved course");Check(!gate.TrySubmit(1,second),"A later branch cannot race the first course");}
  Check(gate.TrySubmit(2,first),"Different airports have independent progress");
  gate.Observe(1,first);Check(gate.TrySubmit(1,second),"Only observing the permanent first edge admits the next branch");
  gate.Observe(1,first);Check(!gate.TrySubmit(1,second),"A stale observation cannot release a newer pending branch");
  Console.WriteLine("Airport pedestrian repair passed");
 }
}
`);
  assert.match(execFileSync('dotnet',['run','--project',path.join(dir,'Tests.csproj'),'-c','Release','--',fixture],{encoding:'utf8',timeout:120000}),/Airport pedestrian repair passed/);
 }finally{await rm(dir,{recursive:true,force:true});}
});
