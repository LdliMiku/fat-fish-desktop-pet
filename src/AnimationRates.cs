using System;
using System.Globalization;
namespace FatFishPet
{
    internal enum AnimationKind { Turn, Blink, Breath, Pickup, Landing, Sway, Click, IdleLook, HeadPet, Hearts }
    internal sealed class AnimationRates
    {
        public const double Minimum=.25, Maximum=5;
        public static readonly string[] Names={"转头跟随","眨眼动作","待机呼吸","拎起过渡","落地过渡","悬空晃动","点击反馈","左右张望","摸头回应","摸头爱心"};
        private readonly double[] values={1,1,1,1,1,1,1,1,1,1};
        public bool Read(string key,double value)
        {
            if(!key.StartsWith("speed_",StringComparison.Ordinal))return false;
            AnimationKind kind;
            if(Enum.TryParse<AnimationKind>(key.Substring(6),out kind)&&Enum.IsDefined(typeof(AnimationKind),kind))this[kind]=value;
            return true;
        }
        public string[] ToLines()
        {
            var lines=new string[values.Length];
            for(int i=0;i<values.Length;i++)lines[i]="speed_"+((AnimationKind)i)+"="+values[i].ToString("R",CultureInfo.InvariantCulture);
            return lines;
        }
        public double this[AnimationKind kind]
        {
            get { return values[(int)kind]; }
            set { if(!double.IsNaN(value)&&!double.IsInfinity(value))values[(int)kind]=Math.Max(Minimum,Math.Min(Maximum,value)); }
        }
    }
}
