using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace FatFishPet
{
    internal sealed class PetParticles
    {
        private sealed class Heart
        {
            public double X,Y,Size,Age,Life,Rise,Phase,Turns,Orbit,Side;
            public bool Large;
        }
        // 小爱心（只有心形）与大爱心（心形 + 星光）混着生成，大爱心数量单独限制，避免堆得太满。
        private const int Maximum=5;
        private const int MaximumLarge=2;
        private const double LargeChance=.45;
        private readonly List<Heart> hearts=new List<Heart>(Maximum);
        private readonly Random random=new Random();
        private double lastTime=-1,nextBirth;
        private bool emitting,nextLeft;
        private static readonly System.Windows.Media.Imaging.BitmapSource HeartImage=LoadHeart("HeartParticle");
        private static readonly System.Windows.Media.Imaging.BitmapSource HeartImageLarge=LoadHeart("HeartParticleLarge");
        private static System.Windows.Media.Imaging.BitmapSource LoadHeart(string name)
        {
            using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if(stream==null)throw new InvalidOperationException("缺少爱心粒子素材。");
                var bitmap=new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource=stream;
                bitmap.EndInit();bitmap.Freeze();return bitmap;
            }
        }
        public void Update(double now,bool enabled,double rate)
        {
            double elapsed=lastTime<0?0:Math.Max(0,now-lastTime);lastTime=now;
            // Never replay an accumulated burst after a stalled/hidden render loop.
            if(elapsed>1){hearts.Clear();emitting=false;}
            double delta=Math.Min(.10,elapsed)*rate;
            for(int i=hearts.Count-1;i>=0;i--)
            {
                hearts[i].Age+=delta;
                if(hearts[i].Age>=hearts[i].Life)hearts.RemoveAt(i);
            }
            if(enabled&&!emitting){nextBirth=now;nextLeft=random.Next(2)==0;}
            emitting=enabled;
            if(!enabled||now<nextBirth)return;
            nextBirth=now+.25+random.NextDouble()*.13;
            if(hearts.Count>=Maximum)return;
            // Spread conspicuous hearts around both flanks without covering the face.
            bool left=nextLeft;nextLeft=!nextLeft;
            int largeCount=0;foreach(var item in hearts)if(item.Large)largeCount++;
            bool large=largeCount<MaximumLarge&&random.NextDouble()<LargeChance;
            hearts.Add(new Heart {
                X=left?47+random.NextDouble()*8:285+random.NextDouble()*8,
                Y=153+random.NextDouble()*117,
                // 大爱心本体只占贴图宽度的约六成（其余是星光），所以尺寸值要更大一档。
                Size=large?36+random.NextDouble()*10:13+random.NextDouble()*5,
                Life=2.2+random.NextDouble()*.6,Rise=82+random.NextDouble()*20,
                Phase=random.NextDouble()*Math.PI*2,Turns=.9+random.NextDouble()*.4,
                Orbit=10+random.NextDouble()*6,Side=left?-1:1,Large=large
            });
        }
        public void Draw(DrawingContext dc)
        {
            foreach(var heart in hearts)
            {
                double t=heart.Age/heart.Life;
                double fade=Math.Max(0,Math.Min(1,(t-.55)/.45));
                double alpha=Math.Min(1,t/.07)*(1-fade*fade*(3-2*fade));
                // A small rising elliptical orbit, with independent phase per bubble.
                // Position depends only on age: changing speed cannot jump the trajectory.
                double phase=heart.Phase+t*Math.PI*2*heart.Turns;
                double orbitX=heart.Side*heart.Orbit*Math.Sin(phase);
                double orbitY=6*Math.Cos(phase);
                double pulse=1+.055*Math.Sin(phase+.8);
                double depth=.94+.06*Math.Cos(phase);
                dc.PushOpacity(alpha);
                dc.PushTransform(new TranslateTransform(heart.X+orbitX,heart.Y-heart.Rise*t+orbitY));
                dc.PushTransform(new RotateTransform(-13+9*Math.Sin(phase)));
                dc.PushTransform(new ScaleTransform(heart.Size*pulse*depth,heart.Size*pulse));
                // Draw the generated RGBA sprite (kept at its own aspect ratio); motion and opacity
                // remain procedural. Large hearts carry the crayon sparkles.
                var image=heart.Large?HeartImageLarge:HeartImage;
                double aspect=(double)image.PixelHeight/image.PixelWidth;
                // 矩形宽 2，高度也必须是 2×aspect，否则贴图会被压扁。
                dc.DrawImage(image,new Rect(-1,-aspect,2,2*aspect));
                dc.Pop();dc.Pop();dc.Pop();dc.Pop();
            }
        }
    }
}
