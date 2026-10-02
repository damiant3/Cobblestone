import {readFileSync,writeFileSync} from 'node:fs';
import {resolve,join} from 'node:path';
import {createHash} from 'node:crypto';
import {inflateSync} from 'node:zlib';

if(process.argv.length!==3)throw new Error('Usage: sdxl-forge-image-compare.mjs proof-directory');
const root=resolve(process.argv[2]),hash=b=>createHash('sha256').update(b).digest('hex');
if(readFileSync(join(root,'browser.exit'),'utf8').trim()!=='0')throw new Error('Browser matrix did not complete');
const oracle=JSON.parse(readFileSync(join(root,'forge1/evidence.json'),'utf8'));
const log=readFileSync(join(root,'browser.out'),'utf8').split(/\r?\n/).filter(s=>s.startsWith('{')).map(s=>{try{return JSON.parse(s);}catch{return {};}});

function png(path){
  const file=readFileSync(path);if(file.subarray(0,8).toString('hex')!=='89504e470d0a1a0a')throw new Error('PNG signature');
  let w=0,h=0,bpp=0;const chunks=[];
  for(let at=8;at<file.length;){
    const n=file.readUInt32BE(at),type=file.toString('ascii',at+4,at+8);if(at+12+n>file.length)throw new Error('PNG truncated');
    if(type==='IHDR'){w=file.readUInt32BE(at+8);h=file.readUInt32BE(at+12);const colour=file[at+17];if(file[at+16]!==8||![2,6].includes(colour)||file[at+18]||file[at+19]||file[at+20])throw new Error('PNG format');bpp=colour===2?3:4;}
    if(type==='IDAT')chunks.push(file.subarray(at+8,at+8+n));at+=n+12;
  }
  if(w!==1024||h!==1024)throw new Error('PNG dimensions');
  const scan=inflateSync(Buffer.concat(chunks)),stride=w*bpp,decoded=new Uint8Array(h*stride),rgb=new Uint8Array(w*h*3);
  if(scan.length!==h*(stride+1))throw new Error('PNG payload size');
  for(let y=0;y<h;y++){
    const filter=scan[y*(stride+1)];if(filter>4)throw new Error('PNG filter');
    for(let x=0;x<stride;x++){
      const i=y*stride+x,a=x>=bpp?decoded[i-bpp]:0,b=y?decoded[i-stride]:0,c=y&&x>=bpp?decoded[i-stride-bpp]:0;
      let p=0;if(filter===1)p=a;else if(filter===2)p=b;else if(filter===3)p=Math.floor((a+b)/2);else if(filter===4){const v=a+b-c,pa=Math.abs(v-a),pb=Math.abs(v-b),pc=Math.abs(v-c);p=pa<=pb&&pa<=pc?a:pb<=pc?b:c;}
      decoded[i]=(scan[y*(stride+1)+1+x]+p)&255;
    }
  }
  for(let i=0;i<w*h;i++){if(bpp===4&&decoded[i*bpp+3]!==255)throw new Error('Nonopaque browser PNG');for(let c=0;c<3;c++)rgb[i*3+c]=decoded[i*bpp+c];}
  return {rgb,sha256:hash(file)};
}
function compare(a,b){let sum=0,max=0;for(let i=0;i<a.length;i++){const d=Math.abs(a[i]-b[i]);sum+=d;max=Math.max(max,d);}return {mean:sum/a.length,max,count:a.length};}
const rows=[];
for(const [name,id] of [['euler',0],['3m',6]]){
  const folder=join(root,'native-'+id),native=JSON.parse(readFileSync(join(folder,'reference-evidence.json'),'utf8'));
  if(native.nativeExit!==0||native.sampler!==id||native.checkpointSha256!==oracle.checkpointSha256||hash(readFileSync(join(folder,'reference-evidence.json')))!==oracle.nativeReferences[name].evidenceSha256)throw new Error('Native evidence differs');
  const frow=oracle.images.find(r=>r.sampler===name),f=png(join(root,'forge1',frow.file)),n=png(join(folder,native.imageName)),b=png(join(root,'browser-'+id+'.png'));
  if(f.sha256!==frow.sha256||n.sha256!==native.imageSha256)throw new Error('Image provenance differs');
  const recorded=log.find(r=>r.output&&resolve(r.output)===join(root,'browser-'+id+'.png'));
  if(!recorded||recorded.sha256!==b.sha256)throw new Error('Browser image differs from successful grade log');
  const nf=compare(n.rgb,f.rgb),nb=compare(n.rgb,b.rgb),bf=compare(b.rgb,f.rgb);
  if(![frow.nativeComparison.mean,frow.nativeComparison.max,recorded.comparison.mean,recorded.comparison.max].every(Number.isFinite))throw new Error('Independent measurement is missing or nonfinite');
  if(Math.abs(nf.mean-frow.nativeComparison.mean)>1e-12||nf.max!==frow.nativeComparison.max||Math.abs(nb.mean-recorded.comparison.mean)>1e-12||nb.max!==recorded.comparison.max)throw new Error('PNG decoder differs from Pillow or browser canvas measurements');
  if(!(nf.mean<4&&nb.mean<4&&bf.mean<4))throw new Error('Mean image difference exceeds4/255');
  rows.push({sampler:name,nativeForge:nf,browserNative:nb,browserForge:bf,forgeSha256:f.sha256,nativeSha256:n.sha256,browserSha256:b.sha256});
}
writeFileSync(join(root,'comparison.json'),JSON.stringify(rows,null,2)+'\n');
console.log('PASS independent Forge/native/browser comparisons '+JSON.stringify(rows));
