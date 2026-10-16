using System;
using Bimwright.Dwg.Plugin.View;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ViewZoomMathTests
    {
        [Fact]
        public void Near_vertical_unnormalized_live_direction_keeps_target_center_and_full_extent()
        {
            var fit = ViewZoomMath.Fit(new[] { 200000d, 30000, -50 }, new[] { 200100d, 30200, -50 },
                new[] { 171000d, 30000, -14500 }, new[] { 4.9e-11, 0, 221486 }, 0, 2);
            Assert.Equal(29050, fit.CenterX, 7);
            Assert.Equal(100, fit.CenterY, 7);
            Assert.Equal(400, fit.Width, 7);
            Assert.Equal(200, fit.Height, 7);
        }

        [Theory]
        [InlineData(1e-300)]
        [InlineData(1)]
        [InlineData(1e300)]
        public void Direction_magnitude_does_not_change_projection(double scale)
        {
            var fit = ViewZoomMath.Fit(new[] { 10d, 20, 30 }, new[] { 14d, 26, 34 },
                new[] { 0d, 0, 0 }, new[] { scale, scale, scale }, 0.4, 2);
            var reference = ViewZoomMath.Fit(new[] { 10d, 20, 30 }, new[] { 14d, 26, 34 },
                new[] { 0d, 0, 0 }, new[] { 1d, 1, 1 }, 0.4, 2);
            Assert.Equal(reference.CenterX, fit.CenterX, 10);
            Assert.Equal(reference.CenterY, fit.CenterY, 10);
            Assert.Equal(reference.Width, fit.Width, 10);
            Assert.Equal(reference.Height, fit.Height, 10);
        }

        [Fact]
        public void Twisted_top_view_uses_inverse_camera_rotation_and_fits_all_corners()
        {
            var fit = ViewZoomMath.Fit(new[] { 10d, 20, 0 }, new[] { 14d, 26, 0 },
                new[] { 0d, 0, 0 }, new[] { 0d, 0, 10 }, Math.PI / 2, 2);
            Assert.Equal(-23, fit.CenterX, 10);
            Assert.Equal(12, fit.CenterY, 10);
            Assert.Equal(8, fit.Width, 10);
            Assert.Equal(4, fit.Height, 10);
        }

        [Fact]
        public void Side_view_projects_vertical_extent_instead_of_empty_WCS_XY_extent()
        {
            var fit = ViewZoomMath.Fit(new[] { 0d, 10, 20 }, new[] { 0d, 14, 26 },
                new[] { 0d, 0, 0 }, new[] { 1d, 0, 0 }, 0, 2);
            Assert.Equal(12, fit.CenterX, 10);
            Assert.Equal(23, fit.CenterY, 10);
            Assert.Equal(12, fit.Width, 10);
            Assert.Equal(6, fit.Height, 10);
        }

        [Fact]
        public void Bottom_view_has_a_consistent_right_handed_basis()
        {
            var fit = ViewZoomMath.Fit(new[] { 10d, 20, 0 }, new[] { 14d, 26, 0 },
                new[] { 0d, 0, 0 }, new[] { 0d, 0, -100 }, 0, 1);
            Assert.Equal(-12, fit.CenterX, 10);
            Assert.Equal(23, fit.CenterY, 10);
            Assert.Equal(6, fit.Width, 10);
            Assert.Equal(6, fit.Height, 10);
        }

        [Fact]
        public void Small_nonzero_entities_do_not_use_the_degenerate_extent_fallback()
        {
            var fit = ViewZoomMath.Fit(new[] { 0d, 0, 0 }, new[] { 0.002, 0.001, 0 },
                new[] { 0d, 0, 0 }, new[] { 0d, 0, 1 }, 0, 2, 100);
            Assert.Equal(0.002, fit.Width, 10);
            Assert.Equal(0.001, fit.Height, 10);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(double.NaN, 0, 1)]
        [InlineData(0, double.PositiveInfinity, 1)]
        public void Invalid_native_direction_fails_before_setting_a_view(double x, double y, double z)
        {
            Assert.Throws<ArgumentException>(() => ViewZoomMath.Fit(
                new[] { 0d, 0, 0 }, new[] { 1d, 1, 1 }, new[] { 0d, 0, 0 }, new[] { x, y, z }, 0, 2));
        }
    }
}
