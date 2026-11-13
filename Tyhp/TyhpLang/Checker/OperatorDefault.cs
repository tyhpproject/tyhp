using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// <c>operator default(): self</c> — the value <c>default(ThatClass)</c> produces when the
    /// class or enum itself declares the operator. The operator is not inherited: a child is
    /// null unless it declares its own. An unsubstituted type parameter stays on the
    /// <c>Type::defaultValue()</c> path.
    /// </summary>
    internal static class OperatorDefault
    {
        public static bool IsUsable(ObjectOperatorOverloadMethodSymbol op) =>
            op.Operator == OverloadableOperator.Default
            && !op.IsExtensionOperator
            && !op.IsNativePassthrough
            && op.Parameters.Count == 0
            && ReturnTypeIsSelf(op.ReturnType);

        public static bool ReturnTypeIsSelf(ITypeExpression? returnType)
        {
            var name = SpelledTypeName(returnType);
            return name is not null && name.Equals("self", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The class or enum <paramref name="type"/> names when that type itself declares a
        /// usable <c>operator default</c>. Null for type parameters, nullable spellings, and
        /// types that do not declare the operator.
        /// </summary>
        public static ObjectDeclarationSymbol? TryGetUsableTarget(ICheckedType type)
        {
            if (type is NullableCheckedType)
            {
                return null;
            }

            if (type is StaticCheckedType staticType)
            {
                return TryGetUsableTarget(staticType.DeclaringType);
            }

            var obj = type switch
            {
                SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol declared } => declared,
                GenericCheckedType { BaseType: SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol declared } } => declared,
                _ => null,
            };

            return obj is not null && HasUsableOperator(obj) ? obj : null;
        }

        /// <summary>
        /// Same answer as <see cref="TryGetUsableTarget(ICheckedType)"/> from the spelled type
        /// argument of <c>default(Type)</c>. Used by the constant-expression predicate and the
        /// emitter, which read the binder's symbol rather than the checker's inferred type.
        /// <paramref name="enclosing"/> is the class or enum being checked or emitted. A spelled
        /// <c>self</c> or <c>static</c> may carry no bound symbol (property initializers, instance
        /// method bodies), so it falls back to that type, the same type the checker infers.
        /// </summary>
        public static ObjectDeclarationSymbol? TryResolveUsableTarget(
            ITypeExpression? typeExpr,
            ObjectDeclarationSymbol? enclosing = null)
        {
            if (NamesParent(typeExpr))
            {
                return null;
            }

            var obj = BoundConcreteObject(typeExpr, depth: 0) ?? RelativeKeywordTarget(typeExpr, enclosing);
            return obj is not null && HasUsableOperator(obj) ? obj : null;
        }

        /// <summary>
        /// True when the type argument of <c>default(...)</c> is spelled <c>parent</c>. Only
        /// <c>self</c> and <c>static</c> follow the enclosing type's operator, so the checker and
        /// the emitter both skip the operator for <c>parent</c>.
        /// </summary>
        public static bool NamesParent(ITypeExpression? typeExpr) =>
            SpelledTypeName(typeExpr) is { } spelled
            && spelled.Equals("parent", StringComparison.OrdinalIgnoreCase);

        private static ObjectDeclarationSymbol? RelativeKeywordTarget(
            ITypeExpression? typeExpr,
            ObjectDeclarationSymbol? enclosing)
        {
            if (enclosing is null || enclosing.IsCompilerGenerated)
            {
                return null;
            }

            var spelled = SpelledTypeName(typeExpr);
            return spelled is not null
                && (spelled.Equals("self", StringComparison.OrdinalIgnoreCase)
                    || spelled.Equals("static", StringComparison.OrdinalIgnoreCase))
                ? enclosing
                : null;
        }

        public static bool HasUsableOperator(ObjectDeclarationSymbol type)
        {
            if (type.ObjectKind is not (PhpTypeDeclType.Class or PhpTypeDeclType.Enum))
            {
                return false;
            }

            if ((type.Visibility & MemberModifier.Abstract) != 0)
            {
                return false;
            }

            foreach (var op in EnumerateOwnOperators(type))
            {
                if (IsUsable(op))
                {
                    return true;
                }
            }

            return false;
        }

        public static void CheckRecursion(
            TyhpOperatorOverloadAst overload,
            ObjectDeclarationSymbol owner,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (overload.Body is null)
            {
                return;
            }

            WalkBody(overload.Body, owner, state, context, diagnostics, depth: 0);
        }

        private static void WalkBody(
            IBase2Ast node,
            ObjectDeclarationSymbol owner,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics,
            int depth)
        {
            if (depth > 500 || node is PhpObjectTypeDeclAst)
            {
                return;
            }

            switch (node)
            {
                case TyhpDefaultAst defaultExpr when NamesOwner(defaultExpr, owner, selfMeansOwner: true):
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        defaultExpr,
                        MessageCode.CheckerOperatorDefaultRecursive,
                        DisplayName(defaultExpr, owner));
                    break;

                case PhpNewAst newExpr when ConstructsOwner(newExpr, owner):
                    ReportConstructorCycle(newExpr, owner, state, context, diagnostics);
                    break;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    WalkBody(child, owner, state, context, diagnostics, depth + 1);
                }
            }
        }

        private static void ReportConstructorCycle(
            PhpNewAst newExpr,
            ObjectDeclarationSymbol owner,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var constructor = FindConstructor(owner, context.SymbolTree, context.GlobalScope);
            if (constructor?.Parameters is null)
            {
                return;
            }

            var declaring = (constructor.BoundSymbol?.ContainingScope as ObjectDeclarationScope)?.DeclarationSymbol;
            var selfMeansOwner = declaring is null || ReferenceEquals(declaring, owner);
            var index = 0;
            foreach (var param in constructor.Parameters.GetAllNotNull())
            {
                var paramIndex = index;
                if (!param.IsVariadic)
                {
                    index++;
                }

                if (param.IsVariadic || param.DefaultValue is null || Supplies(newExpr, param, paramIndex))
                {
                    continue;
                }

                if (!ContainsDefaultOf(param.DefaultValue, owner, selfMeansOwner, depth: 0))
                {
                    continue;
                }

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    newExpr,
                    MessageCode.CheckerOperatorDefaultConstructorCycle,
                    owner.Name);
                return;
            }
        }

        private static bool ContainsDefaultOf(
            IBase2Ast node,
            ObjectDeclarationSymbol owner,
            bool selfMeansOwner,
            int depth)
        {
            if (depth > 500 || node is PhpObjectTypeDeclAst)
            {
                return false;
            }

            if (node is TyhpDefaultAst defaultExpr && NamesOwner(defaultExpr, owner, selfMeansOwner))
            {
                return true;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null && ContainsDefaultOf(child, owner, selfMeansOwner, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        private static PhpMethodDeclAst? FindConstructor(
            ObjectDeclarationSymbol type,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            int depth = 0)
        {
            if (depth > 32)
            {
                return null;
            }

            if (type.DeclaringAstNode is PhpObjectTypeDeclAst { Body: { } body })
            {
                foreach (var member in body.GetAllNotNull())
                {
                    if (member is PhpMethodDeclAst method && IsConstructor(method))
                    {
                        return method;
                    }
                }
            }

            var parent = TypeComparer.TryGetParentDeclaration(type, symbolTree, globalScope);
            return parent is null ? null : FindConstructor(parent, symbolTree, globalScope, depth + 1);
        }

        private static bool IsConstructor(PhpMethodDeclAst method) =>
            method.BoundSymbol is ObjectConstructorMethodSymbol
            || string.Equals(method.Identifier, "__construct", StringComparison.OrdinalIgnoreCase);

        private static bool Supplies(PhpNewAst newExpr, PhpParameterAst param, int index)
        {
            var args = newExpr.Arguments?.GetAllNotNull().ToList();
            if (args is null || args.Count == 0)
            {
                return false;
            }

            var positional = 0;
            var paramName = param.Name.TrimStart('$');
            foreach (var arg in args)
            {
                if (arg.IsVariadic)
                {
                    return true;
                }

                var named = arg.Name?.ValueString?.TrimStart('$');
                if (!string.IsNullOrEmpty(named))
                {
                    if (named.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    continue;
                }

                if (positional == index)
                {
                    return true;
                }

                positional++;
            }

            return false;
        }

        private static bool NamesOwner(
            TyhpDefaultAst defaultExpr,
            ObjectDeclarationSymbol owner,
            bool selfMeansOwner)
        {
            var bound = BoundConcreteObject(defaultExpr.TypeExpression, depth: 0);
            if (bound is not null)
            {
                return ReferenceEquals(bound, owner);
            }

            var spelled = SpelledTypeName(defaultExpr.TypeExpression);
            if (string.IsNullOrEmpty(spelled))
            {
                return false;
            }

            if (spelled.Equals("self", StringComparison.OrdinalIgnoreCase)
                || spelled.Equals("static", StringComparison.OrdinalIgnoreCase))
            {
                return selfMeansOwner;
            }

            if (spelled.Equals("parent", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var trimmed = spelled.TrimStart('\\');
            if (trimmed.Contains('\\'))
            {
                return trimmed.Equals(owner.FullyQualifiedName.TrimStart('\\'), StringComparison.OrdinalIgnoreCase);
            }

            return trimmed.Equals(owner.Name, StringComparison.Ordinal);
        }

        private static bool ConstructsOwner(PhpNewAst newExpr, ObjectDeclarationSymbol owner)
        {
            if (newExpr.AnonymousClass is not null || newExpr.ClassName is null)
            {
                return false;
            }

            if (newExpr.ClassName.BoundSymbol is ObjectDeclarationSymbol bound)
            {
                return ReferenceEquals(bound, owner);
            }

            var spelled = Spell(newExpr.ClassName);
            if (string.IsNullOrEmpty(spelled))
            {
                return false;
            }

            if (spelled.Equals("self", StringComparison.OrdinalIgnoreCase)
                || spelled.Equals("static", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (spelled.Equals("parent", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var trimmed = spelled.TrimStart('\\');
            if (trimmed.Contains('\\'))
            {
                return trimmed.Equals(owner.FullyQualifiedName.TrimStart('\\'), StringComparison.OrdinalIgnoreCase);
            }

            return trimmed.Equals(owner.Name, StringComparison.Ordinal);
        }

        private static string DisplayName(TyhpDefaultAst defaultExpr, ObjectDeclarationSymbol owner) =>
            SpelledTypeName(defaultExpr.TypeExpression) ?? owner.Name;

        private static ObjectDeclarationSymbol? BoundConcreteObject(ITypeExpression? typeExpr, int depth)
        {
            if (typeExpr is null || depth > 8 || !IsSingleNonNullable(typeExpr))
            {
                return null;
            }

            switch (typeExpr.BoundSymbol)
            {
                case GenericTypeParameterSymbol:
                    return null;
                case ObjectDeclarationSymbol declared:
                    return declared;
                case TypeAliasSymbol alias:
                    return BoundConcreteObject(alias.AliasedType, depth + 1);
                case ObjectTypeAliasSymbol objectAlias:
                    return BoundConcreteObject(objectAlias.AliasedType, depth + 1);
            }

            if (typeExpr is PhpTypeExpressionAst composite)
            {
                var inner = composite.Types?.GetAllNotNull().FirstOrDefault() as ITypeExpression;
                return BoundConcreteObject(inner, depth + 1);
            }

            if (typeExpr is PhpNamedTypeAst { Name.BoundSymbol: ObjectDeclarationSymbol named })
            {
                return named;
            }

            return null;
        }

        private static bool IsSingleNonNullable(ITypeExpression? typeExpr)
        {
            if (typeExpr is null)
            {
                return false;
            }

            if (typeExpr is not PhpTypeExpressionAst composite)
            {
                return true;
            }

            if (composite.IsNullable || composite.TypeKind != PhpTypeKind.Simple)
            {
                return false;
            }

            var members = composite.Types?.GetAllNotNull().ToList() ?? [];
            return members.Count == 1 && members[0] is ITypeExpression;
        }

        public static string? SpelledTypeName(IBase2Ast? typeExpr)
        {
            if (typeExpr is PhpTypeExpressionAst composite)
            {
                if (composite.IsNullable || composite.TypeKind != PhpTypeKind.Simple)
                {
                    return null;
                }

                var members = composite.Types?.GetAllNotNull().ToList();
                if (members is not { Count: 1 } || members[0] is not ITypeExpression inner)
                {
                    return null;
                }

                return SpelledTypeName(inner);
            }

            return typeExpr switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNamedTypeAst named => named.Name?.ValueString ?? named.Name?.Identifier,
                PhpNameAst name => name.ValueString ?? name.Identifier,
                _ => null,
            };
        }

        private static string? Spell(IBase2Ast node)
        {
            if (SpelledTypeName(node) is { Length: > 0 } spelled)
            {
                return spelled;
            }

            if (!string.IsNullOrEmpty(node.ValueString))
            {
                return node.ValueString;
            }

            return string.IsNullOrEmpty(node.Identifier) ? null : node.Identifier;
        }

        private static IEnumerable<ObjectOperatorOverloadMethodSymbol> EnumerateOwnOperators(
            ObjectDeclarationSymbol type)
        {
            var scope = FindDeclaringScope(type);
            if (scope is null)
            {
                yield break;
            }

            foreach (var symbol in ((IBaseScope)scope).GetAllChildSymbols())
            {
                if (symbol is ObjectOperatorOverloadMethodSymbol op && !op.IsExtensionOperator)
                {
                    yield return op;
                }
            }
        }

        private static ObjectDeclarationScope? FindDeclaringScope(ObjectDeclarationSymbol type)
        {
            if (type.ContainingScope is not { } parent)
            {
                return null;
            }

            if (parent is ObjectDeclarationScope own && ReferenceEquals(own.DeclarationSymbol, type))
            {
                return own;
            }

            return FindDeclaringScope(parent, type, depth: 0);
        }

        private static ObjectDeclarationScope? FindDeclaringScope(
            IBaseScope scope,
            ObjectDeclarationSymbol type,
            int depth)
        {
            if (scope is ObjectDeclarationScope own && ReferenceEquals(own.DeclarationSymbol, type))
            {
                return own;
            }

            if (depth > 8)
            {
                return null;
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                if (child is ObjectDeclarationScope objectScope
                    && ReferenceEquals(objectScope.DeclarationSymbol, type))
                {
                    return objectScope;
                }

                var nested = FindDeclaringScope(child, type, depth + 1);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
