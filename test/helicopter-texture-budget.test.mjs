import test from 'node:test';
import assert from 'node:assert/strict';
import {limitTextureMips} from '../tools/limit-helicopter-texture-memory.mjs';
function texture(width,height,mips,format=8){
  const header=Buffer.alloc(18);header.writeUInt16LE(5);header.writeUInt16LE(width,2);header.writeUInt16LE(height,4);
  header.writeUInt16LE(1,6);header[8]=mips;header[9]=format;header[10]=2;header[11]=2;header[12]=1;header[13]=4;header.writeInt32LE(-1,14);
  const chunks=[header];for(let i=0;i<mips;i++){chunks.push(Buffer.alloc(width*height*4,i));width=Math.max(1,width>>1);height=Math.max(1,height>>1);}return Buffer.concat(chunks);
}
test('texture budget keeps exact native lower mip bytes, format, sampling and rectangular dimensions',()=>{
  for(const format of [4,8]){
    const original=texture(16,8,5,format),result=limitTextureMips(original,4);
    assert.equal(result.dropped,2);assert.equal(result.width,4);assert.equal(result.height,2);assert.equal(result.mips,3);
    assert.deepEqual(result.data.subarray(18),original.subarray(18+(16*8+8*4)*4));
    assert.deepEqual(result.data.subarray(9,18),original.subarray(9,18));
    assert.equal(limitTextureMips(result.data,4).dropped,0);
  }
});
test('texture budget rejects malformed data and cannot invent missing mips or reinterpret compressed textures',()=>{
  assert.throws(()=>limitTextureMips(Buffer.alloc(2)),/v5/);
  assert.throws(()=>limitTextureMips(texture(8,8,4).subarray(0,-1),4),/length/);
  assert.throws(()=>limitTextureMips(texture(8,8,1),4),/No existing lower mip/);
  assert.throws(()=>limitTextureMips(texture(8,8,4,108),4),/Unsupported graphics format/);
  const compressed=texture(8,8,4);compressed.writeInt32LE(10,14);
  assert.throws(()=>limitTextureMips(compressed,4),/uncompressed/);
  const volume=texture(8,8,4);volume.writeUInt16LE(2,6);
  assert.throws(()=>limitTextureMips(volume,4),/2D/);
});
