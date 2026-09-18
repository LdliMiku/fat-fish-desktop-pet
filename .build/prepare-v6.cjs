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
 if(expected===3)ordered=parts.sort((a,b)=>a.minX-b.minX);else for(let r=0;r<3;r++)ordered.push(...parts.slice(r*3,r*3+3).sort((a,b)=>a.minX-b.minX));
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
  sources.push({file,frame:{...bank.frames[1],scaleHeight:neutral.h,widthFactor:factor}});
 }
 const closeFile=dir+'/bridge-blinks.png';
 if(fs.existsSync(closeFile)){
  const bank=await readSheet(closeFile),neutral=bank.frames[4];
  const factor=(old.sheet.frames[0].w/old.sheet.baseHeight)/(neutral.w/neutral.h);
  sources[2]={file:closeFile,frame:{...neutral,scaleHeight:neutral.h,widthFactor:factor}};
  for(const i of [0,1,2,3,5,6,7,8])sources.push({file:closeFile,frame:{...bank.frames[i],scaleHeight:neutral.h,widthFactor:factor}});
 }else{if(!process.argv.includes('--open-preview'))throw Error('Closed bridge poses missing');for(let i=34;i<42;i++)sources.push(sources[i]);}
 const cw=Math.max(...sources.map(s=>s.frame.w))+8,ch=Math.max(...sources.map(s=>s.frame.h))+8;
 const layers=[],frames=[];
 for(let i=0;i<sources.length;i++){
  const {file,frame:f}=sources[i],left=(i%4)*cw+4,top=Math.floor(i/4)*ch+4;
  layers.push({input:await sharp(file).extract({left:f.x,top:f.y,width:f.w,height:f.h}).png().toBuffer(),left,top});frames.push({...f,x:left,y:top});
 }
 await sharp({create:{width:cw*4,height:ch*Math.ceil(sources.length/4),channels:4,background:{r:0,g:0,b:0,alpha:0}}}).composite(layers).png().toFile(dir+'/unified-sheet.png');
 old.sheet.frames=frames;old.sheet.width=cw*4;old.sheet.height=ch*Math.ceil(sources.length/4);old.version='0.1';
 fs.writeFileSync(dir+'/atlas.json',JSON.stringify(old,null,2));console.log({frames:frames.length,cell:[cw,ch]});
})().catch(e=>{console.error(e);process.exitCode=1});
