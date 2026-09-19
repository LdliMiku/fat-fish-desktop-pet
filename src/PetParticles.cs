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
        }
        private const int Maximum=6;
        private readonly List<Heart> hearts=new List<Heart>(Maximum);
        private readonly Random random=new Random();
        private double lastTime=-1,nextBirth;
        private bool emitting,nextLeft;
        private static readonly System.Windows.Media.Imaging.BitmapSource HeartImage=LoadHeart();
        private static System.Windows.Media.Imaging.BitmapSource LoadHeart()
        {
            using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("HeartParticle"))
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
            hearts.Add(new Heart {
                X=left?47+random.NextDouble()*8:285+random.NextDouble()*8,
                Y=153+random.NextDouble()*117,Size=13+random.NextDouble()*5,
                Life=2.2+random.NextDouble()*.6,Rise=82+random.NextDouble()*20,
                Phase=random.NextDouble()*Math.PI*2,Turns=.9+random.NextDouble()*.4,
                Orbit=10+random.NextDouble()*6,Side=left?-1:1
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
                // Draw the generated RGBA sprite; motion and opacity remain procedural.
                dc.DrawImage(HeartImage,new Rect(-1,-1,2,2));
                dc.Pop();dc.Pop();dc.Pop();dc.Pop();
            }
        }
    }
}
