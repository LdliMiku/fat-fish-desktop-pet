using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace FatFishPet
{
    // Runtime tweening uses motion correspondence, so moving edges align before their colors blend.
    internal sealed class PetMotionWarp
    {
        private const int Width = 340, Height = 360;
        private readonly Dictionary<int, float[][]> fields = new Dictionary<int, float[][]>();

        public PetMotionWarp()
        {
            using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("MotionFields"))
            {
                if(resource==null)return;
                using(var gzip=new GZipStream(resource,CompressionMode.Decompress))
                using(var reader=new BinaryReader(gzip))
                {
                    if(reader.ReadInt32()!=0x46504D31||reader.ReadInt32()!=Width||reader.ReadInt32()!=Height||reader.ReadInt32()!=2)throw new InvalidDataException("动画位移数据格式不匹配。");
                    int pairs=reader.ReadInt32();
                    for(int i=0;i<pairs;i++)
                    {
                        int a=reader.ReadInt32(),b=reader.ReadInt32();
                        fields.Add(a*64+b,new[]{ReadField(reader),ReadField(reader)});
                    }
                }
            }
        }

        private static float[] ReadField(BinaryReader reader)
        {
            const int gw=Width/2,gh=Height/2;
            var grid=new float[gw*gh*2];for(int i=0;i<grid.Length;i++)grid[i]=reader.ReadInt16()/32f;
            var full=new float[Width*Height*2];
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int xx=Math.Min(x/2,gw-1),yy=Math.Min(y/2,gh-1),right=Math.Min(xx+1,gw-1),bottom=Math.Min(yy+1,gh-1);
                float fx=(x%2)*.5f,fy=(y%2)*.5f;
                for(int c=0;c<2;c++)full[(y*Width+x)*2+c]=(grid[(yy*gw+xx)*2+c]*(1-fx)+grid[(yy*gw+right)*2+c]*fx)*(1-fy)+(grid[(bottom*gw+xx)*2+c]*(1-fx)+grid[(bottom*gw+right)*2+c]*fx)*fy;
            }
            return full;
        }

        public bool Contains(int a,int b) { return fields.ContainsKey(a*64+b); }

        public void RenderWeighted(int[] indices,float[] weights,byte[][] sources,int count,byte[] output)
        {
            var flows=new float[count*count][];
            for(int i=0;i<count;i++)for(int j=0;j<count;j++)
            {
                if(i==j)continue;
                int a=indices[i],b=indices[j];float[][] pair;
                if(fields.TryGetValue(Math.Min(a,b)*64+Math.Max(a,b),out pair))flows[i*count+j]=pair[a<b?0:1];
            }
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int pixel=y*Width+x,fi=pixel*2,oi=pixel*4;
                float r0=0,r1=0,r2=0,r3=0;
                for(int i=0;i<count;i++)
                {
                    float sx=x,sy=y;
                    // Invert the blended displacement at its source position instead of sampling
                    // forward flow at the destination, which misaligns large face turns.
                    for(int iteration=0;iteration<3;iteration++)
                    {
                        float px=Math.Max(0,Math.Min(Width-1.001f,sx)),py=Math.Max(0,Math.Min(Height-1.001f,sy));
                        int gx=(int)px,gy=(int)py,k=(gy*Width+gx)*2;
                        float fx0=px-gx,fy0=py-gy,dx=0,dy=0;
                        for(int j=0;j<count;j++)
                        {
                            var f=flows[i*count+j];if(f==null)continue;
                            dx+=((f[k]*(1-fx0)+f[k+2]*fx0)*(1-fy0)+(f[k+Width*2]*(1-fx0)+f[k+Width*2+2]*fx0)*fy0)*weights[j];
                            dy+=((f[k+1]*(1-fx0)+f[k+3]*fx0)*(1-fy0)+(f[k+Width*2+1]*(1-fx0)+f[k+Width*2+3]*fx0)*fy0)*weights[j];
                        }
                        sx=x-dx;sy=y-dy;
                    }
                    sx=Math.Max(0,Math.Min(Width-1.001f,sx));sy=Math.Max(0,Math.Min(Height-1.001f,sy));
                    int xx=(int)sx,yy=(int)sy,a=(yy*Width+xx)*4;
                    float fx=sx-xx,fy=sy-yy,w=weights[i];
                    float w0=(1-fx)*(1-fy)*w,w1=fx*(1-fy)*w,w2=(1-fx)*fy*w,w3=fx*fy*w;
                    byte[] src=sources[i];int b=a+4,c=a+Width*4,d=c+4;
                    r0+=src[a]*w0+src[b]*w1+src[c]*w2+src[d]*w3;
                    r1+=src[a+1]*w0+src[b+1]*w1+src[c+1]*w2+src[d+1]*w3;
                    r2+=src[a+2]*w0+src[b+2]*w1+src[c+2]*w2+src[d+2]*w3;
                    r3+=src[a+3]*w0+src[b+3]*w1+src[c+3]*w2+src[d+3]*w3;
                }
                output[oi]=(byte)Math.Min(255,r0+.5f);output[oi+1]=(byte)Math.Min(255,r1+.5f);
                output[oi+2]=(byte)Math.Min(255,r2+.5f);output[oi+3]=(byte)Math.Min(255,r3+.5f);
            }
        }

        public void Render(int a,int b,float t,byte[] first,byte[] second,byte[] result)
        {
            float[][] pair=fields[a*64+b];float[] forward=pair[0],backward=pair[1];float u=1-t;
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int index=y*Width+x,fi=index*2;
                float ax=Math.Max(0,Math.Min(Width-1.001f,x-forward[fi]*t)),ay=Math.Max(0,Math.Min(Height-1.001f,y-forward[fi+1]*t));
                float bx=Math.Max(0,Math.Min(Width-1.001f,x-backward[fi]*u)),by=Math.Max(0,Math.Min(Height-1.001f,y-backward[fi+1]*u));
                int ix=(int)ax,iy=(int)ay,jx=(int)bx,jy=(int)by;
                float fx=ax-ix,fy=ay-iy,gx=bx-jx,gy=by-jy;
                int ai=(iy*Width+ix)*4,bi=(jy*Width+jx)*4,target=index*4;
                float a00=(1-fx)*(1-fy)*u,a10=fx*(1-fy)*u,a01=(1-fx)*fy*u,a11=fx*fy*u;
                float b00=(1-gx)*(1-gy)*t,b10=gx*(1-gy)*t,b01=(1-gx)*gy*t,b11=gx*gy*t;
                for(int c=0;c<4;c++)result[target+c]=(byte)(first[ai+c]*a00+first[ai+4+c]*a10+first[ai+Width*4+c]*a01+first[ai+Width*4+4+c]*a11+second[bi+c]*b00+second[bi+4+c]*b10+second[bi+Width*4+c]*b01+second[bi+Width*4+4+c]*b11+.5f);
            }
        }
    }
}
