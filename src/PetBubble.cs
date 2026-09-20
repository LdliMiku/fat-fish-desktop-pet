using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FatFishPet
{
    // 气泡皮肤参数：全部来自 .build/prepare-bubble-ui.cjs 生成的 assets/ui/bubble-meta.json。
    // 换素材时只要替换 assets/ui/bubble-source.png 并重跑该脚本，这里不需要改代码；
    // 元数据缺失或解析失败时退回下面这套默认值（与当前手绘素材一致）。
    internal sealed class BubbleSkin
    {
        public double Scale = 0.13;
        public double BodyLeft = 75, BodyTop = 82, BodyRight = 1691, BodyBottom = 767;
        // 四个角各自尺寸（源图像素）：只有角落里有装饰的那个角需要放大，可见圆角来自素材本身，
        // 所以大小不同的角不会让泡泡一边宽一边窄。
        public double TlW = 376, TlH = 209, TrW = 376, TrH = 237, BlW = 113, BlH = 113, BrW = 771, BrH = 353;
        public double EdgeW = 113, EdgeH = 113;
        public double TailW = 122, TailH = 44, TailTipX = 61, TailTipY = 35;
        public bool TailOnRight = true;
        public bool HasShell;
        public double ShellW, ShellH, ShellLeftOfBodyRight, ShellTopAboveBottom;
        public double PadLeft = 18, PadRight = 18, PadTop = 14, PadBottom = 15;
        public double MaxTextWidth = 280;
        public Color Ink = Color.FromRgb(92, 103, 119);

        public double LeftEdgeW { get { return EdgeW * Scale; } }
        public double TopEdgeH { get { return EdgeH * Scale; } }
        public double TailDisplayWidth { get { return TailW * Scale; } }
        public double TailDrop { get { return TailTipY * Scale; } }

        public static BubbleSkin Load()
        {
            var skin = new BubbleSkin();
            try
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("BubbleMeta"))
                {
                    if (stream == null) return skin;
                    using (var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8))
                    {
                        var json = new System.Web.Script.Serialization.JavaScriptSerializer();
                        var root = json.DeserializeObject(reader.ReadToEnd()) as System.Collections.Generic.Dictionary<string, object>;
                        if (root == null) return skin;
                        skin.Scale = Number(root, "scale", skin.Scale);
                        var body = Object(root, "body");
                        if (body != null)
                        {
                            skin.BodyLeft = Number(body, "left", skin.BodyLeft);
                            skin.BodyTop = Number(body, "top", skin.BodyTop);
                            skin.BodyRight = Number(body, "right", skin.BodyRight);
                            skin.BodyBottom = Number(body, "bottom", skin.BodyBottom);
                        }
                        var corner = Object(root, "corner");
                        if (corner != null)
                        {
                            // 支持三种写法：四角独立 {tl:{w,h},...}、四边 {left,right,top,bottom}、早期 {w,h}。
                            double uniform = Number(corner, "w", 0);
                            var tl = Object(corner, "tl");
                            var tr = Object(corner, "tr");
                            var bl = Object(corner, "bl");
                            var br = Object(corner, "br");
                            if (tl != null) { skin.TlW = Number(tl, "w", skin.TlW); skin.TlH = Number(tl, "h", skin.TlH); }
                            if (tr != null) { skin.TrW = Number(tr, "w", skin.TrW); skin.TrH = Number(tr, "h", skin.TrH); }
                            if (bl != null) { skin.BlW = Number(bl, "w", skin.BlW); skin.BlH = Number(bl, "h", skin.BlH); }
                            if (br != null) { skin.BrW = Number(br, "w", skin.BrW); skin.BrH = Number(br, "h", skin.BrH); }
                            if (uniform > 0) { skin.TlW = skin.TrW = skin.BlW = skin.BrW = uniform; skin.TlH = skin.TrH = skin.BlH = skin.BrH = uniform; }
                        }
                        var edge = Object(root, "edge");
                        if (edge != null) { skin.EdgeW = Number(edge, "w", skin.EdgeW); skin.EdgeH = Number(edge, "h", skin.EdgeH); }
                        var tail = Object(root, "tail");
                        if (tail != null)
                        {
                            skin.TailW = Number(tail, "w", skin.TailW);
                            skin.TailH = Number(tail, "h", skin.TailH);
                            skin.TailTipX = Number(tail, "tipX", skin.TailTipX);
                            skin.TailTipY = Number(tail, "tipY", skin.TailTipY);
                            string side = tail.ContainsKey("side") ? Convert.ToString(tail["side"]) : "";
                            skin.TailOnRight = side != "left";
                        }
                        var shell = Object(root, "shell");
                        if (shell != null)
                        {
                            skin.HasShell = true;
                            skin.ShellW = Number(shell, "w", skin.ShellW);
                            skin.ShellH = Number(shell, "h", skin.ShellH);
                            skin.ShellLeftOfBodyRight = Number(shell, "leftOfBodyRight", skin.ShellLeftOfBodyRight);
                            skin.ShellTopAboveBottom = Number(shell, "topAboveBottom", skin.ShellTopAboveBottom);
                        }
                        var padding = Object(root, "padding");
                        if (padding != null)
                        {
                            skin.PadLeft = Number(padding, "left", skin.PadLeft);
                            skin.PadRight = Number(padding, "right", skin.PadRight);
                            skin.PadTop = Number(padding, "top", skin.PadTop);
                            skin.PadBottom = Number(padding, "bottom", skin.PadBottom);
                        }
                        var text = Object(root, "text");
                        if (text != null)
                        {
                            skin.MaxTextWidth = Number(text, "maxWidth", skin.MaxTextWidth);
                            string ink = text.ContainsKey("ink") ? Convert.ToString(text["ink"]) : "";
                            if (ink != null && ink.Length == 7 && ink[0] == '#')
                            {
                                skin.Ink = Color.FromRgb(
                                    (byte)Convert.ToInt32(ink.Substring(1, 2), 16),
                                    (byte)Convert.ToInt32(ink.Substring(3, 2), 16),
                                    (byte)Convert.ToInt32(ink.Substring(5, 2), 16));
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
            return skin;
        }

        private static System.Collections.Generic.Dictionary<string, object> Object(System.Collections.Generic.Dictionary<string, object> source, string key)
        {
            return source != null && source.ContainsKey(key) ? source[key] as System.Collections.Generic.Dictionary<string, object> : null;
        }

        private static double Number(System.Collections.Generic.Dictionary<string, object> source, string key, double fallback)
        {
            if (source == null || !source.ContainsKey(key)) return fallback;
            double value;
            return double.TryParse(Convert.ToString(source[key]), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) ? value : fallback;
        }
    }

    // 桌面气泡：把大肥鱼说的话显示在角色的侧边。
    // 外观来自参考图的九宫格：四角与装饰按原始比例，四边与中心随文字拉伸；
    // 尾巴和右下角装饰（小鲸鱼那一块）都是独立贴图，所以不会被拉变形。
    internal sealed class PetBubble
    {
        internal static readonly BubbleSkin Skin = BubbleSkin.Load();
        // 气泡与角色之间的横向间隙；角色可见范围会按比例内缩，避免把素材里的透明留白算成距离。
        private const double SideGap = 4;
        private const double CharacterInsetX = 0.14;
        private const double TailAnchorHeight = 0.42;
        public const double MinimumSeconds = 3.0;
        public const double MaximumSeconds = 14.0;
        public static double MaxTextWidth { get { return Skin.MaxTextWidth; } }

        private readonly Window window;
        private readonly DispatcherTimer hideTimer;
        private readonly DispatcherTimer followTimer;
        private readonly Canvas host = new Canvas();
        private double bubbleWidth, bubbleHeight;
        private string current = "";
        private bool mirrored;

        public bool Enabled = true;
        public Func<Rect> AnchorProvider;
        public event Action OpenChatRequested;
        public bool OnRightSide { get { return mirrored; } }

        public PetBubble()
        {
            window = new Window
            {
                Title = "大肥鱼气泡",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = null,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Focusable = false,
                // 让切片边界落在整像素上：否则拉伸区与四角的重采样比例不同，
               Opacity = 0,
                UseLayoutRounding = true,
                Content = host
            };
            window.MouseLeftButtonUp += delegate { Dismiss(true); };
            hideTimer = new DispatcherTimer();
            hideTimer.Tick += delegate { hideTimer.Stop(); Fade(false); };
            followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            followTimer.Tick += delegate { Reposition(); };
        }

        public static double LifetimeFor(string message)
        {
            string text = message == null ? "" : message;
            return Math.Max(MinimumSeconds, Math.Min(MaximumSeconds, 2.2 + text.Length * 0.16));
        }

        private static BitmapSource Load(string name)
        {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("缺少气泡素材：" + name);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }

        private static Image Slice(BitmapSource image, int x, int y, int width, int height, double targetWidth, double targetHeight)
        {
            // 每条切片都向外多取 2 像素（同时把目标矩形也外扩同样比例）：这样切片边缘的像素
            // 是邻居的内容，缩放采样在边界混入的也是同样的画面，不会出现淡淡的接缝线。
            const double Bleed = 2;
            double fx = width > 0 ? targetWidth / width : 1, fy = height > 0 ? targetHeight / height : 1;
            double sx = x - Bleed / fx, sy = y - Bleed / fy;
            double sw = width + 2 * Bleed / fx, sh = height + 2 * Bleed / fy;
            double left = -Bleed, top = -Bleed, w = targetWidth + 2 * Bleed, h = targetHeight + 2 * Bleed;
            if (sx < 0) { double cut = -sx; sx = 0; sw -= cut; left += cut * fx; w -= cut * fx; }
            if (sy < 0) { double cut = -sy; sy = 0; sh -= cut; top += cut * fy; h -= cut * fy; }
            if (sx + sw > image.PixelWidth) { double cut = sx + sw - image.PixelWidth; sw -= cut; w -= cut * fx; }
            if (sy + sh > image.PixelHeight) { double cut = sy + sh - image.PixelHeight; sh -= cut; h -= cut * fy; }
            var crop = new CroppedBitmap(image, new Int32Rect((int)Math.Round(sx), (int)Math.Round(sy), Math.Max(1, (int)Math.Round(sw)), Math.Max(1, (int)Math.Round(sh))));
            crop.Freeze();
            // left/top 是相对切片左上角的偏移；调用方按原位置放置，这里用 Margin 抵消。
            return new Image { Source = crop, Width = Math.Max(0, w), Height = Math.Max(0, h), Stretch = Stretch.Fill, Margin = new Thickness(left, top, 0, 0) };
        }

        private static void Place(Canvas canvas, UIElement element, double left, double top)
        {
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            canvas.Children.Add(element);
        }

        public static FrameworkElement BuildContent(string message) { return BuildContent(message, false); }

        public static FrameworkElement BuildContent(string message, bool mirrored)
        {
            var text = new TextBlock
            {
                Text = message ?? "",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                FontSize = 13.5,
                LineHeight = 21,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                Foreground = new SolidColorBrush(Skin.Ink)
            };
            text.Measure(new Size(Skin.MaxTextWidth, double.PositiveInfinity));
            double textWidth = Math.Min(Skin.MaxTextWidth, Math.Ceiling(text.DesiredSize.Width));
            double textHeight = Math.Ceiling(text.DesiredSize.Height);

            BitmapSource body = Load("BubbleBody");
            BitmapSource tailImage = Load("BubbleTail");

            double scale = Skin.Scale;
            // 切片几何取整到整像素：边界落在非整数像素上时，拉伸切片会在边缘混入透明像素，
            // 在深色桌面上表现为一条淡淡的灰线（浅色背景看不出来）。
            double tlW = Math.Round(Skin.TlW * scale), tlH = Math.Round(Skin.TlH * scale);
            double trW = Math.Round(Skin.TrW * scale), trH = Math.Round(Skin.TrH * scale);
            double blW = Math.Round(Skin.BlW * scale), blH = Math.Round(Skin.BlH * scale);
            double brW = Math.Round(Skin.BrW * scale), brH = Math.Round(Skin.BrH * scale);
            double edgeW = Math.Round(Skin.EdgeW * scale), edgeH = Math.Round(Skin.EdgeH * scale);
            double width = Math.Round(Math.Max(Math.Max(tlW + trW, blW + brW) + 8, Skin.PadLeft + textWidth + Skin.PadRight));
            double height = Math.Round(Math.Max(Math.Max(tlH + blH, trH + brH) + 8, Skin.PadTop + textHeight + Skin.PadBottom));

            var canvas = new Canvas { Width = width + 10, Height = height + 14, Background = Brushes.Transparent };
            var art = new Canvas { Width = canvas.Width, Height = canvas.Height, Background = Brushes.Transparent };
            if (mirrored) art.RenderTransform = new ScaleTransform(-1, 1, canvas.Width / 2, 0);

            int bl = (int)Skin.BodyLeft, bt = (int)Skin.BodyTop, br = (int)Skin.BodyRight, bb = (int)Skin.BodyBottom;
            int tlWp = (int)Skin.TlW, tlHp = (int)Skin.TlH, trWp = (int)Skin.TrW, trHp = (int)Skin.TrH;
            int blWp = (int)Skin.BlW, blHp = (int)Skin.BlH, brWp = (int)Skin.BrW, brHp = (int)Skin.BrH;
            int edgeWp = (int)Skin.EdgeW, edgeHp = (int)Skin.EdgeH;
            double midWDisplay = Math.Max(0, width - edgeW * 2), midHDisplay = Math.Max(0, height - edgeH * 2);
            // 先画四边与中心（拉伸区），再画四角（原样）压住接缝。
            Place(art, Slice(body, bl + tlWp, bt, Math.Max(1, br - trWp - bl - tlWp), edgeHp, width - tlW - trW, edgeH), tlW, 0);
            Place(art, Slice(body, bl + blWp, bb - edgeHp, Math.Max(1, br - brWp - bl - blWp), edgeHp, width - blW - brW, edgeH), blW, height - edgeH);
            Place(art, Slice(body, bl, bt + tlHp, edgeWp, Math.Max(1, bb - blHp - bt - tlHp), edgeW, height - tlH - blH), 0, tlH);
            Place(art, Slice(body, br - edgeWp, bt + trHp, edgeWp, Math.Max(1, bb - brHp - bt - trHp), edgeW, height - trH - brH), width - edgeW, trH);
            Place(art, Slice(body, bl + edgeWp, bt + edgeHp, Math.Max(1, br - bl - edgeWp * 2), Math.Max(1, bb - bt - edgeHp * 2), midWDisplay, midHDisplay), edgeW, edgeH);
            Place(art, Slice(body, bl, bt, tlWp, tlHp, tlW, tlH), 0, 0);
            Place(art, Slice(body, br - trWp, bt, trWp, trHp, trW, trH), width - trW, 0);
            Place(art, Slice(body, bl, bb - blHp, blWp, blHp, blW, blH), 0, height - blH);
            Place(art, Slice(body, br - brWp, bb - brHp, brWp, brHp, brW, brH), width - brW, height - brH);

            // 尾巴和右下角装饰（小鲸鱼那一块）都在右下角切片里，跟着切片一起保持原始比例，
            // 所以不需要单独贴图，也不会出现描边断开或接缝。
            canvas.Children.Add(art);

            // 文字整体在气泡里居中：左右按气泡中线对称，上下按剩余高度均分。
            // （左右不能直接用 PadLeft/PadRight，因为右下角切片很宽，两者并不对称。）
            double contentWidth = Math.Max(textWidth, width - (Skin.PadLeft + Skin.PadRight));
            double textLeft = Math.Max(0, (width - contentWidth) / 2);
            double textTop = Math.Max(Skin.PadTop, (height - textHeight) / 2);
            text.Width = contentWidth;
            Place(canvas, text, textLeft, textTop);
            text.Measure(new Size(contentWidth, double.PositiveInfinity));
            text.Arrange(new Rect(textLeft, textTop, contentWidth, textHeight));
            return canvas;
        }

        public void Show(string message)
        {
            if (!Enabled || string.IsNullOrEmpty(message)) return;
            if (!window.IsVisible || current != message)
            {
                mirrored = WillAppearOnRight();
                BuildHost(message);
                if (!window.IsVisible) window.Show();
            }
            current = message;
            window.UpdateLayout();
            Reposition();
            Fade(true);
            followTimer.Start();
            hideTimer.Interval = TimeSpan.FromSeconds(LifetimeFor(message));
            hideTimer.Stop(); hideTimer.Start();
        }

        private void BuildHost(string message)
        {
            host.Children.Clear();
            var content = BuildContent(message, mirrored);
            host.Children.Add(content);
            host.Width = content.Width;
            host.Height = content.Height;
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            bubbleWidth = content.Width;
            bubbleHeight = content.Height;
        }

        private bool WillAppearOnRight()
        {
            if (AnchorProvider == null) return mirrored;
            Rect character = AnchorProvider();
            Rect area = SystemParameters.WorkArea;
            return (area.Right - character.Right) >= (character.Left - area.Left);
        }

        public void Hide()
        {
            hideTimer.Stop();
            Dismiss(false);
        }

        private void Dismiss(bool openChat)
        {
            hideTimer.Stop();
            followTimer.Stop();
            if (window.IsVisible) window.Hide();
            current = "";
            if (openChat)
            {
                var handler = OpenChatRequested;
                if (handler != null) handler();
            }
        }

        private void Fade(bool show)
        {
            window.BeginAnimation(Window.OpacityProperty, new DoubleAnimation(show ? 1.0 : 0.0, TimeSpan.FromSeconds(show ? 0.12 : 0.3)));
        }

        private void Reposition()
        {
            if (!window.IsVisible || AnchorProvider == null || bubbleWidth <= 0) return;
            Rect character = AnchorProvider();
            Rect area = SystemParameters.WorkArea;
            double bodyWidth = bubbleWidth - 10, bodyHeight = bubbleHeight - 14;
            double left, top; bool onRight;
            PlacementFor(character, new Size(bodyWidth, bodyHeight), area, out left, out top, out onRight);
            if (onRight != mirrored)
            {
                mirrored = onRight;
                if (!string.IsNullOrEmpty(current)) BuildHost(current);
            }
            window.Left = left;
            window.Top = top;
        }

        // 侧边放置：角色靠近左边缘就出现在右边，反之在左边；竖向让尾巴尖对着她上半身。
        public static void PlacementFor(Rect character, Size bubble, Rect area, out double left, out double top, out bool onRight)
        {
            Rect visible = Inset(character);
            onRight = (area.Right - visible.Right) >= (visible.Left - area.Left);
            left = onRight ? visible.Right + SideGap : visible.Left - SideGap - bubble.Width;
            double targetTailY = visible.Top + visible.Height * TailAnchorHeight;
            top = targetTailY - Skin.TailDrop - bubble.Height;
            left = Math.Max(area.Left + 2, Math.Min(left, area.Right - bubble.Width - 2));
            top = Math.Max(area.Top + 2, Math.Min(top, area.Bottom - bubble.Height - 2));
        }

        // 角色素材四周有透明留白，按比例内缩后才是肉眼看到的身体边缘。
        private static Rect Inset(Rect character)
        {
            double dx = character.Width * CharacterInsetX;
            return new Rect(character.Left + dx, character.Top, Math.Max(1, character.Width - dx * 2), character.Height);
        }

        public void Close()
        {
            hideTimer.Stop();
            followTimer.Stop();
            window.Close();
        }
    }
}
