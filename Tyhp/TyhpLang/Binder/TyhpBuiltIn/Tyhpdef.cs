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
            "H4sIAAAAAAAAE81ZbW/bNhD+nl9BYMOypWmCfa6bIks7IBjaBonbfkxoibK5SJRBUkmMaf99d0e9kHqxUzkY2g+pRR2Pzz083h1Ps3d2s1rHIjk4PWXzlWB2sxaGScXsShqWyFQwrgUM" +
            "WKEVT5nN2RxmMK5ituIPgpm1iCS8yARXUi1RwIIeEorybA0a9AlqPzq6WHG1RJkHnhawykqA5keZwuTcWJbKe5FuWMQLAwsagyK4jMqtWwr1xsJILWImkkRE9uTo6ODgFP6wI3bOkjTn" +
            "1ikHWfgJFix4dA/iiw0TEuZrtogyblcs12yZrU9g4ukB2sxub9+DIRlY8tYpetNornlhCcziANdYXUSWaEI7Ay039A6UcK35BpT8pMVS5ordbLJFTnORAsWziux2GVRAuLVYa2GEgiXQ" +
            "Zn9KnjAOJmrJF2lrJdgsY7drIB7loEjSC9hZqUQcIPzK9SdU9bZS/Ob/QHAMGw20I4QADC4ZV4hmc3w6A2ANyCnYkkJFFhmfxM6f1ey9KWpcYRIMN3dvEFHKjZmG4AKn7gtAMaGKbBqA" +
            "DzCzWt9DM4UFq7mcuA9znLr3NjgA4JLVjpxOZ+WLEXEDajb/rD4v/oZISFjEkxUqNsHmlR6T7mg1syftJ2WChEcTg89lPf1lXJuIPP1+TMeNJAmgUm4wKcAIxyQTAZFcWVyKJDDLJRIy" +
            "yeCyTTQPTxCkP24lhEm0yu1XO/YN4uGzdo0eAt7cPgZDpTe7bOdOIXatgS5tNz2a9zhGV5VOR8JHkS2E/pw83/zGZ8/28plM2FUeDxs2wYsCEz+S7r0NHNttPy9NOi65MhY9eloywNn7" +
            "H9kaw4tEQhf4GmTfybpjtZk9PbdF3LxMrrsARVsNCaF3zMdKE+R2Fpt1OUq6txSjAHqBltkhOjgzMK9IuXbvOQ2RssCsP0BDHfsA9AGDf4dwzg5Z6X5Tpd08LfI8bR6ofm6enPLmURVp" +
            "K5nJJxG3kiJNmoc1XF685cD7rIyaxwiKeaximwEJxy4YyInk5rGuy+qBwLmqkfZYVwPBYX7j3VfaO4XadAitNqb2JTQXcQXkfqoGOyQfvvv5n5D5fw/Du0yhqDr21jFimQFPJ+wT3LUK" +
            "UwAvG/gf0l0Mt63IpptjtigICo3CJW9RyDTGXLkB75HRgNJePkRMX1DMR1tuQ3seKCSqvIVQOXNGATDK3ODcLYlJnqb5o7v9/Y7JPcv1FutNWGd1gfZw1gOB5Ksu2ZRVjMB7wMsSPqx4" +
            "kPTLWtS355euQWXH6F0bEQLYshkg7ZE+vCtbaTL9ErJrzU5jaoHe7Fc/iMOBJ9Qnuut7t7fnxnvdVJMdG89+zP3ykA9t3bhxPem+gRQgI0i9TXKiYKpy+7qOmU6QUtxAhhpYeSsm38fO" +
            "qKAY3bdyh+3PNWYr/gB0kAnKsRRR9qJbOXiqymF6wkJjrIxoul5Ve7BbXdQ+54Wtxn5s+IUUVFLU2jthlxQXsZiUscBm4Io/4GuvwOypxUCqXmfcRityCvEUibXFuCotVgwdp1AfURJU" +
            "3JAisrJf+V77FVHT74H4nesYT4r7ZVzxu5QPQvXjs5t0TZIzqhjmbqhT/FXKy6A3dOwm/CU2picKgzNP1Vkl+xU5DHRT+eQpdvcR3wvaBQ+w9nzIZbyLBLKuqnQxcmARvBRKaMhcUJSB" +
            "KnS3ASbei2Q2d3TcCNuzyr1xFbAbGUQCS9hOGw5bwE9RWsToGHjTV/VDvU0gcS82J4xdJnBDESiEsHPqG1dV2HE4RBoN7OyxG3uUpi7zST++cgoX8J5a6Xeo5g7VqEoSbw7ZGi7cAXsD" +
            "3FyBVXCYZts85JjNL8kyEXhFdSDKyoj5h6cdMjspRh6ArpDlAdAdP9zl0kMXfD8+0hEWbnkMOHS6qpaFdE/Ap6vb2zOISM0WqFeNAteEblBWijqn49kX+dCubh+EzqUnR1TRYFhDamEL" +
            "XaXjoMU92Lm+JmlUM5sH3eyWeX8YgQ2EgPCQD6Jw3ZSBJoiPYIzN51AIftp2VTz47eCzwJ+HeYAOsJZLqaq8RJ8m8sJ2On346lfzWy/dvpdJ0rGLILSnqv/OxzkIzsvvIVpMhs31j33D" +
            "r2ULCCKHqOXwjsmkgxjn3Ln78V0AfbiimTeBvqW3rW/ejlRI5ViSHCV9xAy0YswIipPjJgxtwR40D92wYb3v52sfkraDGLPYDbuwPapbCx7nKsX05lVfXnXmWlXc9j/fum+0dqXzRya0" +
            "zrXBHUMx962VeuTU8XIfBU+bDjKWafjRV8QnbtU4F84XMn4vqq9Tkmq2CD87Z1lhawLocnI1EtgJKH4nRhR3tW137EEauZCptJTKHzHyE3he2DzD3g9dtBct5l6mPTfXoO0zaNtKd8vz" +
            "Ra4eACJ+5SG+8/rXWMl+bkJv2laIoRMPuJX3dfAZXtZBGJyBGu0Awq2+XnZz23gU9tuS/m3gP2Qtk818IAAA"));
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