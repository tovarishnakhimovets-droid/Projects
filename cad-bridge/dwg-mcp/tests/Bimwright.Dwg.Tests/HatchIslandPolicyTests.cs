using System;
using Bimwright.Dwg.Plugin.Hatches;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class HatchIslandPolicyTests
    {
        // Four nested squares and a disconnected building: 100-36+16-4+25.
        [Theory]
        [InlineData(0, 101)] [InlineData(1, 89)] [InlineData(2, 125)]
        public void NestedCourtyardsRespectTheVisibleIslandStyle(int style, int expectedArea)
        {
            int[] parents = { -1, 0, 1, 2, -1 }, areas = { 100, 36, 16, 4, 25 };
            int actual = 0;
            foreach (int i in HatchIslandPolicy.FilledLoops(style, parents))
            {
                actual += areas[i];
                if (HatchIslandPolicy.SubtractChild(style))
                    for (int j = 0; j < parents.Length; j++) if (parents[j] == i) actual -= areas[j];
            }
            Assert.Equal(expectedArea, actual);
        }
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)]
        public void ParentCanAppearAfterItsChild(int style)
        {
            Assert.Equal(new[] { 1, 2 }, HatchIslandPolicy.FilledLoops(style, new[] { 1, -1, -1 }));
        }
        [Fact]
        public void InvalidTreesAndUnknownStylesRefuse()
        {
            Assert.Throws<ArgumentException>(() => HatchIslandPolicy.FilledLoops(0, new[] { 1, 0 }));
            Assert.Throws<ArgumentException>(() => HatchIslandPolicy.FilledLoops(0, new[] { 2 }));
            Assert.Throws<ArgumentException>(() => HatchIslandPolicy.FilledLoops(0, new[] { -2 }));
            Assert.Throws<ArgumentException>(() => HatchIslandPolicy.FilledLoops(3, new[] { -1 }));
        }
    }
}
