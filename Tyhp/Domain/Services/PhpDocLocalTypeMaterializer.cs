using System.Text;
using System.Text.RegularExpressions;
using Tyhp.Domain.Services.PhpDoc;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Turns PHPDoc local type aliases (<c>@phpstan-type</c>, <c>@psalm-type</c>,
    /// <c>@phan-type</c>) and imports (<c>@phpstan-import-type</c> /
    /// <c>@psalm-import-type</c>) into tyhpdef structs or <c>type</c> aliases,
    /// and rewrites references to those names in signatures.
    /// Array / list shapes become structs; every other RHS becomes a type alias.
    /// When the tag is scoped to a class-like (or function), the emitted name is
    /// prefixed with that owner so reused names like <c>Options</c> do not collide.
    /// </summary>
    public sealed class PhpDocLocalTypeMaterializer
    {
        private static readonly Regex Identifiers = new(
            @"(?<![\w\\$])[A-Za-z_][A-Za-z0-9_]*",
            RegexOptions.Compiled);

        private readonly Dictionary<string, string> _localToEmitted;

        private PhpDocLocalTypeMaterializer(Dictionary<string, string> localToEmitted)
        {
            this._localToEmitted = localToEmitted;
            this.Structs = [];
            this.Aliases = [];
        }

        public List<TyhpdefClassDeclaration> Structs { get; }

        public List<TyhpdefTypeAlias> Aliases { get; }

        public static PhpDocLocalTypeMaterializer Build(
            PhpDocBlock doc,
            string? ownerName,
            PhpTypeNameResolver resolver)
        {
            ArgumentNullException.ThrowIfNull(doc);
            ArgumentNullException.ThrowIfNull(resolver);

            var owner = (ownerName ?? "").Trim();
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in doc.TypeAliasTags)
            {
                var local = (tag.ParameterName ?? "").Trim();
                if (local.Length == 0 || map.ContainsKey(local))
                {
                    continue;
                }

                map[local] = PrefixName(owner, local);
            }

            foreach (var tag in doc.ImportTypeTags)
            {
                var local = (tag.ParameterName ?? "").Trim();
                if (local.Length == 0 || map.ContainsKey(local))
                {
                    continue;
                }

                var fromClass = (tag.TypeExpression ?? "").Trim();
                var imported = (tag.Description ?? local).Trim();
                if (fromClass.Length == 0 || imported.Length == 0)
                {
                    continue;
                }

                var sourceFqcn = resolver.ResolveName(fromClass);
                var sourceShort = ShortName(sourceFqcn);
                var emittedShort = PrefixName(sourceShort, imported);
                map[local] = QualifyWithSource(sourceFqcn, emittedShort, resolver.Namespace);
            }

            var materializer = new PhpDocLocalTypeMaterializer(map);
            var seenEmitted = new HashSet<string>(map.Values, StringComparer.Ordinal);
            foreach (var tag in doc.TypeAliasTags)
            {
                var local = (tag.ParameterName ?? "").Trim();
                if (local.Length == 0 || !map.TryGetValue(local, out var emitted))
                {
                    continue;
                }

                var raw = (tag.TypeExpression ?? "").Trim();
                if (raw.Length == 0)
                {
                    continue;
                }

                if (PhpDocTypeParser.TryParseArrayShape(raw, out var fields))
                {
                    materializer.AddStruct(emitted, fields, resolver, seenEmitted);
                }
                else
                {
                    var aliased = materializer.Rewrite(
                        resolver.ResolveImportedNamesOnly(PhpDocTypeParser.Normalize(raw)));
                    materializer.Aliases.Add(new TyhpdefTypeAlias
                    {
                        Name = emitted,
                        AliasedType = PhpAstTypeExtractor.ToTyhpdefType(
                            string.IsNullOrWhiteSpace(aliased) ? "mixed" : aliased),
                    });
                }
            }

            return materializer;
        }

        public string Rewrite(string? type)
        {
            var trimmed = (type ?? "").Trim();
            if (trimmed.Length == 0 || this._localToEmitted.Count == 0)
            {
                return trimmed;
            }

            return Identifiers.Replace(trimmed, match =>
            {
                var ident = match.Value;
                if (PhpAstTypeExtractor.BuiltinTypes.Contains(ident))
                {
                    return ident;
                }

                return this._localToEmitted.TryGetValue(ident, out var emitted) ? emitted : ident;
            });
        }

        private void AddStruct(
            string emittedName,
            IReadOnlyList<PhpDocTypeParser.ArrayShapeField> fields,
            PhpTypeNameResolver resolver,
            HashSet<string> seenEmitted)
        {
            var properties = new List<TyhpdefProperty>();
            foreach (var field in fields)
            {
                string type;
                if (field.NestedShape is { Count: >= 0 })
                {
                    var nestedName = UniqueEmitted(emittedName + Pascalize(field.Key), seenEmitted);
                    this.AddStruct(nestedName, field.NestedShape, resolver, seenEmitted);
                    type = nestedName;
                }
                else
                {
                    type = this.Rewrite(
                        resolver.ResolveImportedNamesOnly(
                            string.IsNullOrWhiteSpace(field.Type) ? "mixed" : field.Type));
                    if (string.IsNullOrWhiteSpace(type))
                    {
                        type = "mixed";
                    }
                }

                if (field.Optional)
                {
                    type = MakeOptional(type);
                }

                properties.Add(new TyhpdefProperty
                {
                    Name = StructPropertyName(field),
                    Type = PhpAstTypeExtractor.ToTyhpdefType(type),
                });
            }

            this.Structs.Add(new TyhpdefClassDeclaration
            {
                Kind = "struct",
                Name = emittedName,
                Properties = properties,
            });
        }

        private static string PrefixName(string owner, string typeName)
        {
            if (owner.Length == 0)
            {
                return typeName;
            }

            if (typeName.StartsWith(owner, StringComparison.Ordinal))
            {
                return typeName;
            }

            return owner + typeName;
        }

        private static string ShortName(string fqcn)
        {
            var trimmed = (fqcn ?? "").Trim().TrimStart('\\');
            var slash = trimmed.LastIndexOf('\\');
            return slash < 0 ? trimmed : trimmed[(slash + 1)..];
        }

        private static string QualifyWithSource(string sourceFqcn, string emittedShort, string currentNs)
        {
            var trimmed = (sourceFqcn ?? "").Trim().TrimStart('\\');
            var slash = trimmed.LastIndexOf('\\');
            var sourceNs = slash < 0 ? "" : trimmed[..slash];
            if (string.Equals(sourceNs, (currentNs ?? "").Trim().Trim('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return emittedShort;
            }

            if (sourceNs.Length == 0)
            {
                return "\\" + emittedShort;
            }

            return "\\" + sourceNs + "\\" + emittedShort;
        }

        private static string UniqueEmitted(string candidate, HashSet<string> seen)
        {
            var name = candidate;
            var n = 2;
            while (!seen.Add(name))
            {
                name = candidate + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                n++;
            }

            return name;
        }

        private static string MakeOptional(string type)
        {
            var trimmed = type.Trim();
            if (trimmed.Length == 0)
            {
                return "mixed";
            }

            if (trimmed.StartsWith('?')
                || trimmed.Equals("mixed", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase)
                || ContainsNullMember(trimmed))
            {
                return trimmed;
            }

            if (trimmed.Contains('|', StringComparison.Ordinal) || trimmed.Contains('&', StringComparison.Ordinal))
            {
                return trimmed + "|null";
            }

            return "?" + trimmed;
        }

        private static bool ContainsNullMember(string type)
        {
            var depthAngle = 0;
            var depthParen = 0;
            var start = 0;
            for (var i = 0; i <= type.Length; i++)
            {
                var c = i < type.Length ? type[i] : '|';
                switch (c)
                {
                    case '<':
                        depthAngle++;
                        break;
                    case '>':
                        if (depthAngle > 0)
                        {
                            depthAngle--;
                        }

                        break;
                    case '(':
                        depthParen++;
                        break;
                    case ')':
                        if (depthParen > 0)
                        {
                            depthParen--;
                        }

                        break;
                    case '|' when depthAngle == 0 && depthParen == 0:
                        var member = type[start..i].Trim().TrimStart('?');
                        if (member.Equals("null", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        start = i + 1;
                        break;
                }
            }

            return false;
        }

        private static string StructPropertyName(PhpDocTypeParser.ArrayShapeField field)
        {
            var key = field.Key;
            if (field.KeyIsInteger || LooksLikeInteger(key))
            {
                return key + " as $_" + key.TrimStart('-');
            }

            if (field.KeyIsQuotedString || !IsValidIdentifier(key))
            {
                return "'" + key.Replace("'", "\\'", StringComparison.Ordinal) + "' as $" + SanitizeIdent(key);
            }

            return key;
        }

        private static bool LooksLikeInteger(string key)
        {
            if (key.Length == 0)
            {
                return false;
            }

            var i = key[0] == '-' ? 1 : 0;
            if (i >= key.Length)
            {
                return false;
            }

            for (; i < key.Length; i++)
            {
                if (key[i] is < '0' or > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidIdentifier(string key)
        {
            if (key.Length == 0 || !(char.IsAsciiLetter(key[0]) || key[0] == '_'))
            {
                return false;
            }

            for (var i = 1; i < key.Length; i++)
            {
                if (!(char.IsAsciiLetterOrDigit(key[i]) || key[i] == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string SanitizeIdent(string key)
        {
            var sb = new StringBuilder(key.Length);
            foreach (var c in key)
            {
                if (char.IsAsciiLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }

            if (sb.Length == 0 || !(char.IsAsciiLetter(sb[0]) || sb[0] == '_'))
            {
                sb.Insert(0, '_');
            }

            return sb.ToString();
        }

        private static string Pascalize(string key)
        {
            if (LooksLikeInteger(key))
            {
                return "_" + key.TrimStart('-');
            }

            var sb = new StringBuilder(key.Length);
            var upper = true;
            foreach (var c in key)
            {
                if (c is '_' or '-' or ' ' or '.')
                {
                    upper = true;
                    continue;
                }

                if (!char.IsAsciiLetterOrDigit(c))
                {
                    upper = true;
                    continue;
                }

                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }

            if (sb.Length == 0)
            {
                return "Nested";
            }

            if (!(char.IsAsciiLetter(sb[0]) || sb[0] == '_'))
            {
                sb.Insert(0, '_');
            }

            return sb.ToString();
        }
    }
}
