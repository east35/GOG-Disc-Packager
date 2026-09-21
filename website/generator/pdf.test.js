import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createPdf } from './pdf.js';
import { newProject, parseProject } from './model.js';
import { readFileSync } from 'node:fs';

test('PDF declares every page at its requested size and has valid byte offsets', async () => {
  const canvas = () => ({width:100,height:100,getContext:()=>({save(){},restore(){},setTransform(){},fillRect(){}}),toDataURL:()=> 'data:image/jpeg;base64,/9j/2Q=='});
  const result=createPdf([{canvas:canvas(),widthMm:120,heightMm:120},{canvas:canvas(),widthMm:287,heightMm:176.4}]);
  const bytes=Buffer.from(await result.arrayBuffer()), text=bytes.toString('latin1');
  assert.equal(result.type,'application/pdf');
  assert.match(text,/\/Count 2\b/);
  assert.match(text,/\/MediaBox \[0 0 340\.1575 340\.1575\]/);
  const offset=Number(text.match(/startxref\n(\d+)/)[1]);
  assert.equal(text.slice(offset,offset+4),'xref');
  const entries=text.slice(offset).split('\n').slice(3,11);
  entries.forEach((entry,index)=>assert.ok(text.slice(Number(entry.slice(0,10))).startsWith(`${index+1} 0 obj`)));
});
test('interior artwork and black/white settings survive JSON and reject invalid choices', () => {
  const p=newProject();p.game.artwork.interior={imageUrl:'data:image/png;base64,AAAA',scale:1.2,focalPoint:{x:.4,y:.6}};
  p.game.artwork.interiorMode='artwork';p.game.artwork.spineBackground='white';p.game.artwork.mediaMarkColor='white';
  assert.deepEqual(parseProject(JSON.stringify(p)),p);
  p.game.artwork.spineBackground='red';assert.throws(()=>parseProject(JSON.stringify(p)));
});
test('every disc media color has a local PNG asset', () => {
  for (const name of ['dvd','cd','blu-ray']) for (const color of ['black','white']) {
    const bytes=readFileSync(new URL(`../public/assets/template/${name}-${color}.png`,import.meta.url));
    assert.equal(bytes.subarray(1,4).toString(),'PNG');
  }
});
test('preservation settings round trip and older projects default to disabled', () => {
  const p = newProject();
  p.game.preservation = { enabled: true, x: 0.25, y: 0.8, size: 72 };
  assert.deepEqual(parseProject(JSON.stringify(p)).game.preservation, p.game.preservation);
  p.game.preservation.x = 2;
  assert.throws(() => parseProject(JSON.stringify(p)));
  delete p.game.preservation;
  assert.equal(parseProject(JSON.stringify(p)).game.preservation.enabled, false);
});
