using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    public sealed partial class DeclarationRule
    {
        /// <summary>
        /// Validates a source or tyhpdef type-alias declaration
        /// (<c>type Foo&lt;T&gt; = T|null;</c>, file-level or class-level).
        ///
        /// <see cref="TyhpTypeAliasAst.GenericArguments"/> is the one declaration-generic list
        /// stored as a direct <c>AstChildren</c> entry rather than a grammar addon (contrast
        /// classes/functions/methods, whose lists live on <c>AstGrammarAddons["identifier"]</c> and
        /// are validated from the bound symbol's <c>GenericParameters</c> instead). Because of that,
        /// this node used to reach the checker only through <c>CheckNode</c>'s default child
        /// traversal, under whatever ambient <see cref="CheckerState"/> the declaration site
        /// happened to be walked with — which never included the alias's own generic parameters
        /// (FOUND_BUGS #20: <c>type Optional&lt;T&gt; = T|null;</c> reported TYHP4054, class-level
        /// <c>type Row&lt;T&gt;</c> reported TYHP3003). Owning the node here lets the alias body
        /// (and its generic parameter declarations' constraints/defaults) resolve in a forked state
        /// that puts those parameters in scope, mirroring <c>CheckFunction</c> / <c>CheckMethod</c> /
        /// <c>CheckObjectType</c>.
        /// </summary>
        private static void CheckTypeAlias(
            TyhpTypeAliasAst alias,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            AttributeRule.ValidateDeclarationAttributes(alias, state, context, diagnostics);

            var aliasModifiers = CheckerHelpers.ToMemberModifiers(alias.Modifiers);
            if (CheckerHelpers.CountVisibilityModifiers(aliasModifiers) > 1)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    alias,
                    MessageCode.CheckerMultipleVisibilities,
                    alias.Name?.ValueString ?? alias.Identifier);
            }

            if (alias.BoundSymbol is ObjectDeclarationSymbol { IsStruct: true } structDecl)
            {
                var structState = state.Split(ScopeType.ObjectTypeDeclaration);
                structState.EnclosingObject = structDecl;
                structState.EnclosingObjectType = CheckedTypes.FromSymbol(structDecl);
                structState.ObjectGenerics = structDecl.GenericParameters;
                if (structDecl.GenericParameters.Count > 0)
                {
                    GenericConstraintResolver.ResolveAll(structDecl.GenericParameters, structState, context);
                }

                CheckStructShapeAlias(alias, structDecl, structState, context, diagnostics);
                CheckTypeAliasGenericArgumentsAndBody(alias, structState, context);
                return;
            }

            if (alias.BoundSymbol is TypeAliasSymbol or ObjectTypeAliasSymbol)
            {
                var boundSymbol = (IBaseSymbol)alias.BoundSymbol;
                var aliasState = CheckerHelpers.WithAliasBodyContext(state, boundSymbol);

                var generics = boundSymbol switch
                {
                    TypeAliasSymbol fileAlias => fileAlias.GenericParameters,
                    ObjectTypeAliasSymbol objectAlias => objectAlias.GenericParameters,
                    _ => (IReadOnlyList<GenericTypeParameterSymbol>)Array.Empty<GenericTypeParameterSymbol>(),
                };

                if (generics.Count > 0)
                {
                    GenericConstraintResolver.ResolveAll(generics, aliasState, context);
                }

                CheckTypeAliasGenericArgumentsAndBody(alias, aliasState, context);
                return;
            }

            // Unbound / recovery placeholder — walk children under the ambient state (same as the
            // previous default traversal) rather than silently skipping validation.
            CheckTypeAliasGenericArgumentsAndBody(alias, state, context);
        }

        private static void CheckTypeAliasGenericArgumentsAndBody(
            TyhpTypeAliasAst alias,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (alias.GenericArguments is not null)
            {
                foreach (var genericArg in alias.GenericArguments.GetAllNotNull())
                {
                    context.CheckNode(genericArg, state);
                }
            }

            if (alias.TypeExpression is not null)
            {
                context.CheckNode(alias.TypeExpression, state);
            }
        }

        private static void CheckStructShapeAlias(
            TyhpTypeAliasAst alias,
            ObjectDeclarationSymbol structDecl,
            CheckerState structState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var shape = alias.StructShape;
            if (shape?.Extends is not { } extends)
            {
                return;
            }

            var parent = TypeComparer.TryGetParentDeclaration(
                structDecl, context.SymbolTree, context.GlobalScope);
            if (parent is { IsStruct: false }
                && !string.Equals(parent.Name, "struct", StringComparison.OrdinalIgnoreCase))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    structState,
                    extends,
                    MessageCode.CheckerGenericConstraintNotSatisfied,
                    structDecl.Name,
                    "struct");
                return;
            }

            if (parent is null)
            {
                var extendsType = context.ResolveExpressionType(extends, structState);
                if (CheckerHelpers.TryGetObjectDeclaration(extendsType) is { IsStruct: false }
                    && !CheckerHelpers.IsBuiltInName(extendsType, "struct"))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        structState,
                        extends,
                        MessageCode.CheckerGenericConstraintNotSatisfied,
                        structDecl.Name,
                        "struct");
                }

                return;
            }

            var typeArgs = GenericInheritanceBindings.GetExtendsTypeArguments(structDecl);
            if (typeArgs is null || typeArgs.Count == 0)
            {
                return;
            }

            var resolvedArgs = typeArgs
                .Select(arg => context.ResolveTypeAnnotation(arg, structState))
                .ToList();

            GenericTypeArgumentValidator.ValidateInstantiation(
                CheckedTypes.FromSymbol(parent),
                resolvedArgs,
                extends,
                structState,
                context.SymbolTree,
                context.GlobalScope,
                diagnostics,
                (typeAst, st, isRet, isUser) =>
                    context.ResolveTypeAnnotation(typeAst, st, isRet, isUser));
        }
    }
}
