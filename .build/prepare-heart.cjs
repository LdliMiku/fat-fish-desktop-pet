const sharp=require('C:/Users/Lenovo/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
(async()=>{
 const source='assets/particles/pink-heart-source.png';
 const {data,info}=await sharp(source).ensureAlpha().raw().toBuffer({resolveWithObject:true});
 let left=info.width,top=info.height,right=-1,bottom=-1;
 for(let y=0;y<info.height;y++)for(let x=0;x<info.width;x++)if(data[(y*info.width+x)*4+3]>8){
  left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);
 }
 if(right<0)throw Error('Empty heart asset');
 await sharp(source).extract({left,top,width:right-left+1,height:bottom-top+1})
  .resize(120,120,{fit:'contain',background:{r:0,g:0,b:0,alpha:0}})
  .extend({top:4,bottom:4,left:4,right:4,background:{r:0,g:0,b:0,alpha:0}})
  .png().toFile('assets/particles/pink-heart.png');
})().catch(e=>{console.error(e);process.exitCode=1;});
