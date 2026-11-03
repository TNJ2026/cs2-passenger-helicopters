import test from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import {inside, selectDependencies} from '../tools/restore-helicopter-model-assets.mjs';

test('restore selects the complete shared dependency closure without unrelated model history', () => {
  const assets = [
    {id:'airport',path:'Airport.Prefab',refs:['mesh','stop']},
    {id:'mesh',path:'Mesh.Prefab',refs:['surface']},
    {id:'surface',path:'material.Surface',refs:['texture']},
    {id:'stop',path:'Stop.Prefab',refs:['mesh']},
    {id:'texture',path:'paint.Texture',refs:[]},
    {id:'old',path:'OldAirport.Prefab',refs:['texture']},
    {id:'bus',path:'Bus.Prefab',refs:[]},
  ];
  assert.deepEqual(selectDependencies(assets,['Airport']).map(a=>a.id),['airport','mesh','surface','stop','texture']);
  assert.throws(()=>selectDependencies(assets,['Missing']),/Expected one/);
  assert.throws(()=>selectDependencies([{id:'a',path:'A.Prefab',refs:['missing']}],['A']),/Dependency missing/);
  assert.throws(()=>selectDependencies([...assets,{id:'duplicate',path:'Airport.Prefab',refs:[]}],['Airport']),/Expected one/);
});
test('asset restoration paths cannot select the root or escape to another directory or drive', () => {
  const root=path.resolve('restore-test');
  assert.equal(inside(root,path.join(root,'model','asset.Prefab')),true);
  assert.equal(inside(root,root),false);
  assert.equal(inside(root,path.resolve(root,'..','other','asset.Prefab')),false);
  assert.equal(inside(root,path.resolve(root+'-other','asset.Prefab')),false);
});
