using System;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

internal static class ResizeWindowChecks
{
    static readonly BindingFlags Fields=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    [STAThread] static int Main(string[] args)
    {
        Window window=null;
        try
        {
            ResizeMotionChecks.Run();
            var assembly=Assembly.LoadFrom(args[0]);
            var type=assembly.GetType("FatFishPet.PetWindow",true);
            window=(Window)Activator.CreateInstance(type,new object[]{false});
            window.Left=100;window.Top=100;
            var handle=new WindowInteropHelper(window).EnsureHandle();
            var surface=(FrameworkElement)window.Content;
            surface.Measure(new Size(window.Width,window.Height));
            surface.Arrange(new Rect(0,0,window.Width,window.Height));
            surface.UpdateLayout();
            int nativeResizes=0;
            HwndSource.FromHwnd(handle).AddHook(delegate(IntPtr h,int message,IntPtr w,IntPtr l,ref bool handled){if(message==5)nativeResizes++;return IntPtr.Zero;});
            var pet=(FrameworkElement)type.GetField("pet",Fields).GetValue(window);
            var motion=type.GetField("sizeMotion",Fields).GetValue(window);
            var current=motion.GetType().GetProperty("Current");
            type.GetField("ready",Fields).SetValue(window,true);
            var resize=type.GetMethod("ResizePet",Fields);
            var frame=type.GetMethod("OnSizeFrame",Fields);
            double width=window.Width,height=window.Height;
            for(int i=0;i<240;i++)
            {
                if(i==0||i==60||i==120)resize.Invoke(window,new object[]{i==60?90.0:500.0});
                var rendering=Activator.CreateInstance(typeof(RenderingEventArgs),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{TimeSpan.FromSeconds(i/60.0)},null);
                frame.Invoke(window,new object[]{null,rendering});
                window.Dispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));
                Check(window.Width==width&&window.Height==height,"Window dimensions changed during zoom");
                Check(pet.ActualWidth==340&&pet.ActualHeight==360,"Zoom triggered sprite relayout");
                Point foot=pet.RenderTransform.Transform(new Point(170,340));
                Check(Math.Abs(foot.X-348)<1e-8&&Math.Abs(foot.Y-688)<1e-8,"Foot anchor drifted");
            }
            Check(nativeResizes==0,"Layered surface received WM_SIZE during zoom");
            Check(Math.Abs((double)current.GetValue(motion,null)-500)<.02,"Window zoom did not settle");
            var click=(ScaleTransform)type.GetField("scale",Fields).GetValue(window);
            click.ScaleX=click.ScaleY=.95;
            Point clickedFoot=pet.RenderTransform.Transform(new Point(170,340));
            Check(Math.Abs(clickedFoot.X-348)<1e-8&&Math.Abs(clickedFoot.Y-688)<1e-8,"Original click feedback moved resize anchor");
            click.ScaleX=click.ScaleY=1;
            type.GetField("ready",Fields).SetValue(window,false);
            Console.WriteLine("PASS: 240 WPF window zoom frames, no WM_SIZE, no sprite relayout, stable foot/click anchors, settled target.");
            return 0;
        }
        catch(Exception error){Console.WriteLine("FAIL: "+error);return 1;}
        finally
        {
            if(window!=null)
            {
                window.GetType().GetField("ready",Fields).SetValue(window,false);
                window.Close();
            }
        }
    }
}
