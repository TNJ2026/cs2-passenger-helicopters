import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

test('104m border has upward faces, fills only two outside metres and preserves frontage',async()=>{
 const obj=await readFile(new URL('../assets_game_import/heliport-lot-104/CWHeliportLotBorder104/CWHeliportLotBorder104.obj',import.meta.url),'utf8');
 const lines=obj.split(/\r?\n/),vertices=lines.filter(l=>l.startsWith('v ')).map(l=>l.split(/\s+/).slice(1).map(Number));
 const faces=lines.filter(l=>l.startsWith('f ')).map(l=>l.split(/\s+/).slice(1).map(s=>Number(s.split('/')[0])-1));
 assert.equal(faces.length,4);
 let area=0;
 for(const face of faces){
  const [a,b,c]=face.map(i=>vertices[i]);
  const crossY=(b[2]-a[2])*(c[0]-a[0])-(b[0]-a[0])*(c[2]-a[2]);
  assert.ok(crossY>0);area+=crossY/2;
  const zs=face.map(i=>vertices[i][2]);assert.ok(Math.max(...zs)<=-50||Math.min(...zs)>=52);
 }
 assert.equal(area,224);
 const local=vertices.map(([x,y,z])=>[z-1,y,x-6]);
 assert.equal(Math.min(...local.map(v=>v[0])),-52);assert.equal(Math.max(...local.map(v=>v[0])),52);
 assert.equal(Math.min(...local.map(v=>v[2])),-56);assert.equal(Math.max(...local.map(v=>v[2])),56);
 assert.equal(13*8,104);assert.equal(14*8,112);
});
