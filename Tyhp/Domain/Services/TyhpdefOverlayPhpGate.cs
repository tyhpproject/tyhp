using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Versioning;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Decides whether an overlay declaration is part of one <c>tyhp overlay stamp</c> pass.
    /// Gates are the same <c>declare(php=…)</c> / <c>#[\Tyhp\Php(…)]</c> constraints the
    /// binder evaluates with <see cref="PhpVersionConstraint"/>.
    /// </summary>
    internal static class TyhpdefOverlayPhpGate
    {
        private const string TyhpPhpAttributeFqn = "\\Tyhp\\Php";
        private const string TyhpPhpVersionArgumentName = "version";

        /// <summary>
        /// True when <paramref name="node"/> should receive a stamp for
        /// <paramref name="targetPhpVersion"/>. A null target keeps the historical
        /// unfiltered rewrite. No gate means the declaration is included in every pass.
        /// An unsatisfied or invalid gate excludes it so another overload of the same
        /// name is left unchanged.
        /// </summary>
        public static bool IsIncluded(
            IBase2Ast node,
            IReadOnlyList<string> enclosingDeclareConstraints,
            string? targetPhpVersion)
        {
            if (string.IsNullOrWhiteSpace(targetPhpVersion))
            {
                return true;
            }

            if (!TryBuildConstraints(node, enclosingDeclareConstraints, out var constraints))
            {
                return false;
            }

            foreach (var constraint in constraints)
            {
                var evaluated = PhpVersionConstraint.Evaluate(targetPhpVersion, constraint);
                if (!evaluated.ConstraintIsValid || !evaluated.IsSatisfied)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Semicolon <c>declare(php=…);</c> directives at file or namespace scope. They gate
        /// the whole file, matching the binder. Directives inside a declare block or a
        /// class body are not file-level.
        /// </summary>
        public static List<string> CollectFileLevelConstraints(IBase2Ast root)
        {
            var found = new List<string>();
            CollectFileLevel(root, found);
            return found;
        }

        public static bool IsDeclareBlock(PhpDeclareAst declare)
            => declare.Body is not null and not PhpNopStatementAst;

        public static bool TryReadSolePhpConstraint(PhpDeclareAst declare, out string constraint)
        {
            constraint = "";
            if (declare.Declarations == null)
            {
                return false;
            }

            string? found = null;
            var otherCount = 0;
            foreach (var decl in declare.Declarations.GetAllNotNull())
            {
                var key = decl.Identifier ?? "";
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (key.Equals("php", StringComparison.OrdinalIgnoreCase))
                {
                    found = ReadDeclareValue(decl.Value);
                }
                else
                {
                    otherCount++;
                }
            }

            if (found == null || otherCount > 0)
            {
                return false;
            }

            constraint = found;
            return true;
        }

        private static void CollectFileLevel(IBase2Ast node, List<string> found)
        {
            if (node is PhpClassBodyAst)
            {
                return;
            }

            if (node is PhpDeclareAst declare)
            {
                if (IsDeclareBlock(declare))
                {
                    return;
                }

                if (TryReadSolePhpConstraint(declare, out var constraint))
                {
                    found.Add(constraint);
                }

                return;
            }

            foreach (var child in node.AstChildren)
            {
                if (child != null)
                {
                    CollectFileLevel(child, found);
                }
            }
        }

        /// <summary>
        /// AND-combines enclosing <c>declare(php=…)</c> constraints with
        /// <c>#[\Tyhp\Php]</c> on <paramref name="node"/>. Returns false when an attribute
        /// constraint is invalid syntax (the declaration is omitted, same as the binder).
        /// A missing or non-string attribute argument does not apply that attribute as a gate.
        /// </summary>
        private static bool TryBuildConstraints(
            IBase2Ast node,
            IReadOnlyList<string> enclosingDeclareConstraints,
            out List<string> constraints)
        {
            constraints = new List<string>(enclosingDeclareConstraints);
            var attributeConstraints = new List<string>();
            var sawAttribute = false;
            var anyArgumentInvalid = false;
            var attributeConstraintInvalid = false;

            foreach (var attribute in EnumerateAttributes(node))
            {
                if (!IsTyhpPhpAttribute(attribute))
                {
                    continue;
                }

                sawAttribute = true;
                if (!TryReadTyhpPhpVersionArgument(attribute, out var version))
                {
                    anyArgumentInvalid = true;
                    continue;
                }

                if (!PhpVersionConstraint.IsValid(version))
                {
                    attributeConstraintInvalid = true;
                    continue;
                }

                attributeConstraints.Add(version);
            }

            if (!sawAttribute)
            {
                return true;
            }

            if (anyArgumentInvalid)
            {
                return true;
            }

            if (attributeConstraintInvalid)
            {
                return false;
            }

            constraints.AddRange(attributeConstraints);
            return true;
        }

        private static IEnumerable<PhpAttributeAst> EnumerateAttributes(IBase2Ast node)
        {
            foreach (var attributeNode in node.AstAttributes)
            {
                switch (attributeNode)
                {
                    case PhpAttributeAst attribute:
                        yield return attribute;
                        break;
                    case PhpAttributeListAst list:
                        foreach (var child in list.GetAllNotNull())
                        {
                            if (child is PhpAttributeAst nested)
                            {
                                yield return nested;
                            }
                        }

                        break;
                }
            }
        }

        private static bool IsTyhpPhpAttribute(PhpAttributeAst attribute)
        {
            var written = AttributeName(attribute.Name);
            if (string.IsNullOrWhiteSpace(written))
            {
                return false;
            }

            var trimmed = written.Trim();
            return trimmed.Equals(TyhpPhpAttributeFqn, StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Tyhp\\Php", StringComparison.OrdinalIgnoreCase);
        }

        private static string? AttributeName(IExpression? expression) =>
            expression switch
            {
                null => null,
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                _ => expression.ValueString ?? expression.Identifier,
            };

        private static bool TryReadTyhpPhpVersionArgument(PhpAttributeAst attribute, out string version)
        {
            version = "";
            var arguments = attribute.Arguments?.GetAllNotNull();
            if (arguments is null)
            {
                return false;
            }

            PhpArgumentAst? chosen = null;
            PhpArgumentAst? firstPositional = null;
            foreach (var argument in arguments)
            {
                if (argument is not PhpArgumentAst phpArgument)
                {
                    continue;
                }

                var argName = phpArgument.Name?.ValueString;
                if (string.IsNullOrEmpty(argName))
                {
                    firstPositional ??= phpArgument;
                    continue;
                }

                if (argName.Equals(TyhpPhpVersionArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = phpArgument;
                    break;
                }
            }

            chosen ??= firstPositional;
            if (chosen?.Expression is null)
            {
                return false;
            }

            return TryReadStringLiteral(chosen.Expression, out version);
        }

        private static bool TryReadStringLiteral(IExpression expression, out string value)
        {
            value = "";
            switch (expression)
            {
                case PhpScalarAst scalar:
                    if (scalar.ValueInt64.HasValue || scalar.ValueDecimal.HasValue || scalar.ValueBoolean.HasValue)
                    {
                        return false;
                    }

                    if (string.IsNullOrEmpty(scalar.ValueString))
                    {
                        return false;
                    }

                    value = UnquotePhpString(scalar.ValueString);
                    return true;

                case PhpEncapsStringAst encaps:
                    value = UnquotePhpString(encaps.ValueString ?? encaps.TokenValue?.ValueString);
                    return !string.IsNullOrEmpty(value);

                case PhpEncapsListAst list:
                    value = string.Concat(
                        list.GetAllNotNull().Select(part => UnquotePhpString(part.ValueString)));
                    return !string.IsNullOrEmpty(value);

                default:
                    if (string.IsNullOrEmpty(expression.ValueString))
                    {
                        return false;
                    }

                    value = UnquotePhpString(expression.ValueString);
                    return !string.IsNullOrEmpty(value);
            }
        }

        private static string ReadDeclareValue(IExpression? valueExpr)
        {
            if (valueExpr == null)
            {
                return "";
            }

            switch (valueExpr)
            {
                case PhpScalarAst scalar:
                    if (!string.IsNullOrEmpty(scalar.ValueString))
                    {
                        return UnquotePhpString(scalar.ValueString);
                    }

                    if (scalar.ValueInt64.HasValue)
                    {
                        return scalar.ValueInt64.Value.ToString();
                    }

                    if (scalar.ValueBoolean.HasValue)
                    {
                        return scalar.ValueBoolean.Value ? "true" : "false";
                    }

                    break;

                case PhpEncapsStringAst encaps:
                    return UnquotePhpString(encaps.ValueString ?? encaps.TokenValue?.ValueString);

                case PhpEncapsListAst list:
                    return string.Concat(
                        list.GetAllNotNull().Select(part => UnquotePhpString(part.ValueString)));
            }

            if (!string.IsNullOrEmpty(valueExpr.ValueString))
            {
                return UnquotePhpString(valueExpr.ValueString);
            }

            return "";
        }

        private static string UnquotePhpString(string? literal)
        {
            if (string.IsNullOrEmpty(literal))
            {
                return "";
            }

            if (literal.Length >= 2
                && ((literal[0] == '\'' && literal[^1] == '\'')
                    || (literal[0] == '"' && literal[^1] == '"')))
            {
                return literal[1..^1];
            }

            return literal;
        }
    }
}
