using System;
using System.Globalization;
using Bimwright.Dwg.Plugin.Blocks;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class BlockAngleDisplayTests
    {
        [Theory]
        [InlineData("108,9\u00b0", "108,9")]
        [InlineData("90\u00b0", "90")]
        [InlineData("108.90%%d", "108.90")]
        [InlineData("108,90%%D", "108,90")]
        [InlineData("\t 108,900 \u00b0 \r\n", "108,900")]
        [InlineData(".9\u00b0", ".9")]
        [InlineData(",9%%d", ",9")]
        [InlineData("+.900", "+.900")]
        [InlineData("-,900\u00b0", "-,900")]
        [InlineData("00090.0000", "00090.0000")]
        [InlineData("-0,000%%d", "-0,000")]
        [InlineData("90.", "90.")]
        [InlineData("90,", "90,")]
        [InlineData("-360\u00b0", "-360")]
        [InlineData("+360,0000%%d", "+360,0000")]
        public void Native_decimal_display_is_preserved_without_independent_rounding(string text, string expected)
        {
            Assert.True(NumericAngleTextPolicy.TryGetNumeric(text, out var numeric, out var error), error);
            Assert.Equal(expected, numeric);
            Assert.Null(error);
        }

        [Fact]
        public void Decimal_comma_is_not_changed_by_the_current_process_culture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Assert.True(NumericAngleTextPolicy.TryGetNumeric("108,9\u00b0", out var numeric, out var error), error);
                Assert.Equal("108,9", numeric);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                Assert.True(NumericAngleTextPolicy.TryGetNumeric("108.90%%d", out numeric, out error), error);
                Assert.Equal("108.90", numeric);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" \t\r\n")]
        [InlineData("\u00b0")]
        [InlineData("%%d")]
        [InlineData(".")]
        [InlineData(",")]
        [InlineData("+")]
        [InlineData("-.")]
        [InlineData("Angle: 90\u00b0")]
        [InlineData("90 deg")]
        [InlineData("90 radians")]
        [InlineData("90\u00b0 0' 0\" ")]
        [InlineData("90d0'0\"")]
        [InlineData("90\u00b0 +/-1\u00b0")]
        [InlineData("90\u00b1 1")]
        [InlineData("90 91")]
        [InlineData("90\n91")]
        [InlineData("90.1,2")]
        [InlineData("1,000,000")]
        [InlineData("1e2\u00b0")]
        [InlineData("1E+2")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("\u221e")]
        [InlineData("90\u00b0\u00b0")]
        [InlineData("90%%d\u00b0")]
        [InlineData("90\u00b0%%d")]
        [InlineData("90%%p")]
        [InlineData("\\A1;90%%d")]
        [InlineData("{90%%d}")]
        [InlineData("\\S90^91;")]
        [InlineData("%<\\AcExpr 90>%")]
        [InlineData("(progn 90)")]
        [InlineData("90; save")]
        [InlineData("90\0")]
        [InlineData("9\t0")]
        [InlineData("\u221290\u00b0")]
        [InlineData("\u0669\u0660\u00b0")]
        [InlineData("<>\u00b0")]
        [InlineData(" \\ ")]
        public void Suppressed_or_ambiguous_display_is_rejected(string text)
        {
            Assert.False(NumericAngleTextPolicy.TryGetNumeric(text, out var numeric, out var error));
            Assert.Null(numeric);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData("360.01\u00b0")]
        [InlineData("-360,01%%d")]
        [InlineData("9999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999")]
        public void Nonfinite_or_out_of_range_angles_are_rejected_without_a_value(string text)
        {
            Assert.False(NumericAngleTextPolicy.TryGetNumeric(text, out var numeric, out var error));
            Assert.Null(numeric);
            Assert.Contains("finite", error, StringComparison.Ordinal);
        }

        [Fact]
        public void Input_bound_applies_before_whitespace_or_units_are_stripped()
        {
            var boundary = "90" + new string(' ', NumericAngleTextPolicy.MaximumTextLength - 2);
            Assert.True(NumericAngleTextPolicy.TryGetNumeric(boundary, out var numeric, out var error), error);
            Assert.Equal("90", numeric);
            Assert.False(NumericAngleTextPolicy.TryGetNumeric(boundary + " ", out numeric, out error));
            Assert.Null(numeric);
            Assert.Contains("256", error, StringComparison.Ordinal);
        }
    }
}
