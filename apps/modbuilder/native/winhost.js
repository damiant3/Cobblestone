(function(root){
  'use strict';
  const align=(n,a)=>Math.ceil(n/a)*a;
  // Codex passes target/argv in RDI/RSI and retains its heap frontier in R10.
  // Win64 uses RCX/RDX/R8/R9, 32-byte shadow space, then four stack arguments.
  // Save R10 across the external call; align RSP without changing the caller's frame.
  const thunk=Uint8Array.from([
    0x55,0x48,0x89,0xe5,0x41,0x52,0x48,0x83,0xe4,0xf0,0x48,0x83,0xec,0x60,
    0x48,0x89,0xf8,0x49,0x89,0xf2,
    0x49,0x8b,0x0a,0x49,0x8b,0x52,0x08,0x4d,0x8b,0x42,0x10,0x4d,0x8b,0x4a,0x18,
    0x4d,0x8b,0x5a,0x20,0x4c,0x89,0x5c,0x24,0x20,
    0x4d,0x8b,0x5a,0x28,0x4c,0x89,0x5c,0x24,0x28,
    0x4d,0x8b,0x5a,0x30,0x4c,0x89,0x5c,0x24,0x30,
    0x4d,0x8b,0x5a,0x38,0x4c,0x89,0x5c,0x24,0x38,
    0xff,0xd0,0x4c,0x8b,0x55,0xf8,0x48,0x89,0xec,0x5d,0xc3
  ]);
  function specialize(input,map,consoleMode=false,options={}){
    const src=new Uint8Array(input), view=new DataView(src.buffer,src.byteOffset,src.byteLength);
    const need=(at,n)=>{if(!Number.isSafeInteger(at)||at<0||n<0||at+n>src.length)throw new Error('PE field outside input');};
    const u16=at=>{need(at,2);return view.getUint16(at,true);};
    const u32=at=>{need(at,4);return view.getUint32(at,true);};
    const u64=at=>{need(at,8);const n=Number(view.getBigUint64(at,true));if(!Number.isSafeInteger(n))throw new Error('Oversized PE address');return n;};
    if(u16(0)!==0x5a4d)throw new Error('Expected PE executable');
    const pe=u32(60),coff=pe+4,opt=coff+20;
    if(u32(pe)!==0x4550||u16(coff)!==0x8664||u16(opt)!==0x20b||u16(coff+2)!==2)throw new Error('Expected Prism x64 PE with two sections');
    const base=u64(opt+24),sectionAt=opt+u16(coff+16),sections=[];
    for(let i=0;i<2;i++){const at=sectionAt+40*i;need(at,40);const name=String.fromCharCode(...src.slice(at,at+8)).replace(/\0.*$/,'');const s={name,at,size:u32(at+8),rva:u32(at+12),rawSize:u32(at+16),raw:u32(at+20)};need(s.raw,s.rawSize);sections.push(s);}
    const code=sections.find(s=>s.name==='.text'),idata=sections.find(s=>s.name==='.idata');
    if(base!==0x100000||!code||!idata||code.rva!==0x2000||idata.rva!==0x1000||code.size>code.rawSize)throw new Error('Unsupported hosted Windows layout');
    const offset=rva=>{const s=sections.find(s=>rva>=s.rva&&rva<s.rva+s.rawSize);if(!s)throw new Error('Import RVA outside sections');return s.raw+rva-s.rva;};
    const cstr=rva=>{let at=offset(rva),out='';for(let i=0;i<256;i++){need(at,1);const c=src[at++];if(!c)return out;if(c>127)throw new Error('Non-ASCII PE import');out+=String.fromCharCode(c);}throw new Error('Unterminated PE import');};
    const imports=[],importAt=offset(u32(opt+120));
    for(let i=0;i<4;i++){const at=importAt+i*20;need(at,20);const lookup=u32(at),name=u32(at+12),iat=u32(at+16);if(!lookup&&!name&&!iat)break;
      if(imports.length>=2)throw new Error('Unexpected import count');
      const funcs=[];for(let j=0;j<32;j++){const value=u64(offset(lookup)+j*8);if(!value)break;if(value>0xffffffff)throw new Error('Ordinal imports unsupported');funcs.push(cstr(value+2));}
      if(!funcs.length||funcs.length>=32||iat<idata.rva+80||iat+8*(funcs.length+1)>idata.rva+2048)throw new Error('Unsupported import table');
      imports.push({dll:cstr(name),funcs,iat:iat-idata.rva});
    }
    if(!imports.some(d=>d.dll.toLowerCase()==='kernel32.dll'&&d.funcs.includes('VirtualAlloc')&&d.funcs.includes('ExitProcess')))throw new Error('Missing hosted runtime imports');
    const entry=code.raw+u32(opt+16)-code.rva;let allocationSlot=null;
    for(let i=entry;i<Math.min(entry+96,code.raw+code.size)-7;i++)if(src[i]===0xff&&src[i+1]===0x14&&src[i+2]===0x25){allocationSlot=u32(i+3)-base-idata.rva;break;}
    const firstSlot=allocationSlot-24;
    if(![88,188,196].includes(firstSlot))throw new Error('Unknown compiler Win64 import ABI');
    const kernel=['GetStdHandle','WriteFile','ExitProcess','VirtualAlloc','ReadFile'];
    if(firstSlot===196)kernel.push('AddVectoredExceptionHandler');
    imports.length=0;imports.push({dll:'kernel32.dll',funcs:kernel,iat:firstSlot});
    if(firstSlot!==88)imports.push({dll:'ws2_32.dll',funcs:['WSAStartup','socket','bind','listen','accept','closesocket','recv','send','setsockopt'],iat:firstSlot+(kernel.length+1)*8});
    const matches=map.split(/\r?\n/).map(line=>/^0x([0-9a-f]+)\s+(\d+)\s+(.+)$/i.exec(line)).filter(m=>m&&/(^|_)mb-native-call$/.test(m[3]));
    if(matches.length!==1)throw new Error('Compile with map and passes=none; expected one mb-native-call adapter');
    const patch=parseInt(matches[0][1],16)-base-code.rva,size=Number(matches[0][2]);
    if(patch<0||patch+5>code.size||size<5||src[code.raw+patch]!==0x55)throw new Error('Native adapter symbol has an unsupported prologue/address');
    imports.push({dll:'kernel32.dll',funcs:['LoadLibraryExW','GetProcAddress'],iat:2048});
    const data=new Uint8Array(4096),dv=new DataView(data.buffer);let cursor=2560;
    const p32=(at,v)=>dv.setUint32(at,v,true),p64=(at,v)=>dv.setBigUint64(at,BigInt(v),true);
    const string=(s,hint=false)=>{cursor=align(cursor,2);const at=cursor;cursor+=s.length+1+(hint?2:0);if(cursor>data.length)throw new Error('Native import page full');for(let i=0;i<s.length;i++)data[at+(hint?2:0)+i]=s.charCodeAt(i);return at;};
    imports.forEach((im,index)=>{const names=im.funcs.map(n=>string(n,true)),dll=string(im.dll);cursor=align(cursor,8);const lookup=cursor;cursor+=8*(names.length+1);if(cursor>data.length)throw new Error('Native import page full');names.forEach((at,i)=>{p64(lookup+8*i,idata.rva+at);p64(im.iat+8*i,idata.rva+at);});p32(index*20,idata.rva+lookup);p32(index*20+12,idata.rva+dll);p32(index*20+16,idata.rva+im.iat);});
    const returnUrl=String(options.returnUrl||'');
    if(returnUrl.length>8192||/[^\x20-\x7e]/.test(returnUrl)||returnUrl && !/^(file:\/\/\/|https?:\/\/)/.test(returnUrl))throw new Error('Unsupported helper return URL');
    const loader=new Uint8Array(options.loader||0),port=options.port||8789;
    if(!Number.isInteger(port)||port<1024||port>65535||loader.length>4194304)throw new Error('Invalid native helper configuration');
    if(loader.length&&(loader[0]!==77||loader[1]!==90))throw new Error('Expected a PE loader template');
    const urlBytes=new TextEncoder().encode(returnUrl),thunkAt=align(code.size,16),urlAt=align(thunkAt+thunk.length,16),loaderAt=align(urlAt+urlBytes.length+1,16),newCodeSize=loaderAt+loader.length,header=u32(opt+60),newRaw=header+4096,out=new Uint8Array(newRaw+align(newCodeSize,512));
    p64(2080,base+code.rva+urlAt);p64(2088,urlBytes.length);p64(2096,options.uninstall?1:0);
    p64(2104,base+code.rva+loaderAt);p64(2112,loader.length);p64(2120,port);
    out.set(src.slice(0,header));out.set(data,header);out.set(src.slice(code.raw,code.raw+code.size),newRaw);out.set(thunk,newRaw+thunkAt);
    out.set(urlBytes,newRaw+urlAt);
    out.set(loader,newRaw+loaderAt);
    const ov=new DataView(out.buffer);out[newRaw+patch]=0xe9;ov.setInt32(newRaw+patch+1,thunkAt-patch-5,true);
    ov.setUint32(idata.at+8,4096,true);ov.setUint32(idata.at+16,4096,true);ov.setUint32(idata.at+20,header,true);
    ov.setUint32(code.at+8,newCodeSize,true);ov.setUint32(code.at+16,align(newCodeSize,512),true);ov.setUint32(code.at+20,newRaw,true);
    ov.setUint32(opt+4,newCodeSize,true);ov.setUint32(opt+8,4096,true);ov.setUint32(opt+56,align(code.rva+newCodeSize,4096),true);ov.setUint16(opt+68,consoleMode?3:2,true);
    ov.setUint32(opt+120,idata.rva,true);ov.setUint32(opt+124,4096,true);ov.setUint32(opt+208,idata.rva+Math.min(...imports.map(i=>i.iat)),true);ov.setUint32(opt+212,2072-Math.min(...imports.map(i=>i.iat)),true);
    return out;
  }
  const api={specialize};if(typeof module!=='undefined'&&module.exports)module.exports=api;else root.ModBuilderWinHost=api;
})(globalThis);
