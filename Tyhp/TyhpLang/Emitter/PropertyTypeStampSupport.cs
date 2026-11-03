using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// <c>#[\Tyhp\PropertyType('…')]</c>: the declared Tyhp type of a property or promoted
    /// constructor parameter, stamped when the PHP hint loses information (type arguments,
    /// struct types, source type aliases). <c>\Tyhp\Type::fromProperty()</c> reads it.
    /// The spelling is the <c>\Tyhp\Type::parse()</c> format: class names fully qualified
    /// without a leading <c>\</c>, structs as <c>struct Name</c>, class type arguments positional.
    /// </summary>
    internal static class PropertyTypeStampSupport
    {
        private const string AttributeFqn = "Tyhp\\PropertyType";

        private static readonly HashSet<string> ParsableBuiltins = new(StringComparer.OrdinalIgnoreCase)
        {
            "string", "int", "float", "bool", "true", "false", "null", "void", "mixed", "never",
            "object", "callable", "iterable", "resource", "array", "decimal",
        };

        public static bool IsPropertyTypeAttribute(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol bound }
                && IsAttributeName(bound.FullyQualifiedName))
            {
                return true;
            }

            return attribute.Name is PhpNameAst name
                && (name.ValueString ?? name.Identifier) is { } text
                && text.Trim().StartsWith('\\')
                && IsAttributeName(text);
        }

        /// <summary>
        /// Stamp line for <paramref name="type"/>, or null when the PHP hint already says
        /// everything (or the type has a part the runtime cannot spell, such as a type parameter).
        /// </summary>
        public static string? TryFormatAttributeLine(ITypeExpression? type)
        {
            var spelled = TrySpellLossy(type);
            return spelled is null
                ? null
                : "#[\\Tyhp\\PropertyType('" + spelled.Replace("\\", "\\\\").Replace("'", "\\'") + "')]";
        }

        /// <summary>
        /// <c>\Tyhp\Type::parse()</c> spelling of <paramref name="type"/> when its PHP hint loses
        /// information; null when the hint is exact or the type cannot be spelled at runtime.
        /// </summary>
        public static string? TrySpellLossy(ITypeExpression? type)
        {
            if (type is null)
            {
                return null;
            }

            var lossy = false;
            var spelled = Spell(type, ref lossy);
            return spelled is not null && lossy ? spelled : null;
        }

        private static bool IsAttributeName(string? name)
            => name is not null
                && name.Trim().TrimStart('\\').Equals(AttributeFqn, StringComparison.OrdinalIgnoreCase);

        private static string? Spell(ITypeExpression type, ref bool lossy)
        {
            switch (type)
            {
                case PhpTypeExpressionAst composite:
                    return SpellComposite(composite, ref lossy);
                case PhpBuiltinTypeAst builtin:
                    var builtinName = (builtin.Identifier ?? "").Trim().TrimStart('\\').ToLowerInvariant();
                    return ParsableBuiltins.Contains(builtinName)
                        ? AppendTypeArguments(builtinName, builtin, ref lossy)
                        : null;
                case PhpNamedTypeAst named:
                    var head = SpellNamedHead(named, ref lossy);
                    return head is null ? null : AppendTypeArguments(head, named, ref lossy);
                default:
                    return null;
            }
        }

        private static string? SpellNamedHead(PhpNamedTypeAst named, ref bool lossy)
        {
            var bound = named.BoundSymbol ?? named.Name?.BoundSymbol;
            switch (bound)
            {
                case ObjectDeclarationSymbol { FullyQualifiedName: { Length: > 0 } fqn } obj:
                    if (obj.IsStruct)
                    {
                        lossy = true;
                        return "struct " + fqn.TrimStart('\\');
                    }

                    return fqn.TrimStart('\\');
                case ObjectTypeAliasSymbol classAlias when !IsTyhpdefSymbol(classAlias):
                    // Class-level aliases register as `Owner\Name`.
                    if (classAlias.ContainingScope?.DeclarationSymbol is not ObjectDeclarationSymbol
                        {
                            FullyQualifiedName: { Length: > 0 } ownerFqn,
                        })
                    {
                        return null;
                    }

                    lossy = true;
                    return ownerFqn.TrimStart('\\') + "\\" + classAlias.Name;
                case TypeAliasSymbol
                    when bound.FullyQualifiedName is { Length: > 0 } aliasFqn && !IsTyhpdefSymbol(bound):
                    // Source aliases register with `\Tyhp\Type::register()` under their FQN.
                    lossy = true;
                    return aliasFqn.TrimStart('\\');
                case BuiltInTypeSymbol builtin:
                    var name = builtin.Name.Trim().TrimStart('\\').ToLowerInvariant();
                    return ParsableBuiltins.Contains(name) ? name : null;
                default:
                    return null;
            }
        }

        private static string? SpellComposite(PhpTypeExpressionAst composite, ref bool lossy)
        {
            if (composite.IsStatic)
            {
                return null;
            }

            // Struct members are rewritten to `array` before emit; the wrapper keeps the binding.
            if (composite.BoundSymbol is ObjectDeclarationSymbol { IsStruct: true, FullyQualifiedName: { Length: > 0 } structFqn }
                && composite.Types?.GetAllNotNull().Count() == 1)
            {
                lossy = true;
                var structSpelling = "struct " + structFqn.TrimStart('\\');
                return composite.IsNullable ? "?" + structSpelling : structSpelling;
            }

            var parts = new List<string>();
            foreach (var child in composite.Types?.GetAllNotNull() ?? [])
            {
                var spelled = Spell(child, ref lossy);
                if (spelled is null)
                {
                    return null;
                }

                var wrap = composite.TypeKind != PhpTypeKind.Intersection
                    && child is PhpTypeExpressionAst { TypeKind: PhpTypeKind.Intersection };
                parts.Add(wrap ? "(" + spelled + ")" : spelled);
            }

            if (parts.Count == 0)
            {
                return null;
            }

            var joined = string.Join(composite.TypeKind == PhpTypeKind.Intersection ? "&" : "|", parts);
            if (!composite.IsNullable || joined is "mixed" or "null" || parts.Contains("null"))
            {
                return joined;
            }

            return parts.Count == 1 && !joined.Contains('&') && !joined.StartsWith('?')
                ? "?" + joined
                : joined + "|null";
        }

        private static string? AppendTypeArguments(string head, IBase2Ast node, ref bool lossy)
        {
            var args = TyhpdefTypeSpeller.GetGenericTypeArguments(node);
            if (args.Count == 0)
            {
                return head;
            }

            var spelled = new List<string>();
            foreach (var arg in args)
            {
                var argText = Spell(arg, ref lossy);
                if (argText is null)
                {
                    return null;
                }

                spelled.Add(argText);
            }

            if (spelled.Any(a => a != "mixed"))
            {
                lossy = true;
            }

            return head + "<" + string.Join(", ", spelled) + ">";
        }

        private static bool IsTyhpdefSymbol(IBaseSymbol symbol)
        {
            var file = symbol.SourceFile ?? "";
            return file.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                || file.Contains("<tyhpdef:", StringComparison.OrdinalIgnoreCase)
                || symbol is BaseSymbol { DeclaringAstNode.LanguageMode: "tyhpdef" };
        }
    }
}
