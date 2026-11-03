using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
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

        /// <summary>
        /// Spells a raw <c>extends</c> class-name node (not an <see cref="ITypeExpression"/>), keeping
        /// any generic arguments and qualifying the head from its bound symbol, or from
        /// <paramref name="resolved"/> when the name node was never bound.
        /// </summary>
        public static string? SpellClassName(IClassName? className, IBaseSymbol? resolved = null)
        {
            if (className is null)
            {
                return null;
            }

            var written = className is PhpNameAst name
                ? NameText(name)
                : className.ValueString ?? className.Identifier ?? "";
            if (string.IsNullOrWhiteSpace(written))
            {
                return null;
            }

            var spelled = AppendTypeArguments(SpellBoundHead(className.BoundSymbol ?? resolved, written), className);
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
                TyhpTemplateStringTypeAst template => SpellTemplate(template),
                _ => SpellFallback(type),
            };

        /// <summary>
        /// Re-spells a template string type from its source parts. Literal text (including hole
        /// quantifiers) is kept as written; hole names are qualified from their bound symbols.
        /// </summary>
        private static string SpellTemplate(TyhpTemplateStringTypeAst template)
        {
            var text = new System.Text.StringBuilder("\"");
            foreach (var part in template.EncapsList?.GetAllNotNull() ?? [])
            {
                var isHole = part is PhpNameAst || part is IExpression and not PhpEncapsStringAst and not TokenValueAst;
                if (!isHole)
                {
                    text.Append(UnquoteRaw((part as IBase2Ast)?.ValueString));
                    continue;
                }

                var spelledHole = SpellTemplateHole((IExpression)part);
                if (spelledHole is null)
                {
                    return "string";
                }

                text.Append("${").Append(spelledHole).Append('}');
            }

            return text.Append('"').ToString();
        }

        private static string? SpellTemplateHole(IExpression hole)
        {
            switch (hole)
            {
                case PhpNameAst name:
                {
                    var written = NameText(name);
                    return string.IsNullOrWhiteSpace(written)
                        ? null
                        : AppendTypeArguments(SpellBoundHead(name.BoundSymbol, written), name);
                }
                case ITypeExpression type:
                    return Spell(type);
                case PhpBinaryOpAst { Left: IExpression left, Right: IExpression right }:
                {
                    var spelledLeft = SpellTemplateHole(left);
                    var spelledRight = SpellTemplateHole(right);
                    return spelledLeft is null || spelledRight is null ? null : spelledLeft + "|" + spelledRight;
                }
                case PhpUnaryOpAst { Operand: PhpScalarAst operand } unary
                    when unary.Operator?.ValueString is "-" or "+":
                    var spelledOperand = SpellTemplateHole(operand);
                    return spelledOperand is null ? null : unary.Operator.ValueString + spelledOperand;
                case PhpScalarAst scalar:
                    return scalar.AstChildren.FirstOrDefault() is TokenValueAst { ValueString: { Length: > 0 } scalarText }
                        ? scalarText
                        : null;
                case TokenValueAst { ValueString: { Length: > 0 } token }:
                    return token;
                case PhpEncapsListAst encaps when PhpStringLiteralHelper.TryGetStaticLiteral(encaps, out var literal):
                    return "'" + literal.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
                default:
                    return null;
            }
        }

        private static string UnquoteRaw(string? raw)
        {
            if (raw is null)
            {
                return "";
            }

            return raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' ? raw[1..^1] : raw;
        }

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

            if (!expr.IsNullable || HasTopLevelNullMember(joined))
            {
                return joined;
            }

            if (!joined.Contains('|', StringComparison.Ordinal) && !joined.Contains('&', StringComparison.Ordinal))
            {
                return "?" + joined.TrimStart('?');
            }

            return joined + "|null";
        }

        /// <summary>
        /// True when a spelled union has a top-level member that is exactly <c>null</c> (or
        /// <c>?T</c>). A name that merely contains the letters <c>null</c> (<c>\PregMatchSetNullable</c>)
        /// or a <c>null</c> nested inside generic arguments does not make the type nullable.
        /// </summary>
        private static bool HasTopLevelNullMember(string spelled)
        {
            var depth = 0;
            var start = 0;
            for (var i = 0; i <= spelled.Length; i++)
            {
                var atEnd = i == spelled.Length;
                if (!atEnd)
                {
                    var c = spelled[i];
                    if (c is '<' or '(' or '{' or '[')
                    {
                        depth++;
                    }
                    else if (c is '>' or ')' or '}' or ']')
                    {
                        depth--;
                    }
                }

                if (!atEnd && !(spelled[i] == '|' && depth == 0))
                {
                    continue;
                }

                var member = spelled[start..i].Trim();
                if (member.StartsWith('?')
                    || string.Equals(member, "null", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                start = i + 1;
            }

            return false;
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

        internal static IReadOnlyList<ITypeExpression> GetGenericTypeArguments(IBase2Ast node)
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
