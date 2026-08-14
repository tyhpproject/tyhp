namespace Tyhp.Domain.Diagnostics
{
    /// <summary>
    /// Levenshtein-based "did you mean" helper for unknown-symbol diagnostics.
    /// Producers pass in-scope candidate names; the closest match (within a length-scaled
    /// distance budget) becomes a <see cref="DiagnosticSuggestion"/>.
    /// </summary>
    public static class DidYouMean
    {
        /// <summary>
        /// Computes the Levenshtein edit distance between two strings.
        /// </summary>
        public static int Distance(string a, string b)
        {
            a ??= string.Empty;
            b ??= string.Empty;

            if (a.Length == 0)
            {
                return b.Length;
            }

            if (b.Length == 0)
            {
                return a.Length;
            }

            // Ensure a is the shorter string so the rolling row stays small.
            if (a.Length > b.Length)
            {
                (a, b) = (b, a);
            }

            var previous = new int[a.Length + 1];
            var current = new int[a.Length + 1];

            for (var i = 0; i <= a.Length; i++)
            {
                previous[i] = i;
            }

            for (var j = 1; j <= b.Length; j++)
            {
                current[0] = j;
                var bj = b[j - 1];
                for (var i = 1; i <= a.Length; i++)
                {
                    var cost = a[i - 1] == bj ? 0 : 1;
                    current[i] = Math.Min(
                        Math.Min(current[i - 1] + 1, previous[i] + 1),
                        previous[i - 1] + cost);
                }

                (previous, current) = (current, previous);
            }

            return previous[a.Length];
        }

        /// <summary>
        /// Returns a length-scaled maximum edit distance for suggestions.
        /// Short identifiers stay strict; longer names allow a few typos.
        /// </summary>
        public static int MaxDistanceFor(string actual)
        {
            var length = actual?.Length ?? 0;
            if (length <= 3)
            {
                return 1;
            }

            if (length <= 6)
            {
                return 2;
            }

            return 3;
        }

        /// <summary>
        /// Finds the closest candidate to <paramref name="actual"/> within the distance budget,
        /// or <see langword="null"/> when nothing is close enough. Candidates that equal
        /// <paramref name="actual"/> ordinally are skipped — the name is already spelled that way,
        /// so re-suggesting it is not a fix.
        /// </summary>
        /// <param name="actual">The unresolved identifier.</param>
        /// <param name="candidates">In-scope names to consider.</param>
        /// <param name="maxDistance">
        /// Optional override; defaults to <see cref="MaxDistanceFor"/>.
        /// </param>
        /// <param name="comparison">
        /// How characters are compared when computing distance. Default is ordinal ignore-case
        /// so a casing-only typo still ranks as a perfect match and returns the candidate's spelling.
        /// </param>
        public static string? FindBestMatch(
            string? actual,
            IEnumerable<string>? candidates,
            int? maxDistance = null,
            StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (string.IsNullOrEmpty(actual) || candidates is null)
            {
                return null;
            }

            var budget = maxDistance ?? MaxDistanceFor(actual);
            if (budget < 0)
            {
                return null;
            }

            var ignoreCase = comparison != StringComparison.Ordinal;
            var foldedActual = ignoreCase ? actual.ToLowerInvariant() : actual;

            string? best = null;
            var bestDistance = int.MaxValue;

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                // Exact ordinal match — the name exists as-written; not a suggestion.
                if (string.Equals(actual, candidate, StringComparison.Ordinal))
                {
                    continue;
                }

                // Fast reject when lengths alone exceed the budget.
                if (Math.Abs(actual.Length - candidate.Length) > budget)
                {
                    continue;
                }

                var distance = ignoreCase
                    ? Distance(foldedActual, candidate.ToLowerInvariant())
                    : Distance(actual, candidate);

                if (distance > budget || distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = candidate;

                if (bestDistance == 0)
                {
                    // Case-only difference (or other ignore-case equality) — take it.
                    break;
                }
            }

            return best;
        }

        /// <summary>
        /// Builds a machine-applicable suggestion that replaces <paramref name="unknownName"/>
        /// with <paramref name="replacement"/>, starting <paramref name="columnOffset"/> characters
        /// after the diagnostic's primary location.
        /// </summary>
        public static DiagnosticSuggestion CreateReplacementSuggestion(
            IDiagnostic diagnostic,
            string unknownName,
            string replacement,
            string? description = null,
            int columnOffset = 0)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            ArgumentNullException.ThrowIfNull(replacement);

            var length = Math.Max(1, (unknownName ?? string.Empty).Length);
            var start = diagnostic.Column + Math.Max(0, columnOffset);
            var span = new DiagnosticSpan(
                diagnostic.FileName ?? string.Empty,
                diagnostic.Line,
                start,
                diagnostic.Line,
                start + length);

            return DiagnosticSuggestion.Create(span, replacement, description);
        }

        /// <summary>
        /// Attaches a "did you mean" suggestion (and matching <see cref="Diagnostic.Help"/> when
        /// help is empty) when a close candidate exists. Returns the original diagnostic unchanged
        /// when no match is found.
        /// </summary>
        /// <param name="unknownName">
        /// The unresolved name <em>as written in the source</em> at the diagnostic's position, so the
        /// edit span can be derived from it. Qualified names keep their namespace prefix.
        /// </param>
        public static Diagnostic Attach(
            Diagnostic diagnostic,
            string? unknownName,
            IEnumerable<string>? candidates,
            int? maxDistance = null)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);

            if (!TryGetReplaceableSegment(unknownName, out var segment, out var columnOffset))
            {
                return diagnostic;
            }

            var match = FindBestMatch(segment, candidates, maxDistance);
            if (match is null)
            {
                return diagnostic;
            }

            var description = CLI.Message.Localize("CLI_DiagnosticDidYouMean", match);
            var updated = diagnostic;

            // Producers report file-level problems at line 0; there is no source text to edit there,
            // so those diagnostics get the prose hint without a machine-applicable suggestion.
            if (diagnostic.HasSourcePosition)
            {
                updated = updated.WithSuggestion(
                    CreateReplacementSuggestion(diagnostic, segment, match, description, columnOffset));
            }

            if (string.IsNullOrWhiteSpace(diagnostic.Help))
            {
                updated = updated.WithHelp(description);
            }

            return updated;
        }

        /// <summary>
        /// Isolates the part of a written name that a suggestion may replace: the final namespace
        /// segment, past any leading nullable marker. <paramref name="columnOffset"/> is that
        /// segment's distance from the start of the written name, so the edit lands on the segment
        /// rather than on the prefix.
        /// </summary>
        /// <remarks>
        /// Returns <see langword="false"/> for composite type text (unions, intersections,
        /// generics, …). Those have no single identifier to swap, and matching the whole string
        /// against simple names produces both a nonsense hint and an edit that would delete
        /// syntax the user meant to keep.
        /// </remarks>
        private static bool TryGetReplaceableSegment(
            string? writtenName,
            out string segment,
            out int columnOffset)
        {
            segment = string.Empty;
            columnOffset = 0;

            if (string.IsNullOrEmpty(writtenName))
            {
                return false;
            }

            var start = writtenName[0] == '?' ? 1 : 0;
            var lastSeparator = writtenName.LastIndexOf('\\');
            if (lastSeparator >= start)
            {
                start = lastSeparator + 1;
            }

            if (start >= writtenName.Length)
            {
                return false;
            }

            for (var i = start; i < writtenName.Length; i++)
            {
                var ch = writtenName[i];
                if (ch != '_' && !char.IsLetterOrDigit(ch))
                {
                    return false;
                }
            }

            segment = writtenName[start..];
            columnOffset = start;
            return true;
        }
    }
}
