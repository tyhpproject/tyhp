using System.IO.Compression;
using System.Collections.ObjectModel;

namespace Tyhp.TyhpLang.Binder.TyhpBuiltIn
{
    internal static class Tyhpdef
    {
        private static readonly Lazy<List<string>> _all = new(() => new List<string> { ExtTypes });
        public static List<string> All => _all.Value;

        private static readonly Lazy<ReadOnlyDictionary<string, string>> _allKeyed = new(() => (new Dictionary<string, string> { { "__tyhp_types", ExtTypes } }).AsReadOnly());
        public static ReadOnlyDictionary<string, string> AllKeyed => _allKeyed.Value;

        private static readonly Lazy<string> _extTypes = new(() => Decompress(
            "H4sIAAAAAAAC/81Z32/bNhB+z19BYMOypa6NPddNkaUdEAxtgsRtHxNKomwuEmmQVBJj2v++O1I/SEm2EzkY2ofUoo7H7z4e746n+QezWa0Tlh7NZmSxYsRs1kwTLohZcU1SnjFCFYMBw5SgGTGSLGAGoS" +
            "IhK/rAiF6zmMOLnFHBxRIFDOixQrHM16BBTVH7ycn5ioolyjzQrIBVVgw0P/IMJkttSMbvWbYhMS00LKg1iuAyQhq3FOpNmOaKJYSlKYvN9OTk6GgGf8gJOSNpJqlxykEWfoIFEY3vQTzaEMZhviJRnFOz" +
            "IlKRZb6ewsTZEdpMbm8/giE5WPLeKXrXaK55ISnMogBXG1XExtKEdgZabuw7UEKVohtQ8pNiSy4FudnkkbRzkQJB84rsdhlUYHErtlZMMwFLoM3+FJkSCiYqTqOstRJs5onbNRCPJSji9gXsLBcsCRB+o+" +
            "oLqnpfKX73fyCYwEYD7QghAINLJhWi+QKfTgFYA3IMtrQQsUHGR7HzZzX7YIoaVxgFw809GEScUa3HITjHqYcCEISJIh8H4BPMrNb30IxhwSjKR+7DAqcevA0OALhktSOz8ax81SxpQM0Xl+Iy+hsiocXC" +
            "ngwTiQ42r/SYdEermT1qP20mSGk8Mvhc1NNfx7UtkbOXY5o0klYAlVKNSQFGKCaZGIikwuBSVgKzXMohkwwu20Tz8ARB+qOGQ5hEq9x+tWPfIR4+a9fsQ8Cb28dgqPRml+3cMcSuFdClzKZH8wHH6KrS6U" +
            "j4zPKIqcv0+eY3Pnt6kM/kzKxkMmzY7EDP/mx1H2zgtt3289Ko4yKFNujR45IBzj78yNYYXiUSusDXIHsh647VZvb43BZT/Tq57hwU7TQkhN4xHytNkNtbbNblqNW9oxgF0BFaZobooETDvCKjyr2ndsgq" +
            "C8z6AzTUsQ9AHxH4dwzn7JiU7rettJunSMqsebD1c/PklDePoshayZw/saSVZFnaPKzh8uItB95neNw8xlDMYxXbDHA4dsGAtCQ3j3VdVg8EzlWNtMe6GggO8zvvvtLeKcSmQ2i1MbUvobmIKyD3SzXYIf" +
            "n4w8//hMz/exzeZQphq2NvHc2WOfA0JV/grlXoAnjZwP+Q7hK4bcUm20xIVFgodhQueVHBswRz5Qa8h8cDSnv5EDF9RTEfbbkL7Vmg0FLlLYTKiTMKgNnMDc7dkpjKLJOP7vb3Oyb3XKod1uuwzuoC7eGs" +
            "BwLJN12ybVbRDO8Br0v4sOJB0i9qUd+eX7oGlR2j921ECGDHZoC0R/rwruykSfdLyK41e42pBXqz3/wgDgeeUJ/oru/d3p5p73VTTXZsPP0x98tDPrR1243rSfcNtAEyhtTbJCcbTIU0b+uY6QRtihvIUA" +
            "Mr78Tk+9ipLSi27lu5x/bnGrMTfwA6yATlthRR9qJbOXiqymF6wkJjWxnRdL2q9mC3uqh9zgtbjf3Y8AspqKRsa29KLmxcxGKSJwybgSv6gK+9ArOnFgOpeJtTE6+sU7CnmK0NxlVusGLoOIX4jJKg4sYq" +
            "slb2K99rvyJq+j0Qv6VK8KS4X9oVv0v+wEQ/PrtJ11ZybiuGhRvqFH+V8jLoDU3chL/YRvdEYXDuqTqtZL8hh4FuWz55it19xPeCdsEjrD0fJE/2kWCtqypdjBxYBC+ZYAoyFxRloArdbYCJjyydLxwdN8" +
            "z0rHJvXAXsRgaRwBKm04bDFvBTnBUJOgbe9EX9UG8TSNyzzZSQixRuKAyFELa0feOqCpuEQ1ajhp2duLFHrusy3+rHV05hBO9tK/0O1dyhGlFJ4s0hX8OFO2BvgJsrsAoO03yXh0zI4sJaxgKvqA5EWRmx" +
            "+PS0R2YvxcgD0BWyPAC644f7XHrogu/HR3uEmVseA449XVXLgrsn4NPV7e0ZRKR6B9SrRoFrQjcoK0Wd0/Hsi3xoV7cP4trdZUiVHQxrSMVMoap0HLS4BzvX11Ya1cwXQTe7Zd4fRmADISA85IMoXDdloA" +
            "niI9jG5nMoBD9tuyoe/HbwWeDPwjxgD7DiSy6qvGQ/TcjCdDp9+OpX/Vsv3X7kadqxy0JoT1X/nY9zEJyX30O0mAyb6x/5jl/LIggix6jl+I7wtIMY59y5+/FdAH24olk0gb6lt61v3m+pkMptSfIFdg1d" +
            "aQHjywGOQFWDUIwmUmQY872SxCtZXP+Gmv43Tffh0qyUfCRMKak07gWKuQ+QtnFs20DuS9msaati7YJfQlkydasmkrl9zuk9qz7ZcFvIxPgtNs8LUzNlK/arLdHOAsWPp4jirrbtjjxwzSOecWPz2yOGQw" +
            "ueFkbm2BCxt8+oxdxLP2f6GrRdgrYhx8dtWIQ8n0vxABDx04flW9a/ttWxZzrc8V3VCbr+wNZ7n8ye4QkdhIGf1mgHEO70x7Ib8LeHJr9X55fI/wGcQoIckR8AAA=="));
        public static string ExtTypes => _extTypes.Value;

        private static string Decompress(string encoded)
        {
            var encodedBytes = Convert.FromBase64String(encoded);
            using (var mem = new MemoryStream(encodedBytes, false))
            {
                using (var gzip = new GZipStream(mem, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}