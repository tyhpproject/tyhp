namespace Tyhp.TyhpLang.Versioning
{
    /// <summary>
    /// Composer-compatible PHP platform version constraint, evaluated against
    /// <c>output.phpVersion</c> for <c>declare(php=…)</c> and <c>#[\Tyhp\Php]</c> gates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Grammar follows Composer’s documented constraint language (ranges, <c>||</c>,
    /// <c>^</c>, <c>~</c>, wildcards, hyphen ranges, stability flags) as it applies to
    /// numeric PHP versions. Invalid strings never throw; use <see cref="TryParse"/> or
    /// <see cref="Evaluate"/> so the checker can map failures to diagnostic 4300
    /// (<c>CheckerPhpVersionInvalidConstraint</c>).
    /// </para>
    /// <para>
    /// Tyhp policy (not Composer exact-match): a bare or <c>=</c> constraint with a
    /// partial version such as <c>"8.2"</c> / <c>"=8.2"</c> matches the entire minor
    /// (<c>&gt;=8.2.0 &lt;8.3.0</c>). Comparison operators (<c>&gt;=</c>, <c>&lt;</c>, …)
    /// still pad missing components with zeros, matching Composer.
    /// </para>
    /// </remarks>
    public sealed class PhpVersionConstraint
    {
        private readonly IReadOnlyList<IReadOnlyList<Comparator>> _orGroups;

        private PhpVersionConstraint(string original, IReadOnlyList<IReadOnlyList<Comparator>> orGroups)
        {
            this.Original = original;
            this._orGroups = orGroups;
        }

        /// <summary>The constraint string that parsed successfully (trimmed).</summary>
        public string Original { get; }

        /// <summary>
        /// Parses a Composer constraint. Never throws; invalid input returns <see langword="false"/>
        /// with <paramref name="parsed"/> set to <see langword="null"/>.
        /// </summary>
        public static bool TryParse(string? constraint, out PhpVersionConstraint? parsed)
            => TryParse(constraint, out parsed, out _);

        /// <summary>
        /// Parses a Composer constraint. Never throws. On failure, <paramref name="error"/> is a
        /// stable message the checker can attach to diagnostic 4300.
        /// </summary>
        public static bool TryParse(string? constraint, out PhpVersionConstraint? parsed, out string? error)
        {
            parsed = null;
            error = null;

            try
            {
                if (constraint is null || string.IsNullOrWhiteSpace(constraint))
                {
                    error = "PHP version constraint is empty.";
                    return false;
                }

                var original = constraint.Trim();
                if (!TryParseOrGroups(original, out var orGroups, out error))
                {
                    parsed = null;
                    return false;
                }

                parsed = new PhpVersionConstraint(original, orGroups);
                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                parsed = null;
                error = "Could not parse PHP version constraint.";
                return false;
            }
        }

        /// <summary>True when <paramref name="constraint"/> is valid Composer syntax for PHP versions.</summary>
        public static bool IsValid(string? constraint) => TryParse(constraint, out _);

        /// <summary>
        /// Normalizes <c>output.phpVersion</c> (<c>"8.2"</c> / <c>"8.2.0"</c>) for comparison.
        /// Never throws; null/empty/invalid returns <see langword="false"/>.
        /// </summary>
        public static bool TryNormalizeTarget(string? phpVersion, out PhpVersion version)
            => PhpVersion.TryParse(phpVersion, out version);

        /// <summary>
        /// Whether <paramref name="targetPhpVersion"/> satisfies this constraint.
        /// Invalid or empty target returns <see langword="false"/> without throwing.
        /// </summary>
        public bool IsSatisfiedBy(string? targetPhpVersion)
        {
            if (!TryNormalizeTarget(targetPhpVersion, out var target))
            {
                return false;
            }

            return this.Matches(target);
        }

        /// <summary>
        /// Parse-and-evaluate convenience. Returns <see langword="false"/> when the constraint
        /// is invalid, the target is invalid, or the target is outside the constraint.
        /// Prefer <see cref="Evaluate"/> when the caller must distinguish invalid syntax (4300)
        /// from a valid-but-unsatisfied gate.
        /// </summary>
        public static bool IsSatisfied(string? targetPhpVersion, string? constraint)
            => Evaluate(targetPhpVersion, constraint).IsSatisfied;

        /// <summary>
        /// Evaluates <paramref name="constraint"/> against <paramref name="targetPhpVersion"/>
        /// without throwing. Inspect <see cref="PhpVersionConstraintResult.ConstraintIsValid"/>
        /// for diagnostic 4300 and <see cref="PhpVersionConstraintResult.IsSatisfied"/> for gating.
        /// </summary>
        public static PhpVersionConstraintResult Evaluate(string? targetPhpVersion, string? constraint)
        {
            if (!TryParse(constraint, out var parsed, out var error))
            {
                return PhpVersionConstraintResult.InvalidConstraint(error);
            }

            if (!TryNormalizeTarget(targetPhpVersion, out var target))
            {
                return PhpVersionConstraintResult.InvalidTarget(parsed);
            }

            var satisfied = parsed!.Matches(target);
            return PhpVersionConstraintResult.Ok(parsed, satisfied);
        }

        /// <summary>
        /// True when some PHP version could satisfy every constraint in <paramref name="left"/>
        /// AND every constraint in <paramref name="right"/> simultaneously — the two lists are
        /// each AND-ed internally (e.g. an enclosing <c>declare(php=…)</c> stack combined with a
        /// <c>#[\Tyhp\Php]</c> attribute), and this checks whether the two combined ranges share
        /// any version. Used by the binder to tell version-disjoint same-name declarations
        /// (allowed) and distinct-signature overloads under overlapping gates (allowed) apart
        /// from ones whose effective ranges overlap and whose parameter lists also collide
        /// (<c>4303</c>).
        /// </summary>
        /// <remarks>
        /// This is an exact interval check, not a discrete-minor probe: for every literal version
        /// referenced by either side's comparators (<c>&gt;=</c>, <c>&gt;</c>, <c>&lt;=</c>,
        /// <c>&lt;</c>, <c>=</c>), membership can only change exactly at that version or the patch
        /// immediately above it, so sampling those points (plus the floor <c>0.0.0</c>) never
        /// misses a real overlap — including patch-level ranges such as <c>">=8.3.5 &lt;8.3.10"</c>
        /// vs <c>">=8.3.7 &lt;8.3.12"</c> that a whole-minor probe would miss. The one known gap is
        /// a <c>!=</c> exclusion landing exactly on the sole shared point of an otherwise
        /// single-version overlap — rare in authored gates and not worth the added complexity.
        /// Invalid constraint strings contribute no restriction (never throws); diagnostic 4300
        /// owns reporting those separately.
        /// </remarks>
        public static bool AnyOverlap(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var leftGroups = ExpandAndGroups(left);
            var rightGroups = ExpandAndGroups(right);

            foreach (var leftGroup in leftGroups)
            {
                foreach (var rightGroup in rightGroups)
                {
                    if (AndGroupsOverlap(leftGroup, rightGroup))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Classifies the AND of <paramref name="constraints"/> over every PHP version at or above
        /// <paramref name="minimumPhpVersion"/>: never true, true for every such version, or true for
        /// only some of them. For the last case, <see cref="PhpRuntimeGate.Expression"/> is a PHP
        /// boolean expression over <c>\PHP_VERSION_ID</c> that selects exactly those versions.
        /// </summary>
        /// <remarks>
        /// Membership only changes at a literal bound or the patch above it, so sampling the minimum
        /// plus those points (at or above the minimum) describes the whole half-line exactly.
        /// Invalid constraint strings contribute no restriction. An unparseable minimum yields
        /// <see cref="PhpRuntimeGateKind.Always"/> so callers never emit a check they cannot justify.
        /// </remarks>
        public static PhpRuntimeGate ClassifyAtOrAbove(IReadOnlyList<string> constraints, string? minimumPhpVersion)
        {
            if (!TryNormalizeTarget(minimumPhpVersion, out var minimum))
            {
                return new PhpRuntimeGate(PhpRuntimeGateKind.Always, null);
            }

            var groups = ExpandAndGroups(constraints);
            var points = new SortedSet<PhpVersion> { minimum };
            foreach (var group in groups)
            {
                foreach (var comparator in group)
                {
                    if (!comparator.TryGetBound(out var bound))
                    {
                        continue;
                    }

                    if (bound >= minimum)
                    {
                        points.Add(bound);
                    }

                    var above = bound.IncrementAt(3);
                    if (above >= minimum)
                    {
                        points.Add(above);
                    }
                }
            }

            var ordered = points.ToList();
            var truths = ordered
                .Select(point => groups.Any(group => AllMatch(group, point)))
                .ToList();

            if (truths.All(t => t))
            {
                return new PhpRuntimeGate(PhpRuntimeGateKind.Always, null);
            }

            if (!truths.Any(t => t))
            {
                return new PhpRuntimeGate(PhpRuntimeGateKind.Never, null);
            }

            var intervals = new List<string>();
            var index = 0;
            while (index < ordered.Count)
            {
                if (!truths[index])
                {
                    index++;
                    continue;
                }

                var last = index;
                while (last + 1 < ordered.Count && truths[last + 1])
                {
                    last++;
                }

                var parts = new List<string>();
                if (index > 0)
                {
                    parts.Add($"\\PHP_VERSION_ID >= {ToVersionId(ordered[index])}");
                }

                if (last + 1 < ordered.Count)
                {
                    parts.Add($"\\PHP_VERSION_ID < {ToVersionId(ordered[last + 1])}");
                }

                intervals.Add(string.Join(" && ", parts));
                index = last + 1;
            }

            var expression = intervals.Count == 1
                ? intervals[0]
                : string.Join(" || ", intervals.Select(i => i.Contains("&&", StringComparison.Ordinal) ? $"({i})" : i));
            return new PhpRuntimeGate(PhpRuntimeGateKind.Conditional, expression);
        }

        private static int ToVersionId(PhpVersion version)
            => (version.Major * 10000) + (version.Minor * 100) + version.Patch;

        /// <summary>
        /// Expands a list of AND-ed constraint strings (each itself possibly a <c>||</c> union)
        /// into the cross product of AND-groups representing their conjunction. An empty or
        /// all-invalid input list yields a single unconstrained (empty) group.
        /// </summary>
        private static List<List<Comparator>> ExpandAndGroups(IReadOnlyList<string> constraints)
        {
            List<List<Comparator>> result = [[]];
            foreach (var constraintText in constraints)
            {
                if (!TryParse(constraintText, out var parsed) || parsed is null)
                {
                    continue;
                }

                var next = new List<List<Comparator>>(result.Count * parsed._orGroups.Count);
                foreach (var existing in result)
                {
                    foreach (var orGroup in parsed._orGroups)
                    {
                        var combined = new List<Comparator>(existing.Count + orGroup.Count);
                        combined.AddRange(existing);
                        combined.AddRange(orGroup);
                        next.Add(combined);
                    }
                }

                if (next.Count > 0)
                {
                    result = next;
                }
            }

            return result;
        }

        private static bool AndGroupsOverlap(IReadOnlyList<Comparator> left, IReadOnlyList<Comparator> right)
        {
            var candidates = new HashSet<PhpVersion> { new(0, 0, 0, 3) };
            CollectCandidates(left, candidates);
            CollectCandidates(right, candidates);

            foreach (var candidate in candidates)
            {
                if (AllMatch(left, candidate) && AllMatch(right, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectCandidates(IReadOnlyList<Comparator> group, HashSet<PhpVersion> candidates)
        {
            foreach (var comparator in group)
            {
                if (!comparator.TryGetBound(out var bound))
                {
                    continue;
                }

                candidates.Add(bound);
                candidates.Add(bound.IncrementAt(3));
            }
        }

        private static bool AllMatch(IReadOnlyList<Comparator> comparators, PhpVersion candidate)
        {
            foreach (var comparator in comparators)
            {
                if (!comparator.Matches(candidate))
                {
                    return false;
                }
            }

            return true;
        }

        private bool Matches(PhpVersion target)
        {
            foreach (var andGroup in this._orGroups)
            {
                var all = true;
                foreach (var comparator in andGroup)
                {
                    if (!comparator.Matches(target))
                    {
                        all = false;
                        break;
                    }
                }

                if (all && andGroup.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseOrGroups(
            string constraint,
            out IReadOnlyList<IReadOnlyList<Comparator>> orGroups,
            out string? error)
        {
            orGroups = Array.Empty<IReadOnlyList<Comparator>>();
            error = null;

            var orParts = SplitOr(constraint);
            if (orParts.Count == 0)
            {
                error = FormatParseError(constraint);
                return false;
            }

            var groups = new List<IReadOnlyList<Comparator>>(orParts.Count);
            foreach (var orPart in orParts)
            {
                var trimmed = orPart.Trim();
                if (trimmed.Length == 0)
                {
                    error = FormatParseError(constraint);
                    return false;
                }

                if (!TryParseAndGroup(trimmed, out var comparators, out error))
                {
                    error ??= FormatParseError(constraint);
                    return false;
                }

                groups.Add(comparators);
            }

            orGroups = groups;
            return true;
        }

        private static List<string> SplitOr(string constraint)
        {
            var parts = new List<string>();
            var start = 0;
            for (var i = 0; i < constraint.Length; i++)
            {
                if (constraint[i] != '|')
                {
                    continue;
                }

                parts.Add(constraint[start..i]);
                if (i + 1 < constraint.Length && constraint[i + 1] == '|')
                {
                    i++;
                }

                start = i + 1;
            }

            parts.Add(constraint[start..]);
            return parts;
        }

        private static bool TryParseAndGroup(
            string andGroup,
            out List<Comparator> comparators,
            out string? error)
        {
            comparators = [];
            error = null;
            var pos = 0;
            var sawPrimitive = false;

            while (pos < andGroup.Length)
            {
                SkipAndSeparators(andGroup, ref pos);
                if (pos >= andGroup.Length)
                {
                    break;
                }

                if (!TryReadPrimitive(andGroup, ref pos, out var primitive, out error))
                {
                    error ??= FormatParseError(andGroup);
                    comparators = [];
                    return false;
                }

                comparators.AddRange(primitive);
                sawPrimitive = true;
            }

            if (!sawPrimitive || comparators.Count == 0)
            {
                error = FormatParseError(andGroup);
                comparators = [];
                return false;
            }

            return true;
        }

        private static void SkipAndSeparators(string text, ref int pos)
        {
            while (pos < text.Length && (text[pos] == ',' || char.IsWhiteSpace(text[pos])))
            {
                pos++;
            }
        }

        private static bool TryReadPrimitive(
            string text,
            ref int pos,
            out List<Comparator> comparators,
            out string? error)
        {
            comparators = [];
            error = null;

            if (text[pos] == '~')
            {
                if (pos + 1 < text.Length && text[pos + 1] == '>')
                {
                    error = "Invalid operator \"~>\"; use \"~\".";
                    return false;
                }

                return TryReadUnaryRange(text, ref pos, caret: false, out comparators, out error);
            }

            if (text[pos] == '^')
            {
                return TryReadUnaryRange(text, ref pos, caret: true, out comparators, out error);
            }

            if (TryReadHyphenRange(text, pos, out var hyphenLength, out comparators, out error))
            {
                pos += hyphenLength;
                return error is null;
            }

            if (error is not null)
            {
                return false;
            }

            if (TryReadMatchAll(text, pos, out var starLength))
            {
                pos += starLength;
                comparators = [Comparator.MatchAll];
                return true;
            }

            if (TryReadOperator(text, pos, out var op, out var opLength))
            {
                var versionPos = pos + opLength;
                while (versionPos < text.Length && char.IsWhiteSpace(text[versionPos]))
                {
                    versionPos++;
                }

                if (!TryReadVersionToken(text, versionPos, out var tokenLength, out var version, out var hasWildcard))
                {
                    error = FormatParseError(text);
                    return false;
                }

                if (!TryComparatorsForOperator(op, version, hasWildcard, out comparators, out error))
                {
                    return false;
                }

                pos = versionPos + tokenLength;
                return true;
            }

            if (TryReadVersionToken(text, pos, out var bareLength, out var bareVersion, out var bareWildcard))
            {
                comparators = ComparatorsForExactOrPartial(bareVersion, bareWildcard);
                pos += bareLength;
                return true;
            }

            error = FormatParseError(text);
            return false;
        }

        private static bool TryReadUnaryRange(
            string text,
            ref int pos,
            bool caret,
            out List<Comparator> comparators,
            out string? error)
        {
            comparators = [];
            error = null;
            var versionPos = pos + 1;
            if (versionPos < text.Length && char.IsWhiteSpace(text[versionPos]))
            {
                error = FormatParseError(text);
                return false;
            }

            if (!TryReadVersionToken(text, versionPos, out var tokenLength, out var version, out var hasWildcard))
            {
                error = FormatParseError(text);
                return false;
            }

            comparators = caret
                ? ComparatorsForCaret(version, hasWildcard)
                : ComparatorsForTilde(version, hasWildcard);
            pos = versionPos + tokenLength;
            return true;
        }

        private static bool TryReadHyphenRange(
            string text,
            int pos,
            out int length,
            out List<Comparator> comparators,
            out string? error)
        {
            length = 0;
            comparators = [];
            error = null;

            if (!TryReadVersionToken(text, pos, out var leftLength, out var left, out var leftWild))
            {
                return false;
            }

            var hyphenPos = pos + leftLength;
            if (hyphenPos >= text.Length || !char.IsWhiteSpace(text[hyphenPos]))
            {
                return false;
            }

            while (hyphenPos < text.Length && char.IsWhiteSpace(text[hyphenPos]))
            {
                hyphenPos++;
            }

            if (hyphenPos >= text.Length || text[hyphenPos] != '-')
            {
                return false;
            }

            var rightStart = hyphenPos + 1;
            if (rightStart >= text.Length || !char.IsWhiteSpace(text[rightStart]))
            {
                // `8.2-8.4` (no spaces) is not a Composer hyphen range.
                return false;
            }

            while (rightStart < text.Length && char.IsWhiteSpace(text[rightStart]))
            {
                rightStart++;
            }

            if (!TryReadVersionToken(text, rightStart, out var rightLength, out var right, out var rightWild))
            {
                error = FormatParseError(text);
                return true;
            }

            comparators = ComparatorsForHyphen(left, leftWild, right, rightWild);
            length = (rightStart + rightLength) - pos;
            return true;
        }

        private static bool TryReadMatchAll(string text, int pos, out int length)
        {
            length = 0;
            if (pos >= text.Length)
            {
                return false;
            }

            var end = pos;
            if (text[pos] == '*')
            {
                end = pos + 1;
                if (end + 1 < text.Length && text[end] == '.' && text[end + 1] == '*')
                {
                    end += 2;
                }
            }
            else if (text[pos] != '@')
            {
                return false;
            }

            if (end < text.Length && text[end] == '@')
            {
                if (!TryReadStabilityFlag(text, end, out var flagLength))
                {
                    return false;
                }

                end += flagLength;
            }

            if (end == pos || !EndsPrimitive(text, end))
            {
                return false;
            }

            length = end - pos;
            return true;
        }

        private static bool TryReadStabilityFlag(string text, int pos, out int length)
        {
            length = 0;
            if (pos >= text.Length || text[pos] != '@')
            {
                return false;
            }

            ReadOnlySpan<string> flags = ["stable", "alpha", "beta", "dev", "RC", "rc"];
            foreach (var flag in flags)
            {
                if (pos + 1 + flag.Length <= text.Length
                    && text.AsSpan(pos + 1, flag.Length).Equals(flag, StringComparison.OrdinalIgnoreCase)
                    && EndsPrimitive(text, pos + 1 + flag.Length))
                {
                    length = 1 + flag.Length;
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadOperator(string text, int pos, out string op, out int length)
        {
            op = "";
            length = 0;
            if (pos >= text.Length)
            {
                return false;
            }

            string[] operators = [">=", "<=", "!=", "<>", "==", ">", "<", "="];
            foreach (var candidate in operators)
            {
                if (pos + candidate.Length <= text.Length
                    && text.AsSpan(pos, candidate.Length).Equals(candidate, StringComparison.Ordinal))
                {
                    op = candidate;
                    length = candidate.Length;
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadVersionToken(
            string text,
            int pos,
            out int length,
            out PhpVersion version,
            out bool hasWildcard)
        {
            length = 0;
            version = default;
            hasWildcard = false;
            if (pos >= text.Length)
            {
                return false;
            }

            var end = pos;
            while (end < text.Length && !IsPrimitiveTerminator(text[end]))
            {
                end++;
            }

            if (end == pos)
            {
                return false;
            }

            var token = text[pos..end];
            if (!PhpVersion.TryParseToken(token, out version, out hasWildcard))
            {
                return false;
            }

            length = end - pos;
            return true;
        }

        private static bool EndsPrimitive(string text, int pos)
            => pos >= text.Length || IsPrimitiveTerminator(text[pos]);

        private static bool IsPrimitiveTerminator(char c)
            => char.IsWhiteSpace(c) || c is ',' or '|';

        private static bool TryComparatorsForOperator(
            string op,
            PhpVersion version,
            bool hasWildcard,
            out List<Comparator> comparators,
            out string? error)
        {
            comparators = [];
            error = null;

            if (op is "=" or "==")
            {
                comparators = ComparatorsForExactOrPartial(version, hasWildcard);
                return true;
            }

            if (hasWildcard)
            {
                error = "Wildcards cannot be combined with comparison operators.";
                return false;
            }

            comparators = op switch
            {
                ">=" => [Comparator.Gte(version)],
                ">" => [Comparator.Gt(version)],
                "<=" => [Comparator.Lte(version)],
                "<" => [Comparator.Lt(version)],
                "!=" or "<>" => [Comparator.Neq(version)],
                _ => [],
            };

            if (comparators.Count == 0)
            {
                error = FormatParseError(op);
                return false;
            }

            return true;
        }

        private static List<Comparator> ComparatorsForExactOrPartial(PhpVersion version, bool hasWildcard)
        {
            if (!hasWildcard && version.SpecifiedComponentCount >= 3)
            {
                return [Comparator.Eq(version)];
            }

            var incrementAt = Math.Max(1, version.SpecifiedComponentCount);
            return
            [
                Comparator.Gte(version),
                Comparator.Lt(version.IncrementAt(incrementAt)),
            ];
        }

        private static List<Comparator> ComparatorsForTilde(PhpVersion version, bool hasWildcard)
        {
            _ = hasWildcard;
            var specified = Math.Max(1, version.SpecifiedComponentCount);
            var highPosition = Math.Max(1, specified - 1);
            return
            [
                Comparator.Gte(version),
                Comparator.Lt(version.IncrementAt(highPosition)),
            ];
        }

        private static List<Comparator> ComparatorsForCaret(PhpVersion version, bool hasWildcard)
        {
            _ = hasWildcard;
            int position;
            if (version.Major != 0 || version.SpecifiedComponentCount < 2)
            {
                position = 1;
            }
            else if (version.Minor != 0 || version.SpecifiedComponentCount < 3)
            {
                position = 2;
            }
            else
            {
                position = 3;
            }

            return
            [
                Comparator.Gte(version),
                Comparator.Lt(version.IncrementAt(position)),
            ];
        }

        private static List<Comparator> ComparatorsForHyphen(
            PhpVersion left,
            bool leftWild,
            PhpVersion right,
            bool rightWild)
        {
            _ = leftWild;
            var lower = Comparator.Gte(left);

            if (!rightWild && right.SpecifiedComponentCount >= 3)
            {
                return [lower, Comparator.Lte(right)];
            }

            var incrementAt = right.SpecifiedComponentCount <= 1 ? 1 : 2;
            return [lower, Comparator.Lt(right.IncrementAt(incrementAt))];
        }

        private static string FormatParseError(string constraint)
            => $"Could not parse PHP version constraint '{constraint}'.";

        private readonly struct Comparator
        {
            private readonly Kind _kind;
            private readonly PhpVersion _bound;

            private Comparator(Kind kind, PhpVersion bound)
            {
                this._kind = kind;
                this._bound = bound;
            }

            public static Comparator MatchAll => new(Kind.MatchAll, default);

            public static Comparator Gte(PhpVersion version) => new(Kind.Gte, version);

            public static Comparator Gt(PhpVersion version) => new(Kind.Gt, version);

            public static Comparator Lte(PhpVersion version) => new(Kind.Lte, version);

            public static Comparator Lt(PhpVersion version) => new(Kind.Lt, version);

            public static Comparator Eq(PhpVersion version) => new(Kind.Eq, version);

            public static Comparator Neq(PhpVersion version) => new(Kind.Neq, version);

            /// <summary>
            /// The literal version this comparator is bound to, when it has one. <see cref="Kind.MatchAll"/>
            /// carries no bound and returns <see langword="false"/>.
            /// </summary>
            public bool TryGetBound(out PhpVersion bound)
            {
                if (this._kind == Kind.MatchAll)
                {
                    bound = default;
                    return false;
                }

                bound = this._bound;
                return true;
            }

            public bool Matches(PhpVersion target) => this._kind switch
            {
                Kind.MatchAll => true,
                Kind.Gte => target >= this._bound,
                Kind.Gt => target > this._bound,
                Kind.Lte => target <= this._bound,
                Kind.Lt => target < this._bound,
                Kind.Eq => target == this._bound,
                Kind.Neq => target != this._bound,
                _ => false,
            };

            private enum Kind
            {
                MatchAll,
                Gte,
                Gt,
                Lte,
                Lt,
                Eq,
                Neq,
            }
        }
    }

    /// <summary>How a gate relates to every PHP version at or above a minimum.</summary>
    public enum PhpRuntimeGateKind
    {
        /// <summary>No version at or above the minimum satisfies the gate.</summary>
        Never,

        /// <summary>Every version at or above the minimum satisfies the gate.</summary>
        Always,

        /// <summary>Only some versions at or above the minimum satisfy the gate.</summary>
        Conditional,
    }

    /// <summary>
    /// Result of <see cref="PhpVersionConstraint.ClassifyAtOrAbove"/>. <see cref="Expression"/> is
    /// set only for <see cref="PhpRuntimeGateKind.Conditional"/>.
    /// </summary>
    public readonly record struct PhpRuntimeGate(PhpRuntimeGateKind Kind, string? Expression);

    /// <summary>
    /// Result of <see cref="PhpVersionConstraint.Evaluate"/>. Never produced by throwing;
    /// invalid syntax sets <see cref="ConstraintIsValid"/> to <see langword="false"/> so
    /// the checker can emit diagnostic 4300.
    /// </summary>
    public readonly struct PhpVersionConstraintResult
    {
        private PhpVersionConstraintResult(
            bool constraintIsValid,
            bool targetIsValid,
            bool isSatisfied,
            string? error,
            PhpVersionConstraint? constraint)
        {
            this.ConstraintIsValid = constraintIsValid;
            this.TargetIsValid = targetIsValid;
            this.IsSatisfied = isSatisfied;
            this.Error = error;
            this.Constraint = constraint;
        }

        /// <summary>False when the constraint string is not valid Composer syntax.</summary>
        public bool ConstraintIsValid { get; }

        /// <summary>False when the target <c>output.phpVersion</c> could not be normalized.</summary>
        public bool TargetIsValid { get; }

        /// <summary>
        /// True only when both the constraint and target are valid and the target lies in the range.
        /// </summary>
        public bool IsSatisfied { get; }

        /// <summary>Parse failure message when <see cref="ConstraintIsValid"/> is false; otherwise null.</summary>
        public string? Error { get; }

        /// <summary>The parsed constraint when <see cref="ConstraintIsValid"/> is true.</summary>
        public PhpVersionConstraint? Constraint { get; }

        internal static PhpVersionConstraintResult InvalidConstraint(string? error)
            => new(false, false, false, error ?? "Could not parse PHP version constraint.", null);

        internal static PhpVersionConstraintResult InvalidTarget(PhpVersionConstraint? constraint)
            => new(true, false, false, null, constraint);

        internal static PhpVersionConstraintResult Ok(PhpVersionConstraint constraint, bool satisfied)
            => new(true, true, satisfied, null, constraint);
    }
}
