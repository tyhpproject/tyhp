namespace Tyhp.XDebugProxy.SourceMap
{
    /// <summary>
    /// Proxy-side Source Map v3 VLQ decoder. Independent of the compiler's internal
    /// <c>VlqEncoder</c>; this type only consumes on-disk <c>.map</c> files.
    /// </summary>
    /// <remarks>
    /// Each signed integer is stored in sign-magnitude form (least-significant bit is the
    /// sign: 0 = non-negative, 1 = negative; remaining bits are the absolute value), then
    /// split into 5-bit groups. Every group except the last has the continuation bit (bit 6,
    /// value 32) set. Each 6-bit digit uses the Base64 alphabet <c>A–Za–z0–9+/</c>. VLQ is
    /// self-delimiting, so concatenated values need no separator.
    /// </remarks>
    public static class SourceMapDecoder
    {
        private const int VlqBaseShift = 5;
        private const int VlqBase = 1 << VlqBaseShift;
        private const int VlqBaseMask = VlqBase - 1;
        private const int VlqContinuationBit = VlqBase;
        private const string Base64Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        private static readonly int[] Base64Values = BuildBase64Lookup();

        /// <summary>
        /// Decode a single concatenated VLQ segment (no commas or semicolons) into its
        /// component signed integers.
        /// </summary>
        /// <param name="base64VlqSegment">
        /// One mapping segment, e.g. <c>AAAA</c> or <c>AACA</c>. Must not contain
        /// <c>,</c> or <c>;</c>.
        /// </param>
        /// <returns>
        /// The decoded integers in order. An empty string yields an empty array.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="base64VlqSegment"/> is null.</exception>
        /// <exception cref="FormatException">
        /// The input is truncated, contains a character outside the Base64 VLQ alphabet, or
        /// decodes to a value that does not fit in <see cref="int"/>.
        /// </exception>
        public static int[] DecodeVlq(string base64VlqSegment)
        {
            ArgumentNullException.ThrowIfNull(base64VlqSegment);

            if (base64VlqSegment.Length == 0)
            {
                return [];
            }

            var values = new List<int>();
            int offset = 0;
            while (offset < base64VlqSegment.Length)
            {
                values.Add(DecodeOne(base64VlqSegment, ref offset));
            }

            return values.ToArray();
        }

        /// <summary>
        /// Decode a full Source Map v3 <c>mappings</c> field into one inner list of
        /// <see cref="MappingEntry"/> per generated line (0-based line index).
        /// </summary>
        /// <remarks>
        /// Lines are <c>;</c>-separated. Segments within a line are <c>,</c>-separated.
        /// Generated column resets to 0 at the start of each line; source index, original
        /// line/column, and name index are running deltas across the whole string.
        /// Segment field counts of 1 (generated column only), 4, or 5 are accepted. 2- and
        /// 3-field segments apply the generated-column delta and are otherwise ignored.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="mappingsField"/> is null.</exception>
        /// <exception cref="FormatException">A VLQ segment is truncated or contains an illegal character.</exception>
        public static IReadOnlyList<IReadOnlyList<MappingEntry>> DecodeMappings(string mappingsField)
        {
            ArgumentNullException.ThrowIfNull(mappingsField);

            if (mappingsField.Length == 0)
            {
                return [];
            }

            string[] lines = mappingsField.Split(';');
            var result = new List<IReadOnlyList<MappingEntry>>(lines.Length);

            int previousGeneratedColumn = 0;
            int previousSourceIndex = 0;
            int previousOriginalLine = 0;
            int previousOriginalColumn = 0;
            int previousNameIndex = 0;

            for (int generatedLine = 0; generatedLine < lines.Length; generatedLine++)
            {
                previousGeneratedColumn = 0;
                string group = lines[generatedLine];
                if (group.Length == 0)
                {
                    result.Add([]);
                    continue;
                }

                var entries = new List<MappingEntry>();
                foreach (string segment in group.Split(','))
                {
                    if (segment.Length == 0)
                    {
                        continue;
                    }

                    int[] fields = DecodeVlq(segment);
                    if (fields.Length == 0)
                    {
                        continue;
                    }

                    int generatedColumn = previousGeneratedColumn + fields[0];
                    previousGeneratedColumn = generatedColumn;

                    if (fields.Length is 4 or 5)
                    {
                        int sourceIndex = previousSourceIndex + fields[1];
                        int originalLine = previousOriginalLine + fields[2];
                        int originalColumn = previousOriginalColumn + fields[3];
                        previousSourceIndex = sourceIndex;
                        previousOriginalLine = originalLine;
                        previousOriginalColumn = originalColumn;

                        int? nameIndex = null;
                        if (fields.Length == 5)
                        {
                            nameIndex = previousNameIndex + fields[4];
                            previousNameIndex = nameIndex.Value;
                        }

                        entries.Add(new MappingEntry(
                            generatedLine,
                            generatedColumn,
                            sourceIndex,
                            originalLine,
                            originalColumn,
                            nameIndex));
                    }
                    else if (fields.Length == 1)
                    {
                        entries.Add(new MappingEntry(
                            generatedLine,
                            generatedColumn,
                            OriginalSourceIndex: null,
                            OriginalLine: null,
                            OriginalColumn: null,
                            OriginalNameIndex: null));
                    }
                    // 2- and 3-field segments are illegal in Source Map v3; the generated
                    // column delta is still consumed so subsequent relative values stay aligned.
                }

                result.Add(entries);
            }

            return result;
        }

        private static int DecodeOne(string vlq, ref int offset)
        {
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
                int digit = c < Base64Values.Length ? Base64Values[c] : -1;
                if (digit < 0)
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

        private static int[] BuildBase64Lookup()
        {
            var lookup = new int[128];
            Array.Fill(lookup, -1);
            for (int i = 0; i < Base64Chars.Length; i++)
            {
                lookup[Base64Chars[i]] = i;
            }

            return lookup;
        }
    }
}
