using System.Security.Cryptography;

namespace Tyhp.Helpers {
    static class General
    {
        public static TRet Match<TKey, TRet>(TKey matchPredicate, Dictionary<TKey, Func<TRet>> options, TRet defaultValue)
            where TKey : notnull
        {
            Func<TRet>? value;
            options.TryGetValue(matchPredicate, out value);
            if (value != null) {
                return value();
            } else {
                return defaultValue;
            }
        }

        public static TRet Match<TKey, TRet>(TKey matchPredicate, Dictionary<TKey, TRet> options, TRet defaultValue)
            where TKey : notnull
        {
            TRet? value;
            options.TryGetValue(matchPredicate, out value);
            return value ?? defaultValue;
        }

        public static string GenerateRandomId()
        {
            var byteArray = RandomNumberGenerator.GetBytes(16);
            return "_" + BitConverter.ToString(byteArray).Replace("-","").ToLower();

        }
    }
}