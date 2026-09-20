// 用程序里真实的 PetParticles 渲染摸头爱心，叠在角色上，输出预览图。
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FatFishPet;

internal static class ParticlePreview
{
    private const int W = 340, H = 360;

    [STAThread]
    private static int Main(string[] args)
    {
        string characterPath = args.Length > 0 ? args[0] : ".build/character-hold.png";
        string output = args.Length > 1 ? args[1] : "particle-preview.png";

        var particles = new PetParticles();
        double t = 0;
        for (int i = 0; i < 240; i++) { t += 1.0 / 60; particles.Update(t, true, 1); }

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(43, 74, 82)), null, new Rect(0, 0, W, H));
            if (File.Exists(characterPath))
            {
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(Path.GetFullPath(characterPath)); image.EndInit(); image.Freeze();
                double scale = 300.0 / image.PixelHeight;
                double w = image.PixelWidth * scale;
                dc.DrawImage(image, new Rect((W - w) / 2, 40, w, 300));
            }
            particles.Draw(dc);
        }
        var target = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using (FileStream stream = File.Create(output)) encoder.Save(stream);
        Console.WriteLine("wrote " + output);
        return 0;
    }
}
