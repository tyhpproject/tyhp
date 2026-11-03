using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// The text PHP's <c>(string)</c> cast produces for <c>int</c> and <c>float</c> values. A
    /// <c>${int}</c> / <c>${float}</c> template hole accepts exactly these spellings.
    /// </summary>
    internal static partial class TemplateStringNumericText
    {
        /// <summary>Significant digits PHP uses for float-to-string (the default <c>precision</c> ini).</summary>
        private const int FloatPrecision = 14;

        [GeneratedRegex("^(0|-?[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
        private static partial Regex CanonicalIntPattern();

        [GeneratedRegex(@"^-?[0-9]+(\.[0-9]+)?(E[+-][0-9]+)?$", RegexOptions.CultureInvariant)]
        private static partial Regex FloatSpellingPattern();

        public static bool IsIntText(string text) =>
            CanonicalIntPattern().IsMatch(text)
            && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

        public static bool IsFloatText(string text)
        {
            double value;
            switch (text)
            {
                case "INF":
                    value = double.PositiveInfinity;
                    break;
                case "-INF":
                    value = double.NegativeInfinity;
                    break;
                case "NAN":
                    value = double.NaN;
                    break;
                default:
                    if (!FloatSpellingPattern().IsMatch(text)
                        || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    {
                        return false;
                    }

                    break;
            }

            return FormatFloat(value) == text;
        }

        public static string FormatInt(long value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>PHP's <c>(string)$float</c>: 14 significant digits, <c>zend_gcvt</c> layout.</summary>
        public static string FormatFloat(double value)
        {
            if (double.IsNaN(value))
            {
                return "NAN";
            }

            if (double.IsInfinity(value))
            {
                return value > 0 ? "INF" : "-INF";
            }

            if (value == 0)
            {
                return double.IsNegative(value) ? "-0" : "0";
            }

            var scientific = Math.Abs(value).ToString("E" + (FloatPrecision - 1), CultureInfo.InvariantCulture);
            var exponentAt = scientific.IndexOf('E');
            var digits = scientific[..exponentAt].Replace(".", "").TrimEnd('0');
            if (digits.Length == 0)
            {
                digits = "0";
            }

            var decimalPoint = int.Parse(scientific[(exponentAt + 1)..], CultureInfo.InvariantCulture) + 1;
            var text = new StringBuilder();
            if (value < 0)
            {
                text.Append('-');
            }

            if (decimalPoint < -3 || decimalPoint > FloatPrecision)
            {
                var exponent = decimalPoint - 1;
                text.Append(digits[0]).Append('.');
                text.Append(digits.Length > 1 ? digits[1..] : "0");
                text.Append('E').Append(exponent < 0 ? '-' : '+').Append(Math.Abs(exponent));
            }
            else if (decimalPoint <= 0)
            {
                text.Append("0.").Append('0', -decimalPoint).Append(digits);
            }
            else if (digits.Length <= decimalPoint)
            {
                text.Append(digits).Append('0', decimalPoint - digits.Length);
            }
            else
            {
                text.Append(digits, 0, decimalPoint).Append('.').Append(digits, decimalPoint, digits.Length - decimalPoint);
            }

            return text.ToString();
        }
    }
}
