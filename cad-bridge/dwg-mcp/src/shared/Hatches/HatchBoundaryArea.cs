using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.Hatches
{
    internal static class HatchBoundaryArea
    {
        // Do not confuse an unavailable Hatch.Area cache with invalid geometry.
        // Reconstruct the actual loops, including islands, using the same native
        // Region modeler and validation as the clipping operation.
        internal static double Read(Hatch hatch)
        {
            var geometry = new HatchClipGeometry(hatch.GeometricExtents.MinPoint, Stopwatch.StartNew());
            var regions = geometry.HatchRegions(hatch, out _);
            try { return regions.Sum(region => region.Area); }
            finally { foreach (var region in regions) region.Dispose(); }
        }
    }
}
