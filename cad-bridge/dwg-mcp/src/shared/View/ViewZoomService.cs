using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace Bimwright.Dwg.Plugin.View
{
    internal static class ViewZoomService
    {
        internal static ViewInfo ZoomExtents(Document doc)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            Point3d min = db.Extmin;
            Point3d max = db.Extmax;

            return SetView(ed, min, max, 100d);
        }

        internal static ViewInfo ZoomWindow(Document doc, Point3d corner1, Point3d corner2)
        {
            var ed = doc.Editor;

            double minX = Math.Min(corner1.X, corner2.X);
            double maxX = Math.Max(corner1.X, corner2.X);
            double minY = Math.Min(corner1.Y, corner2.Y);
            double maxY = Math.Max(corner1.Y, corner2.Y);
            double minZ = Math.Min(corner1.Z, corner2.Z);
            double maxZ = Math.Max(corner1.Z, corner2.Z);

            return SetView(ed, new Point3d(minX, minY, minZ), new Point3d(maxX, maxY, maxZ));
        }

        internal static ViewInfo ZoomToEntity(Document doc, Entity entity)
        {
            var ed = doc.Editor;
            Extents3d ext = entity.GeometricExtents;

            return SetView(ed, ext.MinPoint, ext.MaxPoint);
        }

        private static ViewInfo SetView(Editor ed, Point3d minimum, Point3d maximum, double zeroSpan = 1d)
        {
            using (var view = ed.GetCurrentView())
            {
                if (view.PerspectiveEnabled)
                    throw new InvalidOperationException("Zoom fitting requires an orthographic view.");
                var fit = ViewZoomMath.Fit(
                    new[] { minimum.X, minimum.Y, minimum.Z }, new[] { maximum.X, maximum.Y, maximum.Z },
                    new[] { view.Target.X, view.Target.Y, view.Target.Z },
                    new[] { view.ViewDirection.X, view.ViewDirection.Y, view.ViewDirection.Z },
                    view.ViewTwist, view.Width / view.Height, zeroSpan);
                Point2d center2d = new Point2d(fit.CenterX, fit.CenterY);

                view.CenterPoint = center2d;
                view.Height = fit.Height;
                view.Width = fit.Width;

                ed.SetCurrentView(view);

                return new ViewInfo(
                    center: new double[] { center2d.X, center2d.Y },
                    width: fit.Width,
                    height: fit.Height,
                    target: new double[] { view.Target.X, view.Target.Y, view.Target.Z }
                );
            }
        }
    }

    internal class ViewInfo
    {
        public ViewInfo(double[] center, double width, double height, double[] target)
        {
            Center = center;
            Width = width;
            Height = height;
            Target = target;
        }

        public double[] Center { get; }
        public double Width { get; }
        public double Height { get; }
        public double[] Target { get; }
    }
}
