using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FatFishPet
{
    [DataContract]
    internal sealed class AnimationAtlas
    {
        [DataMember(Name = "sheet")] public SpriteAtlas Sheet { get; set; }
    }

    [DataContract]
    internal sealed class SpriteAtlas
    {
        [DataMember(Name = "baseHeight")] public double BaseHeight { get; set; }
        [DataMember(Name = "frames")] public SpriteFrame[] Frames { get; set; }
    }

    [DataContract]
    internal sealed class SpriteFrame
    {
        [DataMember(Name = "x")] public int X { get; set; }
        [DataMember(Name = "y")] public int Y { get; set; }
        [DataMember(Name = "w")] public int Width { get; set; }
        [DataMember(Name = "h")] public int Height { get; set; }
        [DataMember(Name = "headX")] public double HeadX { get; set; }
        [DataMember(Name = "scaleHeight")] public double ScaleHeight { get; set; }
        [DataMember(Name = "widthFactor")] public double WidthFactor { get; set; }
    }

    internal struct PetPose
    {
        public double[] Weights;
        public double Lift;
        public double Angle;
        public double ScaleX;
        public double ScaleY;
        public bool Gaze;
        public double Blink;
        public double LookX, LookY;
    }

    internal enum PetMotionState { Idle, PickingUp, Lifted, Landing }

    internal sealed class PetMotion
    {
        public AnimationRates Rates = new AnimationRates();
        private double actionClock, breathClock, swayClock, blinkClock, lastClock;
        private double targetX, targetY, lookX, lookY, lookVX, lookVY, lastGazeTime;
        private double lastMouseX, lastMouseY, lastMouseMove;
        private bool mouseSeen, gazeClockStarted;
        private readonly Random blinkRandom = new Random();
        private double nextBlink = 3.4, blinkStarted = -10, blinkAmount;
        private double stateStarted;
        private double pickupDuration;
        private double velocity;
        private double velocityAt;
        private PetPose transitionSource;
        private bool shortLanding;
        public PetMotionState State { get; private set; }

        public void SetGazeOffset(double x, double y, double now)
        {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) { x = 0; y = 0; }
            if (!mouseSeen || Math.Abs(x - lastMouseX) > .01 || Math.Abs(y - lastMouseY) > .01)
            {
                mouseSeen = true; lastMouseX = x; lastMouseY = y; lastMouseMove = now;
            }
            double radius = Math.Sqrt(x*x+y*y);
            double strength = Ease((radius - 45) / 240);
            if (now-lastMouseMove >= 5) strength = 0;
            targetX = radius > 0 ? x/Math.Max(Math.Abs(x),Math.Abs(y))*strength : 0;
            targetY = radius > 0 ? y/Math.Max(Math.Abs(x),Math.Abs(y))*strength : 0;
        }

        public void BeginLift(double now)
        {
            transitionSource = Evaluate(now);
            pickupDuration = transitionSource.Lift > .1 ? .18 : .44;
            State = PetMotionState.PickingUp;
            stateStarted = actionClock;
        }

        public void EndLift(double now)
        {
            if (State != PetMotionState.PickingUp && State != PetMotionState.Lifted) return;
            transitionSource = Evaluate(now);
            shortLanding = transitionSource.Lift < .25;
            State = PetMotionState.Landing;
            stateStarted = actionClock;
        }

        public void SetVelocity(double value, double now)
        {
            velocity = Math.Max(-180, Math.Min(180, value));
            velocityAt = now;
        }

        public PetPose Evaluate(double now)
        {
            double delta=Math.Max(0,now-lastClock);lastClock=now;
            actionClock+=delta*(State==PetMotionState.PickingUp?Rates[AnimationKind.Pickup]:State==PetMotionState.Landing?Rates[AnimationKind.Landing]:1);
            breathClock+=delta*Rates[AnimationKind.Breath];swayClock+=delta*Rates[AnimationKind.Sway];blinkClock+=delta*Rates[AnimationKind.Blink];
            if(now>=nextBlink){blinkStarted=blinkClock;nextBlink=now+3+blinkRandom.NextDouble()*2;}
            double bt=blinkClock-blinkStarted;
            blinkAmount=bt<.08?Ease(bt/.08):bt<.13?1:bt<.25?1-Ease((bt-.13)/.12):0;
            double elapsed = Math.Max(0, actionClock - stateStarted);
            if (State == PetMotionState.PickingUp && elapsed >= pickupDuration)
            {
                State = PetMotionState.Lifted;
                stateStarted += pickupDuration;
                elapsed = actionClock - stateStarted;
            }
            if (State == PetMotionState.Landing && elapsed >= (shortLanding ? .24 : .60))
            {
                State = PetMotionState.Idle;
                stateStarted += shortLanding ? .24 : .60;
                elapsed = actionClock - stateStarted;
                lookX=lookY=lookVX=lookVY=0;
                lastGazeTime=now;
                blinkStarted = -10;
                nextBlink = now + 3 + blinkRandom.NextDouble() * 2;
            }
            double lift = 0, compression = 0;
            double[] weights;
            if (State == PetMotionState.Idle) weights = IdlePose(elapsed, now);
            else if (State == PetMotionState.PickingUp)
            {
                lift = Lerp(transitionSource.Lift, 1, Ease(elapsed / pickupDuration));
                if (pickupDuration < .2) weights = Mix(transitionSource.Weights, Single(12), Ease(elapsed / pickupDuration));
                else if (elapsed < .08) weights = Mix(transitionSource.Weights, Single(0), Ease(elapsed / .08));
                else if (elapsed < .16) weights = Between(0, 9, Ease((elapsed - .08) / .08));
                else if (elapsed < .24) weights = Between(9, 10, Ease((elapsed - .16) / .08));
                else if (elapsed < .34) weights = Between(10, 11, Ease((elapsed - .24) / .10));
                else weights = Between(11, 12, Ease((elapsed - .34) / .10));
            }
            else if (State == PetMotionState.Lifted)
            {
                lift = 1;
                weights = Between(12,13,blinkAmount);
            }
            else
            {
                lift = transitionSource.Lift * (1 - Ease(elapsed / (shortLanding ? .18 : .34)));
                if (shortLanding) weights = Mix(transitionSource.Weights, Single(0), Ease(elapsed / .18));
                else if (elapsed < .16) weights = Mix(transitionSource.Weights, Single(14), Ease(elapsed / .16));
                else if (elapsed < .32) weights = Between(14, 15, Ease((elapsed - .16) / .16));
                else weights = Between(15, 0, Ease((elapsed - .32) / .22));
                if (!shortLanding && elapsed > .22 && elapsed < .54) compression = Math.Sin(Math.PI * (elapsed - .22) / .32);
            }
            double swayVelocity = velocity * Math.Exp(-8 * Math.Max(0, now - velocityAt));
            double breath = Math.Sin(breathClock * Math.PI * 2 / 3.7) * (1 - lift);
            double scaleX = 1 - .0015 * breath + .011 * compression;
            double scaleY = 1 + .006 * breath - .022 * compression;
            if (State == PetMotionState.PickingUp && elapsed < .12)
            {
                scaleX = Lerp(transitionSource.ScaleX, scaleX, Ease(elapsed / .12));
                scaleY = Lerp(transitionSource.ScaleY, scaleY, Ease(elapsed / .12));
            }
            else if (State == PetMotionState.Landing && elapsed < .10)
            {
                scaleX = Lerp(transitionSource.ScaleX, scaleX, Ease(elapsed / .10));
                scaleY = Lerp(transitionSource.ScaleY, scaleY, Ease(elapsed / .10));
            }
            return new PetPose
            {
                Weights = weights, Lift = lift,
                Angle = (.055 * Math.Sin(swayClock * 2.6) - swayVelocity * .00022) * lift,
                ScaleX = scaleX, ScaleY = scaleY,
                Gaze = State == PetMotionState.Idle || (State == PetMotionState.PickingUp && transitionSource.Gaze && elapsed < .08),
                LookX = State == PetMotionState.Idle ? lookX : State == PetMotionState.PickingUp ? transitionSource.LookX*(1-Ease(elapsed/.30)) : 0,
                LookY = State == PetMotionState.Idle ? lookY : State == PetMotionState.PickingUp ? transitionSource.LookY*(1-Ease(elapsed/.30)) : 0,
                Blink = State == PetMotionState.PickingUp ? transitionSource.Blink*(1-Ease(elapsed/.08)) : blinkAmount
            };
        }

        private double[] IdlePose(double time, double now)
        {
            double dt = gazeClockStarted ? Math.Max(0,Math.Min(.05,now-lastGazeTime)) : 0;
            gazeClockStarted=true;lastGazeTime=now;
            // Scale animation time, not the 5-second real cursor inactivity timeout.
            dt*=Rates[AnimationKind.Turn];
            while(dt>0)
            {
                double h=Math.Min(dt,1.0/120);dt-=h;
                Follow(ref lookX,ref lookVX,targetX,h);
                Follow(ref lookY,ref lookVY,targetY,h);
            }
            return GazeWeights(lookX,lookY);
        }

        internal static double[] GazeWeights(double x,double y)
        {
            x=Math.Max(-1,Math.Min(1,x));y=Math.Max(-1,Math.Min(1,y));
            double ax=Math.Abs(x),ay=Math.Abs(y);var w=new double[34];
            int horizontal=x<0?19:21,vertical=y<0?17:23;
            int diagonal=y<0?(x<0?16:18):(x<0?22:24);
            w[0]=1-Math.Max(ax,ay);
            w[diagonal]=Math.Min(ax,ay);
            if(ax>=ay)w[horizontal]=ax-ay;else w[vertical]=ay-ax;
            return w;
        }

        private static void Follow(ref double position, ref double speed, double target, double dt)
        {
            // Damped pursuit retains velocity when the target changes: no restarting pose clips.
            double acceleration=100*(target-position)-20*speed;
            acceleration=Math.Max(-16,Math.Min(16,acceleration));
            speed=Math.Max(-2.6,Math.Min(2.6,speed+acceleration*dt));
            position=Math.Max(-1,Math.Min(1,position+speed*dt));
        }

        private static double[] Single(int frame) { var weights = new double[34]; weights[frame] = 1; return weights; }
        private static double[] Between(int from, int to, double t) { var weights = new double[34]; weights[from] += 1 - t; weights[to] += t; return weights; }
        private static double[] Mix(double[] a, double[] b, double t) { var weights = new double[34]; for (int i = 0; i < 34; i++) weights[i] = Lerp(a[i], b[i], t); return weights; }
        private static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        private static double Ease(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
    }

    internal sealed class PetSpriteView : FrameworkElement, IDisposable
    {
        public const double SceneWidth = 340;
        public const double SceneHeight = 360;
        public const double CharacterHeight = 300;
        public const double Ground = 340;
        private const int CanvasWidth = 340;
        private const int CanvasHeight = 360;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly PetMotion motion = new PetMotion();
        private readonly BitmapSource[] frames = new BitmapSource[34];
        private readonly byte[][] pixels = new byte[34][];
        private readonly byte[] mixPixels = new byte[CanvasWidth * CanvasHeight * 4];
        private readonly int[] activeFrames = new int[34];
        private readonly int[] activeWeights = new int[34];
        private readonly int[] previousWeights = new int[34];
        private readonly PetMotionWarp warp = new PetMotionWarp();
        private readonly Dictionary<int, BitmapSource> tweenCache = new Dictionary<int, BitmapSource>();
        private readonly Queue<int> tweenOrder = new Queue<int>();
        private readonly WriteableBitmap mixed = new WriteableBitmap(CanvasWidth, CanvasHeight, 96, 96, PixelFormats.Pbgra32, null);
        private readonly List<double> renderIntervals = new List<double>();
        private TimeSpan previousRenderingTime = TimeSpan.MinValue;
        private double previousRenderClock;
        private double diagnosticStarted;
        private bool diagnosticsFinished;
        private bool subscribed;
        private bool disposed;
        private bool previousWasMix;
        private PetPose pose;
        public AnimationRates Rates { get { return motion.Rates; } set { motion.Rates=value; } }
        public string DiagnosticPath { get; set; }
        public Func<Vector?> CursorOffsetProvider { get; set; }

        public PetSpriteView()
        {
            AnimationAtlas atlas;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AnimationAtlas"))
            {
                if (stream == null) throw new InvalidOperationException("缺少动画配置，请重新构建程序。");
                atlas = (AnimationAtlas)new DataContractJsonSerializer(typeof(AnimationAtlas)).ReadObject(stream);
            }
            LoadFrames(atlas.Sheet);
            pose = motion.Evaluate(0);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            Loaded += delegate { if (!subscribed && !disposed) { CompositionTarget.Rendering += OnRendering; subscribed = true; } };
            Unloaded += delegate { StopRendering(); };
        }

        internal static BitmapImage LoadBitmap(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("缺少角色素材：" + name);
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            }
        }

        private void LoadFrames(SpriteAtlas bank)
        {
            if (bank == null || bank.Frames == null || bank.Frames.Length != 34 || bank.BaseHeight <= 0) throw new InvalidOperationException("角色动画配置不完整。");
            var bitmap = LoadBitmap("UnifiedSprites");
            double factor = CharacterHeight / bank.BaseHeight;
            for (int i = 0; i < 34; i++)
            {
                SpriteFrame frame = bank.Frames[i];
                factor = CharacterHeight / (frame.ScaleHeight > 0 ? frame.ScaleHeight : bank.BaseHeight);
                double widthFactor=frame.WidthFactor>0?frame.WidthFactor:1;
                var crop = new CroppedBitmap(bitmap, new Int32Rect(frame.X, frame.Y, frame.Width, frame.Height)); crop.Freeze();
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                    dc.DrawImage(crop, new Rect(SceneWidth / 2 - frame.HeadX * factor * widthFactor, (frame.ScaleHeight > 0 ? Ground-frame.Height*factor : Ground - CharacterHeight), frame.Width * factor * widthFactor, frame.Height * factor));
                var rendered = new RenderTargetBitmap(CanvasWidth, CanvasHeight, 96, 96, PixelFormats.Pbgra32);
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                rendered.Render(visual); rendered.Freeze(); frames[i] = rendered;
                pixels[i] = new byte[CanvasWidth * CanvasHeight * 4]; rendered.CopyPixels(pixels[i], CanvasWidth * 4, 0);
            }
            // The ahoge is a shared animation layer: keep its neutral orientation on right turns.
            foreach(int index in new[]{18,21,24})
            {
                // Feather the attachment into the moving head instead of cutting a horizontal seam.
                for(int y=0;y<80;y++)for(int x=0;x<CanvasWidth;x++)
                {
                    double weight=Math.Min(1,(80-y)/20.0);int p=(y*CanvasWidth+x)*4;
                    for(int c=0;c<4;c++)pixels[index][p+c]=(byte)Math.Round(pixels[0][p+c]*weight+pixels[index][p+c]*(1-weight));
                }
                var stable=BitmapSource.Create(CanvasWidth,CanvasHeight,96,96,PixelFormats.Pbgra32,null,pixels[index],CanvasWidth*4);
                stable.Freeze();frames[index]=stable;
            }
        }

        private void OnRendering(object sender, EventArgs args)
        {
            var rendering = args as RenderingEventArgs;
            if (rendering != null && rendering.RenderingTime == previousRenderingTime) return;
            if (rendering != null) previousRenderingTime = rendering.RenderingTime;
            double now = clock.Elapsed.TotalSeconds;
            if (!diagnosticsFinished && !string.IsNullOrEmpty(DiagnosticPath) && now > 1)
            {
                if (diagnosticStarted == 0) diagnosticStarted = now;
                else if (previousRenderClock > 0) renderIntervals.Add((now - previousRenderClock) * 1000);
                if (now - diagnosticStarted >= 6) WriteDiagnostics(now - diagnosticStarted);
            }
            previousRenderClock = now;
            if (CursorOffsetProvider != null && motion.State == PetMotionState.Idle)
            {
                Vector? offset = CursorOffsetProvider();
                motion.SetGazeOffset(offset.HasValue ? offset.Value.X : 0, offset.HasValue ? offset.Value.Y : 0, now);
            }
            pose = motion.Evaluate(now);
            InvalidateVisual();
        }

        private void WriteDiagnostics(double duration)
        {
            diagnosticsFinished = true;
            if (renderIntervals.Count == 0) return;
            renderIntervals.Sort();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticPath));
                File.WriteAllLines(DiagnosticPath, new[]
                {
                    "version=0.9", "renderCallbacks=" + renderIntervals.Count,
                    "seconds=" + duration.ToString("F3", CultureInfo.InvariantCulture),
                    "averageHz=" + (renderIntervals.Count / duration).ToString("F2", CultureInfo.InvariantCulture),
                    "p95Milliseconds=" + renderIntervals[(int)((renderIntervals.Count - 1) * .95)].ToString("F2", CultureInfo.InvariantCulture),
                    "renderTier=" + (RenderCapability.Tier >> 16)
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void BeginLift() { motion.BeginLift(clock.Elapsed.TotalSeconds); UpdatePose(); }
        public void EndLift() { motion.EndLift(clock.Elapsed.TotalSeconds); UpdatePose(); }
        public void SetDragVelocity(double velocity) { motion.SetVelocity(velocity, clock.Elapsed.TotalSeconds); }
        private void UpdatePose() { pose = motion.Evaluate(clock.Elapsed.TotalSeconds); InvalidateVisual(); }
        private void StopRendering() { if (subscribed) { CompositionTarget.Rendering -= OnRendering; subscribed = false; } }
        public void Dispose() { disposed = true; StopRendering(); clock.Stop(); }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            dc.PushTransform(new ScaleTransform(ActualWidth / SceneWidth, ActualHeight / SceneHeight));
            DrawPose(dc, pose); dc.Pop();
        }

        private BitmapSource Composite(double[] weights)
        {
            int count = 0, total = 0, strongest = 0;
            for (int i = 0; i < 34; i++)
            {
                int weight = (int)Math.Round(weights[i] * 256);
                if (weight <= 0) continue;
                activeFrames[count] = i; activeWeights[count] = weight;
                if (weight > activeWeights[strongest]) strongest = count;
                total += weight; count++;
            }
            if (count == 0) return frames[0];

            activeWeights[strongest] += 256 - total;
            if (count == 1) { previousWasMix = false; return frames[activeFrames[0]]; }
            if (count == 2 && warp.Contains(activeFrames[0], activeFrames[1]))
            {
                previousWasMix = false;
                int a=activeFrames[0],b=activeFrames[1],step=(int)Math.Round(activeWeights[1]/16.0);
                if(step==0)return frames[a];if(step==16)return frames[b];
                int key=(a*64+b)*17+step;
                BitmapSource cached;if(tweenCache.TryGetValue(key,out cached))return cached;
                warp.Render(a,b,step/16f,pixels[a],pixels[b],mixPixels);
                cached=BitmapSource.Create(CanvasWidth,CanvasHeight,96,96,PixelFormats.Pbgra32,null,mixPixels,CanvasWidth*4);cached.Freeze();
                tweenCache.Add(key,cached);tweenOrder.Enqueue(key);
                if(tweenOrder.Count>64)tweenCache.Remove(tweenOrder.Dequeue());
                return cached;
            }
            bool changed = !previousWasMix;
            for (int i = 0; i < 34; i++) { int value = (int)Math.Round(weights[i] * 256); if (previousWeights[i] != value) changed = true; previousWeights[i] = value; }
            if (!changed) return mixed;
            var sourcePixels=new byte[count][];var sourceWeights=new float[count];
            for(int i=0;i<count;i++){sourcePixels[i]=pixels[activeFrames[i]];sourceWeights[i]=activeWeights[i]/256f;}
            warp.RenderWeighted(activeFrames,sourceWeights,sourcePixels,count,mixPixels);
            // Interpolate premultiplied RGBA, not overlapping semi-transparent images: opaque areas stay opaque.
            mixed.WritePixels(new Int32Rect(0, 0, CanvasWidth, CanvasHeight), mixPixels, CanvasWidth * 4, 0);
            previousWasMix = true;
            return mixed;
        }

        private readonly Dictionary<int, byte[]> blinkCache = new Dictionary<int, byte[]>();
        private readonly Queue<int> blinkOrder = new Queue<int>();
        private readonly int[] gazeFrames=new int[3];
        private readonly float[] gazeWeights=new float[3];
        private readonly byte[][] gazeSources=new byte[3][];
        private long previousGazeKey = -1;

        private static readonly double[][] eyeCenters = {
            new double[]{141,169,193,169},
            new double[]{134,151,180,154}, new double[]{146,151,195,151}, new double[]{163,155,207,151},
            new double[]{132,163,174,168}, new double[]{141,169,193,169}, new double[]{166,168,208,163},
            new double[]{137,172,182,179}, new double[]{146,177,194,177}, new double[]{163,179,206,172}
        };
        internal static double EyeMask(int frame,int x,int y)
        {
            if(y<132||y>196||x<108||x>230)return 0;
            var e=eyeCenters[frame==0?0:frame-15];
            double a=Math.Sqrt(Math.Pow((x-e[0])/20,2)+Math.Pow((y-e[1])/16,2));
            double b=Math.Sqrt(Math.Pow((x-e[2])/20,2)+Math.Pow((y-e[3])/16,2));
            return Math.Max(0,Math.Min(1,(1-Math.Min(a,b))*5));
        }

        private byte[] BlinkPixels(int frame,double blink)
        {
            int step=(int)Math.Round(blink*24);
            if(step<=0)return pixels[frame];
            int closed=frame==0?2:frame+9;

            int key=frame*25+step;byte[] result;
            if(blinkCache.TryGetValue(key,out result))return result;
            result=new byte[mixPixels.Length];
            if(step>=24)Array.Copy(pixels[closed],result,result.Length);
            else warp.Render(frame,closed,step/24f,pixels[frame],pixels[closed],result);
            // A blink changes eyelids only; never substitute the closed frame's head/body/alpha.
            byte[] original=pixels[frame];
            for(int y=0;y<CanvasHeight;y++)for(int x=0;x<CanvasWidth;x++)
            {
                int p=(y*CanvasWidth+x)*4;
                double mask=EyeMask(frame,x,y);
                if(mask<=0){for(int c=0;c<4;c++)result[p+c]=original[p+c];}
                else
                {
                    for(int c=0;c<3;c++)result[p+c]=(byte)Math.Round(original[p+c]*(1-mask)+result[p+c]*mask);
                    result[p+3]=original[p+3];
                }
            }
            blinkCache.Add(key,result);blinkOrder.Enqueue(key);
            if(blinkOrder.Count>64)blinkCache.Remove(blinkOrder.Dequeue());
            return result;
        }
        private BitmapSource CompositeGaze(PetPose current)
        {
            int n=0;
            long key=(long)Math.Round(current.Blink*24);
            for(int i=0;i<34;i++)if(current.Weights[i]>0.000001)
            {
                gazeFrames[n]=i;gazeWeights[n]=(float)current.Weights[i];
                key=(key<<17)|((long)i<<11)|(long)Math.Round(current.Weights[i]*1024);n++;
            }
            if(key==previousGazeKey)return mixed;
            for(int i=0;i<n;i++)gazeSources[i]=BlinkPixels(gazeFrames[i],current.Blink);
            if(n==1)Array.Copy(gazeSources[0],mixPixels,mixPixels.Length);
            else warp.RenderWeighted(gazeFrames,gazeWeights,gazeSources,n,mixPixels);
            mixed.WritePixels(new Int32Rect(0,0,CanvasWidth,CanvasHeight),mixPixels,CanvasWidth*4,0);
            previousWasMix=false;previousGazeKey=key;return mixed;
        }

        internal void DrawPose(DrawingContext dc, PetPose current)
        {
            if(!current.Gaze)previousGazeKey=-1;
            BitmapSource image = current.Gaze ? CompositeGaze(current) : Composite(current.Weights);
            dc.PushTransform(new TranslateTransform(0, -27 * current.Lift));
            dc.PushTransform(new RotateTransform(current.Angle * 180 / Math.PI, SceneWidth / 2, 70));
            dc.PushTransform(new ScaleTransform(current.ScaleX, current.ScaleY, SceneWidth / 2, Ground));
            dc.DrawImage(image, new Rect(0, 0, SceneWidth, SceneHeight));
            dc.Pop(); dc.Pop(); dc.Pop();
        }
    }
}
