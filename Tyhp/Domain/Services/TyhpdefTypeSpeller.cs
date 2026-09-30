using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Spells a bound Tyhp type expression as tyhpdef source. Unlike
    /// <see cref="PhpAstTypeExtractor.SpellType"/> (PHP hints for Reflection / PHP-source harvest), this keeps
    /// generic arguments, builtin keywords such as <c>struct</c>, generic parameter names,
    /// and utility types un-namespaced, and qualifies class/alias names from
    /// <see cref="IBase2Ast.BoundSymbol"/> rather than the declaring namespace.
    /// </summary>
    internal static class TyhpdefTypeSpeller
    {
        public static string? Spell(ITypeExpression? type)
        {
            if (type is null)
            {
                return null;
            }

            var spelled = SpellCore(type);
            return string.IsNullOrWhiteSpace(spelled) ? null : spelled;
        }

        private static string SpellCore(ITypeExpression type) =>
            type switch
            {
                PhpBuiltinTypeAst builtin => AppendTypeArguments(NormalizeBuiltin(builtin.Identifier ?? ""), builtin),
                PhpNamedTypeAst named => AppendTypeArguments(SpellNamedHead(named), named),
                PhpTypeExpressionAst composite => SpellComposite(composite),
                TyhpCallableShapeAst callable => SpellCallableShape(callable),
                TyhpReturnTypeGuardAst guard => SpellTypeGuard(guard),
                TyhpEllipsisTypeAst => "...",
                TyhpPostfixEllipsisTypeAst postfix => SpellRequired(postfix.InnerType) + "...",
                TyhpTemplateStringTypeAst template => template.ValueString ?? "string",
                _ => SpellFallback(type),
            };

        private static string SpellRequired(ITypeExpression? type)
            => Spell(type) ?? "mixed";

        private static string SpellFallback(ITypeExpression type)
        {
            var bound = type.BoundSymbol;
            if (bound != null)
            {
                return SpellBoundHead(bound, type.Identifier ?? type.ValueString ?? "");
            }

            var written = type.Identifier ?? type.ValueString ?? "";
            return string.IsNullOrWhiteSpace(written) ? "mixed" : SpellWrittenHead(written);
        }

        private static string SpellNamedHead(PhpNamedTypeAst named)
        {
            var written = NameText(named.Name);
            if (IsRelativeClassKeyword(written))
            {
                return written.ToLowerInvariant();
            }

            var bound = named.BoundSymbol ?? named.Name?.BoundSymbol;
            return SpellBoundHead(bound, written);
        }

        private static string SpellBoundHead(IBaseSymbol? bound, string written)
        {
            switch (bound)
            {
                case GenericTypeParameterSymbol parameter:
                    return parameter.Name;
                case BuiltInTypeSymbol builtin:
                    return NormalizeBuiltin(FirstNonEmpty(written, builtin.Name));
                case BuiltInUtilityTypeSymbol utility:
                    return utility.Name;
                case { FullyQualifiedName: { Length: > 0 } fqn }
                    when bound is ObjectDeclarationSymbol
                        or TypeAliasSymbol
                        or ObjectTypeAliasSymbol
                        or FunctionDeclarationSymbol:
                    return RootAnchor(fqn);
            }

            return SpellWrittenHead(written);
        }

        private static string SpellWrittenHead(string written)
        {
            var trimmed = (written ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return "mixed";
            }

            if (IsRelativeClassKeyword(trimmed) || IsBuiltinName(trimmed))
            {
                return NormalizeBuiltin(trimmed);
            }

            if (trimmed.StartsWith('\\'))
            {
                return trimmed;
            }

            return trimmed.Contains('\\', StringComparison.Ordinal) ? "\\" + trimmed : trimmed;
        }

        private static string SpellComposite(PhpTypeExpressionAst expr)
        {
            if (expr.IsStatic)
            {
                return "static";
            }

            var parts = new List<(string Text, bool WrapInUnion, bool WrapInIntersection)>();
            foreach (var child in expr.Types?.GetAllNotNull() ?? [])
            {
                var spelled = Spell(child);
                if (string.IsNullOrWhiteSpace(spelled))
                {
                    continue;
                }

                var isCallableShape = TyhpCallableShapeAst.Find(child) is not null;
                var isIntersection = child is PhpTypeExpressionAst { TypeKind: PhpTypeKind.Intersection };
                parts.Add((spelled, isIntersection || isCallableShape, isCallableShape));
            }

            if (parts.Count == 0)
            {
                return expr.IsNullable ? "mixed" : "";
            }

            string joined;
            if (expr.TypeKind == PhpTypeKind.Intersection)
            {
                joined = string.Join("&", parts.Select(p => p.WrapInIntersection ? "(" + p.Text + ")" : p.Text));
            }
            else if (expr.TypeKind == PhpTypeKind.Union || parts.Count > 1)
            {
                joined = string.Join("|", parts.Select(p => p.WrapInUnion ? "(" + p.Text + ")" : p.Text));
            }
            else
            {
                joined = parts[0].Text;
            }

            if (!expr.IsNullable || joined.Contains("null", StringComparison.OrdinalIgnoreCase))
            {
                return joined;
            }

            if (!joined.Contains('|', StringComparison.Ordinal) && !joined.Contains('&', StringComparison.Ordinal))
            {
                return "?" + joined.TrimStart('?');
            }

            return joined + "|null";
        }

        private static string SpellCallableShape(TyhpCallableShapeAst shape)
        {
            var returnType = SpellRequired(shape.ReturnType);
            if (shape.IsUnknownArity)
            {
                return "callable(...): " + returnType;
            }

            var parameters = new List<string>();
            foreach (var parameter in shape.Parameters?.GetAllNotNull() ?? [])
            {
                parameters.Add(SpellCallableShapeParameter(parameter));
            }

            return "callable(" + string.Join(", ", parameters) + "): " + returnType;
        }

        private static string SpellCallableShapeParameter(TyhpCallableShapeParameterAst parameter)
        {
            var spelled = SpellRequired(parameter.TypeExpression);
            if (parameter.IsRef)
            {
                spelled += " &";
            }

            var name = parameter.SourceParameterName;
            if (!string.IsNullOrEmpty(name) && name[0] != '$')
            {
                name = "$" + name;
            }

            if (parameter.IsVariadic)
            {
                spelled += string.IsNullOrEmpty(name) ? " ..." : " ..." + name;
            }
            else if (!string.IsNullOrEmpty(name))
            {
                spelled += spelled.EndsWith('&') ? name : " " + name;
            }

            if (parameter.IsOptional)
            {
                spelled += " =";
            }

            return spelled;
        }

        private static string SpellTypeGuard(TyhpReturnTypeGuardAst guard)
        {
            var subject = SpellGuardSubject(guard.GuardSubject, guard.GuardVariable);
            return subject + " is " + SpellRequired(guard.TypeExpression);
        }

        private static string SpellGuardSubject(IExpression? subject, TokenValueAst? variable)
        {
            if (!string.IsNullOrEmpty(variable?.ValueString))
            {
                return variable!.ValueString!;
            }

            switch (subject)
            {
                case PhpVariableAst { VariableToken.ValueString: { Length: > 0 } name }:
                    return name;
                case PhpDereferenceableAst dereferenceable
                    when dereferenceable.Suffix is PhpArrayAccessAst access:
                    var receiver = SpellGuardSubject(dereferenceable.Base as IExpression, null);
                    var index = SpellIndex(access.IndexExpression);
                    return receiver + "[" + index + "]";
                case { ValueString: { Length: > 0 } text }:
                    return text;
                default:
                    return "$value";
            }
        }

        private static string SpellIndex(IExpression? index) =>
            index switch
            {
                PhpVariableAst { VariableToken.ValueString: { Length: > 0 } name } => name,
                PhpScalarAst scalar => scalar.ValueString
                    ?? scalar.ValueInt64?.ToString()
                    ?? scalar.Identifier
                    ?? "0",
                { ValueString: { Length: > 0 } text } => text,
                { Identifier: { Length: > 0 } id } => id,
                _ => "0",
            };

        private static string AppendTypeArguments(string head, IBase2Ast node)
        {
            if (string.IsNullOrWhiteSpace(head))
            {
                return head;
            }

            var args = GetGenericTypeArguments(node);
            if (args.Count == 0)
            {
                return head;
            }

            var spelled = args
                .Select(SpellRequired)
                .Where(a => a.Length > 0)
                .ToList();
            return spelled.Count == 0 ? head : head + "<" + string.Join(", ", spelled) + ">";
        }

        private static IReadOnlyList<ITypeExpression> GetGenericTypeArguments(IBase2Ast node)
        {
            if (TryAddonTypeArguments(node, out var fromNode))
            {
                return fromNode;
            }

            if (node is PhpNamedTypeAst named)
            {
                if (named.Name != null && TryAddonTypeArguments(named.Name, out var fromName))
                {
                    return fromName;
                }

                if (named.Name is TyhpGenericIdentifierAst genericOnNamed)
                {
                    return TypeArgumentsFromIdentifier(genericOnNamed);
                }
            }

            if (node is TyhpGenericIdentifierAst generic)
            {
                return TypeArgumentsFromIdentifier(generic);
            }

            return [];
        }

        private static bool TryAddonTypeArguments(IBase2Ast node, out IReadOnlyList<ITypeExpression> args)
        {
            foreach (var key in new[] { "typeName", "identifier" })
            {
                if (node.AstGrammarAddons.TryGetValue(key, out var addon)
                    && addon is PhpTypeExpressionListAst list)
                {
                    var collected = list.GetAllNotNull().ToList();
                    if (collected.Count > 0)
                    {
                        args = collected;
                        return true;
                    }
                }
            }

            args = [];
            return false;
        }

        private static IReadOnlyList<ITypeExpression> TypeArgumentsFromIdentifier(TyhpGenericIdentifierAst generic)
        {
            if (generic.GenericArguments is PhpTypeExpressionListAst usage)
            {
                return usage.GetAllNotNull().ToList();
            }

            if (generic.GenericArguments is TyhpGenericsTypeArgumentListAst declared)
            {
                return declared.GetAllNotNull()
                    .Select(a => a.TypeConstraint ?? a.DefaultType)
                    .OfType<ITypeExpression>()
                    .ToList();
            }

            return [];
        }

        private static string NameText(IExpression? name) =>
            name switch
            {
                TyhpGenericIdentifierAst generic => generic.ValueString ?? generic.Identifier ?? "",
                PhpNameAst n => n.ValueString ?? n.Identifier ?? "",
                _ => name?.ValueString ?? name?.Identifier ?? "",
            };

        private static string RootAnchor(string fqn)
        {
            var trimmed = fqn.Trim();
            return trimmed.StartsWith('\\') ? trimmed : "\\" + trimmed;
        }

        private static string NormalizeBuiltin(string name)
        {
            var trimmed = (name ?? "").Trim().TrimStart('\\');
            return trimmed.ToLowerInvariant() switch
            {
                "integer" => "int",
                "boolean" => "bool",
                "double" => "float",
                "decimal" => "decimal",
                "struct" => "struct",
                "self" or "static" or "parent" => trimmed.ToLowerInvariant(),
                _ => IsBuiltinName(trimmed) ? trimmed.ToLowerInvariant() : trimmed,
            };
        }

        private static bool IsBuiltinName(string name)
        {
            var trimmed = name.Trim().TrimStart('\\');
            return PhpAstTypeExtractor.BuiltinTypes.Contains(trimmed)
                || trimmed.Equals("struct", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("decimal", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRelativeClassKeyword(string text) =>
            string.Equals(text, "self", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "static", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "parent", StringComparison.OrdinalIgnoreCase);

        private static string FirstNonEmpty(string first, string second)
            => string.IsNullOrWhiteSpace(first) ? second : first;
    }
}
