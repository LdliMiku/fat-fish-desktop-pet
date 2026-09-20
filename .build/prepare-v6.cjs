const fs=require('fs'),path=require('path');
let sharp;
try { sharp=require('sharp'); }
catch (_) { sharp=require('C:/Users/Lenovo/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp'); }
async function readSheet(file,expected=9){
 const {data,info}=await sharp(file).ensureAlpha().raw().toBuffer({resolveWithObject:true});
 const w=info.width,h=info.height,seen=new Uint8Array(w*h),q=new Int32Array(w*h),parts=[];
 for(let k=0;k<w*h;k++){
 if(seen[k]||data[k*4+3]<=96)continue;
 let read=0,end=1,count=0,minX=w,maxX=0,minY=h,maxY=0;q[0]=k;seen[k]=1;
 while(read<end){const n=q[read++],x=n%w,y=Math.floor(n/w);count++;minX=Math.min(minX,x);maxX=Math.max(maxX,x);minY=Math.min(minY,y);maxY=Math.max(maxY,y);
 for(let dy=-1;dy<=1;dy++)for(let dx=-1;dx<=1;dx++){const xx=x+dx,yy=y+dy;if(xx<0||xx>=w||yy<0||yy>=h)continue;const p=yy*w+xx;if(!seen[p]&&data[p*4+3]>96){seen[p]=1;q[end++]=p;}}}
 if(count>4000)parts.push({minX,maxX,minY,maxY,count});}
 if(parts.length!==expected)throw Error(file+' unexpected component count '+parts.length);
 parts.sort((a,b)=>(a.minY+a.maxY)-(b.minY+b.maxY));let ordered=[];
 if(expected===3||expected===5)ordered=parts.sort((a,b)=>a.minX-b.minX);else for(let r=0;r<3;r++)ordered.push(...parts.slice(r*3,r*3+3).sort((a,b)=>a.minX-b.minX));
 const frames=ordered.map(b=>{
 const x=Math.max(0,b.minX-2),y=Math.max(0,b.minY-2),right=Math.min(w,b.maxX+3),bottom=Math.min(h,b.maxY+3),bh=b.maxY-b.minY+1;
 let sum=0,n=0;for(let yy=Math.floor(b.minY+bh*.93);yy<=b.maxY;yy++)for(let xx=b.minX;xx<=b.maxX;xx++)if(data[(yy*w+xx)*4+3]>200){sum+=xx;n++;}
 return {x,y,w:right-x,h:bottom-y,headX:sum/n-x};});
 return {width:w,height:h,frames};
}
(async()=>{
 const dir='assets/animations/v6',old=JSON.parse(fs.readFileSync('assets/animations/v5/atlas.json','utf8'));
 const sources=old.sheet.frames.map(f=>({file:'assets/animations/v5/unified-sheet.png',frame:{...f}}));
 // Use the generated neutral from the same bridge artwork as the gaze poses.
 const neutralFile=dir+'/bridges/17.png',neutralBank=await readSheet(neutralFile,3),nf=neutralBank.frames[0];
 const neutralFactor=(old.sheet.frames[0].w/old.sheet.baseHeight)/(nf.w/nf.h);
 sources[0]={file:neutralFile,frame:{...nf,scaleHeight:nf.h,widthFactor:neutralFactor}};
 sources[8]=sources[0];
 for(const id of [16,17,18,19,21,22,23,24]){
  const file=dir+'/bridges/'+id+'.png',bank=await readSheet(file,3),neutral=bank.frames[0];
  const factor=(old.sheet.frames[0].w/old.sheet.baseHeight)/(neutral.w/neutral.h);
  // Use the full drawing from the SAME bridge as its half pose. Both retain
  // that bridge's neutral height and width calibration, never a regenerated strip.
  if(id>=16&&id<=18)sources[id]={file,frame:{...bank.frames[2],scaleHeight:neutral.h,widthFactor:factor}};
  sources.push({file,frame:{...bank.frames[1],scaleHeight:neutral.h,widthFactor:factor}});
 }
 const closeFile=dir+'/bridge-blinks.png';
 if(fs.existsSync(closeFile)){
  const bank=await readSheet(closeFile),neutral=bank.frames[4];
  const factor=(old.sheet.frames[0].w/old.sheet.baseHeight)/(neutral.w/neutral.h);
  sources[2]={file:closeFile,frame:{...neutral,scaleHeight:neutral.h,widthFactor:factor}};
  for(const i of [0,1,2,3,5,6,7,8])sources.push({file:closeFile,frame:{...bank.frames[i],scaleHeight:neutral.h,widthFactor:factor}});
 }else{if(!process.argv.includes('--open-preview'))throw Error('Closed bridge poses missing');for(let i=34;i<42;i++)sources.push(sources[i]);}
 // One complete bank: calibration neutral, two hands-up entry drawings,
 // left full/half, bowed center, right half/full, hands-down recovery (50-58).
 // Use ONE neutral scale for every pose; never normalize a bowed/turned
 // drawing's shorter height or wider hair into an enlarged body.
 // Head-pet drawings actually used by the build. This file is the user's
 // hand-editable master: it started as the tone-matched copy of
 // head-pet-original-tone-sheet.png and may be edited directly. Do not run
 // .build/prepare-head-pet-tone.cjs after manual edits, it would overwrite it.
 const petFile=dir+'/head-pet-manual-sheet.png',petBank=await readSheet(petFile,9),petNeutral=petBank.frames[0];
 // Reference already includes the original width correction; do not apply it twice.
 for(const frame of petBank.frames)sources.push({file:petFile,frame:{...frame,scaleHeight:petNeutral.h,widthFactor:1}});
 const cw=Math.max(...sources.map(s=>s.frame.w))+8,ch=Math.max(...sources.map(s=>s.frame.h))+8;
 const layers=[],frames=[];
 for(let i=0;i<sources.length;i++){
  const {file,frame:f}=sources[i],left=(i%4)*cw+4,top=Math.floor(i/4)*ch+4;
  layers.push({input:await sharp(file).extract({left:f.x,top:f.y,width:f.w,height:f.h}).png().toBuffer(),left,top});frames.push({...f,x:left,y:top});
 }
 const sheetWidth=cw*4,sheetHeight=ch*Math.ceil(sources.length/4);
 const composed=await sharp({create:{width:sheetWidth,height:sheetHeight,channels:4,background:{r:0,g:0,b:0,alpha:0}}}).composite(layers).png().toBuffer();
 // 色调统一：以正视帧（第 0 帧）为基准，把每一帧的不透明像素平均亮度调到同一水平，
 // 这样各个动作之间切换时不再出现明暗跳变。只改 RGB，alpha、几何与注册数据完全不动；
 // 单帧修正量限制在 ±8 之内，避免异常素材被过度拉伸。
 // 统计每帧的不透明像素、整体通道和、蓝度权重总和，以及"蓝色像素"（w>0.75）的通道和。
 // 蓝度 w 会平滑过渡，避免在阈值附近产生可见的色块边界。
 const MAX_TONE_SHIFT=10,ALPHA_FLOOR=8,OPAQUE=200,BLUE_W=.75;
 const raw=await sharp(composed).ensureAlpha().raw().toBuffer({resolveWithObject:true});
 const blueness=(r,g,b)=>Math.max(0,Math.min(1,(b-r-20)/40));
 const stats=frames.map(f=>{
  const s={n:0,sum:[0,0,0],wSum:0,blueN:0,blueSum:[0,0,0],wBlueSum:0};
  for(let y=f.y;y<f.y+f.h;y++)for(let x=f.x;x<f.x+f.w;x++){
   const p=(y*sheetWidth+x)*4;if(raw.data[p+3]<=OPAQUE)continue;
   const r=raw.data[p],g=raw.data[p+1],b=raw.data[p+2],w=blueness(r,g,b);
   s.n++;s.sum[0]+=r;s.sum[1]+=g;s.sum[2]+=b;s.wSum+=w;
   if(w>BLUE_W){s.blueN++;s.blueSum[0]+=r;s.blueSum[1]+=g;s.blueSum[2]+=b;s.wBlueSum+=w;}
  }
  return s;
 });
 // 每帧求一组"基础偏移 + 蓝度加权偏移"，同时满足：整体均值对齐基准帧、蓝色像素均值对齐基准帧。
 const target=stats[0];
 const shifts=stats.map(s=>{
  const shift=[0,0,0];
  if(s.n===0)return shift;
  for(let c=0;c<3;c++){
   const m=s.sum[c]/s.n,b=s.blueN>0?s.blueSum[c]/s.blueN:m;
   const tm=target.sum[c]/target.n,tb=target.blueN>0?target.blueSum[c]/target.blueN:tm;
   const det=s.n*s.wBlueSum-s.wSum*s.blueN;
   if(Math.abs(det)<1e-6||s.blueN===0){shift[c]={base:tm-m,extra:0};continue;}
   const A=s.n*(tm-m),B=s.blueN*(tb-b);
   shift[c]={base:(s.wBlueSum*A-s.wSum*B)/det,extra:(s.n*B-s.blueN*A)/det};
  }
  return shift;
 });
 const clampShift=v=>Math.max(-MAX_TONE_SHIFT,Math.min(MAX_TONE_SHIFT,v));
 const out=Buffer.from(raw.data);
 for(let i=1;i<frames.length;i++){
  const f=frames[i],s=shifts[i];
  for(let y=f.y;y<f.y+f.h;y++)for(let x=f.x;x<f.x+f.w;x++){
   const p=(y*sheetWidth+x)*4;if(out[p+3]<=ALPHA_FLOOR)continue;
   const r=out[p],g=out[p+1],b=out[p+2],w=blueness(r,g,b);
   for(let c=0;c<3;c++){
    const d=clampShift(s[c].base+s[c].extra*w);
    const value=out[p+c]+d;
    out[p+c]=value<0?0:value>255?255:Math.round(value);
   }
  }
 }
 await sharp(out,{raw:{width:sheetWidth,height:sheetHeight,channels:4}}).png().toFile(dir+'/unified-sheet.png');
 old.sheet.frames=frames;old.sheet.width=cw*4;old.sheet.height=ch*Math.ceil(sources.length/4);old.version='0.1';
 fs.writeFileSync(dir+'/atlas.json',JSON.stringify(old,null,2));
 const summary=shifts.map((s,i)=>({frame:i,base:s.map(v=>Math.round(v.base*10)/10),blueExtra:s.map(v=>Math.round(v.extra*10)/10)}));
 console.log({frames:frames.length,cell:[cw,ch],toneBaseFrames:summary.filter(v=>v.frame<3||v.frame%13===0).length,exampleShifts:summary.filter(v=>[16,17,24,35,41,50,55].includes(v.frame))});
})().catch(e=>{console.error(e);process.exitCode=1});
