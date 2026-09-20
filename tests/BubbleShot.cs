// 用当前代码渲染指定文本的气泡，用于和用户截图逐像素比对。
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FatFishPet;

internal static class BubbleShot
{
    [STAThread]
    private static int Main(string[] args)
    {
        string text = args.Length > 0 ? args[0] : "呀！";
        string output = args.Length > 1 ? args[1] : "bubble-shot.png";
        FrameworkElement content = PetBubble.BuildContent(text, false);
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        content.Arrange(new Rect(new Point(0, 0), content.DesiredSize));
        content.UpdateLayout();
        int w = (int)Math.Ceiling(content.DesiredSize.Width), h = (int)Math.Ceiling(content.DesiredSize.Height);
        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(content);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(245, 245, 246)), null, new Rect(0, 0, w + 40, h + 40));
            dc.DrawImage(target, new Rect(20, 20, w, h));
        }
        var final = new RenderTargetBitmap(w + 40, h + 40, 96, 96, PixelFormats.Pbgra32);
        final.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(final));
        using (FileStream stream = File.Create(output)) encoder.Save(stream);
        Console.WriteLine("wrote " + output + " (" + w + "x" + h + ")");
        return 0;
    }
}
