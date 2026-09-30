using System.Text.RegularExpressions;

namespace Tyhp.TyhpLang.Versioning
{
    /// <summary>
    /// A numeric PHP platform version (<c>major.minor.patch</c>) used with
    /// <see cref="PhpVersionConstraint"/>. Missing components on a <c>output.phpVersion</c>
    /// target are padded with zeros (<c>"8.2"</c> → <c>8.2.0</c>).
    /// </summary>
    public readonly struct PhpVersion : IEquatable<PhpVersion>, IComparable<PhpVersion>
    {
        /// <summary>
        /// Optional <c>v</c> prefix, up to four numeric or trailing wildcard components,
        /// Composer stability suffixes, build metadata, and <c>@stable</c>/<c>@dev</c> flags.
        /// </summary>
        private static readonly Regex VersionTokenRegex = new(
            @"^(?:v)?" +
            @"(?<major>\d+)" +
            @"(?:\.(?<minor>\d+|x|\*))?" +
            @"(?:\.(?<patch>\d+|x|\*))?" +
            @"(?:\.(?<rev>\d+|x|\*))?" +
            @"(?:[._-]?(?:stable|beta|b|RC|alpha|a|patch|pl|p)(?:[.-]?\d+)?)?" +
            @"(?:[.-]?dev)?" +
            @"(?:\+[^\s@]+)?" +
            @"(?:@(?:stable|RC|beta|alpha|dev))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Major component (e.g. <c>8</c> in <c>8.2.0</c>).</summary>
        public int Major { get; }

        /// <summary>Minor component (e.g. <c>2</c> in <c>8.2.0</c>).</summary>
        public int Minor { get; }

        /// <summary>Patch component (e.g. <c>0</c> in <c>8.2.0</c>).</summary>
        public int Patch { get; }

        /// <summary>
        /// How many leading numeric (non-wildcard) components were present in the spelling.
        /// <c>"8.2"</c> is 2; <c>"8.2.0"</c> is 3; <c>"8.2.*"</c> is 2.
        /// </summary>
        public int SpecifiedComponentCount { get; }

        /// <summary>Creates a version from already-normalized non-negative components.</summary>
        public PhpVersion(int major, int minor, int patch, int specifiedComponentCount = 3)
        {
            this.Major = major;
            this.Minor = minor;
            this.Patch = patch;
            this.SpecifiedComponentCount = specifiedComponentCount;
        }

        /// <summary>
        /// Parses a concrete PHP version such as <c>output.phpVersion</c> (<c>"8.2"</c>,
        /// <c>"8.2.0"</c>, optional <c>v</c> prefix). Wildcards and constraint operators are rejected.
        /// Never throws; null/empty/invalid returns <see langword="false"/>.
        /// </summary>
        public static bool TryParse(string? text, out PhpVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!TryParseToken(text.Trim(), out version, out var hasWildcard) || hasWildcard)
            {
                version = default;
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public int CompareTo(PhpVersion other)
        {
            var c = this.Major.CompareTo(other.Major);
            if (c != 0)
            {
                return c;
            }

            c = this.Minor.CompareTo(other.Minor);
            if (c != 0)
            {
                return c;
            }

            return this.Patch.CompareTo(other.Patch);
        }

        /// <inheritdoc />
        public bool Equals(PhpVersion other)
            => this.Major == other.Major && this.Minor == other.Minor && this.Patch == other.Patch;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is PhpVersion other && this.Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(this.Major, this.Minor, this.Patch);

        /// <summary>Three-part dotted spelling (<c>8.2.0</c>).</summary>
        public override string ToString() => $"{this.Major}.{this.Minor}.{this.Patch}";

        public static bool operator ==(PhpVersion left, PhpVersion right) => left.Equals(right);

        public static bool operator !=(PhpVersion left, PhpVersion right) => !left.Equals(right);

        public static bool operator <(PhpVersion left, PhpVersion right) => left.CompareTo(right) < 0;

        public static bool operator >(PhpVersion left, PhpVersion right) => left.CompareTo(right) > 0;

        public static bool operator <=(PhpVersion left, PhpVersion right) => left.CompareTo(right) <= 0;

        public static bool operator >=(PhpVersion left, PhpVersion right) => left.CompareTo(right) >= 0;

        /// <summary>
        /// Parses a Composer version token used inside a constraint (wildcards allowed).
        /// Stability flags and build metadata are accepted and ignored for PHP platform matching.
        /// </summary>
        internal static bool TryParseToken(string text, out PhpVersion version, out bool hasWildcard)
        {
            version = default;
            hasWildcard = false;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var match = VersionTokenRegex.Match(text.Trim());
            if (!match.Success)
            {
                return false;
            }

            if (!TryReadComponent(match.Groups["major"].Value, out var major, out var majorWild)
                || majorWild)
            {
                return false;
            }

            var specified = 1;
            var minor = 0;
            var patch = 0;
            var seenWildcard = false;

            if (match.Groups["minor"].Success)
            {
                if (!TryReadComponent(match.Groups["minor"].Value, out minor, out var minorWild))
                {
                    return false;
                }

                if (minorWild)
                {
                    seenWildcard = true;
                    minor = 0;
                }
                else
                {
                    specified = 2;
                }
            }

            if (match.Groups["patch"].Success)
            {
                if (seenWildcard)
                {
                    if (!IsWildcardComponent(match.Groups["patch"].Value))
                    {
                        return false;
                    }
                }
                else if (!TryReadComponent(match.Groups["patch"].Value, out patch, out var patchWild))
                {
                    return false;
                }
                else if (patchWild)
                {
                    seenWildcard = true;
                    patch = 0;
                }
                else
                {
                    specified = 3;
                }
            }

            if (match.Groups["rev"].Success)
            {
                var revText = match.Groups["rev"].Value;
                if (seenWildcard)
                {
                    if (!IsWildcardComponent(revText))
                    {
                        return false;
                    }
                }
                else if (IsWildcardComponent(revText))
                {
                    seenWildcard = true;
                }
                else if (!int.TryParse(revText, out var rev) || rev < 0)
                {
                    return false;
                }
                else
                {
                    specified = 4;
                    if (rev != 0)
                    {
                        // PHP platform versions are three-part; a non-zero fourth component
                        // is not a PHP version we evaluate against output.phpVersion.
                        return false;
                    }
                }
            }

            version = new PhpVersion(major, minor, patch, specified);
            hasWildcard = seenWildcard;
            return true;
        }

        internal PhpVersion IncrementAt(int oneBasedComponent)
        {
            return oneBasedComponent switch
            {
                1 => new PhpVersion(this.Major + 1, 0, 0, 3),
                2 => new PhpVersion(this.Major, this.Minor + 1, 0, 3),
                _ => new PhpVersion(this.Major, this.Minor, this.Patch + 1, 3),
            };
        }

        private static bool TryReadComponent(string text, out int value, out bool isWildcard)
        {
            value = 0;
            isWildcard = IsWildcardComponent(text);
            if (isWildcard)
            {
                return true;
            }

            return int.TryParse(text, out value) && value >= 0;
        }

        private static bool IsWildcardComponent(string text)
            => text.Equals("x", StringComparison.OrdinalIgnoreCase)
               || text.Equals("*", StringComparison.Ordinal);
    }
}
