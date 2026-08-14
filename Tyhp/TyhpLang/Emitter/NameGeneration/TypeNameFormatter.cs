using System;
using System.Collections.Generic;
using System.Linq;

namespace Tyhp.TyhpLang.Emitter.NameGeneration
{
    internal static class TypeNameFormatter
    {
        public static string FormatTypeNameSegment(string? typeName)
        {
            // Rule 1: null/whitespace → ""
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrWhiteSpace(typeName))
            {
                return "";
            }

            // Rule 7: Strip trailing ? before formatting
            if (typeName.EndsWith("?"))
            {
                typeName = typeName.Substring(0, typeName.Length - 1);
            }

            // Rule 2: Trim leading \
            typeName = typeName.TrimStart('\\');

            // Rule 3: If equals "self" (ordinal ignore case) → "This"
            if (string.Equals(typeName, "self", StringComparison.OrdinalIgnoreCase))
            {
                return "This";
            }

            // Rule 4: If contains |, split on |, trim, drop "null", then check sets
            if (typeName.Contains("|"))
            {
                var parts = typeName.Split('|');
                var trimmedParts = parts.Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p));
                var nonNullParts = trimmedParts.Where(
                    p => !string.Equals(p, "null", StringComparison.OrdinalIgnoreCase));

                if (nonNullParts.Count() == 0)
                {
                    return "";
                }

                var set = new HashSet<string>(nonNullParts, StringComparer.OrdinalIgnoreCase);

                if (set.Count == 2 && set.Contains("int") && set.Contains("float"))
                {
                    return "Number";
                }

                if (set.Count == 5 && set.Contains("int") && set.Contains("string")
                    && set.Contains("float") && set.Contains("bool") && set.Contains("array"))
                {
                    return "Scalar";
                }

                // Map each remaining part via step 5 and join with "Or"
                return string.Join("Or", nonNullParts.Select(FormatTypeNameSegment));
            }

            // Rule 6: Generics - if name contains < and >
            if (typeName.Contains("<") && typeName.Contains(">"))
            {
                var ltIndex = typeName.IndexOf('<');
                var gtIndex = typeName.LastIndexOf('>');
                var baseName = typeName.Substring(0, ltIndex);
                var argsStr = typeName.Substring(ltIndex + 1, gtIndex - ltIndex - 1);
                var args = argsStr.Split(',').Select(a => a.Trim());

                var baseFormatted = FormatSingleSegment(baseName);
                var argsFormatted = args.Select(FormatTypeNameSegment).Where(a => !string.IsNullOrEmpty(a));
                var argsJoined = string.Join("_", argsFormatted);

                return baseFormatted + "Of" + argsJoined;
            }

            // Rule 5: For a single type name
            return FormatSingleSegment(typeName);
        }

        private static string FormatSingleSegment(string typeName)
        {
            // Take last segment after \
            var segments = typeName.Split('\\');
            var lastSegment = segments.Last();

            if (string.IsNullOrEmpty(lastSegment))
            {
                return "";
            }

            // Capitalize first character, leave rest as-is
            return char.ToUpperInvariant(lastSegment[0]) + lastSegment.Substring(1);
        }

        public static string FormatUnionSegments(IEnumerable<string> typeNames)
        {
            if (typeNames == null)
            {
                return "";
            }

            return string.Join("Or", typeNames.Select(FormatTypeNameSegment).Where(s => !string.IsNullOrEmpty(s)));
        }
    }
}
