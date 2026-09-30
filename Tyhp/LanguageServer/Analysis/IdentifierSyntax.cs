namespace Tyhp.LanguageServer.Analysis
{
    /// <summary>
    /// Identifier and keyword checks for rename validation and name-token ranging.
    /// </summary>
    internal static class IdentifierSyntax
    {
        private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "if", "else", "elseif", "endif", "for", "foreach", "endfor", "endforeach",
            "while", "endwhile", "do", "switch", "endswitch", "match", "case", "default",
            "break", "continue", "try", "catch", "finally", "throw", "return", "yield",
            "function", "fn", "class", "interface", "trait", "enum", "struct",
            "namespace", "use", "const", "new", "clone", "async", "await",
            "public", "protected", "private", "static", "abstract", "final", "readonly",
            "extends", "implements", "instanceof", "as", "parent", "self",
            "true", "false", "null", "echo", "print", "isset", "empty", "unset",
            "include", "require", "include_once", "require_once",
            "global", "var", "declare", "goto", "and", "or", "xor",
            "int", "string", "float", "bool", "array", "mixed", "void", "never",
            "iterable", "callable", "object", "decimal", "this",
            "list", "die", "exit", "__halt_compiler",
        };

        /// <summary>
        /// True when <paramref name="name"/> is a Tyhp/PHP identifier, optionally with a
        /// leading <c>$</c>. Keywords and qualified names are rejected.
        /// </summary>
        public static bool IsValidIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string bare = StripDollar(name.Trim());
            if (bare.Length == 0 || bare.Contains('\\', StringComparison.Ordinal))
            {
                return false;
            }

            if (!IsIdentifierStart(bare[0]))
            {
                return false;
            }

            for (int i = 1; i < bare.Length; i++)
            {
                if (!IsIdentifierChar(bare[i]))
                {
                    return false;
                }
            }

            return !Keywords.Contains(bare);
        }

        public static bool IsKeyword(string name)
            => !string.IsNullOrEmpty(name) && Keywords.Contains(StripDollar(name));

        public static bool IsSelfStaticParent(string name)
            => string.Equals(name, "self", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "static", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "parent", StringComparison.OrdinalIgnoreCase);

        public static bool IsThisName(string name)
        {
            string bare = StripDollar(name);
            return string.Equals(bare, "this", StringComparison.OrdinalIgnoreCase);
        }

        public static string StripDollar(string name)
            => name.StartsWith('$') ? name[1..] : name;

        public static string EnsureDollar(string name)
            => name.StartsWith('$') ? name : "$" + name;

        public static bool IsIdentifierChar(char c)
            => c == '_' || char.IsAsciiLetterOrDigit(c);

        public static bool IsIdentifierStart(char c)
            => c == '_' || char.IsAsciiLetter(c);
    }
}
