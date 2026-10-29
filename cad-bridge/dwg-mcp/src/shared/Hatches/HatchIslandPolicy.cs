using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.Hatches
{
    // Normal alternates islands, Outer stops at the first hole, Ignore fills roots.
    internal static class HatchIslandPolicy
    {
        internal static int[] FilledLoops(int style, int[] parents)
        {
            HatchClipInput.Need(style >= 0 && style <= 2, "unsupported hatch island style");
            var result = new List<int>();
            for (int i = 0; i < parents.Length; i++)
            {
                HatchClipInput.Need(parents[i] >= -1, "invalid loop parent");
                int depth = 0;
                for (int p = parents[i]; p >= 0; p = parents[p])
                    HatchClipInput.Need(p < parents.Length && parents[p] >= -1 && ++depth <= parents.Length, "invalid or cyclic loop containment");
                if (style == 0 ? depth % 2 == 0 : depth == 0) result.Add(i);
            }
            return result.ToArray();
        }

        internal static bool SubtractChild(int style) => style != 2;
    }
}
