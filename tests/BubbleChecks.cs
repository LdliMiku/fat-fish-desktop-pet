// Headless checks for the desktop speech bubble: text wrapping, size growth,
// display duration mapping and a rendered preview PNG of the bubble itself.
// The preview is produced from the same BuildContent used by the running pet.
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FatFishPet;

internal static class BubbleChecks
{
    private static int failures;

    // 底边以下是否画出了尾巴（尾巴贴图允许超出本体矩形）。
    // 尾巴是否落在指定的底角（mirrored 为 true 时应在左下角）。
    // 尾巴和小鲸鱼现在都在本体右下角切片里，所以检查底角区域内是否有蓝色墨色（贴图内容）。
    private static bool CornerInk(RenderTargetBitmap bitmap, int width, int height, int bodyWidth, int bodyHeight, bool mirrored)
    {
        int y0 = Math.Max(0, bodyHeight - 30), y1 = Math.Min(height - 1, bodyHeight - 2);
        int from = mirrored ? 0 : (int)(bodyWidth * 0.6);
        int to = mirrored ? (int)(bodyWidth * 0.4) : Math.Min(width - 1, bodyWidth);
        var row = new byte[width * 4];
        for (int y = y0; y <= y1; y++)
        {
            bitmap.CopyPixels(new Int32Rect(0, y, width, 1), row, width * 4, 0);
            for (int x = Math.Max(0, from); x <= Math.Min(width - 1, to); x++)
                if (row[x * 4 + 3] > 200 && row[x * 4] - row[x * 4 + 2] > 25) return true;   // Pbgra32：B 在前
        }
        return false;
    }

    private static bool OpaqueBelow(RenderTargetBitmap bitmap, int width, int height, int bodyWidth, int bodyHeight)
    {
        int y0 = Math.Max(0, bodyHeight - 2), y1 = Math.Min(height - 1, bodyHeight + 13);
        var row = new byte[(width) * 4];
        for (int y = y0; y <= y1; y++)
        {
            bitmap.CopyPixels(new Int32Rect(0, y, width, 1), row, width * 4, 0);
            for (int x = 0; x < width; x++) if (row[x * 4 + 3] > 120) return true;
        }
        return false;
    }

    // 右下角外溢区域是否画出了鲸鱼。
    private static bool OpaqueRight(RenderTargetBitmap bitmap, int width, int height, int bodyWidth, int bodyHeight)
    {
        int y0 = Math.Max(0, bodyHeight - 46), y1 = height - 1;
        var row = new byte[width * 4];
        for (int y = y0; y <= y1; y++)
        {
            bitmap.CopyPixels(new Int32Rect(0, y, width, 1), row, width * 4, 0);
            for (int x = Math.Max(0, bodyWidth - 4); x < width; x++) if (row[x * 4 + 3] > 120) return true;
        }
        return false;
    }

    private static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name);
        if (!ok) failures++;
    }

    private static FrameworkElement Arrange(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));
        element.UpdateLayout();
        return element;
    }

    private static RenderTargetBitmap Render(FrameworkElement element, out int width, out int height)
    {
        width = (int)Math.Ceiling(element.DesiredSize.Width);
        height = (int)Math.Ceiling(element.DesiredSize.Height);
        var bitmap = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string preview = args.Length > 0 ? args[0] : "bubble-preview.png";
        Check(PetBubble.LifetimeFor("在的。") >= PetBubble.MinimumSeconds, "short line keeps at least 3 seconds");
        Check(PetBubble.LifetimeFor(new string('字', 400)) == PetBubble.MaximumSeconds, "long line capped at 14 seconds");
        Check(PetBubble.LifetimeFor("我在的，摸摸头。") >= PetBubble.LifetimeFor("在的。"), "longer text shows longer");
        Check(PetBubble.LifetimeFor(null) == PetBubble.MinimumSeconds, "empty message uses the minimum");

        string[] samples = {
            "我在的，摸摸头。",
            "今天也陪你一起，别太累了，记得起来喝口水再继续。",
            "唔……这个我还不太懂，等我接上网络再好好回你，你先教教我吧，或者我们换个话题也行。"
        };
        // 第三张用镜像版：模拟角色在屏幕左半边时气泡出现在她右侧、尾巴落在左下角。
        bool[] mirroredSample = { false, false, true };
        var bitmaps = new RenderTargetBitmap[samples.Length];
        var widths = new int[samples.Length];
        var heights = new int[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            var element = Arrange(PetBubble.BuildContent(samples[i], mirroredSample[i]));
            bitmaps[i] = Render(element, out widths[i], out heights[i]);
            Check(widths[i] <= PetBubble.MaxTextWidth + 90, "bubble " + i + " wraps inside the maximum width");
            Check(heights[i] > 30, "bubble " + i + " has a visible height");
        }
        // 画布右侧留 10、底部留 14 像素给鲸鱼和尾巴外溢，本体尺寸要从画布尺寸里减掉。
        int bodyWidth = widths[0] - 10, bodyHeight = heights[0] - 14;
        var fill = new byte[4];
        bitmaps[0].CopyPixels(new Int32Rect(bodyWidth / 2, bodyHeight - 18, 1, 1), fill, 4, 0);
        // Pbgra32 的字节顺序是 B、G、R、A。
        // 不限定色相，只要求贴图内部是不透明的浅色填充，方便以后换风格。
        int average = (fill[0] + fill[1] + fill[2]) / 3;
        Check(fill[3] > 200 && average > 190, "fill is opaque and light (B=" + fill[0] + " G=" + fill[1] + " R=" + fill[2] + ")");
        bool outline = false;
        var row = new byte[16 * 4];
        bitmaps[0].CopyPixels(new Int32Rect(0, bodyHeight / 2, 16, 1), row, 64, 0);
        for (int x = 0; x < 16; x++) if (row[x * 4 + 3] > 200 && row[x * 4] - row[x * 4 + 2] > 30) outline = true;
        Check(outline, "blue outline along the left edge");
        Check(OpaqueBelow(bitmaps[0], widths[0], heights[0], bodyWidth, bodyHeight), "tail sprite drawn below the bottom edge");
        Check(CornerInk(bitmaps[0], widths[0], heights[0], bodyWidth, bodyHeight, false), "whale and tail sit at the bottom right when the bubble is on her left");
        Check(CornerInk(bitmaps[2], widths[2], heights[2], widths[2] - 10, heights[2] - 14, true), "mirrored bubble puts the whale and tail at the bottom left");
        Check(widths[0] <= widths[2], "short text does not make a wider bubble");
        Check(heights[2] > heights[0], "longer text makes the bubble taller");

        // 侧边放置：角色靠左边缘时气泡出现在她右侧，靠右边缘时出现在左侧；都在工作区内。
        var area = new Rect(0, 0, 1920, 1080);
        var bubble = new Size(300, 90);
        double left, top; bool onRight;
        PetBubble.PlacementFor(new Rect(60, 500, 160, 300), bubble, area, out left, out top, out onRight);
        Check(onRight && left > 140, "bubble appears on her right when she hugs the left edge (left=" + left.ToString("0") + ")");
        PetBubble.PlacementFor(new Rect(1700, 500, 160, 300), bubble, area, out left, out top, out onRight);
        Check(!onRight && left + bubble.Width <= 1730, "bubble appears on her left when she hugs the right edge (left=" + left.ToString("0") + ")");
        PetBubble.PlacementFor(new Rect(900, 500, 160, 300), bubble, area, out left, out top, out onRight);
        Check(left >= area.Left && left + bubble.Width <= area.Right && top >= area.Top && top + bubble.Height <= area.Bottom, "bubble stays inside the work area");
        PetBubble.PlacementFor(new Rect(900, 20, 160, 300), bubble, area, out left, out top, out onRight);
        Check(top >= area.Top, "bubble is clamped at the top edge (top=" + top.ToString("0") + ")");

        // 尾巴贴图的上边界就是气泡底边线，叠上去不应该在底边上方留下竖直接缝。
        var seamRow = new byte[widths[1] * 4];
        int seamY = Math.Max(0, bodyHeight - 5);
        bitmaps[1].CopyPixels(new Int32Rect(0, seamY, widths[1], 1), seamRow, widths[1] * 4, 0);
        int minB = 255, maxB = 0;
        double tailDisplayWidth = PetBubble.Skin.TailDisplayWidth;
        // 只检查装饰贴图没覆盖到的左侧底边：那里应该是一条平滑的填充，不应出现贴图接缝。
        int from = (int)Math.Round(PetBubble.Skin.TlW * PetBubble.Skin.Scale) + 4;
        int to = Math.Min((int)(bodyWidth * 0.5), Math.Max(from + 4, bodyWidth - 4));
        for (int x = from; x <= to; x++)
        {
            // 只看纸面填充（够亮），跳过文字、描边和鲸鱼贴图里的深色。
            if (seamRow[x * 4 + 3] < 200) continue;
            if ((seamRow[x * 4] + seamRow[x * 4 + 1] + seamRow[x * 4 + 2]) / 3 < 225) continue;
            int value = seamRow[x * 4];
            if (value < minB) minB = value;
            if (value > maxB) maxB = value;
        }
        Check(maxB - minB <= 12, "no vertical seam above the bottom edge (spread=" + (maxB - minB) + ")");

        int totalWidth = 0, totalHeight = 16;
        for (int i = 0; i < samples.Length; i++) { totalWidth = Math.Max(totalWidth, widths[i]); totalHeight += heights[i] + 12; }
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            // 用深色背景预览：桌面壁纸多是深色，浅色背景会把接缝藏起来看不出问题。
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(43, 74, 82)), null, new Rect(0, 0, totalWidth + 32, totalHeight + 16));
            double y = 16;
            for (int i = 0; i < samples.Length; i++)
            {
                context.DrawImage(bitmaps[i], new Rect(16, y, widths[i], heights[i]));
                y += heights[i] + 12;
            }
        }
        var canvas = new RenderTargetBitmap(totalWidth + 32, totalHeight + 16, 96, 96, PixelFormats.Pbgra32);
        canvas.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(canvas));
        using (FileStream stream = File.Create(preview)) encoder.Save(stream);
        Check(new FileInfo(preview).Length > 1000, "preview image written to " + preview);

        Console.WriteLine(failures == 0 ? "PASS: bubble checks" : ("FAIL: bubble checks (" + failures + ")"));
        return failures == 0 ? 0 : 1;
    }
}
