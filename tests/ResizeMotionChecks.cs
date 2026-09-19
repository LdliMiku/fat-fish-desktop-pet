using System;
using System.Windows;
using FatFishPet;

internal static class ResizeMotionChecks
{
    private static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    internal static void Run()
    {
        foreach(double hz in new[]{30.0,60.0,144.0})
        {
            var motion=new PetSizeMotion(90);motion.SetTarget(600);
            double previous=motion.Current;
            for(int i=0;i<(int)hz;i++)
            {
                motion.Advance(1/hz);
                Check(motion.Current>=previous&&motion.Current<=600,"Resize overshot or reversed");
                previous=motion.Current;
            }
            Check(motion.Current==600&&!motion.IsMoving,"Resize failed to settle");
            motion.SetTarget(90);double before=motion.Current;
            motion.SetTarget(400);Check(motion.Current==before,"New target snapped visible size");
            motion.Advance(1/hz);Check(motion.Current<before&&motion.Current>400,"Resize reversal jumped");
        }
        var stalled=new PetSizeMotion(90);stalled.SetTarget(600);stalled.Advance(2);
        Check(stalled.Current<400,"Delayed frame jumped to target");
        foreach(var area in new[]{new Rect(0,0,1920,1040),new Rect(-1920,-200,1920,1080),new Rect(0,0,800,600)})
        foreach(double height in new[]{90.0,300.0,480.0})
        foreach(var input in new[]{new Point(-3000,-2000),new Point(3000,2000),new Point(300,200)})
        {
            var bounds=PetSizeMotion.Bounds(height);var point=PetSizeMotion.ClampPosition(input,area,height);
            bounds.Offset(point.X,point.Y);Check(area.Contains(bounds),"Visible pet cannot reach or fit screen edge");
            var relative=PetSizeMotion.Bounds(height);
            double footX=relative.Left+relative.Width/2,footY=relative.Top+8+340*height/300;
            Check(Math.Abs(footX-PetSizeMotion.AnchorX)<1e-8&&Math.Abs(footY-PetSizeMotion.AnchorY)<1e-8,"Resize anchor moved");
        }
        Console.WriteLine("PASS: resize smoothing at 30/60/144 Hz, retarget/reverse/stall, foot anchor and multi-monitor bounds.");
    }

}

