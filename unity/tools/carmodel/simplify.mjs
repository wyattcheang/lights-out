// meshoptimizer simplify: node simplify.mjs <in.f32> <in.u32> <targetTris> <out.u32>
import {MeshoptSimplifier as S} from 'meshoptimizer';import fs from 'fs';
await S.ready;
const [,,vf,ff,tgt,out]=process.argv;
const V=new Float32Array(fs.readFileSync(vf).buffer.slice(0)),F=new Uint32Array(fs.readFileSync(ff).buffer.slice(0));
const [r,err]=S.simplify(F,V,3,Math.min(F.length,+tgt*3),0.05);
console.log(`  ${F.length/3} -> ${r.length/3} tris (error ${err.toFixed(5)})`);
fs.writeFileSync(out,Buffer.from(r.buffer,r.byteOffset,r.byteLength));
