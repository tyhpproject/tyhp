using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Validates compile-time constructs: nameof, typeof, default, and variable_exists.</summary>
    public sealed class CompileTimeRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(TyhpNameofAst),
            typeof(TyhpTypeofAst),
            typeof(TyhpDefaultAst),
            typeof(TyhpVariableExistsAst),
        ];

        public bool SuppressChildTraversal(IBase2Ast node) => true;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case TyhpNameofAst nameofExpr:
                    CheckNameof(nameofExpr, state, context, diagnostics);
                    break;
                case TyhpTypeofAst typeofExpr:
                    CheckTypeof(typeofExpr, state, context, diagnostics);
                    break;
                case TyhpDefaultAst defaultExpr:
                    CheckDefault(defaultExpr, state, context, diagnostics);
                    break;
                case TyhpVariableExistsAst variableExists:
                    CheckVariableExists(variableExists, state, diagnostics);
                    break;
            }
        }

        private static void CheckNameof(
            TyhpNameofAst nameofExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (nameofExpr.Expression is not IExpression expression)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, nameofExpr, MessageCode.CheckerNonConstantExpression);
                return;
            }

            if (expression is PhpInlineFunctionAst closure)
            {
                CheckNameofPropertyPathFn(closure, state, diagnostics);
                return;
            }

            if (!IsNameofReference(expression, state, context))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, expression, MessageCode.CheckerNonConstantExpression);
            }
        }

        /// <summary>
        /// Story 16 Phase 3 — <c>nameof(fn ($x) => $x->a->b)</c> folds to the last property
        /// segment. The fn must be an arrow function whose body is a simple property chain.
        /// </summary>
        private static void CheckNameofPropertyPathFn(
            PhpInlineFunctionAst closure,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (!closure.IsArrowFunction
                || !PropertyPathSupport.TryGetNameofPropertyPathLastSegment(closure, out _))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    closure,
                    MessageCode.CheckerPropertyPathInvalidBody);
            }
        }

        private static bool IsInScopeGenericParameter(PhpNameAst name, CheckerState state)
        {
            var simpleName = name.ValueString?.TrimStart('\\');
            if (string.IsNullOrEmpty(simpleName))
            {
                return false;
            }

            bool Matches(IReadOnlyList<GenericTypeParameterSymbol> generics) =>
                generics.Any(gp => string.Equals(gp.Name, simpleName, StringComparison.Ordinal));

            return Matches(state.FunctionGenerics) || Matches(state.ObjectGenerics);
        }

        private static bool IsObjectGenericParameter(PhpNameAst name, CheckerState state)
        {
            var simpleName = name.ValueString?.TrimStart('\\');
            if (string.IsNullOrEmpty(simpleName))
            {
                return false;
            }

            return state.ObjectGenerics.Any(gp => string.Equals(gp.Name, simpleName, StringComparison.Ordinal));
        }

        /// <summary>
        /// True when the name denotes a generic parameter declared by the enclosing function or
        /// method itself, as opposed to one inherited from the enclosing class.
        /// </summary>
        private static bool IsCallableGenericParameter(PhpNameAst name, CheckerState state)
        {
            var simpleName = name.ValueString?.TrimStart('\\');
            if (string.IsNullOrEmpty(simpleName))
            {
                return false;
            }

            return state.FunctionGenerics.Any(gp => string.Equals(gp.Name, simpleName, StringComparison.Ordinal));
        }

        private static void CheckTypeof(
            TyhpTypeofAst typeofExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (typeofExpr.Expression is null)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, typeofExpr, MessageCode.CheckerNonConstantExpression);
                return;
            }

            if (typeofExpr.Expression is ITypeExpression typeExpr)
            {
                context.ResolveTypeAnnotation(typeExpr, state);
                return;
            }

            if (typeofExpr.Expression is PhpNameAst name)
            {
                // `typeof(TValue)` / `typeof(User)` parse the type reference as a bareword name
                // expression (not an `ITypeExpression`), which the binder does not bind. Accept the
                // name when it denotes an in-scope generic type parameter or resolves to a declared
                // type (class/interface/enum/trait or type alias) before reporting it as unresolved.
                if (name.BoundSymbol is null
                    && !IsInScopeGenericParameter(name, state)
                    && !ResolvesToDeclaredType(name, state, context))
                {
                    var unknown = name.ValueString ?? string.Empty;
                    var fromScope = state.EnclosingFunction?.ContainingScope
                        ?? state.EnclosingObject?.ContainingScope
                        ?? (IBaseScope)context.GlobalScope;
                    CheckerHelpers.ReportErrorWithDidYouMean(
                        diagnostics,
                        state,
                        name,
                        MessageCode.BinderSymbolNotFound,
                        unknown,
                        InScopeNameCandidates.CollectTypeNames(fromScope),
                        unknown);
                }

                // A generic parameter the callable declares itself shadows a class generic of the same
                // name and has no instance to read from, so it is served by the Mechanism D binder
                // rather than by instance tracking. Flagging that binder happens in
                // DeclarationRule.FlagGenericVariantIfNeeded, which sees every body position.
                if (!IsCallableGenericParameter(name, state) && IsObjectGenericParameter(name, state))
                {
                    // The argument bound to a class generic parameter is recorded on the instance, so
                    // a static member has nothing to read it from — `static::` cannot substitute.
                    // Reject rather than folding to `mixed`, which would hide the authoring mistake.
                    if (CheckerHelpers.IsInStaticContext(state))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics,
                            state,
                            name,
                            MessageCode.CheckerGenericTypeofInStaticContext,
                            name.ValueString?.TrimStart('\\') ?? string.Empty);
                        return;
                    }

                    context.MarkRequiresRuntimeGenericTracking(state.EnclosingObject);
                }

                return;
            }

            var resolved = context.ResolveExpressionType(typeofExpr.Expression, state);
            if (resolved is UnresolvedCheckedType)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, typeofExpr.Expression, MessageCode.CheckerNonConstantExpression);
            }
        }

        /// <summary>
        /// True when a bareword name inside <c>typeof(...)</c> resolves to a declared type: a
        /// class/interface/enum/trait (<see cref="ObjectDeclarationSymbol"/>) or a type alias. The
        /// binder deliberately leaves <c>typeof</c> arguments unbound (to support generic type
        /// parameters written as barewords), so real types must be resolved here the same way
        /// <c>nameof</c> resolves its target.
        /// </summary>
        private static bool ResolvesToDeclaredType(
            PhpNameAst name,
            CheckerState state,
            CheckerRuleContext context)
            => ResolveNameofSymbol(name.ValueString ?? string.Empty, state, context)
                is ObjectDeclarationSymbol or TypeAliasSymbol or ObjectTypeAliasSymbol;

        private static void CheckDefault(
            TyhpDefaultAst defaultExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (defaultExpr.TypeExpression is null)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, defaultExpr, MessageCode.CheckerNonConstantExpression);
                return;
            }

            var resolved = context.ResolveTypeAnnotation(defaultExpr.TypeExpression, state);
            if (resolved is UnresolvedCheckedType)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, defaultExpr.TypeExpression, MessageCode.CheckerNonConstantExpression);
                return;
            }

            if (resolved is UnionCheckedType union
                && !union.Members.Any(m => m is LiteralCheckedType { Value: null } or { IsNullable: true }))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, defaultExpr.TypeExpression, MessageCode.CheckerMixedInComposite);
            }

            // A generic the callable declares itself is served by the Mechanism D binder parameter, and
            // shadows a class generic of the same name; flagging that binder happens in
            // DeclarationRule.FlagGenericVariantIfNeeded, which sees every body position.
            if (CheckerHelpers.NamesGenericParameterIn(defaultExpr.TypeExpression, state.FunctionGenerics)
                || !CheckerHelpers.NamesGenericParameterIn(defaultExpr.TypeExpression, state.ObjectGenerics))
            {
                return;
            }

            // The argument bound to a class generic is recorded on the instance, so a static member has
            // nothing to read it from. Reject rather than folding to `null`, which would silently give
            // the wrong zero value for every type but the nullable ones.
            if (CheckerHelpers.IsInStaticContext(state))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    defaultExpr.TypeExpression,
                    MessageCode.CheckerGenericDefaultInStaticContext,
                    CheckerHelpers.SoleTypeName(defaultExpr.TypeExpression) ?? string.Empty);
                return;
            }

            context.MarkRequiresRuntimeGenericTracking(state.EnclosingObject);
        }

        private static void CheckVariableExists(
            TyhpVariableExistsAst variableExists,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            switch (variableExists.Expression)
            {
                // Simple `$name` only — reject variable-variables (`$$v`): GetVariableName would
                // return the *inner* name (`v`), which is not the compile-time target of `$$v`.
                case PhpVariableAst variable when IsSimpleVariableExistsArgument(variable):
                    return;
                // Single-quoted / double-quoted constant strings are visited as PhpEncapsListAst
                // (one PhpEncapsStringAst child), not PhpScalarAst.
                case PhpEncapsListAst encapsList when IsConstantEncapsString(encapsList):
                    return;
                case PhpEncapsStringAst encaps when IsNonEmptyConstantEncapsString(encaps):
                    return;
                case PhpScalarAst { ScalarType: PhpScalarType.String, ValueString: not null } scalar
                    when HasNonEmptyStringScalarValue(scalar):
                    return;
                case TokenValueAst { ValueString: not null } token
                    when !string.IsNullOrEmpty(StripVariableExistsQuotes(token.ValueString)):
                    return;
                default:
                    CheckerHelpers.ReportError(
                        diagnostics, state, variableExists, MessageCode.CheckerNonConstantExpression);
                    break;
            }
        }

        private static bool IsSimpleVariableExistsArgument(PhpVariableAst variable)
        {
            if (variable.VariableExpression is PhpVariableAst)
            {
                return false;
            }

            return CheckerHelpers.GetVariableName(variable) is not null;
        }

        private static bool IsConstantEncapsString(PhpEncapsListAst encapsList)
        {
            var parts = encapsList.GetAllNotNull().ToList();
            return parts.Count == 1
                && parts[0] is PhpEncapsStringAst encaps
                && IsNonEmptyConstantEncapsString(encaps);
        }

        private static bool IsNonEmptyConstantEncapsString(PhpEncapsStringAst encaps)
        {
            var raw = encaps.ValueString ?? encaps.TokenValue?.ValueString;
            return !string.IsNullOrEmpty(StripVariableExistsQuotes(raw));
        }

        private static bool HasNonEmptyStringScalarValue(PhpScalarAst scalar)
        {
            var raw = scalar.ValueString;
            if (string.IsNullOrEmpty(raw))
            {
                var token = scalar.AstChildren.ElementAtOrDefault(0) as TokenValueAst;
                raw = token?.ValueString;
            }

            return !string.IsNullOrEmpty(StripVariableExistsQuotes(raw));
        }

        /// <summary>
        /// Strips surrounding quotes and a leading <c>$</c> from a variable_exists string argument,
        /// matching emitter name extraction. Empty results are treated as non-constant / invalid.
        /// </summary>
        private static string? StripVariableExistsQuotes(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            if (raw.Length >= 2
                && ((raw[0] == '\'' && raw[^1] == '\'') || (raw[0] == '"' && raw[^1] == '"')))
            {
                raw = raw[1..^1];
            }

            if (raw.StartsWith('$'))
            {
                raw = raw[1..];
            }

            return string.IsNullOrEmpty(raw) ? null : raw;
        }

        private static bool IsNameofReference(
            IExpression expression,
            CheckerState state,
            CheckerRuleContext context)
        {
            switch (expression)
            {
                case PhpVariableAst variable:
                    return variable.BoundSymbol is VariableSymbol
                        || CheckerHelpers.GetVariableName(variable) is { } varName
                            && state.LookupVariable(varName) is not null;

                case PhpNameAst nameAst:
                    // Bare names for in-scope generics are unbound (same as typeof(T)); accept them
                    // so nameof(T) / nameof(TBatchReturn) constant-folds instead of TYHP4090.
                    return nameAst.BoundSymbol is not null
                        || IsInScopeGenericParameter(nameAst, state)
                        || ResolveNameofSymbol(nameAst.ValueString ?? string.Empty, state, context) is not null;

                case PhpDereferenceableAst deref:
                    return deref.Base is not null
                        && IsNameofReference((IExpression)deref.Base, state, context)
                        && (deref.Suffix is null
                            || deref.Suffix is PhpInstanceMemberAccessAst { MemberName: not null }
                            || deref.Suffix is PhpStaticMemberAccessAst { Member: not null }
                            || deref.Suffix is PhpClassConstantAccessAst { Member: not null });

                case ITypeExpression:
                    return true;

                default:
                    return expression.BoundSymbol is not null;
            }
        }

        private static IBaseSymbol? ResolveNameofSymbol(
            string name,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var scope = GetResolutionScope(state, context.GlobalScope);
            var resolver = new NameResolver(context.SymbolTree, new DiagnosticBag());
            var segments = name.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            return segments.Length == 1
                ? resolver.ResolveSymbol(segments[0], scope)
                    ?? resolver.ResolveRelativeName(segments, scope)
                : resolver.ResolveQualifiedName(segments)
                    ?? resolver.ResolveRelativeName(segments, scope);
        }

        private static IBaseScope GetResolutionScope(CheckerState state, GlobalScope globalScope)
        {
            if (state.EnclosingFunction?.ContainingScope is IBaseScope functionScope)
            {
                return functionScope;
            }

            if (state.EnclosingObject?.ContainingScope is IBaseScope objectScope)
            {
                return objectScope;
            }

            return globalScope;
        }
    }
}
