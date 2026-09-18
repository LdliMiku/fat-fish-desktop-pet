using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("大肥鱼桌宠")]
[assembly: AssemblyDescription("可以点击、拖动的透明桌面伙伴")]
[assembly: AssemblyVersion("0.9.0.0")]

namespace FatFishPet
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (var instance = new Mutex(true, "Local\\FatFishPet-v1", out created))
            {
                if (!created) return;
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.Run(new PetWindow(Array.IndexOf(args, "--animation-diagnostics") >= 0));
                }
                catch (Exception error)
                {
                    MessageBox.Show("桌宠启动遇到了问题：\n" + error.Message,
                        "大肥鱼桌宠", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }

    internal sealed class PetWindow : Window
    {
        private const double DefaultPetHeight = 300;
        private const double MinimumPetHeight = 90;
        private const double MaximumPetHeight = 600;
        private readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "settings.txt");
        private readonly PetSpriteView pet;
        private readonly ScaleTransform scale = new ScaleTransform(1, 1);
        private readonly Forms.NotifyIcon tray;
        private readonly DispatcherTimer sizeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        private Window sizeWindow;
        private NumericSettingRow sizeRow;
        private Window speedWindow;
        private readonly AnimationRates rates=new AnimationRates();
        private readonly NumericSettingRow[] speedRows=new NumericSettingRow[7];
        private double petHeight = 300;
        private double savedLeft = double.NaN;
        private double savedTop = double.NaN;
        private bool ready;
        private bool closing;
        private bool settingsUseAnimationLayout;
        private bool pointerPressed;
        private bool dragging;
        private NativePoint pressCursor;
        private NativePoint previousCursor;
        private double pressLeft;
        private double pressTop;
        private readonly Stopwatch dragClock = new Stopwatch();
        private double previousDragTime;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        public PetWindow(bool animationDiagnostics = false)
        {
            Title = "大肥鱼桌宠";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = true;
            ShowActivated = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            ReadSettings();

            var iconSprite = PetSpriteView.LoadBitmap("PetSprite");
            Icon = BitmapFrame.Create(iconSprite);
            pet = new PetSpriteView
            {
                DiagnosticPath = animationDiagnostics ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "animation-performance.txt") : null,
                Margin = new Thickness(8),
                Cursor = Cursors.Hand,
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, PetSpriteView.Ground / PetSpriteView.SceneHeight)
            };
            System.Windows.Automation.AutomationProperties.SetName(pet, "大肥鱼，按住左键拖动，右键打开菜单");
            System.Windows.Automation.AutomationProperties.SetHelpText(pet, "按住左键拖动 · 滚轮调节大小 · 右键打开菜单");
            RenderOptions.SetBitmapScalingMode(pet, BitmapScalingMode.HighQuality);
            Content = pet;
            pet.CursorOffsetProvider = GetCursorOffset;
            pet.Rates=rates;
            ApplySize();
            if (!settingsUseAnimationLayout && IsFinite(savedLeft) && IsFinite(savedTop))
            {
                double oldWidth = petHeight * iconSprite.PixelWidth / iconSprite.PixelHeight + 16;
                savedLeft += (oldWidth - Width) / 2;
                savedTop += petHeight + 8 - Height + FootInset;
            }
            MouseLeftButtonDown += OnPetPressed;
            MouseMove += OnPetMoved;
            MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { if (pointerPressed) { FinishPointerInteraction(true); e.Handled = true; } };
            LostMouseCapture += delegate { if (pointerPressed) FinishPointerInteraction(false); };
            MouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (!ready || pointerPressed || e.Delta == 0) return;
                ResizePet(petHeight * Math.Pow(1.05, e.Delta / 120.0));
                e.Handled = true;
            };
            sizeSaveTimer.Tick += delegate { sizeSaveTimer.Stop(); SaveSettings(); };
            MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                var menu = CreateMenu();
                menu.PlacementTarget = this;
                menu.IsOpen = true;
                e.Handled = true;
            };

            tray = new Forms.NotifyIcon
            {
                Text = "大肥鱼桌宠 · 双击找回 / 右键退出",
                Icon = System.Drawing.SystemIcons.Information,
                Visible = true
            };
            var trayMenu = new Forms.ContextMenuStrip();
            trayMenu.Items.Add("找回桌宠", null, delegate { Dispatcher.Invoke(new Action(ResetPosition)); });
            trayMenu.Items.Add("退出桌宠", null, delegate { Dispatcher.Invoke(new Action(Close)); });
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(ResetPosition)); };

            Loaded += delegate
            {
                if (IsFinite(savedLeft) && IsFinite(savedTop))
                {
                    Left = savedLeft;
                    Top = savedTop;
                    KeepOnScreen();
                }
                else ResetPosition();
                ResizePet(petHeight);
                ready = true;
                SaveSettings();
            };
            Closed += delegate
            {
                closing = true;
                pet.Dispose();
                sizeSaveTimer.Stop();
                SaveSettings();
                tray.Visible = false;
                tray.Dispose();
                trayMenu.Dispose();
            };
        }

        private readonly Stopwatch cursorIdleClock = Stopwatch.StartNew();
        private NativePoint lastGazeCursor;
        private bool hasGazeCursor;

        private Vector? GetCursorOffset()
        {
            if (!ready || !IsVisible || pet.ActualWidth <= 0 || pet.ActualHeight <= 0) return null;
            NativePoint cursor;
            if (!GetCursorPos(out cursor)) return null;
            if (!hasGazeCursor || cursor.X != lastGazeCursor.X || cursor.Y != lastGazeCursor.Y)
            {
                hasGazeCursor = true;
                lastGazeCursor = cursor;
                cursorIdleClock.Restart();
            }
            if (cursorIdleClock.Elapsed.TotalSeconds >= 5) return new Vector(0, 0);
            Point local = pet.PointFromScreen(new Point(cursor.X, cursor.Y));
            return new Vector(local.X / pet.ActualWidth * PetSpriteView.SceneWidth - PetSpriteView.SceneWidth / 2,
                local.Y / pet.ActualHeight * PetSpriteView.SceneHeight - 170);
        }

        private void OnPetPressed(object sender, MouseButtonEventArgs e)
        {
            if (!ready || pointerPressed || e.ChangedButton != MouseButton.Left) return;
            if (!GetCursorPos(out pressCursor)) return;
            if (!CaptureMouse()) return;
            e.Handled = true;
            pointerPressed = true;
            dragging = false;
            pressLeft = Left;
            pressTop = Top;
            previousCursor = pressCursor;
            previousDragTime = 0;
            dragClock.Restart();
        }

        private Vector CursorDelta(NativePoint from, NativePoint to)
        {
            var delta = new Vector(to.X - from.X, to.Y - from.Y);
            var source = PresentationSource.FromVisual(this);
            return source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice.Transform(delta) : delta;
        }

        private void OnPetMoved(object sender, MouseEventArgs e)
        {
            if (!pointerPressed) return;
            if (e.LeftButton != MouseButtonState.Pressed) { FinishPointerInteraction(false); return; }
            NativePoint cursor;
            if (!GetCursorPos(out cursor)) return;
            Vector delta = CursorDelta(pressCursor, cursor);
            if (!dragging && (Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance))
            {
                dragging = true;
                pet.Cursor = Cursors.SizeAll;
                pet.BeginLift();
            }
            if (dragging)
            {
                Left = pressLeft + delta.X;
                Top = pressTop + delta.Y;
                double time = dragClock.Elapsed.TotalSeconds;
                pet.SetDragVelocity(CursorDelta(previousCursor, cursor).X / Math.Max(.016, time - previousDragTime));
                previousCursor = cursor;
                previousDragTime = time;
            }
            e.Handled = true;
        }

        private void FinishPointerInteraction(bool allowClick)
        {
            bool wasDragging = dragging;
            pointerPressed = false;
            dragging = false;
            dragClock.Stop();
            if (IsMouseCaptured) ReleaseMouseCapture();
            pet.Cursor = Cursors.Hand;
            if (wasDragging) pet.EndLift();
            if (closing) return;
            KeepOnScreen();
            SaveSettings();
            if (!wasDragging && allowClick) ReactToClick();
        }

        private void ReactToClick()
        {
            var animation = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(105/rates[AnimationKind.Click]))
            {
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private ContextMenu CreateMenu()
        {
            var menu = new ContextMenu { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13 };
            menu.Items.Add(new MenuItem { Header = "大肥鱼 · 桌面小伙伴", IsEnabled = false });
            menu.Items.Add(new Separator());
            var size = new MenuItem { Header = "调节大小…" };
            size.Click += delegate { ShowSizeWindow(); };
            menu.Items.Add(size);
            var speeds=new MenuItem{Header="调节动画速度…"};
            speeds.Click+=delegate { ShowSpeedWindow(); };menu.Items.Add(speeds);
            var alwaysOnTop = new MenuItem { Header = "置顶显示", IsCheckable = true, IsChecked = Topmost };
            alwaysOnTop.Click += delegate { Topmost = alwaysOnTop.IsChecked; SaveSettings(); };
            menu.Items.Add(alwaysOnTop);
            var reset = new MenuItem { Header = "回到屏幕右下角" };
            reset.Click += delegate { ResetPosition(); };
            menu.Items.Add(reset);
            menu.Items.Add(new Separator());
            var exit = new MenuItem { Header = "退出桌宠" };
            exit.Click += delegate { Close(); };
            menu.Items.Add(exit);
            return menu;
        }

        private Window MakeSettingsWindow(string title,UIElement content,double width)
        {
            var window=new Window{Title=title,Width=width,SizeToContent=SizeToContent.Height,MaxHeight=GetWorkingArea().Height-40,
                WindowStyle=WindowStyle.ToolWindow,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Owner=this,
                WindowStartupLocation=WindowStartupLocation.Manual,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13,
                Background=Brushes.White,Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
            window.Loaded+=delegate {
                var area=GetWorkingArea();double left=Left-window.ActualWidth-18;
                if(left<area.Left)left=Left+Width+18;
                window.Left=Math.Max(area.Left,Math.Min(left,area.Right-window.ActualWidth));
                window.Top=Math.Max(area.Top,Math.Min(Top,area.Bottom-window.ActualHeight));
            };
            window.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){window.Close();e.Handled=true;}};
            return window;
        }
        private void ShowSizeWindow()
        {
            if(sizeWindow!=null){sizeWindow.Activate();return;}
            var panel=new StackPanel{Margin=new Thickness(22)};
            sizeRow=new NumericSettingRow("桌宠大小","%",MinimumPetHeight/DefaultPetHeight*100,GetMaximumPetHeight()/DefaultPetHeight*100,petHeight/DefaultPetHeight*100);
            sizeRow.ValueChanged+=delegate(double value){ResizePet(value/100*DefaultPetHeight);};panel.Children.Add(sizeRow);
            panel.Children.Add(new TextBlock{Text="拖动滑块即时生效；输入百分比后按回车或移开焦点。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,14)});
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            var reset=new Button{Content="恢复 100%",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,10,0)};
            reset.Click+=delegate{ResizePet(DefaultPetHeight);};buttons.Children.Add(reset);
            var done=new Button{Content="完成",Padding=new Thickness(18,6,18,6)};
            done.Click+=delegate{if(sizeRow.Commit())sizeWindow.Close();};buttons.Children.Add(done);panel.Children.Add(buttons);
            sizeWindow=MakeSettingsWindow("调节桌宠大小",panel,410);
            sizeWindow.Closed+=delegate{sizeSaveTimer.Stop();SaveSettings();sizeWindow=null;sizeRow=null;};
            sizeWindow.Show();
        }
        private void ShowSpeedWindow()
        {
            if(speedWindow!=null){speedWindow.Activate();return;}
            var panel=new StackPanel{Margin=new Thickness(22)};
            panel.Children.Add(new TextBlock{Text="动画播放倍率",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
            panel.Children.Add(new TextBlock{Text="1 为原速度，2 为两倍速度。范围 0.25–5 倍。\n滑块即时生效；输入后按回车或移开焦点。",Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,16),TextWrapping=TextWrapping.Wrap});
            string[] names={"转头跟随","眨眼动作","待机呼吸","拎起过渡","落地过渡","悬空晃动","点击反馈"};
            for(int i=0;i<names.Length;i++)
            {
                AnimationKind kind=(AnimationKind)i;
                var row=new NumericSettingRow(names[i],"倍",AnimationRates.Minimum,AnimationRates.Maximum,rates[kind]);
                speedRows[i]=row;row.ValueChanged+=delegate(double value){rates[kind]=value;SaveSettings();};panel.Children.Add(row);
            }
            panel.Children.Add(new TextBlock{Text="眨眼间隔和静止 5 秒回正的等待时间不受倍率影响。",FontSize=12,Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            var reset=new Button{Content="全部恢复 1 倍",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,10,0)};
            reset.Click+=delegate{for(int i=0;i<7;i++){rates[(AnimationKind)i]=1;speedRows[i].SetValue(1);}SaveSettings();};buttons.Children.Add(reset);
            var done=new Button{Content="完成",Padding=new Thickness(18,6,18,6)};
            done.Click+=delegate{bool valid=true;foreach(var row in speedRows)if(!row.Commit())valid=false;if(valid)speedWindow.Close();};buttons.Children.Add(done);panel.Children.Add(buttons);
            speedWindow=MakeSettingsWindow("调节动画速度",panel,450);
            speedWindow.Closed+=delegate{SaveSettings();speedWindow=null;};speedWindow.Show();
        }

        private double GetMaximumPetHeight()
        {
            var area = GetWorkingArea();
            double fitWidth = (area.Width - 16) * PetSpriteView.CharacterHeight / PetSpriteView.SceneWidth;
            double fitHeight = (area.Height - 16) * PetSpriteView.CharacterHeight / PetSpriteView.SceneHeight;
            return Math.Max(MinimumPetHeight, Math.Min(MaximumPetHeight, Math.Min(fitHeight, fitWidth)));
        }

        private void ResizePet(double height)
        {
            if (!IsFinite(height)) return;
            double bottom = Top + Height - FootInset;
            double center = Left + Width / 2;
            petHeight = Math.Max(MinimumPetHeight, Math.Min(GetMaximumPetHeight(), height));
            ApplySize();
            if (IsFinite(center) && IsFinite(bottom)) { Left = center - Width / 2; Top = bottom - Height + FootInset; }
            KeepOnScreen();
            UpdateSizeControls();
            if (ready) { sizeSaveTimer.Stop(); sizeSaveTimer.Start(); }
        }

        private void UpdateSizeControls()
        {
            if(sizeRow==null)return;
            sizeRow.Maximum=GetMaximumPetHeight()/DefaultPetHeight*100;
            sizeRow.SetValue(petHeight/DefaultPetHeight*100);
        }

        private void ApplySize()
        {
            Height = petHeight * PetSpriteView.SceneHeight / PetSpriteView.CharacterHeight + 16;
            Width = petHeight * PetSpriteView.SceneWidth / PetSpriteView.CharacterHeight + 16;
        }

        private double FootInset { get { return 8 + petHeight * (PetSpriteView.SceneHeight - PetSpriteView.Ground) / PetSpriteView.CharacterHeight; } }

        private void ResetPosition()
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 28;
            Top = area.Bottom - Height - 16;
            KeepOnScreen();
            Show();
            SaveSettings();
        }

        private Rect GetWorkingArea()
        {
            var source = PresentationSource.FromVisual(this);
            if (source == null || source.CompositionTarget == null || !IsFinite(Left) || !IsFinite(Top)) return SystemParameters.WorkArea;
            var toPixels = source.CompositionTarget.TransformToDevice;
            var fromPixels = source.CompositionTarget.TransformFromDevice;
            var center = toPixels.Transform(new Point(Left + Width / 2, Top + Height / 2));
            var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
            var bounds = screen.WorkingArea;
            var topLeft = fromPixels.Transform(new Point(bounds.Left, bounds.Top));
            var bottomRight = fromPixels.Transform(new Point(bounds.Right, bounds.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        private void KeepOnScreen()
        {
            var area = GetWorkingArea();
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
        }

        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private void ReadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath)) return;
                foreach (string line in File.ReadAllLines(settingsPath))
                {
                    string[] pair = line.Split('=');
                    if (pair.Length != 2) continue;
                    double value;
                    if (!double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !IsFinite(value)) continue;
                    if(rates.Read(pair[0],value))continue;
                    switch (pair[0])
                    {
                        case "left": savedLeft = value; break;
                        case "top": savedTop = value; break;
                        case "height": petHeight = Math.Max(MinimumPetHeight, Math.Min(MaximumPetHeight, value)); break;
                        case "topmost": Topmost = value != 0; break;
                        case "layout": settingsUseAnimationLayout = value >= 3; break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void SaveSettings()
        {
            if (!ready) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                var lines = new List<string>
                {
                    "left=" + Left.ToString("R", CultureInfo.InvariantCulture),
                    "top=" + Top.ToString("R", CultureInfo.InvariantCulture),
                    "height=" + petHeight.ToString("R", CultureInfo.InvariantCulture),
                    "topmost=" + (Topmost ? "1" : "0"),
                    "layout=3"
                };
                lines.AddRange(rates.ToLines());
                File.WriteAllLines(settingsPath,lines);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
