using System.Text;

namespace Tyhp.TyhpLang.Emitter.SourceMap
{
    /// <summary>
    /// Variable Length Quantity (VLQ) Base64 encoding and decoding for Source Map v3
    /// <c>mappings</c> strings.
    /// </summary>
    /// <remarks>
    /// Each signed integer is converted to sign-magnitude form (least-significant bit is the
    /// sign: 0 = non-negative, 1 = negative; remaining bits are the absolute value), then
    /// split into 5-bit groups. Every group except the last has the continuation bit (bit 6,
    /// value 32) set. Each 6-bit digit is encoded with the standard Base64 alphabet
    /// <c>A–Za–z0–9+/</c>. VLQ is self-delimiting, so concatenated values need no separator.
    /// </remarks>
    internal static class VlqEncoder
    {
        private const int VlqBaseShift = 5;
        private const int VlqBase = 1 << VlqBaseShift;
        private const int VlqBaseMask = VlqBase - 1;
        private const int VlqContinuationBit = VlqBase;
        private const string Base64Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        private static readonly Dictionary<char, int> Base64Values = BuildBase64Lookup();

        /// <summary>
        /// Encode a single signed integer to a VLQ Base64 string.
        /// </summary>
        public static string Encode(int value)
        {
            var builder = new StringBuilder(8);
            EncodeTo(builder, value);
            return builder.ToString();
        }

        /// <summary>
        /// Encode an array of integers, concatenating the VLQ strings with no separator.
        /// </summary>
        public static string Encode(int[] values)
        {
            ArgumentNullException.ThrowIfNull(values);

            var builder = new StringBuilder(values.Length * 4);
            foreach (int value in values)
            {
                EncodeTo(builder, value);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Decode a single VLQ value starting at <paramref name="offset"/> in
        /// <paramref name="vlq"/>, advancing <paramref name="offset"/> past the consumed
        /// characters.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="vlq"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="offset"/> is outside <paramref name="vlq"/>.
        /// </exception>
        /// <exception cref="FormatException">
        /// The input is truncated, contains a character outside the Base64 VLQ alphabet, or
        /// decodes to a value that does not fit in <see cref="int"/>.
        /// </exception>
        public static int Decode(string vlq, ref int offset)
        {
            ArgumentNullException.ThrowIfNull(vlq);

            if (offset < 0 || offset >= vlq.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            ulong result = 0;
            int shift = 0;

            while (true)
            {
                if (offset >= vlq.Length)
                {
                    throw new FormatException("Truncated VLQ value.");
                }

                char c = vlq[offset++];
                if (!Base64Values.TryGetValue(c, out int digit))
                {
                    throw new FormatException($"Invalid Base64 VLQ character: '{c}'.");
                }

                int chunk = digit & VlqBaseMask;
                result |= (ulong)chunk << shift;

                if ((digit & VlqContinuationBit) == 0)
                {
                    break;
                }

                shift += VlqBaseShift;
                if (shift > 32)
                {
                    throw new FormatException("VLQ value is too large.");
                }
            }

            return FromVlqBits(result);
        }

        /// <summary>
        /// Decode an entire segment string into its component integers.
        /// </summary>
        public static int[] DecodeSegment(string vlq)
        {
            ArgumentNullException.ThrowIfNull(vlq);

            if (vlq.Length == 0)
            {
                return [];
            }

            var values = new List<int>();
            int offset = 0;
            while (offset < vlq.Length)
            {
                values.Add(Decode(vlq, ref offset));
            }

            return values.ToArray();
        }

        private static void EncodeTo(StringBuilder builder, int value)
        {
            ulong vlq = ToVlqBits(value);

            do
            {
                int digit = (int)(vlq & (uint)VlqBaseMask);
                vlq >>= VlqBaseShift;
                if (vlq != 0)
                {
                    digit |= VlqContinuationBit;
                }

                builder.Append(Base64Chars[digit]);
            }
            while (vlq != 0);
        }

        private static ulong ToVlqBits(int value)
        {
            if (value < 0)
            {
                return ((ulong)(-(long)value) << 1) | 1UL;
            }

            return (ulong)value << 1;
        }

        private static int FromVlqBits(ulong vlq)
        {
            bool negative = (vlq & 1UL) != 0;
            ulong magnitude = vlq >> 1;

            if (negative)
            {
                if (magnitude > (ulong)int.MaxValue + 1UL)
                {
                    throw new FormatException("VLQ value is too large.");
                }

                if (magnitude == (ulong)int.MaxValue + 1UL)
                {
                    return int.MinValue;
                }

                return -(int)magnitude;
            }

            if (magnitude > (ulong)int.MaxValue)
            {
                throw new FormatException("VLQ value is too large.");
            }

            return (int)magnitude;
        }

        private static Dictionary<char, int> BuildBase64Lookup()
        {
            var lookup = new Dictionary<char, int>(Base64Chars.Length);
            for (int i = 0; i < Base64Chars.Length; i++)
            {
                lookup[Base64Chars[i]] = i;
            }

            return lookup;
        }
    }
}
