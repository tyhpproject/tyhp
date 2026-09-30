using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Tyhp.Domain.Services.PhpDoc;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Extracts tyhpdef types from PHP AST nodes by merging runtime type hints with PHPDoc.
    /// </summary>
    public sealed class PhpAstTypeExtractor
    {
        internal static readonly HashSet<string> WeakPhpHints = new(StringComparer.OrdinalIgnoreCase)
        {
            "array",
            "iterable",
            "object",
            "mixed",
            "callable",
        };

        internal static readonly HashSet<string> BuiltinTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "int", "integer", "string", "bool", "boolean", "float", "double",
            "void", "mixed", "null", "true", "false", "never", "resource",
            "array", "iterable", "object", "callable", "self", "static", "parent",
        };

        private readonly TyhpdefGenerationOptions _options;

        /// <summary>
        /// Unique short names for PHP globals and <c>Psr\*</c> types from the
        /// extern catalog, used when PHPDoc / type hints omit a <c>use</c> import.
        /// </summary>
        public IReadOnlyDictionary<string, string> KnownShortNames { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// FQCNs skipped because they are <c>@internal</c> and
        /// <see cref="TyhpdefGenerationOptions.IncludeInternal"/> is off.
        /// </summary>
        public HashSet<string> ExcludedInternalTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public PhpAstTypeExtractor(TyhpdefGenerationOptions options)
        {
            this._options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Prefer PHPDoc when the PHP hint is weak (<c>array</c>/<c>iterable</c>/<c>object</c>/
        /// <c>mixed</c>/<c>callable</c> or absent); otherwise prefer the PHP type hint.
        /// Neither → <c>mixed</c>. PHPDoc types are normalized via <see cref="PhpDocTypeParser.Normalize"/>.
        /// When <paramref name="resolver"/> is given, names the file actually <c>use</c>-imports
        /// (e.g. <c>Document</c> for <c>use Elastica\Document;</c>) are qualified to their FQCN.
        /// Bare names with no matching import — typically a local PHPDoc alias such
        /// as <c>@phpstan-type Options</c> — are left exactly as written here so
        /// <see cref="PhpDocLocalTypeMaterializer"/> can rewrite them to the emitted
        /// struct or type-alias name (for example <c>ElasticaHandlerOptions</c>).
        /// </summary>
        public static string MergeTypes(string? phpHint, string? phpDocType, PhpTypeNameResolver? resolver = null)
        {
            var hint = (phpHint ?? "").Trim();
            var docRaw = (phpDocType ?? "").Trim();
            var doc = docRaw.Length == 0 ? null : PhpDocTypeParser.Normalize(docRaw);
            if (doc is not null && resolver is not null)
            {
                doc = resolver.ResolveImportedNamesOnly(doc);
            }

            if (IsWeakPhpHint(hint))
            {
                if (!string.IsNullOrWhiteSpace(doc) && !string.Equals(doc, "mixed", StringComparison.OrdinalIgnoreCase))
                {
                    return ToTyhpdefType(doc);
                }

                return hint.Length == 0 ? "mixed" : ToTyhpdefType(StripNullablePrefixForBare(hint));
            }

            return ToTyhpdefType(hint);
        }

        /// <summary>
        /// Rewrites PHPDoc spellings that are not valid tyhpdef type syntax.
        /// <c>list</c> is lexer token <c>T_LIST</c>, so <c>list&lt;T&gt;</c> becomes
        /// <c>array&lt;int, T&gt;</c> (the form already used in bundled tyhpdefs).
        /// Bare <c>list</c> / psalm <c>empty</c> / PHPDoc <c>$this</c> / slash-unions
        /// are rewritten so generate output round-trips through the tyhpdef parser.
        /// </summary>
        internal static string ToTyhpdefType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return type;
            }

            var rewritten = ThisTypeName.Replace(type, "static");
            rewritten = SlashUnion.Replace(rewritten, "$1|$2");
            rewritten = ListGeneric.Replace(rewritten, "array<int, ");
            rewritten = BareList.Replace(rewritten, "array");
            rewritten = EmptyTypeName.Replace(rewritten, "mixed");
            rewritten = PhpDocCallable.Replace(rewritten, "callable");
            rewritten = PhpDocScalarAlias.Replace(rewritten, static match => match.Value.ToLowerInvariant() switch
            {
                "integer" or "long" => "int",
                "boolean" => "bool",
                "double" => "float",
                _ => match.Value,
            });
            return DropUnbalancedDelimiters(rewritten.Trim());
        }

        /// <summary>
        /// <c>static</c> is valid as a tyhpdef return type (<c>: static</c>) but not as a
        /// parameter or property type (the lexer token is <c>T_STATIC</c>). PHPDoc often
        /// writes <c>static&lt;K,V&gt;</c> / <c>callable|static</c> in those positions;
        /// emit <c>self</c> instead.
        /// </summary>
        internal static string RewriteStaticTypeInValuePosition(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return type;
            }

            return StaticTypeName.Replace(type, "self");
        }

        private static readonly Regex ListGeneric = new(
            @"(?<![\w\\])(?:non-empty-)?list\s*<",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BareList = new(
            @"(?<![\w\\])(?:non-empty-)?list(?![\w\\<(])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex EmptyTypeName = new(
            @"(?<![\w\\])empty(?![\w\\(])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ThisTypeName = new(
            @"\$this(?![\w])",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex SlashUnion = new(
            @"([A-Za-z_\\][A-Za-z0-9_\\]*|\))\s*/\s*([A-Za-z_\\$][A-Za-z0-9_\\]*|\()",
            RegexOptions.Compiled);

        private static readonly Regex PhpDocCallable = new(
            @"(?<![\w\\])(?:callable|closure)\s*\([^)]*\)(?:\s*:\s*[^|,)&]+)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PhpDocScalarAlias = new(
            @"(?<![\w\\])(?:integer|boolean|double|long)(?![\w\\])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex StaticTypeName = new(
            @"(?<![\w\\])static(?![\w\\])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public string MergeParameterType(
            string? phpHint,
            PhpDocBlock doc,
            string parameterName,
            PhpTypeNameResolver? resolver = null)
        {
            var tag = doc.ParamTags.FirstOrDefault(t =>
                string.Equals(t.ParameterName, StripDollar(parameterName), StringComparison.OrdinalIgnoreCase));
            return RewriteStaticTypeInValuePosition(MergeTypes(phpHint, tag?.TypeExpression, resolver));
        }

        public string MergeReturnType(string? phpHint, PhpDocBlock doc, PhpTypeNameResolver? resolver = null)
            => MergeTypes(phpHint, doc.ReturnTag?.TypeExpression, resolver);

        public string MergeVarType(string? phpHint, PhpDocBlock doc, PhpTypeNameResolver? resolver = null)
            => RewriteStaticTypeInValuePosition(MergeTypes(phpHint, doc.VarTag?.TypeExpression, resolver));

        public string SpellType(ITypeExpression? type, PhpTypeNameResolver resolver)
        {
            if (type is null)
            {
                return "";
            }

            return ToTyhpdefType(SpellTypeCore(type, resolver));
        }

        public List<TyhpdefGenericParameter> ExtractTemplates(PhpDocBlock doc, PhpTypeNameResolver resolver)
        {
            var result = new List<TyhpdefGenericParameter>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in doc.TemplateTags)
            {
                var name = (tag.ParameterName ?? "").Trim();
                if (name.Length == 0 || !seen.Add(name))
                {
                    continue;
                }

                string? constraint = null;
                if (!string.IsNullOrWhiteSpace(tag.TypeExpression))
                {
                    constraint = resolver.ResolveTypeExpression(PhpDocTypeParser.Normalize(tag.TypeExpression));
                }

                result.Add(new TyhpdefGenericParameter { Name = name, Constraint = constraint });
            }

            return result;
        }

        /// <summary>
        /// Copies the source <c>/** */</c> onto IR. Type tags are rewritten to the merged
        /// signature; the original summary/description are kept. Returns null when
        /// <c>--no-docs</c> or the source comment is empty.
        /// </summary>
        public string? CopyDocComment(
            string? raw,
            PhpDocBlock parsed,
            IReadOnlyDictionary<string, string>? mergedParamTypes,
            string? mergedReturnType,
            string? mergedVarType)
        {
            if (!this._options.IncludeDocComments || string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine("/**");
            if (!string.IsNullOrWhiteSpace(parsed.Summary))
            {
                foreach (var line in SplitLines(parsed.Summary))
                {
                    sb.Append(" * ");
                    sb.AppendLine(SanitizeCopiedDocLine(line));
                }
            }

            if (!string.IsNullOrWhiteSpace(parsed.Description))
            {
                if (!string.IsNullOrWhiteSpace(parsed.Summary))
                {
                    sb.AppendLine(" *");
                }

                foreach (var line in SplitLines(parsed.Description))
                {
                    sb.Append(" * ");
                    sb.AppendLine(SanitizeCopiedDocLine(line));
                }
            }

            var wroteTag = false;
            foreach (var tag in parsed.Tags)
            {
                var line = FormatTagLine(tag, mergedParamTypes, mergedReturnType, mergedVarType);
                if (line is null)
                {
                    continue;
                }

                if (!wroteTag && (!string.IsNullOrWhiteSpace(parsed.Summary) || !string.IsNullOrWhiteSpace(parsed.Description)))
                {
                    sb.AppendLine(" *");
                }

                wroteTag = true;
                foreach (var wrapped in SplitLines(line))
                {
                    sb.Append(" * ");
                    sb.AppendLine(SanitizeCopiedDocLine(wrapped));
                }
            }

            if (!parsed.Tags.Any(t => t.TagName.Equals("generated", StringComparison.OrdinalIgnoreCase)))
            {
                if (!wroteTag && (!string.IsNullOrWhiteSpace(parsed.Summary) || !string.IsNullOrWhiteSpace(parsed.Description)))
                {
                    sb.AppendLine(" *");
                }

                sb.AppendLine(" * @generated");
            }

            sb.Append(" */");
            return sb.ToString();
        }

        public static string InferConstType(IExpression? value)
        {
            var spelled = SpellLiteral(value, resolver: null, out var type);
            _ = spelled;
            return type;
        }

        public static string? SpellLiteral(IExpression? value)
            => SanitizeDefaultValue(SpellLiteral(value, resolver: null, out _));

        public static string? SpellLiteral(IExpression? value, PhpTypeNameResolver? resolver)
            => SanitizeDefaultValue(SpellLiteral(value, resolver, out _));

        public static bool IsPrivate(IEnumerable<PhpModifier>? modifiers)
            => modifiers?.Contains(PhpModifier.Private) == true;

        public static bool HasVisibility(IEnumerable<PhpModifier>? modifiers)
            => modifiers is not null
               && modifiers.Any(m => m is PhpModifier.Public or PhpModifier.Protected or PhpModifier.Private);

        public List<string> MethodModifiers(PhpModifierListAst? list, bool isInterface)
        {
            var modifiers = list?.Modifiers.ToList() ?? [];
            var result = new List<string>();
            if (isInterface)
            {
                result.Add("public");
            }
            else if (modifiers.Contains(PhpModifier.Public) || modifiers.Contains(PhpModifier.Var))
            {
                result.Add("public");
            }
            else if (modifiers.Contains(PhpModifier.Protected))
            {
                result.Add("protected");
            }
            else if (modifiers.Contains(PhpModifier.Private))
            {
                result.Add("private");
            }
            else
            {
                result.Add("public");
            }

            if (modifiers.Contains(PhpModifier.Static))
            {
                result.Add("static");
            }

            if (modifiers.Contains(PhpModifier.Abstract))
            {
                result.Add("abstract");
            }

            if (modifiers.Contains(PhpModifier.Final))
            {
                result.Add("final");
            }

            return result;
        }

        public List<string> PropertyModifiers(PhpModifierListAst? list)
        {
            var modifiers = list?.Modifiers.ToList() ?? [];
            var result = new List<string>();
            if (modifiers.Contains(PhpModifier.Public) || modifiers.Contains(PhpModifier.Var))
            {
                result.Add("public");
            }
            else if (modifiers.Contains(PhpModifier.Protected))
            {
                result.Add("protected");
            }
            else if (modifiers.Contains(PhpModifier.Private))
            {
                result.Add("private");
            }
            else
            {
                result.Add("public");
            }

            if (modifiers.Contains(PhpModifier.Static))
            {
                result.Add("static");
            }

            if (modifiers.Contains(PhpModifier.Readonly))
            {
                result.Add("readonly");
            }

            return result;
        }

        public List<string> ClassModifiers(PhpModifierListAst? list)
        {
            var result = new List<string>();
            foreach (var modifier in list?.Modifiers ?? [])
            {
                switch (modifier)
                {
                    case PhpModifier.Abstract:
                        result.Add("abstract");
                        break;
                    case PhpModifier.Final:
                        result.Add("final");
                        break;
                    case PhpModifier.Readonly:
                        result.Add("readonly");
                        break;
                }
            }

            return result;
        }

        public bool ShouldIncludeMember(PhpDocBlock doc, bool isPrivate)
        {
            if (isPrivate)
            {
                return false;
            }

            if (doc.IsInternal && !this._options.IncludeInternal)
            {
                return false;
            }

            if (doc.IsDeprecated && !this._options.IncludeDeprecated)
            {
                return false;
            }

            return true;
        }

        public static string StripDollar(string? name)
        {
            var trimmed = (name ?? "").Trim();
            return trimmed.StartsWith('$') ? trimmed[1..] : trimmed;
        }

        /// <summary>
        /// True when <paramref name="name"/> can be emitted as a tyhpdef
        /// <c>T_VARIABLE</c> / property name. <c>$this</c> is a lexer variable
        /// but not a legal harvested member name (PHPDoc <c>@return $this</c>
        /// is a type, not a parameter).
        /// </summary>
        internal static bool IsHarvestableMemberName(string? name)
        {
            var trimmed = StripDollar(name);
            if (trimmed.Length == 0 || trimmed == "<error>")
            {
                return false;
            }

            if (trimmed.Equals("this", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!char.IsLetter(trimmed[0]) && trimmed[0] != '_')
            {
                return false;
            }

            for (var i = 1; i < trimmed.Length; i++)
            {
                if (!char.IsLetterOrDigit(trimmed[i]) && trimmed[i] != '_')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Omits default expressions that would not round-trip through the tyhpdef parser
        /// (unbalanced delimiters, leftover <c>$this</c>, nowdocs, unquoted prose,
        /// <c>/regex/</c> bodies).
        /// </summary>
        internal static string? SanitizeDefaultValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.Contains("$this", StringComparison.Ordinal)
                || trimmed.Contains('\n')
                || trimmed.Contains('\r')
                || !IsBalanced(trimmed)
                || !IsEmitableDefaultLiteral(trimmed))
            {
                return null;
            }

            return trimmed;
        }

        private static readonly Regex ClassConstDefault = new(
            @"^\\?[A-Za-z_][A-Za-z0-9_\\]*::[A-Za-z_][A-Za-z0-9_]*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Global and namespaced constants (`PHP_INT_MIN`, `\PHP_INT_MAX`, `Foo\BAR`).
        private static readonly Regex GlobalConstDefault = new(
            @"^\\?[A-Za-z_][A-Za-z0-9_]*(\\[A-Za-z_][A-Za-z0-9_]*)*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Harvested defaults must already be a tyhpdef literal, a constant name, or
        /// <c>Class::CONST</c>. Nowdocs, heredocs, <c>/regex/</c>, and unquoted source
        /// text are dropped.
        /// </summary>
        private static bool IsEmitableDefaultLiteral(string trimmed)
        {
            if (trimmed is "true" or "false" or "null" or "[]")
            {
                return true;
            }

            if (trimmed.Length >= 2 && trimmed[0] is '\'' or '"')
            {
                return IsQuotedStringLiteral(trimmed);
            }

            if (trimmed.Length > 2 && trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Skip(2).All(c => char.IsAsciiHexDigit(c) || c is '_');
            }

            if (trimmed.Length > 2 && trimmed.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Skip(2).All(c => c is '0' or '1' or '_');
            }

            if (trimmed.Length > 2 && trimmed.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Skip(2).All(c => c is >= '0' and <= '7' or '_');
            }

            if (double.TryParse(
                    trimmed.TrimStart('+'),
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                return true;
            }

            if (GlobalConstDefault.IsMatch(trimmed))
            {
                return true;
            }

            return ClassConstDefault.IsMatch(trimmed);
        }

        /// <summary>
        /// A single quoted span with the closer as the last character. Concatenation such as
        /// <c>'a'.'b'</c> starts and ends with a quote but is not a tyhpdef string literal.
        /// </summary>
        private static bool IsQuotedStringLiteral(string trimmed)
        {
            var quote = trimmed[0];
            for (var i = 1; i < trimmed.Length; i++)
            {
                if (trimmed[i] == '\\' && i + 1 < trimmed.Length)
                {
                    i++;
                    continue;
                }

                if (trimmed[i] != quote)
                {
                    continue;
                }

                if (i != trimmed.Length - 1)
                {
                    return false;
                }

                return quote != '"' || !trimmed.Contains('$', StringComparison.Ordinal);
            }

            return false;
        }

        private static string SanitizeCopiedDocLine(string line)
            => (line ?? "").Replace("*/", "* /", StringComparison.Ordinal);

        private static string DropUnbalancedDelimiters(string type)
        {
            if (IsBalanced(type))
            {
                return type;
            }

            return "mixed";
        }

        internal static bool IsBalanced(string text)
        {
            var paren = 0;
            var angle = 0;
            var brace = 0;
            var bracket = 0;
            var quote = '\0';
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        continue;
                    }

                    if (c == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (c is '\'' or '"')
                {
                    quote = c;
                    continue;
                }

                switch (c)
                {
                    case '(':
                        paren++;
                        break;
                    case ')':
                        paren--;
                        if (paren < 0)
                        {
                            return false;
                        }

                        break;
                    case '<':
                        angle++;
                        break;
                    case '>':
                        angle--;
                        if (angle < 0)
                        {
                            return false;
                        }

                        break;
                    case '{':
                        brace++;
                        break;
                    case '}':
                        brace--;
                        if (brace < 0)
                        {
                            return false;
                        }

                        break;
                    case '[':
                        bracket++;
                        break;
                    case ']':
                        bracket--;
                        if (bracket < 0)
                        {
                            return false;
                        }

                        break;
                }
            }

            return paren == 0 && angle == 0 && brace == 0 && bracket == 0 && quote == '\0';
        }

        internal static bool IsWeakPhpHint(string? hint)
        {
            var trimmed = (hint ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return true;
            }

            var core = trimmed.TrimStart('?').TrimStart('\\');
            var pipe = core.IndexOf('|');
            var amp = core.IndexOf('&');
            if (pipe >= 0 || amp >= 0)
            {
                return false;
            }

            var generic = core.IndexOf('<');
            if (generic >= 0)
            {
                core = core[..generic];
            }

            return WeakPhpHints.Contains(core.Trim());
        }

        private static string StripNullablePrefixForBare(string hint)
            => hint.Trim();

        private string SpellTypeCore(IBase2Ast type, PhpTypeNameResolver resolver)
        {
            switch (type)
            {
                case PhpBuiltinTypeAst builtin:
                    return builtin.Identifier ?? "";
                case PhpNamedTypeAst named:
                    return resolver.ResolveName(SpellName(named.Name));
                case PhpTypeExpressionAst expr:
                    return SpellCompound(expr, resolver);
                default:
                    if (type is ITypeExpression && type.Identifier is { Length: > 0 } id)
                    {
                        return resolver.ResolveName(id);
                    }

                    return "mixed";
            }
        }

        private string SpellCompound(PhpTypeExpressionAst expr, PhpTypeNameResolver resolver)
        {
            var parts = new List<(string Text, bool IsIntersection)>();
            foreach (var child in expr.Types?.GetAllNotNull() ?? [])
            {
                var spelled = SpellTypeCore(child, resolver);
                if (spelled.Length > 0)
                {
                    var isIntersection = child is PhpTypeExpressionAst childExpr
                        && childExpr.TypeKind == PhpTypeKind.Intersection;
                    parts.Add((spelled, isIntersection));
                }
            }

            if (parts.Count == 0)
            {
                return expr.IsNullable ? "mixed" : "";
            }

            string joined;
            if (expr.TypeKind == PhpTypeKind.Intersection)
            {
                joined = string.Join("&", parts.Select(p => p.Text));
            }
            else if (expr.TypeKind == PhpTypeKind.Union || parts.Count > 1)
            {
                // DNF types (e.g. `(A&B)|null`): a union member that is itself an
                // intersection must be parenthesized, or the emitted tyhpdef is invalid
                // syntax (`A&B|null` does not round-trip through the tyhpdef parser).
                joined = string.Join("|", parts.Select(p => p.IsIntersection ? "(" + p.Text + ")" : p.Text));
            }
            else
            {
                joined = parts[0].Text;
            }

            if (expr.IsNullable && !joined.Contains("null", StringComparison.OrdinalIgnoreCase))
            {
                if (!joined.Contains('|', StringComparison.Ordinal) && !joined.Contains('&', StringComparison.Ordinal))
                {
                    return "?" + joined.TrimStart('?');
                }

                return joined + "|null";
            }

            return joined;
        }

        internal static string SpellName(IBase2Ast? name)
        {
            if (name is null)
            {
                return "";
            }

            if (name is TokenValueAst token)
            {
                return token.ValueString ?? token.Identifier ?? "";
            }

            return name.ValueString ?? name.Identifier ?? "";
        }

        private static string? SpellLiteral(IExpression? value, PhpTypeNameResolver? resolver, out string inferredType)
        {
            inferredType = "mixed";
            if (value is null)
            {
                return null;
            }

            if (value is PhpUnaryOpAst unary
                && unary.IsPrefix
                && (unary.Operator?.ValueString == "-" || unary.Operator?.Identifier == "-"))
            {
                var inner = SpellLiteral(unary.Operand, resolver, out inferredType);
                if (inner is null)
                {
                    return null;
                }

                return "-" + inner.TrimStart('+');
            }

            if (value is PhpEncapsStringAst encapsString)
            {
                inferredType = "string";
                return encapsString.ValueString ?? "''";
            }

            if (value is PhpEncapsListAst encapsList)
            {
                // Nowdoc and heredoc bodies are raw encaps text. The `<<<` delimiters
                // are not included, and the newline before the closer is the end token,
                // so a body of `hi` arrives with no newline and would pass as a constant.
                // Both forms use PhpStringType.Heredoc (T_START_HEREDOC). The value is
                // omitted, but the expression is still a string.
                if (encapsList.StringType == PhpStringType.Heredoc)
                {
                    inferredType = "string";
                    return null;
                }

                inferredType = "string";
                var parts = encapsList.GetAllNotNull().ToList();
                if (parts.Count == 1 && parts[0] is PhpEncapsStringAst single)
                {
                    return single.ValueString ?? encapsList.ValueString ?? "''";
                }

                var joined = string.Concat(parts.Select(p => p.ValueString ?? ""));
                return string.IsNullOrEmpty(joined) ? "''" : joined;
            }

            if (value is PhpScalarAst scalar)
            {
                switch (scalar.ScalarType)
                {
                    case PhpScalarType.String:
                        inferredType = "string";
                        return scalar.ValueString ?? "''";
                    case PhpScalarType.Float:
                        inferredType = "float";
                        return (scalar.ValueDecimal ?? 0m).ToString(CultureInfo.InvariantCulture);
                    default:
                        inferredType = "int";
                        return (scalar.ValueInt64 ?? 0L).ToString(CultureInfo.InvariantCulture);
                }
            }

            if (value is PhpArrayAst or PhpArrayPairListAst)
            {
                inferredType = "array";
                return "[]";
            }

            if (value is PhpDereferenceableAst dereferenceable
                && dereferenceable.Suffix is PhpClassConstantAccessAst constantAccess)
            {
                var className = SpellName(dereferenceable.Base as IBase2Ast);
                var member = SpellName(constantAccess.Member as IBase2Ast);
                if (className.Length > 0 && member.Length > 0)
                {
                    var resolved = resolver is null ? className : resolver.ResolveName(className);
                    inferredType = "mixed";
                    return resolved + "::" + member;
                }
            }

            if (value is PhpNameAst or TokenValueAst)
            {
                var text = (value.ValueString ?? value.Identifier ?? "").Trim();
                if (text.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    inferredType = "bool";
                    return text.ToLowerInvariant();
                }

                if (text.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    inferredType = "null";
                    return "null";
                }
            }

            return null;
        }

        private static string? FormatTagLine(
            PhpDocTag tag,
            IReadOnlyDictionary<string, string>? mergedParamTypes,
            string? mergedReturnType,
            string? mergedVarType)
        {
            var name = tag.TagName;
            if (name is "phpstan-param" or "psalm-param" or "phpstan-return" or "psalm-return"
                or "phpstan-var" or "psalm-var" or "phpstan-template" or "psalm-template"
                or "phpstan-type" or "psalm-type" or "phan-type"
                or "phpstan-import-type" or "psalm-import-type")
            {
                return null;
            }

            if (name is "param")
            {
                var paramName = tag.ParameterName ?? "";
                var type = mergedParamTypes is not null
                    && mergedParamTypes.TryGetValue(paramName, out var merged)
                    ? merged
                    : (tag.TypeExpression ?? "mixed");
                var desc = string.IsNullOrWhiteSpace(tag.Description) ? "" : " " + tag.Description.Replace("\n", "\n *     ", StringComparison.Ordinal);
                return "@param " + type + " $" + paramName + desc;
            }

            if (name is "return")
            {
                var type = mergedReturnType ?? tag.TypeExpression ?? "mixed";
                var desc = string.IsNullOrWhiteSpace(tag.Description) ? "" : " " + tag.Description.Replace("\n", "\n *     ", StringComparison.Ordinal);
                return "@return " + type + desc;
            }

            if (name is "var")
            {
                var type = mergedVarType ?? tag.TypeExpression ?? "mixed";
                var param = string.IsNullOrWhiteSpace(tag.ParameterName) ? "" : " $" + tag.ParameterName;
                var desc = string.IsNullOrWhiteSpace(tag.Description) ? "" : " " + tag.Description.Replace("\n", "\n *     ", StringComparison.Ordinal);
                return "@var " + type + param + desc;
            }

            if (name is "template" or "template-covariant" or "template-contravariant")
            {
                var of = string.IsNullOrWhiteSpace(tag.TypeExpression) ? "" : " of " + PhpDocTypeParser.Normalize(tag.TypeExpression);
                return "@template " + (tag.ParameterName ?? "T") + of;
            }

            if (name == "generated")
            {
                return "@generated";
            }

            var rest = string.IsNullOrWhiteSpace(tag.RawContent) ? "" : " " + tag.RawContent.Trim();
            return "@" + name + rest;
        }

        private static IEnumerable<string> SplitLines(string text)
            => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    /// <summary>
    /// Resolves PHP type names using the current namespace and <c>use</c> imports.
    /// </summary>
    public sealed class PhpTypeNameResolver
    {
        public string Namespace { get; }

        /// <summary>
        /// FQCN of the type currently being harvested, so
        /// <c>class InvalidArgumentException extends \InvalidArgumentException</c>
        /// keeps the global parent instead of becoming a self-extend.
        /// </summary>
        public string? CurrentTypeFqn { get; set; }

        private readonly Dictionary<string, string> _typeImports;

        private readonly IReadOnlyDictionary<string, string> _knownShortNames;

        private readonly IReadOnlySet<string> _declaredFqns;

        public PhpTypeNameResolver(
            string currentNamespace,
            IReadOnlyDictionary<string, string> typeImports,
            IReadOnlyDictionary<string, string>? knownShortNames = null,
            IReadOnlySet<string>? declaredFqns = null)
        {
            this.Namespace = (currentNamespace ?? "").Trim().Trim('\\');
            this._typeImports = new Dictionary<string, string>(typeImports, StringComparer.OrdinalIgnoreCase);
            this._knownShortNames = knownShortNames is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(knownShortNames, StringComparer.OrdinalIgnoreCase);
            this._declaredFqns = declaredFqns ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public string QualifyDeclaredName(string shortName)
        {
            var name = (shortName ?? "").Trim().TrimStart('\\');
            if (name.Length == 0)
            {
                return "";
            }

            if (this.Namespace.Length == 0)
            {
                return "\\" + name;
            }

            return "\\" + this.Namespace + "\\" + name;
        }

        public string ResolveName(string raw)
        {
            var name = (raw ?? "").Trim();
            if (name.Length == 0)
            {
                return "";
            }

            if (name.Equals("namespace", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("namespace\\", StringComparison.OrdinalIgnoreCase))
            {
                var rest = name.Equals("namespace", StringComparison.OrdinalIgnoreCase)
                    ? ""
                    : name["namespace\\".Length..];
                return this.QualifyDeclaredName(rest);
            }

            // A leading `\` is a real FQCN only when it has more than one segment
            // (`\Predis\Command\Command`). Harvest of same-namespace `extends Command`
            // / `use Mixin` often spells a single segment as `\Command` / `\Mixin`;
            // that is not the global type and must still be qualified.
            if (name.StartsWith('\\'))
            {
                if (name.IndexOf('\\', 1) >= 0)
                {
                    return name;
                }

                var bare = name.TrimStart('\\');
                if (bare.Length == 0)
                {
                    return name;
                }

                var resolvedBare = this.ResolveBareName(bare, qualifyUnknown: false);
                if (resolvedBare is not null)
                {
                    return resolvedBare;
                }

                var samePackage = this.QualifyDeclaredName(bare);
                if (this.IsDeclaredSamePackageType(samePackage))
                {
                    return samePackage;
                }

                return name;
            }

            return this.ResolveBareName(name, qualifyUnknown: true) ?? name;
        }

        private bool IsDeclaredSamePackageType(string fqn)
        {
            if (fqn.Length == 0 || this._declaredFqns.Count == 0)
            {
                return false;
            }

            if (this.CurrentTypeFqn is { Length: > 0 } current
                && fqn.Equals(current, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return this._declaredFqns.Contains(fqn);
        }

        private string? ResolveBareName(string name, bool qualifyUnknown)
        {
            var slash = name.IndexOf('\\');
            if (slash < 0)
            {
                if (PhpAstTypeExtractor.BuiltinTypes.Contains(name))
                {
                    return name.ToLowerInvariant() switch
                    {
                        "integer" => "int",
                        "boolean" => "bool",
                        "double" => "float",
                        _ => name.Equals("self", StringComparison.OrdinalIgnoreCase)
                             || name.Equals("static", StringComparison.OrdinalIgnoreCase)
                             || name.Equals("parent", StringComparison.OrdinalIgnoreCase)
                            ? name.ToLowerInvariant()
                            : CanonicalBuiltin(name),
                    };
                }

                if (this._typeImports.TryGetValue(name, out var imported))
                {
                    return imported.StartsWith('\\') ? imported : "\\" + imported.TrimStart('\\');
                }

                // A bare (no leading `\`, no `use` import) name in a real `extends` /
                // `implements` / trait `use` clause always resolves relative to the
                // current namespace in PHP — there is no runtime fallback to a global
                // class of the same short name. So when the current package actually
                // declares `\CurrentNs\Name`, that local type wins over a same-named
                // PHP/PSR catalog global (e.g. `Carbon\Exceptions\RuntimeException`'s
                // own `extends Exception` means its sibling local marker interface
                // `Carbon\Exceptions\Exception`, not the global `\Exception`).
                if (qualifyUnknown)
                {
                    var samePackage = this.QualifyDeclaredName(name);
                    if (this.IsDeclaredSamePackageType(samePackage))
                    {
                        return samePackage;
                    }
                }

                if (this._knownShortNames.TryGetValue(name, out var known))
                {
                    return known.StartsWith('\\') ? known : "\\" + known.TrimStart('\\');
                }

                return qualifyUnknown ? this.QualifyDeclaredName(name) : null;
            }

            var first = name[..slash];
            if (this._typeImports.TryGetValue(first, out var prefix))
            {
                var remainder = name[(slash + 1)..];
                var fqn = prefix.TrimEnd('\\') + "\\" + remainder;
                return fqn.StartsWith('\\') ? fqn : "\\" + fqn;
            }

            return qualifyUnknown ? this.QualifyDeclaredName(name) : null;
        }

        private static readonly Regex ImportableIdentifier = new(
            @"(?<![\w\\$])[A-Za-z_][A-Za-z0-9_]*(?:\\[A-Za-z_][A-Za-z0-9_]*)*",
            RegexOptions.Compiled);

        /// <summary>
        /// Qualifies only identifiers this file's <c>use</c> imports actually name (e.g.
        /// <c>Document</c> for <c>use Elastica\Document;</c>), inside an already-normalized
        /// tyhpdef type expression that may be a union/generic. Unlike <see cref="ResolveName"/>,
        /// a bare name with no matching import is left exactly as written rather than
        /// guessed into the current namespace — that guess is wrong for local PHPDoc
        /// pseudo-types (<c>@phpstan-type Options</c>) and would silently hide them from
        /// <c>TYHP3019</c>. A name that already contains <c>\</c> but no leading <c>\</c>
        /// is a real relative namespace (PHP resolves
        /// <c>FrameDecorator\AbstractFrameDecorator</c> under the current namespace) and
        /// is qualified the same way as <see cref="ResolveName"/>.
        /// </summary>
        public string ResolveImportedNamesOnly(string? type)
        {
            var trimmed = (type ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return trimmed;
            }

            return ImportableIdentifier.Replace(trimmed, match =>
            {
                var ident = match.Value;
                if (ident.Contains('\\', StringComparison.Ordinal))
                {
                    return this.ResolveName(ident);
                }

                if (PhpAstTypeExtractor.BuiltinTypes.Contains(ident))
                {
                    return match.Value;
                }

                if (this._typeImports.TryGetValue(ident, out var imported))
                {
                    return imported.StartsWith('\\') ? imported : "\\" + imported;
                }

                if (this._knownShortNames.TryGetValue(ident, out var known))
                {
                    return known.StartsWith('\\') ? known : "\\" + known;
                }

                return match.Value;
            });
        }

        /// <summary>
        /// Resolve class names inside an already-normalized tyhpdef type expression,
        /// including unqualified names nested in generics / unions / intersections
        /// (<c>ClassMetadata&lt;object&gt;</c> → FQCN). Template parameters and other
        /// undeclared identifiers in a composite type are left as written.
        /// </summary>
        public string ResolveTypeExpression(string? type)
        {
            var trimmed = (type ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return "mixed";
            }

            if (PhpAstTypeExtractor.BuiltinTypes.Contains(trimmed.TrimStart('?')))
            {
                return PhpAstTypeExtractor.ToTyhpdefType(trimmed);
            }

            return PhpAstTypeExtractor.ToTyhpdefType(this.ResolveNamesInTypeExpression(trimmed));
        }

        /// <summary>
        /// Walks identifiers so <c>@template T of ClassMetadata&lt;object&gt;</c> qualifies
        /// <c>ClassMetadata</c> the same way a bare <c>of ClassMetadata</c> does. A leading
        /// <c>\</c> FQCN is left alone. Unknown shorts in a composite type (generic
        /// parameters such as <c>T</c>) are not guessed into the current namespace.
        /// </summary>
        private string ResolveNamesInTypeExpression(string type)
        {
            var composite = type.Contains('<', StringComparison.Ordinal)
                || type.Contains('|', StringComparison.Ordinal)
                || type.Contains('&', StringComparison.Ordinal)
                || type.Contains('(', StringComparison.Ordinal);

            return ImportableIdentifier.Replace(type, match =>
            {
                if (match.Index > 0 && type[match.Index - 1] == '\\')
                {
                    return match.Value;
                }

                var ident = match.Value;
                if (ident.Contains('\\', StringComparison.Ordinal))
                {
                    return this.ResolveName(ident);
                }

                if (PhpAstTypeExtractor.BuiltinTypes.Contains(ident))
                {
                    return match.Value;
                }

                var known = this.ResolveBareName(ident, qualifyUnknown: false);
                if (known is not null)
                {
                    return known;
                }

                var samePackage = this.QualifyDeclaredName(ident);
                if (this.IsDeclaredSamePackageType(samePackage))
                {
                    return samePackage;
                }

                return composite ? match.Value : this.ResolveName(ident);
            });
        }

        private static string CanonicalBuiltin(string name)
        {
            if (name.Equals("integer", StringComparison.OrdinalIgnoreCase))
            {
                return "int";
            }

            if (name.Equals("boolean", StringComparison.OrdinalIgnoreCase))
            {
                return "bool";
            }

            if (name.Equals("double", StringComparison.OrdinalIgnoreCase))
            {
                return "float";
            }

            if (name.Equals("long", StringComparison.OrdinalIgnoreCase))
            {
                return "int";
            }

            return PhpAstTypeExtractor.BuiltinTypes.Contains(name) ? name.ToLowerInvariant() : name;
        }
    }
}
