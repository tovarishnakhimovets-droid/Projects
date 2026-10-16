using System;

namespace Bimwright.Dwg.Plugin.View
{
    // Host-free WCS to DCS fitting. The axes use Autodesk's arbitrary-axis
    // algorithm (1/64 polar cap); the inverse view rotation is +ViewTwist.
    internal static class ViewZoomMath
    {
        internal static (double X, double Y, double Z) Normalize(double x, double y, double z)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z))
                throw new ArgumentException("View direction must be finite and nonzero.");
            double scale = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
            if (scale == 0) throw new ArgumentException("View direction must be finite and nonzero.");
            x /= scale; y /= scale; z /= scale;
            double length = Math.Sqrt(x * x + y * y + z * z);
            return (x / length, y / length, z / length);
        }

        internal static (double CenterX, double CenterY, double Width, double Height) Fit(
            double[] minimum, double[] maximum, double[] target, double[] direction,
            double twist, double aspect, double zeroSpan = 1)
        {
            CheckPoint(minimum); CheckPoint(maximum); CheckPoint(target); CheckPoint(direction);
            if (!Finite(twist) || !Finite(aspect) || aspect <= 0 ||
                !Finite(zeroSpan) || zeroSpan <= 0)
                throw new ArgumentException("View twist, aspect ratio and zero-span fallback must be valid.");
            for (int i = 0; i < 3; i++)
                if (maximum[i] < minimum[i]) throw new ArgumentException("Drawing extents are invalid.");

            var n = Normalize(direction[0], direction[1], direction[2]);
            var x = Math.Abs(n.X) < 1d / 64 && Math.Abs(n.Y) < 1d / 64
                ? Normalize(n.Z, 0, -n.X) : Normalize(-n.Y, n.X, 0);
            var y = Normalize(n.Y * x.Z - n.Z * x.Y,
                n.Z * x.X - n.X * x.Z, n.X * x.Y - n.Y * x.X);
            double c = Math.Cos(twist), s = Math.Sin(twist);
            // Autodesk's DCS->WCS example rotates by -ViewTwist. Dotting with
            // those axes applies the inverse transform, including target offset.
            var xx = (X: x.X * c - y.X * s, Y: x.Y * c - y.Y * s, Z: x.Z * c - y.Z * s);
            var yy = (X: x.X * s + y.X * c, Y: x.Y * s + y.Y * c, Z: x.Z * s + y.Z * c);
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                double px = ((corner & 1) == 0 ? minimum[0] : maximum[0]) - target[0];
                double py = ((corner & 2) == 0 ? minimum[1] : maximum[1]) - target[1];
                double pz = ((corner & 4) == 0 ? minimum[2] : maximum[2]) - target[2];
                double dx = px * xx.X + py * xx.Y + pz * xx.Z;
                double dy = px * yy.X + py * yy.Y + pz * yy.Z;
                minX = Math.Min(minX, dx); maxX = Math.Max(maxX, dx);
                minY = Math.Min(minY, dy); maxY = Math.Max(maxY, dy);
            }
            double width = maxX - minX;
            double height = maxY - minY;
            if (width <= 0) width = zeroSpan;
            if (height <= 0) height = zeroSpan;
            if (width / height > aspect) height = width / aspect;
            else width = height * aspect;
            double centerX = minX + (maxX - minX) / 2;
            double centerY = minY + (maxY - minY) / 2;
            if (!Finite(centerX) || !Finite(centerY) || !Finite(width) || !Finite(height))
                throw new ArgumentException("Drawing extents exceed the supported view range.");
            return (centerX, centerY, width, height);
        }

        private static void CheckPoint(double[] point)
        {
            if (point == null || point.Length != 3 ||
                !Finite(point[0]) || !Finite(point[1]) || !Finite(point[2]))
                throw new ArgumentException("View coordinates must contain three finite values.");
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
