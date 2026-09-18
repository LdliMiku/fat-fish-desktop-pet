using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FatFishPet;

internal static class AnimationChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [STAThread]
    private static void Main(string[] args)
    {
        try { if(args.Length>0){Export();return;} Run(); }
        catch (Exception error) { Console.WriteLine("FAIL: " + error.GetType().FullName + ": " + error.Message); Environment.ExitCode = 1; }
    }

    private static void Export()
    {
        using(var view=new PetSpriteView())
        {
            var raw=(byte[][])typeof(PetSpriteView).GetField("pixels",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(view);
            for(int i=0;i<34;i++)File.WriteAllBytes(Path.Combine(".build","motion-inputs",i.ToString("D2")+".bgra"),raw[i]);
        }
    }
    private static bool Equal(byte[] a,byte[] b,int start){for(int i=start;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    private static void Same(PetPose a, PetPose b, string label)
    {
        double distance = 0; for (int i = 0; i < 34; i++) distance += Math.Abs(a.Weights[i] - b.Weights[i]);
        Check(distance < .005 && Math.Abs(a.Lift-b.Lift)<.002 && Math.Abs(a.Angle-b.Angle)<.002 && Math.Abs(a.ScaleY-b.ScaleY)<.002, "Discontinuous transition: " + label);
    }

    private static void Valid(PetPose p)
    {
        double total=0; foreach(double weight in p.Weights) { Check(weight >= -1e-9 && weight <= 1.00000001, "Invalid blend weight"); total+=weight; }
        Check(Math.Abs(total-1)<1e-7, "Opacity weights do not sum to one");
        Check(p.Lift>=0&&p.Lift<=1, "Invalid lift height");
    }

    private static RenderTargetBitmap Render(PetSpriteView view, PetPose pose, double height)
    {
        double factor=height/PetSpriteView.CharacterHeight;
        int width=(int)Math.Ceiling(PetSpriteView.SceneWidth*factor),h=(int)Math.Ceiling(PetSpriteView.SceneHeight*factor);
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen()){dc.PushTransform(new ScaleTransform(factor,factor));view.DrawPose(dc,pose);dc.Pop();}
        var bitmap=new RenderTargetBitmap(width,h,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);return bitmap;
    }

    private static PetPose At(double time,AnimationKind kind,double rate,bool pickup)
    {
        var m=new PetMotion();m.Rates[kind]=rate;if(pickup)m.BeginLift(0);
        PetPose p=m.Evaluate(0);
        for(double t=1.0/240;t<time;t+=1.0/240){m.SetGazeOffset(400,0,t);p=m.Evaluate(t);}
        m.SetGazeOffset(400,0,time);return m.Evaluate(time);
    }
    private static void RateChecks()
    {
        foreach(double rate in new[]{.25,.5,1.0,2.0,5.0})
        {
            var normal=At(.30,AnimationKind.Turn,1,false);var changed=At(.30/rate,AnimationKind.Turn,rate,false);
            Check(Math.Abs(normal.LookX-changed.LookX)<.025,"Turn multiplier not proportional");
            var m=new PetMotion();m.Rates[AnimationKind.Pickup]=rate;m.BeginLift(0);m.Evaluate(.441/rate);
            Check(m.State==PetMotionState.Lifted,"Pickup speed ignored");m.Rates[AnimationKind.Landing]=rate;
            m.EndLift(.5/rate);m.Evaluate(1.101/rate);Check(m.State==PetMotionState.Idle,"Landing speed ignored");
        }
        Check(Math.Abs(At(1,AnimationKind.Breath,2,false).ScaleY-At(2,AnimationKind.Breath,1,false).ScaleY)<1e-8,"Breathing multiplier ignored");
        Check(Math.Abs(At(1,AnimationKind.Sway,2,true).Angle-At(2,AnimationKind.Sway,1,true).Angle)<1e-8,"Sway multiplier ignored");
        var slowBlink=At(3.7,AnimationKind.Blink,.5,false);var fastBlink=At(3.7,AnimationKind.Blink,2,false);
        Check(slowBlink.Blink>.2&&fastBlink.Blink==0,"Blink multiplier ignored");
        var timed=new PetMotion();timed.Rates[AnimationKind.Turn]=5;
        for(int i=0;i<600;i++){double t=i/120.0;timed.SetGazeOffset(400,0,t);var p=timed.Evaluate(t);if(t>4&&t<4.9)Check(p.LookX>.99,"Speed changed 5 second timeout");}
        var active=new PetMotion();active.BeginLift(0);active.Evaluate(.1);var before=active.Evaluate(.15);active.Rates[AnimationKind.Pickup]=4;Same(before,active.Evaluate(.15),"live rate edit");
        var savedRates=new AnimationRates();savedRates[AnimationKind.Turn]=2.35;savedRates[AnimationKind.Blink]=.25;savedRates[AnimationKind.Click]=5;
        var restored=new AnimationRates();
        foreach(string line in savedRates.ToLines()){var pair=line.Split('=');restored.Read(pair[0],double.Parse(pair[1],CultureInfo.InvariantCulture));}
        for(int i=0;i<7;i++)Check(savedRates[(AnimationKind)i]==restored[(AnimationKind)i],"Rate persistence roundtrip failed");
        double parsed;
        Check(NumericSettingRow.TryValue("1.75x",.25,5,out parsed)&&parsed==1.75,"Decimal speed input failed");
        Check(NumericSettingRow.TryValue("125.5%",30,200,out parsed)&&parsed==125.5,"Decimal size input failed");
        foreach(string bad in new[]{"", "NaN", "Infinity", "-1", "0", "6", "text"})Check(!NumericSettingRow.TryValue(bad,.25,5,out parsed),"Invalid numeric input accepted");
        var row=new NumericSettingRow("Speed","倍",.25,5,1);double received=0;row.ValueChanged+=delegate(double v){received=v;};
        var input=(System.Windows.Controls.TextBox)typeof(NumericSettingRow).GetField("input",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(row);
        input.Text="2.35";Check(row.Commit()&&row.Value==2.35&&received==2.35,"Numeric control did not apply input");
        input.Text="NaN";Check(!row.Commit()&&row.Value==2.35,"Invalid input changed live value");
        var uiPanel=new System.Windows.Controls.StackPanel{Margin=new Thickness(22)};
        uiPanel.Children.Add(new System.Windows.Controls.TextBlock{Text="动画播放倍率",FontSize=22,Margin=new Thickness(0,0,0,16)});
        foreach(string name in new[]{"转头跟随","眨眼动作","待机呼吸","拎起过渡","落地过渡","悬空晃动","点击反馈"})uiPanel.Children.Add(new NumericSettingRow(name,"倍",.25,5,1));
        uiPanel.Children.Add(new NumericSettingRow("桌宠大小（输入示例）","%",30,200,125.5));
        var ui=new System.Windows.Controls.Border{Background=Brushes.White,Child=uiPanel};
        ui.Measure(new Size(450,double.PositiveInfinity));ui.Arrange(new Rect(0,0,450,ui.DesiredSize.Height));ui.UpdateLayout();
        var preview=new RenderTargetBitmap(450,(int)Math.Ceiling(ui.ActualHeight),96,96,PixelFormats.Pbgra32);preview.Render(ui);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(preview));
        using(var file=File.Create("demo/animation-qa/numeric-settings-controls.png"))png.Save(file);
        Console.WriteLine("PASS: per-kind time multipliers, real-time timeout, live rate changes, numeric input commit/validation.");
    }

    private static void Run()
    {
        RateChecks();
        var selected=new List<PetPose>();var seen=new HashSet<int>();
        for(int dir=0;dir<9;dir++)
        {
            var m=new PetMotion();PetPose p=m.Evaluate(0);
            for(int i=0;i<240;i++){double t=i/120.0;m.SetGazeOffset((dir%3-1)*350,(dir/3-1)*350,t);p=m.Evaluate(t);Valid(p);}
            Check(Math.Sign(p.LookX)==Math.Sign(dir%3-1)&&Math.Sign(p.LookY)==Math.Sign(dir/3-1),"Wrong direction");
            selected.Add(p);p.Blink=1;selected.Add(p);
            if(dir!=4){double turned=0;for(int f=16;f<25;f++)turned+=p.Weights[f];Check(turned>.98,"Turn did not use drawn keyframes");}
            for(double t=2;t<8;t+=.01){m.SetGazeOffset((dir%3-1)*350,(dir/3-1)*350,t);p=m.Evaluate(t);}
            Check(Math.Abs(p.LookX)+Math.Abs(p.LookY)<.005,"Stationary gaze failed to settle");
        }
        var chase=new PetMotion();PetPose last=chase.Evaluate(0);double vx=0,vy=0;int blinks=0;bool shut=false;
        for(int i=1;i<7200;i++)
        {
            double t=i/120.0;chase.SetGazeOffset(500*Math.Sin(t*21),500*Math.Cos(t*17),t);
            var p=chase.Evaluate(t);Valid(p);
            double nx=(p.LookX-last.LookX)*120,ny=(p.LookY-last.LookY)*120;
            Check(Math.Abs(nx)<=2.601&&Math.Abs(ny)<=2.601,"Speed limit violated");
            Check(Math.Abs(nx-vx)*120<=16.01&&Math.Abs(ny-vy)*120<=16.01,"Velocity snapped on retarget");
            if(p.Blink>.99&&!shut)blinks++;shut=p.Blink>.99;
            if(i%120==0)selected.Add(p);
            last=p;vx=nx;vy=ny;
        }
        Check(blinks>=11&&blinks<=20,"Blink cadence out of bounds");
        var motion=new PetMotion();PetPose before=motion.Evaluate(2.0);motion.BeginLift(2.0);Same(before,motion.Evaluate(2.0),"idle to pickup");
        seen.Clear();
        for(double t=2;t<2.44;t+=1.0/120){PetPose p=motion.Evaluate(t);Valid(p);for(int i=9;i<=12;i++)if(p.Weights[i]>.5)seen.Add(i);}
        Check(seen.Contains(9)&&seen.Contains(10)&&seen.Contains(11)&&seen.Contains(12),"Pickup in-betweens missing");
        Same(motion.Evaluate(2.44-1e-6),motion.Evaluate(2.44+1e-6),"pickup to held");
        motion.SetVelocity(180,3);PetPose held=motion.Evaluate(3);Check(held.Weights[12]>.99,"Suspended base pose missing");
        PetPose releaseStart=motion.Evaluate(3.01);motion.EndLift(3.01);Same(releaseStart,motion.Evaluate(3.01),"held to release");
        Check(motion.Evaluate(3.30).Weights[15]>.8,"Landing in-between missing");
        Same(motion.Evaluate(3.61-1e-6),motion.Evaluate(3.61+1e-6),"landing to idle");
        Check(motion.State==PetMotionState.Idle,"Did not resume idle");
        motion.BeginLift(4);PetPose early=motion.Evaluate(4.025);motion.EndLift(4.025);Same(early,motion.Evaluate(4.025),"quick release");
        motion.BeginLift(5);motion.Evaluate(5.5);motion.EndLift(5.6);PetPose crouch=motion.Evaluate(5.95);motion.BeginLift(5.95);Same(crouch,motion.Evaluate(5.95),"regrab mid-landing");
        var pickup=new PetMotion();pickup.BeginLift(0);
        for(double t=0;t<.44;t+=.035)selected.Add(pickup.Evaluate(t));
        for(double t=.44;t<4;t+=.23)selected.Add(pickup.Evaluate(t));
        pickup.EndLift(4);for(double t=4;t<4.6;t+=.035)selected.Add(pickup.Evaluate(t));
        using(var view=new PetSpriteView())
        {
            var raw=(byte[][])typeof(PetSpriteView).GetField("pixels",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(view);
            Directory.CreateDirectory(Path.Combine(".build","motion-inputs"));
            for(int i=0;i<34;i++)File.WriteAllBytes(Path.Combine(".build","motion-inputs",i.ToString("D2")+".bgra"),raw[i]);
            foreach(int frame in new[]{18,21,24})for(int p=0;p<60*340*4;p++)Check(raw[frame][p]==raw[0][p],"Right turn flipped the ahoge");
            var blinkMethod=typeof(PetSpriteView).GetMethod("BlinkPixels",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            foreach(int frame in new[]{0,16,17,18,19,21,22,23,24})foreach(double blink in new[]{.25,.5,.75,1.0})
            {
                var bytes=(byte[])blinkMethod.Invoke(view,new object[]{frame,blink});
                for(int y=0;y<360;y++)for(int x=0;x<340;x++)
                {
                    int p=(y*340+x)*4;
                    Check(bytes[p+3]==raw[frame][p+3],"Blink changed alpha/silhouette");
                    if(PetSpriteView.EyeMask(frame,x,y)==0)for(int c=0;c<3;c++)Check(bytes[p+c]==raw[frame][p+c],"Blink changed outside eyes");
                }
            }
            var response=new PetMotion();double reached=0;
            for(int i=0;i<120;i++){double t=i/120.0;response.SetGazeOffset(500,0,t);var rp=response.Evaluate(t);if(rp.LookX>=.9){reached=t;break;}}
            Check(reached>0&&reached<.60,"Gaze response too slow");
            Console.WriteLine("90% gaze response: "+reached.ToString("F3")+" seconds; eyelid-only pixel invariants passed.");
            var baseline=new PetMotion().Evaluate(0);baseline.ScaleX=baseline.ScaleY=1;
            var original=Render(view,baseline,300);var originalBytes=new byte[340*360*4];original.CopyPixels(originalBytes,340*4,0);
            Check(Equal(originalBytes,raw[0],0),"Neutral is not original texture");
            var eyelids=baseline;eyelids.Blink=1;
            var closed=Render(view,eyelids,600);var closedEncoder=new PngBitmapEncoder();closedEncoder.Frames.Add(BitmapFrame.Create(closed));
            using(var f=File.Create("demo/animation-qa/native-v08-blink.png"))closedEncoder.Save(f);
            var gazeTimer=Stopwatch.StartNew();
            for(int i=0;i<120;i++)Render(view,selected[i%18],300);
            gazeTimer.Stop();Console.WriteLine("Keyframe blend render mean: "+(gazeTimer.Elapsed.TotalMilliseconds/120).ToString("F2")+" ms/frame");
            var pickupGaze=new PetMotion();PetPose start=pickupGaze.Evaluate(0);
            for(int i=0;i<=414;i++){double t=i/120.0;pickupGaze.SetGazeOffset(-400,-250,t);start=pickupGaze.Evaluate(t);}
            var startImage=Render(view,start,300);var startBytes=new byte[340*360*4];startImage.CopyPixels(startBytes,340*4,0);
            pickupGaze.BeginLift(414/120.0);var endImage=Render(view,pickupGaze.Evaluate(414/120.0),300);
            var endBytes=new byte[startBytes.Length];endImage.CopyPixels(endBytes,340*4,0);
            Check(Equal(startBytes,endBytes,0),"Blinking gaze jumps on pickup");
            int count=0;
            foreach(double height in new[]{90.0,156.71864983763814,300.0,600.0})foreach(PetPose pose in selected)
            {
                Valid(pose);var bitmap=Render(view,pose,height);int w=bitmap.PixelWidth,h=bitmap.PixelHeight;var bytes=new byte[w*h*4];bitmap.CopyPixels(bytes,w*4,0);int solid=0;
                for(int y=0;y<h;y++)for(int x=0;x<w;x++){int alpha=bytes[(y*w+x)*4+3];if(alpha>96)solid++;if(x==0||y==0||x==w-1||y==h-1)Check(alpha<16,"Clipped silhouette at size "+height);}
                Check(solid>w*h*.1&&solid<w*h*.8,"Unexpected alpha coverage");count++;
            }
            var benchMotion=new PetMotion();benchMotion.BeginLift(0);var timer=Stopwatch.StartNew();
            for(int i=0;i<120;i++)Render(view,benchMotion.Evaluate((i%26)/60.0),300);
            timer.Stop();double mean=timer.Elapsed.TotalMilliseconds/120;
            Directory.CreateDirectory(Path.Combine("demo","animation-qa"));
            var board=new DrawingVisual();
            using(var dc=board.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(234,239,244)),null,new Rect(0,0,1020,1080));
                for(int i=0;i<9;i++)
                {
                    var timeline=new PetMotion();PetPose p=timeline.Evaluate(0);
                    for(double t=0;t<2;t+=.01){timeline.SetGazeOffset((i%3-1)*350,(i/3-1)*350,t);p=timeline.Evaluate(t);}
                    var image=Render(view,p,250);image.Freeze();dc.DrawImage(image,new Rect((i%3)*340+28,(i/3)*360+32,283,300));
                    dc.DrawText(new FormattedText(new[]{"Upper left","Up","Upper right","Left","Original neutral","Right","Lower left","Down","Lower right"}[i],CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),15,Brushes.SlateGray),new Point((i%3)*340+28,(i/3)*360+10));
                }
            }
            var contact=new RenderTargetBitmap(1020,1080,96,96,PixelFormats.Pbgra32);contact.Render(board);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(contact));
            using(var file=File.Create(Path.Combine("demo","animation-qa","native-v08-original-rig.png")))encoder.Save(file);
            var transitions=new DrawingVisual();
            using(var dc=transitions.RenderOpen())
            {
                dc.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(0,0,1360,1080));
                for(int row=0;row<3;row++)for(int col=0;col<4;col++)
                {
                    double f=col/3.0;var p=baseline;p.Gaze=true;
                    p.Weights=PetMotion.GazeWeights(row==0?-f:row==2?f:0,row==1?-f:row==2?f:0);
                    var im=Render(view,p,300);im.Freeze();dc.DrawImage(im,new Rect(col*340,row*360,340,360));
                }
            }
            var tb=new RenderTargetBitmap(1360,1080,96,96,PixelFormats.Pbgra32);tb.Render(transitions);
            var te=new PngBitmapEncoder();te.Frames.Add(BitmapFrame.Create(tb));
            using(var f=File.Create("demo/animation-qa/native-v08-inbetweens.png"))te.Save(f);
            var eyes=new DrawingVisual();using(var dc=eyes.RenderOpen())
            {
                dc.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(0,0,1020,720));
                for(int row=0;row<2;row++)for(int col=0;col<3;col++)
                {var p=baseline;p.Gaze=true;p.Blink=row;p.Weights=PetMotion.GazeWeights(col-1,-1);dc.DrawImage(Render(view,p,300),new Rect(col*340,row*360,340,360));}
            }
            var eb=new RenderTargetBitmap(1020,720,96,96,PixelFormats.Pbgra32);eb.Render(eyes);var ee=new PngBitmapEncoder();ee.Frames.Add(BitmapFrame.Create(eb));
            using(var f=File.Create("demo/animation-qa/v09-upper-blinks.png"))ee.Save(f);
        Console.WriteLine("PASS: "+count+" native WPF renders; real-keyframe 8-way pursuit, bounded speed/acceleration, idle timeout, blinking; no timed gaze loop; pickup/release/regrab continuity; 30%-200% alpha margins.");
            Console.WriteLine("Offscreen blended render mean: "+mean.ToString("F2",CultureInfo.InvariantCulture)+" ms/frame (includes bitmap readback, not measured display FPS).");
        }
    }
}
