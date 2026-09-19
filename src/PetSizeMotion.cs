using System;
using System.Windows;

namespace FatFishPet
{
    // Size is animated inside one persistent layered-window surface.
    internal sealed class PetSizeMotion
    {
        internal const double SurfaceWidth = 696, SurfaceHeight = 736;
        internal const double AnchorX = SurfaceWidth / 2, AnchorY = 688;
        public double Current { get; private set; }
        public double Target { get; private set; }
        public bool IsMoving { get { return Current != Target; } }
        public PetSizeMotion(double initial) { Current = Target = initial; }
        public void SetTarget(double value) { Target = value; }
        public void Snap(double value) { Current = Target = value; }
        public void Advance(double seconds)
        {
            if (!IsMoving || seconds <= 0) return;
            double amount = 1 - Math.Exp(-Math.Min(seconds, .05) / .065);
            Current += (Target - Current) * amount;
            if (Math.Abs(Target - Current) < .02) Current = Target;
        }
        internal static Rect Bounds(double height)
        {
            double scale = height / 300;
            return new Rect(AnchorX - 170 * scale - 8, AnchorY - 340 * scale - 8,
                340 * scale + 16, 360 * scale + 16);
        }
        internal static Point ClampPosition(Point position, Rect workArea, double height)
        {
            Rect visible = Bounds(height);
            return new Point(
                Math.Max(workArea.Left - visible.Left, Math.Min(position.X, workArea.Right - visible.Right)),
                Math.Max(workArea.Top - visible.Top, Math.Min(position.Y, workArea.Bottom - visible.Bottom)));
        }
    }
}
