using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace FatFishPet
{
    internal sealed class NumericSettingRow : StackPanel
    {
        private readonly Slider slider;
        private readonly TextBox input;
        private readonly TextBlock error;
        private bool updating;
        public event Action<double> ValueChanged;
        public double Value { get { return slider.Value; } }
        public double Maximum { get { return slider.Maximum; } set { slider.Maximum=value; } }
        public NumericSettingRow(string title,string unit,double minimum,double maximum,double value)
        {
            Margin=new Thickness(0,0,0,12);
            Children.Add(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,5)});
            var line=new DockPanel();
            var suffix=new TextBlock{Text=unit,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,0,0)};
            DockPanel.SetDock(suffix,Dock.Right);line.Children.Add(suffix);
            input=new TextBox{Width=78,Padding=new Thickness(5),VerticalContentAlignment=VerticalAlignment.Center};
            DockPanel.SetDock(input,Dock.Right);line.Children.Add(input);
            slider=new Slider{Minimum=minimum,Maximum=maximum,Value=value,IsSnapToTickEnabled=false,IsMoveToPointEnabled=true,
                SmallChange=unit=="%"?1:.05,LargeChange=unit=="%"?10:.25,Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
            line.Children.Add(slider);Children.Add(line);
            error=new TextBlock{Foreground=Brushes.Firebrick,FontSize=11,Visibility=Visibility.Collapsed,Margin=new Thickness(0,3,0,0)};Children.Add(error);
            System.Windows.Automation.AutomationProperties.SetName(input,title+"输入");
            System.Windows.Automation.AutomationProperties.SetName(slider,title+"滑块");
            slider.ValueChanged+=delegate { if(!updating){RefreshText();var handler=ValueChanged;if(handler!=null)handler(Value);} };
            input.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){Commit();e.Handled=true;}else if(e.Key==Key.Escape){RefreshText();e.Handled=true;}};
            input.LostKeyboardFocus+=delegate { Commit(); };
            RefreshText();
        }
        internal static bool TryValue(string text,double minimum,double maximum,out double value)
        {
            text=(text??"").Trim().TrimEnd('%','×','x','X').Trim();
            bool parsed=double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out value)||double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value);
            return parsed&&!double.IsNaN(value)&&!double.IsInfinity(value)&&value>=minimum&&value<=maximum;
        }
        public bool Commit()
        {
            double value;
            if(!TryValue(input.Text,slider.Minimum,slider.Maximum,out value))
            {
                error.Text=string.Format("请输入 {0:0.##}–{1:0.##} 之间的数值",slider.Minimum,slider.Maximum);error.Visibility=Visibility.Visible;return false;
            }
            SetValue(value);var handler=ValueChanged;if(handler!=null)handler(Value);return true;
        }
        public void SetValue(double value)
        {
            updating=true;try{slider.Value=Math.Max(slider.Minimum,Math.Min(slider.Maximum,value));RefreshText();}finally{updating=false;}
        }
        private void RefreshText(){input.Text=Value.ToString("0.###",CultureInfo.CurrentCulture);error.Visibility=Visibility.Collapsed;}
    }
}
