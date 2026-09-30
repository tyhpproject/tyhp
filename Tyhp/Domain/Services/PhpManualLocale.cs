namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Maps Tyhp / BCP-47 locales onto php.net manual locale ids
    /// (<c>php_manual_{locale}.html.gz</c>).
    /// </summary>
    public static class PhpManualLocale
    {
        public static readonly string[] KnownLocales =
        [
            "en", "de", "es", "fr", "it", "ja", "pt_BR", "ru", "tr", "uk", "zh",
        ];

        public static string Normalize(string? locale, out bool fellBackToEnglish)
        {
            fellBackToEnglish = false;
            var raw = (locale ?? "").Trim();
            if (raw.Length == 0)
            {
                return "en";
            }

            raw = raw.Replace('-', '_');
            foreach (var known in KnownLocales)
            {
                if (string.Equals(raw, known, StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }

            var language = raw.Split('_', 2)[0];
            if (string.Equals(language, "pt", StringComparison.OrdinalIgnoreCase)
                && raw.Contains("BR", StringComparison.OrdinalIgnoreCase))
            {
                return "pt_BR";
            }

            foreach (var known in KnownLocales)
            {
                if (known.Equals(language, StringComparison.OrdinalIgnoreCase)
                    || known.StartsWith(language + "_", StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }

            fellBackToEnglish = true;
            return "en";
        }

        public static string ManualFileName(string phpNetLocale)
            => $"php_manual_{phpNetLocale}.html.gz";

        public static string ManualUrl(string phpNetLocale)
            => $"https://www.php.net/distributions/manual/{ManualFileName(phpNetLocale)}";

        public static string ManualPageLink(string phpNetLocale, string elementId)
            => $"https://www.php.net/manual/{phpNetLocale}/{elementId}.php";
    }
}
