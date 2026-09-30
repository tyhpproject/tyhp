using System.Globalization;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Parses diagnostic identifiers from <c>tyhp.json</c> / CLI (<c>TYHP8027</c>, <c>8027</c>).
    /// </summary>
    public static class DiagnosticCodeParser
    {
        /// <summary>
        /// Tries to parse a diagnostic code token into a defined <see cref="MessageCode"/>.
        /// Accepts <c>TYHP8027</c> (any case) or a bare numeric code such as <c>8027</c>.
        /// </summary>
        public static bool TryParse(string? value, out MessageCode code)
        {
            code = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim();
            if (text.StartsWith("TYHP", StringComparison.OrdinalIgnoreCase))
            {
                text = text[4..];
            }

            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                || number <= 0
                || !Enum.IsDefined(typeof(MessageCode), number))
            {
                return false;
            }

            code = (MessageCode)number;
            return true;
        }
    }
}
