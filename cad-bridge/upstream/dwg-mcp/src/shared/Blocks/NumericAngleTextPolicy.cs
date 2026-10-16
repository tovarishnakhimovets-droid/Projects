using System;
using System.Globalization;

namespace Bimwright.Dwg.Plugin.Blocks
{
    /// <summary>
    /// Extracts a displayed decimal angle without replacing the host's formatting
    /// or rounding. Input must already be plain text, not an MText expression.
    /// </summary>
    public static class NumericAngleTextPolicy
    {
        public const int MaximumTextLength = 256;

        public static bool TryGetNumeric(string plainText, out string numeric, out string error)
        {
            numeric = null;
            error = null;
            if (plainText == null || plainText.Length > MaximumTextLength)
            {
                error = "displayed angle must contain at most 256 plain-text characters";
                return false;
            }

            var text = plainText.Trim();
            if (text.EndsWith("\u00b0", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 1).TrimEnd();
            else if (text.EndsWith("%%d", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(0, text.Length - 3).TrimEnd();

            if (!IsSingleDecimal(text))
            {
                error = "displayed angle must be one decimal number with an optional terminal degree symbol";
                return false;
            }

            var invariant = text.Replace(',', '.');
            if (!double.TryParse(invariant, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value) ||
                double.IsNaN(value) || double.IsInfinity(value) || value < -360 || value > 360)
            {
                error = "displayed angle must be finite and between -360 and 360 degrees";
                return false;
            }

            numeric = text;
            return true;
        }

        private static bool IsSingleDecimal(string text)
        {
            if (text.Length == 0) return false;
            var index = text[0] == '+' || text[0] == '-' ? 1 : 0;
            var digits = 0;
            while (index < text.Length && IsAsciiDigit(text[index]))
            {
                ++index;
                ++digits;
            }
            if (index < text.Length && (text[index] == '.' || text[index] == ','))
            {
                ++index;
                while (index < text.Length && IsAsciiDigit(text[index]))
                {
                    ++index;
                    ++digits;
                }
            }
            return digits > 0 && index == text.Length;
        }

        private static bool IsAsciiDigit(char value) => value >= '0' && value <= '9';
    }
}
