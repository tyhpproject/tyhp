using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter.Splice
{
    /// <summary>AST helpers for call-site splicing: clone, body extract, simple-arg test, provenance.</summary>
    internal static class SpliceAst
    {
        public static bool TryGetSingleReturnExpression(IBase2Ast? declaringNode, out IExpression expression)
        {
            expression = null!;
            var body = GetBody(declaringNode);
            if (body is null)
            {
                return false;
            }

            var statements = body.GetAllNotNull()
                .Where(s => s is not PhpEmptyStatementAst and not PhpNopStatementAst)
                .ToList();
            if (statements.Count != 1)
            {
                return false;
            }

            switch (statements[0])
            {
                case PhpReturnStatementAst { Expression: IExpression expr }:
                    expression = expr;
                    return true;
                case PhpJumpStatementAst jump
                    when jump.JumpType == PhpJumpType.Return && jump.Expression is IExpression jumpExpr:
                    expression = jumpExpr;
                    return true;
                case PhpUnaryOpAst unary
                    when string.Equals(unary.Operator?.ValueString, "return", StringComparison.OrdinalIgnoreCase)
                    && unary.Operand is IExpression operand:
                    expression = operand;
                    return true;
                default:
                    return false;
            }
        }

        public static PhpStatementBlockAst? GetBody(IBase2Ast? declaringNode) =>
            declaringNode switch
            {
                PhpFunctionDeclAst function => function.Body,
                PhpMethodDeclAst method => method.Body,
                TyhpOperatorOverloadAst op => op.Body,
                TyhpdefInlineExtensionFunctionAst inline => inline.Method?.Body,
                _ => null,
            };

        public static IBase2Ast UnwrapDeclaringNode(IBase2Ast? node) =>
            node is TyhpdefInlineExtensionFunctionAst { Method: { } method } ? method : node!;

        public static string MemberDisplayName(IBaseSymbol callee) =>
            callee.Name;

        /// <summary>
        /// True when PHP still has a method to call: a Tyhp <c>extension { }</c> brace member,
        /// or any class-owned method/operator. False for tyhpdef thin mappings, compiler-generated
        /// synthetic extensions, and short <c>=&gt;</c> Tyhp extension members (erased).
        /// Class-owned operators always keep their PHP method even when authored with <c>=&gt;</c>.
        /// </summary>
        public static bool MemberHasPhpBacker(IBase2Ast? declaringNode, ObjectDeclarationSymbol? owner)
        {
            if (declaringNode is null)
            {
                return false;
            }

            if (string.Equals(declaringNode.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase)
                || declaringNode.OwningFile is TyhpdefSrcFileAst)
            {
                return false;
            }

            if (owner is { IsCompilerGenerated: true, IsExtension: true })
            {
                return false;
            }

            // Form decides the PHP backer for Tyhp `extension { }` members only:
            // `HasPhpBacker = !IsShortSyntax`. Brace bodies emit even when they are a
            // single `return expr;`. Class-owned methods and operators always emit PHP.
            if (owner is { IsExtension: true })
            {
                return ExtensionMemberEmitsPhpBacker(declaringNode);
            }

            return true;
        }

        /// <summary>
        /// True when a Tyhp <c>extension { }</c> member is a brace body and therefore
        /// keeps a PHP backer method. Short <c>fn</c> / <c>operator … =&gt;</c> members do not.
        /// </summary>
        public static bool ExtensionMemberEmitsPhpBacker(IBase2Ast? member)
        {
            var node = UnwrapDeclaringNode(member);
            return node switch
            {
                PhpFunctionDeclAst function => function.Body != null && !function.IsShortSyntax,
                PhpMethodDeclAst method => !method.IsShortSyntax,
                TyhpOperatorOverloadAst op => !op.IsShortSyntax,
                _ => false,
            };
        }

        /// <summary>
        /// True when the Tyhp extension emits a PHP backer class: at least one member has a
        /// brace body. All-<c>=&gt;</c> extensions emit no class at all.
        /// </summary>
        public static bool ExtensionDeclEmitsPhpBackerClass(TyhpExtensionDeclAst extension)
        {
            foreach (var member in extension.FunctionList?.GetAllNotNull() ?? [])
            {
                if (member is TyhpExtensionDeclAst group)
                {
                    if (ExtensionDeclEmitsPhpBackerClass(group))
                    {
                        return true;
                    }

                    continue;
                }

                if (ExtensionMemberEmitsPhpBacker(member))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsOptimizeInlineAttribute(IBase2Ast attribute)
        {
            if (attribute is not PhpAttributeAst attr)
            {
                return false;
            }

            if (IsOptimizeInlineName(GetAttributeName(attr.Name)))
            {
                return true;
            }

            if (attr.Name is PhpNameAst { BoundSymbol: { } bound }
                && IsOptimizeInlineName(bound.FullyQualifiedName))
            {
                return true;
            }

            return false;
        }

        public static bool DeclaresOptimizeInline(IBase2Ast node) =>
            node.AstAttributes.Any(IsOptimizeInlineAttribute);

        /// <summary>
        /// Tyhpdef class-body <c>extension fn</c> stores attributes on the
        /// <see cref="TyhpdefInlineExtensionFunctionAst"/> wrapper, while the method symbol's
        /// <c>DeclaringAstNode</c> is the inner method.
        /// </summary>
        public static bool CalleeDeclaresOptimizeInline(IBaseSymbol callee)
        {
            var declaring = DeclaringNodeOf(callee);
            if (declaring is not null
                && (DeclaresOptimizeInline(declaring) || DeclaresOptimizeInline(UnwrapDeclaringNode(declaring))))
            {
                return true;
            }

            var owner = CallSiteSpliceEngine.OwnerOf(callee);
            foreach (var root in new IBase2Ast?[]
            {
                owner?.InlineExtensionReceiverClass?.DeclaringAstNode,
                owner?.DeclaringAstNode,
                declaring?.OwningFile,
                callee is ObjectMethodSymbol method ? method.DeclaringAstNode?.OwningFile : null,
            })
            {
                if (FindInlineExtensionWrapper(root, callee.Name) is { } wrapper
                    && DeclaresOptimizeInline(wrapper))
                {
                    return true;
                }
            }

            return false;
        }

        internal static TyhpdefInlineExtensionFunctionAst? FindInlineExtensionWrapper(
            IBase2Ast? node,
            string calleeName)
        {
            if (node is null)
            {
                return null;
            }

            if (node is TyhpdefInlineExtensionFunctionAst wrapper
                && NamesEqual(wrapper.Identifier ?? wrapper.Method?.Identifier, calleeName))
            {
                return wrapper;
            }

            foreach (var child in node.AstChildren)
            {
                var found = FindInlineExtensionWrapper(child, calleeName);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }

        public static ObjectMethodSymbol? TryFindSyntheticInlineMember(
            ObjectDeclarationSymbol? receiver,
            string? methodName) =>
            receiver?.FindSyntheticInlineMember(methodName);

        private static string? GetAttributeName(IExpression? expression) =>
            expression switch
            {
                PhpNameAst n => n.ValueString ?? n.Identifier,
                TokenValueAst t => t.ValueString,
                PhpDereferenceableAst deref => FirstNonEmpty(
                    GetAttributeName(deref.Base as IExpression),
                    deref.Identifier,
                    (deref.Suffix as PhpStaticMemberAccessAst)?.Member?.Identifier,
                    (deref.Suffix as PhpClassConstantAccessAst)?.Member?.Identifier),
                IExpression e => e.Identifier,
                _ => null,
            };

        private static string? FirstNonEmpty(params string?[] parts)
        {
            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part))
                {
                    return part;
                }
            }

            return null;
        }

        private static bool IsOptimizeInlineName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var normalized = name.TrimStart('\\');
            return string.Equals(normalized, "Tyhp\\Optimize\\Inline", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "Inline", StringComparison.OrdinalIgnoreCase);
        }

        public static T Clone<T>(T node)
            where T : class, IBase2Ast
        {
            var clone = Base2Ast.Deserialize(node.Serialize());
            CopyRuntimeState(node, clone);
            return (T)(object)clone;
        }

        private static void CopyRuntimeState(IBase2Ast source, IBase2Ast dest)
        {
            dest.BoundSymbol = source.BoundSymbol;
            dest.OwningFile = source.OwningFile;
            dest.OriginalAst = source.OriginalAst;

            if (source is TyhpOperatorOverloadAst srcOp && dest is TyhpOperatorOverloadAst destOp)
            {
                destOp.ExtensionTargetType = srcOp.ExtensionTargetType;
                destOp.IsInlineExtension = srcOp.IsInlineExtension;
            }

            var srcChildren = source.AstChildren;
            var destChildren = dest.AstChildren;
            var n = Math.Min(srcChildren.Count, destChildren.Count);
            for (var i = 0; i < n; i++)
            {
                if (srcChildren[i] is { } s && destChildren[i] is { } d)
                {
                    CopyRuntimeState(s, d);
                }
            }

            var srcAttrs = source.AstAttributes;
            var destAttrs = dest.AstAttributes;
            var a = Math.Min(srcAttrs.Count, destAttrs.Count);
            for (var i = 0; i < a; i++)
            {
                CopyRuntimeState(srcAttrs[i], destAttrs[i]);
            }
        }

        public static void StampOriginalAst(IBase2Ast node, IBase2Ast callSite)
        {
            node.OriginalAst = callSite;
            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    StampOriginalAst(child, callSite);
                }
            }

            foreach (var attr in node.AstAttributes)
            {
                StampOriginalAst(attr, callSite);
            }
        }

        public static string NormalizeName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            return name.StartsWith('$') ? name[1..] : name;
        }

        public static bool NamesEqual(string? a, string? b) =>
            string.Equals(NormalizeName(a), NormalizeName(b), StringComparison.OrdinalIgnoreCase);

        public static bool IsSimpleArgument(IExpression? expression)
        {
            expression = UnwrapParens(expression);
            return expression switch
            {
                PhpVariableAst variable => IsSimpleVariable(variable),
                PhpScalarAst => true,
                PhpMagicConstantAst => true,
                PhpNameAst name when IsConstantName(name) => true,
                PhpDereferenceableAst
                {
                    Base: PhpNameAst,
                    Suffix: PhpClassConstantAccessAst
                } => true,
                _ => false,
            };
        }

        public static bool IsSimpleVariable(PhpVariableAst variable) =>
            variable.VariableExpression is null or TokenValueAst
            && CheckerHelpers.GetVariableName(variable) is { Length: > 0 };

        private static bool IsConstantName(PhpNameAst name)
        {
            var text = name.ValueString ?? name.Identifier;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (text is "true" or "false" or "null"
                || text.Equals("true", StringComparison.OrdinalIgnoreCase)
                || text.Equals("false", StringComparison.OrdinalIgnoreCase)
                || text.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return name.BoundSymbol is ConstantSymbol
                || name.BoundSymbol is ObjectConstantSymbol
                || name.BoundSymbol is MagicConstantSymbol;
        }

        public static IExpression UnwrapParens(IExpression? expression)
        {
            while (expression is PhpDereferenceableExpressionAst paren && paren.Expression is IExpression next)
            {
                expression = next;
            }

            return expression!;
        }

        public static IExpression Parenthesize(IExpression expression, Base2Ast context)
        {
            if (expression is PhpDereferenceableExpressionAst)
            {
                return expression;
            }

            return PhpDereferenceableExpressionAst.CreateFromContext(expression, context);
        }

        public static bool IsAssignmentOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(op.ValueString))
            {
                return IsAssignmentSpelling(op.ValueString);
            }

            var token = (int)(op.ValueInt64 ?? -1);
            return token is TyhpParser.T_SYM_EQUAL
                or TyhpParser.T_PLUS_EQUAL
                or TyhpParser.T_MINUS_EQUAL
                or TyhpParser.T_MUL_EQUAL
                or TyhpParser.T_POW_EQUAL
                or TyhpParser.T_DIV_EQUAL
                or TyhpParser.T_CONCAT_EQUAL
                or TyhpParser.T_MOD_EQUAL
                or TyhpParser.T_AND_EQUAL
                or TyhpParser.T_OR_EQUAL
                or TyhpParser.T_XOR_EQUAL
                or TyhpParser.T_SL_EQUAL
                or TyhpParser.T_SR_EQUAL
                or TyhpParser.T_COALESCE_EQUAL;
        }

        private static bool IsAssignmentSpelling(string op) =>
            op is "=" or "+=" or "-=" or "*=" or "/=" or ".=" or "%=" or "**="
                or "&=" or "|=" or "^=" or "<<=" or ">>=" or "??=" or ":=";

        public static bool IsCompoundAssignmentOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(op.ValueString))
            {
                return op.ValueString is "+=" or "-=" or "*=" or "/=" or ".=" or "%=" or "**="
                    or "&=" or "|=" or "^=" or "<<=" or ">>=" or "??=";
            }

            var token = (int)(op.ValueInt64 ?? -1);
            return token is TyhpParser.T_PLUS_EQUAL
                or TyhpParser.T_MINUS_EQUAL
                or TyhpParser.T_MUL_EQUAL
                or TyhpParser.T_POW_EQUAL
                or TyhpParser.T_DIV_EQUAL
                or TyhpParser.T_CONCAT_EQUAL
                or TyhpParser.T_MOD_EQUAL
                or TyhpParser.T_AND_EQUAL
                or TyhpParser.T_OR_EQUAL
                or TyhpParser.T_XOR_EQUAL
                or TyhpParser.T_SL_EQUAL
                or TyhpParser.T_SR_EQUAL
                or TyhpParser.T_COALESCE_EQUAL;
        }

        public static bool IsIncrementDecrement(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(op.ValueString))
            {
                return op.ValueString is "++" or "--";
            }

            var token = (int)(op.ValueInt64 ?? -1);
            return token is TyhpParser.T_INC or TyhpParser.T_DEC;
        }

        public static bool IsShortCircuitOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(op.ValueString))
            {
                return op.ValueString is "&&" or "||" or "??"
                    || op.ValueString.Equals("and", StringComparison.OrdinalIgnoreCase)
                    || op.ValueString.Equals("or", StringComparison.OrdinalIgnoreCase);
            }

            var token = (int)(op.ValueInt64 ?? -1);
            return token is TyhpParser.T_BOOLEAN_AND
                or TyhpParser.T_BOOLEAN_OR
                or TyhpParser.T_LOGICAL_AND
                or TyhpParser.T_LOGICAL_OR
                or TyhpParser.T_COALESCE;
        }

        public static IReadOnlyList<ParameterInfo>? ParametersOf(IBaseSymbol? symbol) =>
            symbol switch
            {
                ObjectMethodSymbol method => method.Parameters,
                FunctionDeclarationSymbol function => function.Parameters,
                _ => null,
            };

        public static IBase2Ast? DeclaringNodeOf(IBaseSymbol? symbol) =>
            (symbol as BaseSymbol)?.DeclaringAstNode;

        /// <summary>
        /// Bodyless overload signatures are type-only. Splice the implementation that owns
        /// <see cref="ObjectMethodSymbol.Overloads"/> / <see cref="FunctionDeclarationSymbol.Overloads"/>.
        /// Leave a callee that already has a spliceable body unchanged.
        /// </summary>
        public static IBaseSymbol ImplementationForSplice(IBaseSymbol callee)
        {
            if (TryGetSingleReturnExpression(DeclaringNodeOf(callee), out _))
            {
                return callee;
            }

            switch (callee)
            {
                case ObjectMethodSymbol method
                    when method.ContainingScope?.DeclarationSymbol is ObjectDeclarationSymbol owner
                    && owner.Members.TryGetValue(method.Name, out var member)
                    && member is ObjectMethodSymbol primary
                    && !ReferenceEquals(primary, method)
                    && primary.Overloads.Contains(method):
                    return primary;
                case FunctionDeclarationSymbol function
                    when function.ContainingScope is IBaseScope scope
                    && scope.FindChildSymbolByName(function.Name) is FunctionDeclarationSymbol primaryFn
                    && !ReferenceEquals(primaryFn, function)
                    && primaryFn.Overloads.Contains(function):
                    return primaryFn;
                default:
                    return callee;
            }
        }

        public static MemberModifier VisibilityOf(IBaseSymbol? symbol) =>
            (symbol as BaseSymbol)?.Visibility ?? MemberModifier.Public;

        public static bool MethodReturnsRef(IBaseSymbol? symbol)
        {
            var declaring = DeclaringNodeOf(symbol);
            if (declaring is PhpMethodDeclAst method)
            {
                return method.ReturnsRef;
            }

            if (declaring is PhpFunctionDeclAst function)
            {
                return function.ReturnsRef;
            }

            if (declaring is TyhpdefImportFunctionDeclAst import)
            {
                return import.ReturnsRef;
            }

            return false;
        }

        public static bool IsThisName(string? name) =>
            string.Equals(NormalizeName(name), "this", StringComparison.OrdinalIgnoreCase);

        public static IReadOnlyList<SpliceParameter> ParametersFromSymbol(
            IReadOnlyList<ParameterInfo> parameters,
            bool firstIsThis)
        {
            var list = new List<SpliceParameter>(parameters.Count);
            for (var i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                var name = NormalizeName(p.Name);
                // `firstIsThis` already encodes that the first parameter is the receiver (extension
                // owner, or a parameter literally named `$this`). Do NOT re-guard with `IsThisName`:
                // a Tyhp `extension fn foo(extends string $str)` names its receiver `$str`, and that
                // parameter must still be substituted with the call receiver, not matched against
                // the (empty) argument list.
                list.Add(new SpliceParameter(
                    name,
                    p.IsByReference,
                    p.DefaultValue,
                    firstIsThis && i == 0));
            }

            return list;
        }
    }
}
