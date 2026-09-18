using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;

namespace FatFishPet
{
    // A frame owns exactly one source drawing. Motion weights interpolate geometry, never ink/alpha.
    internal sealed class PetMotionWarp
    {
        private const int Width = 340, Height = 360;
        private const int FieldWidth=Width/2,FieldHeight=Height/2,FrameStride=128;
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
                        fields.Add(a*FrameStride+b,new[]{ReadField(reader),ReadField(reader)});
                    }
                }
            }
        }

        private static float[] ReadField(BinaryReader reader)
        {
            const int gw=Width/2,gh=Height/2;
            var grid=new float[gw*gh*2];for(int i=0;i<grid.Length;i++)grid[i]=reader.ReadInt16()/32f;
            return grid;
        }

        public bool Contains(int a,int b) { return fields.ContainsKey(a*FrameStride+b); }

        public void RenderWeighted(int[] indices,float[] weights,byte[][] sources,int count,byte[] output)
        {
            if(count<=0) { Array.Clear(output,0,output.Length);return; }
            int owner=0;for(int i=1;i<count;i++)if(weights[i]>weights[owner])owner=i;
            if(count==1) { Array.Copy(sources[owner],output,output.Length);return; }
            var flows=new float[count][];
            for(int j=0;j<count;j++)
            {
                if(owner==j)continue;
                int a=indices[owner],b=indices[j];float[][] pair;
                if(fields.TryGetValue(Math.Min(a,b)*FrameStride+Math.Max(a,b),out pair))flows[j]=pair[a<b?0:1];
            }
            byte[] source=sources[owner];
            Parallel.For(0,Math.Min(4,Environment.ProcessorCount),worker=>
            {
            int workers=Math.Min(4,Environment.ProcessorCount);
            for(int y=Height*worker/workers;y<Height*(worker+1)/workers;y++)for(int x=0;x<Width;x++)
            {
                int oi=(y*Width+x)*4;
                    float sx=x,sy=y;
                    // Invert the blended displacement at its source position instead of sampling
                    // forward flow at the destination, which misaligns large face turns.
                    for(int iteration=0;iteration<3;iteration++)
                    {
                        float px=Math.Max(0,Math.Min(FieldWidth-1.001f,sx*.5f)),py=Math.Max(0,Math.Min(FieldHeight-1.001f,sy*.5f));
                        int gx=(int)px,gy=(int)py,k=(gy*FieldWidth+gx)*2;
                        float fx0=px-gx,fy0=py-gy,dx=0,dy=0;
                        for(int j=0;j<count;j++)
                        {
                            var f=flows[j];if(f==null)continue;
                            dx+=((f[k]*(1-fx0)+f[k+2]*fx0)*(1-fy0)+(f[k+FieldWidth*2]*(1-fx0)+f[k+FieldWidth*2+2]*fx0)*fy0)*weights[j];
                            dy+=((f[k+1]*(1-fx0)+f[k+3]*fx0)*(1-fy0)+(f[k+FieldWidth*2+1]*(1-fx0)+f[k+FieldWidth*2+3]*fx0)*fy0)*weights[j];
                        }
                        sx=x-dx;sy=y-dy;
                    }
                    sx=Math.Max(0,Math.Min(Width-1.001f,sx));sy=Math.Max(0,Math.Min(Height-1.001f,sy));
                    int xx=(int)sx,yy=(int)sy,a=(yy*Width+xx)*4;
                    float fx=sx-xx,fy=sy-yy;
                    float w0=(1-fx)*(1-fy),w1=fx*(1-fy),w2=(1-fx)*fy,w3=fx*fy;
                    int b=a+4,c=a+Width*4,d=c+4;
                    for(int channel=0;channel<4;channel++)
                        output[oi+channel]=(byte)Math.Min(255,source[a+channel]*w0+source[b+channel]*w1+source[c+channel]*w2+source[d+channel]*w3+.5f);
            }
            });
        }

        public void Render(int a,int b,float t,byte[] first,byte[] second,byte[] result)
        {
            t=Math.Max(0,Math.Min(1,t));
            if(t==0){Array.Copy(first,result,result.Length);return;}
            if(t==1){Array.Copy(second,result,result.Length);return;}
            RenderWeighted(new[]{a,b},new[]{1-t,t},new[]{first,second},2,result);
        }
    }
}
