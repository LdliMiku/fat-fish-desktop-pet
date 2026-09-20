// 按 src/PetApp.cs 里 ShowChatWindow 的结构复刻聊天窗口，渲染成 PNG 便于检查布局。
// 只用来看排版，和程序里的控件是同一套 WPF 类型。
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FatFishPet;

internal static class ChatWindowMock
{
    private static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI");

    private static TextBlock T(string text, double size, Brush ink, double weight = 0)
    {
        return new TextBlock
        {
            Text = text, FontSize = size, Foreground = ink, FontFamily = Font,
            FontWeight = weight >= 600 ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static Border Bubble(string message, bool mine)
    {
        FrameworkElement body;
        if (!mine)
        {
            body = PetBubble.BuildContent(message, true);
            body.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            body = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(213, 232, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(178, 206, 238)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(11, 7, 11, 7),
                MaxWidth = 400,
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = new TextBlock
                {
                    Text = message, FontSize = 13, Foreground = Brushes.Black, FontFamily = Font,
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
                }
            };
        }
        return new Border { Child = body, Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = mine ? HorizontalAlignment.Stretch : HorizontalAlignment.Left };
    }

    private static Border Button(string text, bool primary)
    {
        return new Border
        {
            Background = primary ? new SolidColorBrush(Color.FromRgb(58, 91, 168)) : Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 210, 225)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(8, 0, 0, 0),
            Child = T(text, 13, primary ? Brushes.White : Brushes.Black)
        };
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "chat-mock.png";
        var root = new Border { Background = Brushes.White, Padding = new Thickness(10, 10, 10, 8) };
        var panel = new DockPanel();
        root.Child = panel;

        var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);

        var log = new StackPanel();
        foreach (var pair in new[] {
            new object[] { "你回来啦，今天过得怎么样？", false },
            new object[] { "还行，就是有点累。", true },
            new object[] { "那就先歇一会儿吧，我在这儿陪着。", false },
            new object[] { "嗯，谢谢你。", true },
        }) log.Children.Add(Bubble((string)pair[0], (bool)pair[1]));
        panel.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = log,
            Padding = new Thickness(6),
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252))
        });

        var inputRow = new DockPanel();
        var send = Button("发送", true);
        DockPanel.SetDock(send, Dock.Right); inputRow.Children.Add(send);
        inputRow.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 210, 225)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 5, 6, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Child = T("说点什么…", 13, Brushes.Gray)
        });
        bottom.Children.Add(inputRow);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 8) };
        buttons.Children.Add(Button("接口设置", false));
        buttons.Children.Add(Button("清空记录", false));
        bottom.Children.Add(buttons);
        bottom.Children.Add(T("把想说的话打进去，回车或点「发送」。", 12, Brushes.Gray));

        root.Width = 440; root.Height = 560;
        root.Measure(new Size(440, 560));
        root.Arrange(new Rect(0, 0, 440, 560));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(440, 560, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(output)) encoder.Save(stream);
        Console.WriteLine("wrote " + output);
        return 0;
    }
}
