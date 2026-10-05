import {readFileSync,writeFileSync,mkdirSync,existsSync,createReadStream} from 'node:fs';
import {resolve,dirname,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import {execFileSync,spawnSync} from 'node:child_process';

const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../..'),out=process.argv[2]&&resolve(process.argv[2]);
if(!out||/\s/.test(out)||existsSync(out))throw new Error('Usage: sd15-sampler-reference.mjs new-output-directory-without-spaces');
mkdirSync(out,{recursive:true});
const hash=async p=>{const h=createHash('sha256');for await(const b of createReadStream(p))h.update(b);return h.digest('hex');};
const memory=()=>{const m=JSON.parse(execFileSync('pwsh',['-NoProfile','-Command','Get-CimInstance Win32_OperatingSystem | Select-Object FreePhysicalMemory,FreeVirtualMemory | ConvertTo-Json -Compress'],{encoding:'utf8',windowsHide:true}));if(m.FreePhysicalMemory<=1572864||m.FreeVirtualMemory<=26214400)throw new Error('RAM/commit admission refused');console.log('Memory '+JSON.stringify(m));return m;};
const run=(script,args,label)=>{const r=spawnSync('pwsh',['-NoProfile','-File',join(repo,script),...args],{cwd:repo,encoding:'utf8',windowsHide:true,maxBuffer:16777216});writeFileSync(join(out,label+'.out'),r.stdout||'');writeFileSync(join(out,label+'.err'),r.stderr||'');if(r.stdout)console.log(r.stdout.trim());if(r.stderr)console.error(r.stderr.trim());if(r.error)throw r.error;if(r.status!==0)throw new Error(label+' failed '+r.status);};
const source=join(repo,'apps/diffusion/Sd15SamplerReference.codex'),kernel=join(repo,'seed/Codex.cdx'),checkpoint=join(repo,'build-output/diffusion-models/realisticVisionV60B1_v20Novae.safetensors');
const evidence={sourceSha256:await hash(source),kernelSha256:await hash(kernel),checkpointSha256:await hash(checkpoint),prompt:'a photo of a cat',negative:'',seed:7,steps:6,cfg:7,width:512,height:512};
run('build/bundle-app.ps1',['-Src',source,'-Out',join(out,'reference.codex')],'bundle');
const path=join(out,'reference.codex');
evidence.bundleSha256=await hash(path);evidence.compileMemory=memory();
run('build/compile.ps1',['-Src',path,'-Out',join(out,'reference.cdx'),'-Log',join(out,'compile.log'),'-Kernel',kernel],'compile');
run('build/mint-clip-bpe-disk.ps1',['-Out',join(out,'clip.img')],'mint');
evidence.diskSha256=await hash(join(out,'clip.img'));evidence.cdxSha256=await hash(join(out,'reference.cdx'));
writeFileSync(join(out,'reference.vmargs'),'-gpu-files '+repo.replaceAll('\\','/')+'\n-gpu-out '+out.replaceAll('\\','/')+'\n');
evidence.runMemory=memory();run('build/test-run.ps1',['-Kernel',join(out,'reference.cdx'),'-OutFile',join(out,'result.txt'),'-DiskFile',join(out,'clip.img'),'-VmArgsFile',join(out,'reference.vmargs')],'run');
const result=readFileSync(join(out,'result.txt'),'utf8').replaceAll('\r','');evidence.images=[];
for(let kind=0;kind<=6;kind++){if(!result.split('\n').some(s=>new RegExp('^sampler-'+kind+': ok png 512x512: [0-9]+ bytes$').test(s)))throw new Error('Native sampler failed: '+result);const file='sampler-'+kind+'.png',png=readFileSync(join(out,file));if(png.subarray(0,8).toString('hex')!=='89504e470d0a1a0a'||png.readUInt32BE(16)!==512||png.readUInt32BE(20)!==512)throw new Error('Native PNG shape differs');evidence.images.push({kind,file,scheduler:kind===0?'Automatic':'Karras',sha256:await hash(join(out,file))});}
if(!result.split('\n').some(s=>/^one-step: ok png 512x512: [0-9]+ bytes$/.test(s)))throw new Error('One-step native boundary failed: '+result);
evidence.boundary={kind:0,steps:1,scheduler:'Automatic',file:'one-step.png',sha256:await hash(join(out,'one-step.png'))};
if(await hash(kernel)!==evidence.kernelSha256||await hash(source)!==evidence.sourceSha256)throw new Error('Native inputs moved');evidence.nativeExit=0;
writeFileSync(join(out,'evidence.json'),JSON.stringify(evidence,null,2)+'\n');console.log('PASS native SD1.5 sampler matrix');
