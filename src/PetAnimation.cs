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
        internal const int FrameCount=50;
        internal static readonly int[] DirectionFrames={16,17,18,19,21,22,23,24};
        public AnimationRates Rates = new AnimationRates();
        private double actionClock, breathClock, swayClock, blinkClock, lastClock;
        private double targetX, targetY, lookX, lookY, lookVX, lookVY, lastGazeTime;
        private double lastMouseX, lastMouseY, lastMouseMove;
        private bool mouseSeen, gazeClockStarted;
        private int gazeSector=-1;
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
            if(now-lastMouseMove>=5||radius<(gazeSector<0?95:65))
            {gazeSector=-1;targetX=targetY=0;return;}
            // Settle on an actual drawn pose rather than holding a permanent blend of different faces.
            double angle=Math.Atan2(y,x),sectorAngle=Math.PI/4;
            int candidate=((int)Math.Round(angle/sectorAngle)+8)%8;
            if(gazeSector>=0)
            {
                double difference=Math.Atan2(Math.Sin(angle-gazeSector*sectorAngle),Math.Cos(angle-gazeSector*sectorAngle));
                if(Math.Abs(difference)<=sectorAngle*.5+7*Math.PI/180)candidate=gazeSector;
            }
            gazeSector=candidate;
            targetX=candidate==0||candidate==1||candidate==7?1:candidate>=3&&candidate<=5?-1:0;
            targetY=candidate>=1&&candidate<=3?1:candidate>=5?-1:0;
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
            // Remove the last sub-pixel mixture once pursuit is visually settled.
            if(Math.Abs(x)<.002)x=0;else if(Math.Abs(x)>.998)x=Math.Sign(x);
            if(Math.Abs(y)<.002)y=0;else if(Math.Abs(y)>.998)y=Math.Sign(y);
            double ax=Math.Abs(x),ay=Math.Abs(y),radius=Math.Max(ax,ay);var w=new double[FrameCount];
            if(radius==0){w[0]=1;return w;}
            int cardinal=ax>=ay?(x<0?3:4):(y<0?1:6),diagonal=y<0?(x<0?0:2):(x<0?5:7);
            double angular=Math.Min(ax,ay)/radius,scaled=radius*2;
            int inner=Math.Min(1,(int)scaled),outer=inner+1;double radial=scaled-inner;
            w[RingFrame(inner,cardinal)]+=(1-radial)*(1-angular);
            w[RingFrame(inner,diagonal)]+=(1-radial)*angular;
            w[RingFrame(outer,cardinal)]+=radial*(1-angular);
            w[RingFrame(outer,diagonal)]+=radial*angular;
            return w;
        }

        private static int RingFrame(int ring,int direction)
        {return ring==0?0:ring==1?34+direction:DirectionFrames[direction];}

        private static void Follow(ref double position, ref double speed, double target, double dt)
        {
            // Damped pursuit retains velocity when the target changes: no restarting pose clips.
            double acceleration=100*(target-position)-20*speed;
            acceleration=Math.Max(-16,Math.Min(16,acceleration));
            speed=Math.Max(-2.6,Math.Min(2.6,speed+acceleration*dt));
            position=Math.Max(-1,Math.Min(1,position+speed*dt));
        }

        private static double[] Single(int frame) { var weights = new double[PetMotion.FrameCount]; weights[frame] = 1; return weights; }
        private static double[] Between(int from, int to, double t) { var weights = new double[PetMotion.FrameCount]; weights[from] += 1 - t; weights[to] += t; return weights; }
        private static double[] Mix(double[] a, double[] b, double t) { var weights = new double[PetMotion.FrameCount]; for (int i = 0; i < PetMotion.FrameCount; i++) weights[i] = Lerp(a[i], b[i], t); return weights; }
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
        private readonly BitmapSource[] frames = new BitmapSource[PetMotion.FrameCount];
        private readonly byte[][] pixels = new byte[PetMotion.FrameCount][];
        private readonly byte[] mixPixels = new byte[CanvasWidth * CanvasHeight * 4];
        private readonly int[] activeFrames = new int[PetMotion.FrameCount];
        private readonly int[] activeWeights = new int[PetMotion.FrameCount];
        private readonly int[] previousWeights = new int[PetMotion.FrameCount];
        private readonly PetMotionWarp warp = new PetMotionWarp();
        private readonly Dictionary<int, BitmapSource> tweenCache = new Dictionary<int, BitmapSource>();
        private readonly Queue<int> tweenOrder = new Queue<int>();
        private readonly WriteableBitmap mixed = new WriteableBitmap(CanvasWidth, CanvasHeight, 96, 96, PixelFormats.Pbgra32, null);
        private readonly List<double> renderIntervals = new List<double>();
        private TimeSpan previousRenderingTime = TimeSpan.MinValue;
        private double previousRenderClock;
        private double diagnosticStarted;
        private bool diagnosticsFinished;
        private readonly List<double> activeIntervals=new List<double>(), inactiveIntervals=new List<double>(), drawTimes=new List<double>();
        private bool previousActive;
        private int diagnosticWindows;
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
            if (bank == null || bank.Frames == null || bank.Frames.Length != PetMotion.FrameCount || bank.BaseHeight <= 0) throw new InvalidOperationException("角色动画配置不完整。");
            var bitmap = LoadBitmap("UnifiedSprites");
            double factor = CharacterHeight / bank.BaseHeight;
            for (int i = 0; i < PetMotion.FrameCount; i++)
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
            // Use clean generated top-of-head patches. Keep each original face and body untouched.
            AnimationAtlas repairs;
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("HeadRepairAtlas"))
                repairs=(AnimationAtlas)new DataContractJsonSerializer(typeof(AnimationAtlas)).ReadObject(stream);
            var repairSheet=LoadBitmap("HeadRepairs");int[] targets={18,21,24};
            for(int i=0;i<targets.Length;i++)
            {
                int index=targets[i];var f=repairs.Sheet.Frames[i];double repairScale=CharacterHeight/f.ScaleHeight;
                var crop=new CroppedBitmap(repairSheet,new Int32Rect(f.X,f.Y,f.Width,f.Height));
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
                    dc.DrawImage(crop,new Rect(SceneWidth/2-f.HeadX*repairScale,Ground-f.Height*repairScale,f.Width*repairScale,f.Height*repairScale));
                RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
                var render=new RenderTargetBitmap(CanvasWidth,CanvasHeight,96,96,PixelFormats.Pbgra32);render.Render(visual);
                var repairPixels=new byte[CanvasWidth*CanvasHeight*4];render.CopyPixels(repairPixels,CanvasWidth*4,0);
                for(int y=0;y<115;y++)for(int x=0;x<CanvasWidth;x++)
                {
                    int p=(y*CanvasWidth+x)*4;double weight=Math.Min(1,(115-y)/20.0);
                    if(y>=95&&(pixels[index][p+3]<250||repairPixels[p+3]<250))continue;
                    for(int c=0;c<4;c++)pixels[index][p+c]=(byte)Math.Round(repairPixels[p+c]*weight+pixels[index][p+c]*(1-weight));
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
                else if (previousRenderClock > 0)
                {
                    double interval=(now-previousRenderClock)*1000;renderIntervals.Add(interval);
                    var window=Window.GetWindow(this);bool active=window!=null&&window.IsActive;
                    if(active==previousActive)(active?activeIntervals:inactiveIntervals).Add(interval);
                    previousActive=active;
                }
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
            diagnosticsFinished = ++diagnosticWindows>=20;
            if (renderIntervals.Count == 0) return;
            renderIntervals.Sort();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticPath));
                File.AppendAllLines(DiagnosticPath, new[]
                {
                    "version=0.1", "renderCallbacks=" + renderIntervals.Count,
                    "seconds=" + duration.ToString("F3", CultureInfo.InvariantCulture),
                    "averageHz=" + (renderIntervals.Count / duration).ToString("F2", CultureInfo.InvariantCulture),
                    "p95Milliseconds=" + renderIntervals[(int)((renderIntervals.Count - 1) * .95)].ToString("F2", CultureInfo.InvariantCulture),
                    "renderTier=" + (RenderCapability.Tier >> 16),
                    "utc="+DateTime.UtcNow.ToString("o"),
                    Statistics("active",activeIntervals),Statistics("inactive",inactiveIntervals),Statistics("draw",drawTimes)
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            renderIntervals.Clear();activeIntervals.Clear();inactiveIntervals.Clear();drawTimes.Clear();diagnosticStarted=clock.Elapsed.TotalSeconds;
        }

        private static string Statistics(string name,List<double> values)
        {
            if(values.Count==0)return name+"Count=0";
            values.Sort();double sum=0;foreach(double value in values)sum+=value;
            return string.Format(CultureInfo.InvariantCulture,"{0}Count={1} meanMs={2:F2} p95Ms={3:F2}",name,values.Count,sum/values.Count,values[(int)((values.Count-1)*.95)]);
        }

        public void BeginLift() { motion.BeginLift(clock.Elapsed.TotalSeconds); UpdatePose(); }
        public void EndLift() { motion.EndLift(clock.Elapsed.TotalSeconds); UpdatePose(); }
        public void SetDragVelocity(double velocity) { motion.SetVelocity(velocity, clock.Elapsed.TotalSeconds); }
        private void UpdatePose() { pose = motion.Evaluate(clock.Elapsed.TotalSeconds); InvalidateVisual(); }
        private void StopRendering() { if (subscribed) { CompositionTarget.Rendering -= OnRendering; subscribed = false; } }
        public void Dispose() { disposed = true; StopRendering(); clock.Stop(); }

        protected override void OnRender(DrawingContext dc)
        {
            long started=Stopwatch.GetTimestamp();
            base.OnRender(dc);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            dc.PushTransform(new ScaleTransform(ActualWidth / SceneWidth, ActualHeight / SceneHeight));
            DrawPose(dc, pose); dc.Pop();
            if(!diagnosticsFinished&&!string.IsNullOrEmpty(DiagnosticPath))drawTimes.Add((Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency);
        }

        private BitmapSource Composite(double[] weights)
        {
            int count = 0, total = 0, strongest = 0;
            for (int i = 0; i < PetMotion.FrameCount; i++)
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
                int key=(a*128+b)*17+step;
                BitmapSource cached;if(tweenCache.TryGetValue(key,out cached))return cached;
                warp.Render(a,b,step/16f,pixels[a],pixels[b],mixPixels);
                cached=BitmapSource.Create(CanvasWidth,CanvasHeight,96,96,PixelFormats.Pbgra32,null,mixPixels,CanvasWidth*4);cached.Freeze();
                tweenCache.Add(key,cached);tweenOrder.Enqueue(key);
                if(tweenOrder.Count>64)tweenCache.Remove(tweenOrder.Dequeue());
                return cached;
            }
            bool changed = !previousWasMix;
            for (int i = 0; i < PetMotion.FrameCount; i++) { int value = (int)Math.Round(weights[i] * 256); if (previousWeights[i] != value) changed = true; previousWeights[i] = value; }
            if (!changed) return mixed;
            var sourcePixels=new byte[count][];var sourceWeights=new float[count];
            for(int i=0;i<count;i++){sourcePixels[i]=pixels[activeFrames[i]];sourceWeights[i]=activeWeights[i]/256f;}
            warp.RenderWeighted(activeFrames,sourceWeights,sourcePixels,count,mixPixels);
            // Geometry follows the motion weights; the renderer samples one drawing only.
            mixed.WritePixels(new Int32Rect(0, 0, CanvasWidth, CanvasHeight), mixPixels, CanvasWidth * 4, 0);
            previousWasMix = true;
            return mixed;
        }

        private readonly Dictionary<int, byte[]> blinkCache = new Dictionary<int, byte[]>();
        private readonly Queue<int> blinkOrder = new Queue<int>();
        private readonly int[] gazeFrames=new int[PetMotion.FrameCount];
        private readonly float[] gazeWeights=new float[PetMotion.FrameCount];
        private readonly byte[][] gazeSources=new byte[PetMotion.FrameCount][];
        private readonly int[] previousGazeFrames=new int[PetMotion.FrameCount],previousGazeWeights=new int[PetMotion.FrameCount];
        private int previousGazeCount=-1,previousBlinkStep=-1;

        private static readonly double[][] eyeCenters = {
            new double[]{141,169,193,169},
            new double[]{134,151,180,154}, new double[]{146,151,195,151}, new double[]{163,155,207,151},
            new double[]{132,163,174,168}, new double[]{141,169,193,169}, new double[]{166,168,208,163},
            new double[]{137,172,182,179}, new double[]{146,177,194,177}, new double[]{163,179,206,172}
        };
        internal static double EyeMask(int frame,int x,int y)
        {
            if(y<132||y>196||x<108||x>230)return 0;
            var e=eyeCenters[frame==0?0:frame>=34?0:frame-15];
            double ex1=e[0],ey1=e[1],ex2=e[2],ey2=e[3];
            if(frame>=34)
            {
                var target=eyeCenters[PetMotion.DirectionFrames[(frame-34)%8]-15];double amount=.5;
                ex1+=(target[0]-ex1)*amount;ey1+=(target[1]-ey1)*amount;ex2+=(target[2]-ex2)*amount;ey2+=(target[3]-ey2)*amount;
            }
            double a=Math.Sqrt(Math.Pow((x-ex1)/20,2)+Math.Pow((y-ey1)/16,2));
            double b=Math.Sqrt(Math.Pow((x-ex2)/20,2)+Math.Pow((y-ey2)/16,2));
            return Math.Max(0,Math.Min(1,(1-Math.Min(a,b))*5));
        }

        private byte[] BlinkPixels(int frame,double blink)
        {
            int step=(int)Math.Round(blink*24);
            if(step<=0)return pixels[frame];
            int closed=frame==0?2:frame>=34?frame+8:frame+9;

            int key=frame*25+step;byte[] result;
            if(blinkCache.TryGetValue(key,out result))return result;
            result=new byte[mixPixels.Length];
            byte[] original=pixels[frame],closedPixels=pixels[closed];
            if(step>=24)Array.Copy(closedPixels,result,result.Length);
            else
            {
                // Open/closed drawings are already registered.  Interpolate only their
                // pixels here; the eye mask below prevents the closed pose from ever
                // replacing the hair, face outline, body, tail, or silhouette alpha.
                double amount=step/24.0;
                for(int p=0;p<result.Length;p++)
                    result[p]=(byte)Math.Round(original[p]*(1-amount)+closedPixels[p]*amount);
            }
            // A blink changes eyelids only; never substitute the closed frame's head/body/alpha.
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
            int blinkStep=(int)Math.Round(current.Blink*24);bool changed=blinkStep!=previousBlinkStep;
            for(int i=0;i<PetMotion.FrameCount;i++)if(current.Weights[i]>0.000001)
            {
                gazeFrames[n]=i;gazeWeights[n]=(float)current.Weights[i];
                if(previousGazeFrames[n]!=i||previousGazeWeights[n]!=(int)Math.Round(current.Weights[i]*1024))changed=true;
                n++;
            }
            if(!changed&&n==previousGazeCount)return mixed;
            for(int i=0;i<n;i++)gazeSources[i]=BlinkPixels(gazeFrames[i],current.Blink);
            if(n==1)Array.Copy(gazeSources[0],mixPixels,mixPixels.Length);
            else warp.RenderWeighted(gazeFrames,gazeWeights,gazeSources,n,mixPixels);
            mixed.WritePixels(new Int32Rect(0,0,CanvasWidth,CanvasHeight),mixPixels,CanvasWidth*4,0);
            for(int i=0;i<n;i++){previousGazeFrames[i]=gazeFrames[i];previousGazeWeights[i]=(int)Math.Round(gazeWeights[i]*1024);}
            previousWasMix=false;previousGazeCount=n;previousBlinkStep=blinkStep;return mixed;
        }

        internal void DrawPose(DrawingContext dc, PetPose current)
        {
            if(!current.Gaze)previousGazeCount=-1;
            BitmapSource image = current.Gaze ? CompositeGaze(current) : Composite(current.Weights);
            dc.PushTransform(new TranslateTransform(0, -27 * current.Lift));
            dc.PushTransform(new RotateTransform(current.Angle * 180 / Math.PI, SceneWidth / 2, 70));
            dc.PushTransform(new ScaleTransform(current.ScaleX, current.ScaleY, SceneWidth / 2, Ground));
            dc.DrawImage(image, new Rect(0, 0, SceneWidth, SceneHeight));
            dc.Pop(); dc.Pop(); dc.Pop();
        }
    }
}
