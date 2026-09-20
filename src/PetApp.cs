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
[assembly: AssemblyVersion("0.2.0.0")]

namespace FatFishPet
{
    internal static class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerState { public uint Version, ControlMask, StateMask; }
        [DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool GetProcessInformation(IntPtr process,int kind,ref PowerState state,uint size);
        [DllImport("kernel32.dll",SetLastError=true)]
        private static extern bool SetProcessInformation(IntPtr process,int kind,ref PowerState state,uint size);
        private static void ConfigureAnimationScheduling()
        {
            // A visible animated pet is latency-sensitive even when another window has focus.
            // Keep normal process priority; explicitly opt out of execution-speed power throttling.
            try
            {
                using(var process=Process.GetCurrentProcess())
                {
                    var state=new PowerState{Version=1};
                    if(!GetProcessInformation(process.Handle,4,ref state,12))return;
                    state.ControlMask|=1;state.StateMask&=~1u;
                    SetProcessInformation(process.Handle,4,ref state,12);
                }
            }
            catch(EntryPointNotFoundException) { } // Older Windows keeps its original scheduling.
        }
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (var instance = new Mutex(true, "Local\\FatFishPet-v1", out created))
            {
                if (!created) return;
                try
                {
                    ConfigureAnimationScheduling();
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
        private readonly PetSizeMotion sizeMotion;
        private readonly MatrixTransform sizeTransform = new MatrixTransform();
        private TimeSpan lastSizeFrame = TimeSpan.MinValue;
        private readonly ScaleTransform scale = new ScaleTransform(1, 1, 170, 340);
        private readonly Forms.NotifyIcon tray;
        private readonly DispatcherTimer sizeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        private Window sizeWindow;
        private NumericSettingRow sizeRow;
        private Window speedWindow;
        private Window idleWindow;
        private NumericSettingRow idleRow;
        private Window talkWindow;
        private NumericSettingRow talkRow;
        private readonly ChatEngine chat = new ChatEngine();
        private readonly PetBubble bubble = new PetBubble();
        private bool bubbleEnabled = true;
        private readonly PetTalk talk = new PetTalk();
        private readonly Stopwatch talkClock = Stopwatch.StartNew();
        private DispatcherTimer talkTimer;
        private Window chatWindow, chatConfigWindow;
        private StackPanel chatLog;
        private TextBox chatInput, chatBaseUrl, chatModel;
        private PasswordBox chatKey;
        private TextBlock chatStatus;
        private bool chatBusy;
        private readonly AnimationRates rates=new AnimationRates();
        private readonly IdleLook idleLook = new IdleLook();
        private bool menuOpen, trayMenuOpen;
        private readonly NumericSettingRow[] speedRows=new NumericSettingRow[AnimationRates.Names.Length];
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
            chat.Initialize(AppDomain.CurrentDomain.BaseDirectory);
            sizeMotion = new PetSizeMotion(petHeight);
            Width = PetSizeMotion.SurfaceWidth;
            Height = PetSizeMotion.SurfaceHeight;

            var transform = new TransformGroup();
            transform.Children.Add(scale);
            transform.Children.Add(sizeTransform);
            var iconSprite = PetSpriteView.LoadBitmap("PetSprite");
            Icon = BitmapFrame.Create(iconSprite);
            pet = new PetSpriteView
            {
                DiagnosticPath = animationDiagnostics ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "animation-performance.txt") : null,
                Width = PetSpriteView.SceneWidth,
                Height = PetSpriteView.SceneHeight,
                Cursor = Cursors.Hand,
                RenderTransform = transform,
                UseLayoutRounding = false
            };
            System.Windows.Automation.AutomationProperties.SetName(pet, "大肥鱼，按住左键拖动，右键打开菜单");
            System.Windows.Automation.AutomationProperties.SetHelpText(pet, "按住左键拖动 · 滚轮调节大小 · 右键打开菜单");
            RenderOptions.SetBitmapScalingMode(pet, BitmapScalingMode.HighQuality);
            var surface = new Canvas { Background = null, ClipToBounds = false };
            surface.Children.Add(pet);
            Content = surface;
            pet.CursorOffsetProvider = GetCursorOffset;
            pet.Rates=rates;
            pet.Idle=idleLook;
            bubble.Enabled=bubbleEnabled;
            bubble.AnchorProvider=delegate { Rect head=PetSizeMotion.Bounds(sizeMotion.Current); return new Rect(Left+head.Left,Top+head.Top,head.Width,head.Height); };
            bubble.OpenChatRequested+=delegate { ShowChatWindow(); };
            pet.Petted+=delegate { ShowTalkLine(talk.React("petted",talkClock.Elapsed.TotalSeconds)); };
            pet.InteractionBlockedProvider=delegate { return pointerPressed || menuOpen || trayMenuOpen || sizeWindow != null || speedWindow != null; };
            ApplyVisualSize();
            if (IsFinite(savedLeft) && IsFinite(savedTop))
            {
                // Persist layout-3 visible bounds, not the larger transparent surface.
                double oldWidth = settingsUseAnimationLayout ? petHeight * PetSpriteView.SceneWidth / PetSpriteView.CharacterHeight + 16
                    : petHeight * iconSprite.PixelWidth / iconSprite.PixelHeight + 16;
                double oldGround = 8 + petHeight * (settingsUseAnimationLayout ? PetSpriteView.Ground / PetSpriteView.CharacterHeight : 1);
                savedLeft += oldWidth / 2 - PetSizeMotion.AnchorX;
                savedTop += oldGround - PetSizeMotion.AnchorY;
            }
            MouseLeftButtonDown += OnPetPressed;
            MouseMove += OnPetMoved;
            MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { if (pointerPressed) { FinishPointerInteraction(true); e.Handled = true; } };
            LostMouseCapture += delegate { if (pointerPressed) FinishPointerInteraction(false); };
            MouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (!ready || pointerPressed || e.Delta == 0) return;
                NotifyInteraction();
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
            trayMenu.Opened += delegate { trayMenuOpen=true; NotifyInteraction(); };
            trayMenu.Closed += delegate { trayMenuOpen=false; NotifyInteraction(); };
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
                CompositionTarget.Rendering += OnSizeFrame;
                SaveSettings();
                talkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                talkTimer.Tick += delegate
                {
                    if (closing) return;
                    ShowTalkLine(talk.Tick(talkClock.Elapsed.TotalSeconds, DateTime.Now, chatBusy));
                };
                talkTimer.Start();
            };
            Closed += delegate
            {
                closing = true;
                if (talkTimer != null) talkTimer.Stop();
                bubble.Close();
                CompositionTarget.Rendering -= OnSizeFrame;
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
        private Vector lastGazeOffset;

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
                NotifyInteraction();
            }
            if (sizeMotion.IsMoving)
            {
                // Changing scale moves local coordinates even when the mouse is still.
                // Reset gesture accumulation so zooming cannot impersonate a head stroke.
                pet.SampleHeadPointer(0,0,false);
                return lastGazeOffset;
            }
            Point local = pet.PointFromScreen(new Point(cursor.X, cursor.Y));
            pet.SampleHeadPointer(local.X / pet.ActualWidth * PetSpriteView.SceneWidth,
                local.Y / pet.ActualHeight * PetSpriteView.SceneHeight,
                IsMouseOver && Mouse.LeftButton == MouseButtonState.Released && Mouse.RightButton == MouseButtonState.Released);
            if (cursorIdleClock.Elapsed.TotalSeconds >= 5) return lastGazeOffset = new Vector(0, 0);
            return lastGazeOffset = new Vector(local.X / pet.ActualWidth * PetSpriteView.SceneWidth - PetSpriteView.SceneWidth / 2,
                local.Y / pet.ActualHeight * PetSpriteView.SceneHeight - 170);
        }

        private void OnPetPressed(object sender, MouseButtonEventArgs e)
        {
            if (!ready || pointerPressed || e.ChangedButton != MouseButton.Left) return;
            if (!GetCursorPos(out pressCursor)) return;
            if (!CaptureMouse()) return;
            if (sizeMotion.IsMoving)
            {
                petHeight = sizeMotion.Current;
                sizeMotion.Snap(petHeight);
                UpdateSizeControls();
            }
            e.Handled = true;
            pointerPressed = true;
            NotifyInteraction();
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
                ShowTalkLine(talk.React("lifted", talkClock.Elapsed.TotalSeconds));
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
            NotifyInteraction();
            pointerPressed = false;
            dragging = false;
            dragClock.Stop();
            if (IsMouseCaptured) ReleaseMouseCapture();
            pet.Cursor = Cursors.Hand;
            if (wasDragging)
            {
                pet.EndLift();
                ShowTalkLine(talk.React("landed", talkClock.Elapsed.TotalSeconds));
            }
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
            ShowTalkLine(talk.React("clicked", talkClock.Elapsed.TotalSeconds));
        }

        // 用户和桌宠互动：既通知动画计时，也让主动搭话知道有人在用电脑。
        private void NotifyInteraction()
        {
            pet.NotifyInteraction();
            talk.NotifyInteraction(talkClock.Elapsed.TotalSeconds);
        }

        private void ShowTalkLine(string line)
        {
            if (string.IsNullOrEmpty(line) || !bubble.Enabled) return;
            bubble.Show(line);
        }

        private ContextMenu CreateMenu()
        {
            var menu = new ContextMenu { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 13 };
            menu.Opened += delegate { menuOpen=true; NotifyInteraction(); };
            menu.Closed += delegate { menuOpen=false; NotifyInteraction(); };
            menu.Items.Add(new MenuItem { Header = "大肥鱼 · 桌面小伙伴", IsEnabled = false });
            menu.Items.Add(new Separator());
            var size = new MenuItem { Header = "调节大小…" };
            size.Click += delegate { ShowSizeWindow(); };
            menu.Items.Add(size);
            var speeds=new MenuItem{Header="调节动画速度…"};
            speeds.Click+=delegate { ShowSpeedWindow(); };menu.Items.Add(speeds);
            var idle = new MenuItem { Header="自主小动作（左右张望）", IsCheckable=true, IsChecked=idleLook.Enabled };
            idle.Click += delegate { idleLook.Enabled=idle.IsChecked; NotifyInteraction(); SaveSettings(); };
            menu.Items.Add(idle);
            var lookInterval = new MenuItem { Header="调节张望间隔…" };
            lookInterval.Click += delegate { ShowIdleLookWindow(); };
            menu.Items.Add(lookInterval);
            var chatMenu = new MenuItem { Header="和我说话…" };
            chatMenu.Click += delegate { ShowChatWindow(); };
            menu.Items.Add(chatMenu);
            var bubbleItem = new MenuItem { Header="显示对话气泡", IsCheckable=true, IsChecked=bubbleEnabled };
            bubbleItem.Click += delegate {
                bubbleEnabled=bubbleItem.IsChecked;bubble.Enabled=bubbleItem.IsChecked;
                if(!bubbleItem.IsChecked)bubble.Hide();
                NotifyInteraction();SaveSettings();
            };
            menu.Items.Add(bubbleItem);
            var talkItem = new MenuItem { Header="主动搭话", IsCheckable=true, IsChecked=talk.Enabled };
            talkItem.Click += delegate {
                talk.Enabled=talkItem.IsChecked;
                NotifyInteraction();SaveSettings();
            };
            menu.Items.Add(talkItem);
            var talkGap = new MenuItem { Header="搭话间隔…" };
            talkGap.Click += delegate { ShowTalkWindow(); };
            menu.Items.Add(talkGap);
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
                Rect visible=PetSizeMotion.Bounds(sizeMotion.Current);
                var area=GetWorkingArea();double left=Left+visible.Left-window.ActualWidth-18;
                if(left<area.Left)left=Left+visible.Right+18;
                window.Left=Math.Max(area.Left,Math.Min(left,area.Right-window.ActualWidth));
                window.Top=Math.Max(area.Top,Math.Min(Top+visible.Top,area.Bottom-window.ActualHeight));
            };
            window.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){window.Close();e.Handled=true;}};
            window.Closed+=delegate { NotifyInteraction(); };
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
            string[] names=AnimationRates.Names;
            for(int i=0;i<names.Length;i++)
            {
                AnimationKind kind=(AnimationKind)i;
                var row=new NumericSettingRow(names[i],"倍",AnimationRates.Minimum,AnimationRates.Maximum,rates[kind]);
                speedRows[i]=row;row.ValueChanged+=delegate(double value){rates[kind]=value;SaveSettings();};panel.Children.Add(row);
            }
            panel.Children.Add(new TextBlock{Text="眨眼间隔和静止 5 秒回正的等待时间不受倍率影响。",FontSize=12,Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            var reset=new Button{Content="全部恢复 1 倍",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,10,0)};
            reset.Click+=delegate{for(int i=0;i<speedRows.Length;i++){rates[(AnimationKind)i]=1;speedRows[i].SetValue(1);}SaveSettings();};buttons.Children.Add(reset);
            var done=new Button{Content="完成",Padding=new Thickness(18,6,18,6)};
            done.Click+=delegate{bool valid=true;foreach(var row in speedRows)if(!row.Commit())valid=false;if(valid)speedWindow.Close();};buttons.Children.Add(done);panel.Children.Add(buttons);
            speedWindow=MakeSettingsWindow("调节动画速度",panel,450);
            speedWindow.Closed+=delegate{SaveSettings();speedWindow=null;};speedWindow.Show();
        }

        private void ShowIdleLookWindow()
        {
            if(idleWindow!=null){idleWindow.Activate();return;}
            var panel=new StackPanel{Margin=new Thickness(22)};
            panel.Children.Add(new TextBlock{Text="左右张望间隔",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
            panel.Children.Add(new TextBlock{Text="鼠标和交互安静多久后，偶尔看看两边再回正。范围 10–30 秒，默认 20 秒。\n每次实际等待会在设定值附近小幅浮动；滑块即时生效，输入后按回车或移开焦点。",Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
            idleRow=new NumericSettingRow("张望间隔","秒",IdleLook.MinimumInterval,IdleLook.MaximumInterval,idleLook.Interval,1);
            idleRow.ValueChanged+=delegate(double value){idleLook.Interval=value;NotifyInteraction();SaveSettings();};
            panel.Children.Add(idleRow);
            panel.Children.Add(new TextBlock{Text="移动鼠标、点击或拖动会重新开始等待；「调节动画速度…」里的「左右张望」倍率只控制转头快慢，不影响这个间隔。",FontSize=12,Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            var reset=new Button{Content="恢复 20 秒",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,10,0)};
            reset.Click+=delegate{idleRow.SetValue(IdleLook.DefaultInterval);};buttons.Children.Add(reset);
            var done=new Button{Content="完成",Padding=new Thickness(18,6,18,6)};
            done.Click+=delegate{if(idleRow.Commit())idleWindow.Close();};buttons.Children.Add(done);panel.Children.Add(buttons);
            idleWindow=MakeSettingsWindow("调节张望间隔",panel,430);
            idleWindow.Closed+=delegate{SaveSettings();idleWindow=null;idleRow=null;};idleWindow.Show();
        }

        private void ShowTalkWindow()
        {
            if(talkWindow!=null){talkWindow.Activate();return;}
            var panel=new StackPanel{Margin=new Thickness(22)};
            panel.Children.Add(new TextBlock{Text="主动搭话间隔",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
            panel.Children.Add(new TextBlock{Text="她隔多久主动跟你说一句闲话。范围 5–90 分钟，默认 30 分钟。\n每次实际间隔在设定值附近浮动（0.6–1.4 倍）；久坐提醒（约 45 分钟没互动）和早晚问候不受这个间隔限制。",
                Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
            talkRow=new NumericSettingRow("搭话间隔","分钟",PetTalk.MinimumIntervalMinutes,PetTalk.MaximumIntervalMinutes,talk.IntervalMinutes,5);
            talkRow.ValueChanged+=delegate(double value){talk.IntervalMinutes=value;SaveSettings();};
            panel.Children.Add(talkRow);
            panel.Children.Add(new TextBlock{Text="台词全部是本地的，不联网、不花钱；每天最多 16 次随机闲聊。右键「主动搭话」可以整体关闭。",
                FontSize=12,Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            var reset=new Button{Content="恢复 30 分钟",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,10,0)};
            reset.Click+=delegate{talkRow.SetValue(PetTalk.DefaultIntervalMinutes);};buttons.Children.Add(reset);
            var done=new Button{Content="完成",Padding=new Thickness(18,6,18,6)};
            done.Click+=delegate{if(talkRow.Commit())talkWindow.Close();};buttons.Children.Add(done);panel.Children.Add(buttons);
            talkWindow=MakeSettingsWindow("主动搭话间隔",panel,440);
            talkWindow.Closed+=delegate{SaveSettings();talkWindow=null;talkRow=null;};talkWindow.Show();
        }

        private void ShowChatWindow()
        {
            if(chatWindow!=null){chatWindow.Activate();if(chatInput!=null)chatInput.Focus();return;}
            var panel=new DockPanel{Margin=new Thickness(10,10,10,8)};
            var bottom=new StackPanel{Margin=new Thickness(0,8,0,0)};
            DockPanel.SetDock(bottom,Dock.Bottom);panel.Children.Add(bottom);
            chatLog=new StackPanel();
            panel.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
                Content=chatLog,Padding=new Thickness(10,10,10,6),Background=new SolidColorBrush(Color.FromRgb(250,250,252))});
            var inputRow=new DockPanel();
            var send=new Button{Content="发送",Padding=new Thickness(16,6,16,6)};
            DockPanel.SetDock(send,Dock.Right);inputRow.Children.Add(send);
            chatInput=new TextBox{Padding=new Thickness(6),VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};
            chatInput.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){SendChat();e.Handled=true;}};
            inputRow.Children.Add(chatInput);bottom.Children.Add(inputRow);
            var buttonRow=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,0,8)};
            var config=new Button{Content="接口设置",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,0)};
            config.Click+=delegate{ShowChatConfigWindow();};buttonRow.Children.Add(config);
            var clear=new Button{Content="清空记录",Padding=new Thickness(10,5,10,5)};
            clear.Click+=delegate{chat.ClearHistory();chatLog.Children.Clear();SetChatStatus("已清空本地聊天记录（data/chat-history.jsonl）。");};buttonRow.Children.Add(clear);
            bottom.Children.Add(buttonRow);
            chatStatus=new TextBlock{Foreground=Brushes.DimGray,FontSize=12,TextWrapping=TextWrapping.Wrap};
            bottom.Children.Add(chatStatus);
            chatWindow=new Window{Title="和大肥鱼说话",Width=440,Height=560,MinWidth=340,MinHeight=360,
                WindowStyle=WindowStyle.ToolWindow,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=true,Topmost=true,Owner=this,
                WindowStartupLocation=WindowStartupLocation.Manual,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13,
                Background=Brushes.White,Content=panel};
            chatWindow.Loaded+=delegate {
                Rect visible=PetSizeMotion.Bounds(sizeMotion.Current);
                var area=GetWorkingArea();double left=Left+visible.Left-chatWindow.ActualWidth-18;
                if(left<area.Left)left=Left+visible.Right+18;
                chatWindow.Left=Math.Max(area.Left,Math.Min(left,area.Right-chatWindow.ActualWidth));
                chatWindow.Top=Math.Max(area.Top,Math.Min(Top+visible.Top,area.Bottom-chatWindow.ActualHeight));
            };
            chatWindow.Closed+=delegate{chatWindow=null;chatLog=null;chatInput=null;chatStatus=null;NotifyInteraction();};
            send.Click+=delegate{SendChat();};
            foreach(ChatTurn turn in chat.History.Turns) AddChatBubble(turn.Role,turn.Text);
            if(chat.History.Turns.Count==0)
            {
                SetChatStatus(chat.Config.HasKey?"把想说的话打进去，回车或点「发送」。回复来自 "+chat.Config.Model+"。"
                    :"还没有填 API Key：先点「接口设置」把 DeepSeek 的 Key 填进去，没有 Key 时只能用本地台词回应。");
            }
            else SetChatStatus("继续聊，或点「清空记录」重新开始。");
            chatWindow.Show();chatInput.Focus();
        }

        private void SendChat()
        {
            if(chatBusy||chatInput==null)return;
            string text=(chatInput.Text??"").Trim();
            if(text.Length==0)return;
            List<ChatTurn> context=chat.BuildContext();
            chatInput.Clear();
            AddChatBubble("user",text);
            chat.Record("user",text);
            chatBusy=true;SetChatStatus("大肥鱼正在想……");
            bubble.Show("……");
            ChatEngine engine=chat;
            ThreadPool.QueueUserWorkItem(delegate {
                ChatReply reply=engine.Send(context,text);
                Dispatcher.BeginInvoke(new Action(delegate {
                    chatBusy=false;
                    if(chatLog==null)return;
                    if(reply.Ok)
                    {
                        AddChatBubble("assistant",reply.Text);
                        chat.Record("assistant",reply.Text);
                        bubble.Show(reply.Text);
                    }
                    else
                    {
                        AddChatBubble("assistant","（这次没答上来："+reply.Error+"）");
                        bubble.Show("唔……这次没答上来。");
                    }
                    SetChatStatus(reply.Error);
                }));
            });
        }

        private void SetChatStatus(string text)
        {
            if(chatStatus!=null)chatStatus.Text=text==null?"":text;
        }

        private void AddChatBubble(string role,string text)
        {
            if(chatLog==null)return;
            bool me=role!="assistant";
            FrameworkElement content;
            if(me)
            {
                content=new Border{
                    Background=new SolidColorBrush(Color.FromRgb(213,232,255)),
                    BorderBrush=new SolidColorBrush(Color.FromRgb(178,206,238)),
                    BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(11,7,11,7),
                    MaxWidth=400,HorizontalAlignment=HorizontalAlignment.Right,
                    Child=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Foreground=Brushes.Black}
                };
            }
            else
            {
                // 她那一侧直接用桌面气泡的同一套手绘素材，和外面看到的一致；
                // 聊天里她在左侧，所以镜像一下让尾巴朝左。
                content=PetBubble.BuildContent(text,true);
                content.HorizontalAlignment=HorizontalAlignment.Left;
            }
            chatLog.Children.Add(new Border{Margin=me?new Thickness(0,0,6,4):new Thickness(6,0,0,4),
                HorizontalAlignment=me?HorizontalAlignment.Stretch:HorizontalAlignment.Left,Child=content});
            var parent=chatLog.Parent as ScrollViewer;
            if(parent!=null)parent.ScrollToEnd();
        }

        private void ShowChatConfigWindow()
        {
            if(chatConfigWindow!=null){chatConfigWindow.Activate();return;}
            var panel=new StackPanel{Margin=new Thickness(22)};
            panel.Children.Add(new TextBlock{Text="对话接口设置",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});
            panel.Children.Add(new TextBlock{Text="默认使用 DeepSeek 的 OpenAI 兼容接口；换其它服务商时改下面两项地址和模型名即可。\nAPI Key 用 Windows 的 DPAPI 加密后只保存在本机 data 目录，不写进 settings.txt，也不会被 git 跟踪。",
                Foreground=Brushes.DimGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
            panel.Children.Add(new TextBlock{Text="接口地址",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4)});
            chatBaseUrl=new TextBox{Text=chat.Config.BaseUrl,Padding=new Thickness(6),Margin=new Thickness(0,0,0,12)};
            panel.Children.Add(chatBaseUrl);
            panel.Children.Add(new TextBlock{Text="模型名",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4)});
            chatModel=new TextBox{Text=chat.Config.Model,Padding=new Thickness(6),Margin=new Thickness(0,0,0,12)};
            panel.Children.Add(chatModel);
            panel.Children.Add(new TextBlock{Text="API Key",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4)});
            chatKey=new PasswordBox{Padding=new Thickness(6),Password=chat.Config.ApiKey,Margin=new Thickness(0,0,0,12)};
            panel.Children.Add(chatKey);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            var reset=new Button{Content="恢复默认地址",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0)};
            reset.Click+=delegate{chatBaseUrl.Text=ChatConfig.DefaultBaseUrl;chatModel.Text=ChatConfig.DefaultModel;};buttons.Children.Add(reset);
            var clearKey=new Button{Content="清空 Key",Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0)};
            clearKey.Click+=delegate{chatKey.Password="";};buttons.Children.Add(clearKey);
            var done=new Button{Content="保存",Padding=new Thickness(18,6,18,6)};
            done.Click+=delegate{
                chat.Config.BaseUrl=(chatBaseUrl.Text??"").Trim();
                chat.Config.Model=(chatModel.Text??"").Trim();
                chat.Config.ApiKey=chatKey.Password;
                chat.SaveConfig();
                SetChatStatus(chat.Config.HasKey?"接口设置已保存，可以直接聊了。":"接口设置已保存（还没有填 Key，只会用本地台词）。");
                chatConfigWindow.Close();
            };
            buttons.Children.Add(done);panel.Children.Add(buttons);
            chatConfigWindow=MakeSettingsWindow("对话接口设置",panel,470);
            chatConfigWindow.Closed+=delegate{chatConfigWindow=null;chatBaseUrl=null;chatModel=null;chatKey=null;};
            chatConfigWindow.Show();
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
            petHeight = Math.Max(MinimumPetHeight, Math.Min(GetMaximumPetHeight(), height));
            if (!ready) { sizeMotion.Snap(petHeight); ApplyVisualSize(); }
            else sizeMotion.SetTarget(petHeight);
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

        private void OnSizeFrame(object sender, EventArgs args)
        {
            var frame = args as RenderingEventArgs;
            if (frame == null || frame.RenderingTime == lastSizeFrame) return;
            double delta = lastSizeFrame == TimeSpan.MinValue ? 1.0 / 60 : (frame.RenderingTime - lastSizeFrame).TotalSeconds;
            lastSizeFrame = frame.RenderingTime;
            if (!sizeMotion.IsMoving) return;
            sizeMotion.Advance(delta);
            ApplyVisualSize();
        }

        private void ApplyVisualSize()
        {
            double factor = sizeMotion.Current / PetSpriteView.CharacterHeight;
            sizeTransform.Matrix = new Matrix(factor, 0, 0, factor,
                PetSizeMotion.AnchorX - PetSpriteView.SceneWidth * factor / 2,
                PetSizeMotion.AnchorY - PetSpriteView.Ground * factor);
        }

        private void ResetPosition()
        {
            var area = SystemParameters.WorkArea;
            Rect visible = PetSizeMotion.Bounds(Math.Max(petHeight, sizeMotion.Current));
            Left = area.Right - visible.Right - 28;
            Top = area.Bottom - visible.Bottom - 16;
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
            Rect visible = PetSizeMotion.Bounds(sizeMotion.Current);
            var center = toPixels.Transform(new Point(Left + visible.Left + visible.Width / 2, Top + visible.Top + visible.Height / 2));
            var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
            var bounds = screen.WorkingArea;
            var topLeft = fromPixels.Transform(new Point(bounds.Left, bounds.Top));
            var bottomRight = fromPixels.Transform(new Point(bounds.Right, bounds.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        private void KeepOnScreen()
        {
            var area = GetWorkingArea();
            Point position = PetSizeMotion.ClampPosition(new Point(Left, Top), area, Math.Max(petHeight, sizeMotion.Current));
            Left = position.X;
            Top = position.Y;
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
                    if(rates.Read(pair[0],value)||idleLook.Read(pair[0],value)||talk.Read(pair[0],value))continue;
                    switch (pair[0])
                    {
                        case "left": savedLeft = value; break;
                        case "top": savedTop = value; break;
                        case "height": petHeight = Math.Max(MinimumPetHeight, Math.Min(MaximumPetHeight, value)); break;
                        case "topmost": Topmost = value != 0; break;
                        case "layout": settingsUseAnimationLayout = value >= 3; break;
                        case "bubbleEnabled": bubbleEnabled = value != 0; break;
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
                Rect visible = PetSizeMotion.Bounds(petHeight);
                var lines = new List<string>
                {
                    "left=" + (Left + visible.Left).ToString("R", CultureInfo.InvariantCulture),
                    "top=" + (Top + visible.Top).ToString("R", CultureInfo.InvariantCulture),
                    "height=" + petHeight.ToString("R", CultureInfo.InvariantCulture),
                    "topmost=" + (Topmost ? "1" : "0"),
                    "bubbleEnabled=" + (bubbleEnabled ? "1" : "0"),
                    "layout=3"
                };
                lines.AddRange(rates.ToLines());
                lines.AddRange(idleLook.ToLines());
                lines.AddRange(talk.ToLines());
                File.WriteAllLines(settingsPath,lines);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
