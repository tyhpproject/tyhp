using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Infers symbol-name result types for <c>nameof()</c> (Story 08.5 Phase 4).
    /// </summary>
    internal static class NameofTypeInferrer
    {
        public static ICheckedType Infer(
            TyhpNameofAst nameofExpr,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IBase2Ast, CheckerState, ICheckedType> resolveExpressionType,
            Func<ITypeExpression, CheckerState, ICheckedType> resolveTypeExpression)
        {
            if (nameofExpr.Expression is not IExpression expression)
            {
                return CheckedTypes.String;
            }

            return InferFromExpression(
                expression, state, symbolTree, globalScope, resolveExpressionType, resolveTypeExpression);
        }

        private static ICheckedType InferFromExpression(
            IExpression expression,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IBase2Ast, CheckerState, ICheckedType> resolveExpressionType,
            Func<ITypeExpression, CheckerState, ICheckedType> resolveTypeExpression)
        {
            switch (expression)
            {
                case PhpVariableAst variable:
                    return InferVariable(variable, state, globalScope, resolveTypeExpression);

                case PhpNameAst nameAst:
                    return InferName(nameAst, state, symbolTree, globalScope, resolveTypeExpression);

                case PhpDereferenceableAst deref:
                    return InferDereferenceable(
                        deref, state, symbolTree, globalScope, resolveExpressionType, resolveTypeExpression);

                case PhpInlineFunctionAst closure:
                    return InferNameofPropertyPathFn(
                        closure, state, globalScope, resolveTypeExpression);

                case ITypeExpression typeExpr:
                    return CheckedTypes.String;

                default:
                    return InferFromBoundSymbol(expression, state, globalScope, resolveTypeExpression);
            }
        }

        /// <summary>
        /// <c>nameof(fn (T $x) => $x->prop)</c> types as <c>__PropertyName&lt;T&gt;</c> when the
        /// lambda parameter has a declared type; otherwise plain <c>string</c>.
        /// </summary>
        private static ICheckedType InferNameofPropertyPathFn(
            PhpInlineFunctionAst closure,
            CheckerState state,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, ICheckedType> resolveTypeExpression)
        {
            if (!PropertyPathSupport.TryGetNameofPropertyPathLastSegment(closure, out _))
            {
                return CheckedTypes.String;
            }

            var parameters = closure.Parameters?.GetAllNotNull().ToList() ?? [];
            if (parameters.Count == 1 && parameters[0].Type is { } declaredType)
            {
                var ownerType = resolveTypeExpression(declaredType, state);
                if (ownerType is not UnresolvedCheckedType)
                {
                    return SymbolNameTypeHelper.MakeSymbolNameType(
                        UtilityBehavior.PropertyName, globalScope, [ownerType]);
                }
            }

            return CheckedTypes.String;
        }

        private static ICheckedType InferVariable(
            PhpVariableAst variable,
            CheckerState state,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, ICheckedType> resolveTypeExpression)
        {
            if (variable.BoundSymbol is VariableSymbol { DeclaredType: { } declaredType })
            {
                var varType = resolveTypeExpression(declaredType, state);
                return SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.TypedVarName, globalScope, [varType]);
            }

            var varName = CheckerHelpers.GetVariableName(variable);
            if (varName is not null
                && state.LookupVariable(varName) is { DeclaredType: { } stateType })
            {
                return SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.TypedVarName, globalScope, [stateType]);
            }

            return SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.VarName, globalScope);
        }

        private static ICheckedType InferName(
            PhpNameAst nameAst,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, ICheckedType> resolveTypeExpression)
        {
            if (nameAst.BoundSymbol is not null)
            {
                return InferFromSymbol(nameAst.BoundSymbol, state, globalScope, resolveTypeExpression);
            }

            // nameof(T) on an in-scope generic types as plain string (emitter folds to the spelling).
            if (IsInScopeGenericParameter(nameAst, state))
            {
                return CheckedTypes.String;
            }

            var scope = GetResolutionScope(state, globalScope);
            var resolver = new NameResolver(symbolTree, new Domain.Diagnostics.DiagnosticBag());
            var name = nameAst.ValueString ?? string.Empty;
            var segments = name.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var symbol = segments.Length switch
            {
                0 => null,
                1 => resolver.ResolveSymbol(segments[0], scope)
                    ?? resolver.ResolveRelativeName(segments, scope),
                _ => resolver.ResolveQualifiedName(segments)
                    ?? resolver.ResolveRelativeName(segments, scope),
            };

            if (symbol is not null)
            {
                return InferFromSymbol(symbol, state, globalScope, resolveTypeExpression);
            }

            return CheckedTypes.String;
        }

        private static ICheckedType InferDereferenceable(
            PhpDereferenceableAst deref,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IBase2Ast, CheckerState, ICheckedType> resolveExpressionType,
            Func<ITypeExpression, CheckerState, ICheckedType> resolveTypeExpression)
        {
            if (deref.Base is null || deref.Suffix is null)
            {
                return InferFromExpression(
                    (IExpression)deref, state, symbolTree, globalScope,
                    resolveExpressionType, resolveTypeExpression);
            }

            var ownerType = ResolveOwnerType(deref.Base, state, symbolTree, globalScope, resolveExpressionType);

            switch (deref.Suffix)
            {
                case PhpInstanceMemberAccessAst { MemberName: not null } instanceAccess:
                    return InferMemberName(
                        ownerType, GetExpressionText(instanceAccess.MemberName), isMethod: false, globalScope);

                case PhpStaticMemberAccessAst { Member: not null } staticAccess:
                    return InferMemberName(
                        ownerType, GetExpressionText(staticAccess.Member), isMethod: false, globalScope);

                case PhpClassConstantAccessAst { Member: not null } classConst:
                    return InferClassConstant(ownerType, classConst.Member, state, globalScope);

                case PhpDereferenceableAst inner when inner.Suffix is PhpCallAst:
                    var methodName = GetMethodNameFromChain(deref);
                    if (methodName is not null)
                    {
                        return InferMemberName(ownerType, methodName, isMethod: true, globalScope);
                    }

                    break;
            }

            if (deref.Suffix is PhpDereferenceableAst chain)
            {
                var methodName = GetMethodNameFromChain(chain);
                if (methodName is not null)
                {
                    return InferMemberName(ownerType, methodName, isMethod: true, globalScope);
                }

                if (chain.Suffix is PhpInstanceMemberAccessAst { MemberName: not null } inst)
                {
                    return InferMemberName(
                        ownerType, GetExpressionText(inst.MemberName), isMethod: false, globalScope);
                }

                if (chain.Suffix is PhpStaticMemberAccessAst { Member: not null } stat)
                {
                    return InferMemberName(
                        ownerType, GetExpressionText(stat.Member), isMethod: false, globalScope);
                }

                if (chain.Suffix is PhpClassConstantAccessAst { Member: not null } cc)
                {
                    return InferClassConstant(ownerType, cc.Member, state, globalScope);
                }
            }

            return CheckedTypes.String;
        }

        private static ICheckedType InferFromBoundSymbol(
            IExpression expression,
            CheckerState state,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, ICheckedType> resolveTypeExpression)
        {
            if (expression.BoundSymbol is null)
            {
                return CheckedTypes.String;
            }

            if (expression.BoundSymbol is VariableSymbol variable)
            {
                return InferVariableSymbol(variable, state, globalScope, resolveTypeExpression);
            }

            return InferFromSymbol(expression.BoundSymbol, state, globalScope, resolveTypeExpression);
        }

        private static ICheckedType InferVariableSymbol(
            VariableSymbol variable,
            CheckerState state,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, ICheckedType> resolveTypeExpression)
        {
            if (variable.DeclaredType is { } declaredType)
            {
                var varType = resolveTypeExpression(declaredType, state);
                return SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.TypedVarName, globalScope, [varType]);
            }

            var varName = variable.Name.TrimStart('$');
            if (state.LookupVariable(varName) is { DeclaredType: { } stateType })
            {
                return SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.TypedVarName, globalScope, [stateType]);
            }

            return SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.VarName, globalScope);
        }

        private static ICheckedType InferFromSymbol(
            IBaseSymbol symbol,
            CheckerState state,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, ICheckedType> resolveTypeExpression)
        {
            switch (symbol)
            {
                case ObjectDeclarationSymbol obj:
                    return InferObjectDeclarationName(obj, globalScope);

                case FunctionDeclarationSymbol:
                    return SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.FunctionName, globalScope);

                case ConstantSymbol:
                    return SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.ConstName, globalScope);

                case ObjectConstantSymbol objConst:
                    return InferObjectConstant(objConst, null, state, globalScope);

                case ObjectPropertySymbol:
                    return InferEnclosingMemberName(isMethod: false, state, globalScope);

                case ObjectMethodSymbol or ObjectConstructorMethodSymbol or ObjectAccessorMethodSymbol:
                    return InferEnclosingMemberName(isMethod: true, state, globalScope);

                case VariableSymbol variable:
                    return InferVariableSymbol(variable, state, globalScope, resolveTypeExpression);

                default:
                    return CheckedTypes.String;
            }
        }

        private static ICheckedType InferObjectDeclarationName(
            ObjectDeclarationSymbol obj,
            GlobalScope globalScope)
        {
            // Parity with `TypeName::class`: brand as `__ClassName<ThatType>` (and siblings),
            // not the bare / default-`<object>` form.
            var thatType = CheckedTypes.FromSymbol(obj);
            return obj.ObjectKind switch
            {
                PhpTypeDeclType.Enum => SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.EnumName, globalScope, [thatType]),
                PhpTypeDeclType.Interface => SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.InterfaceName, globalScope, [thatType]),
                PhpTypeDeclType.Trait => SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.TraitName, globalScope, [thatType]),
                _ when obj.IsStruct => SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.StructName, globalScope),
                _ => SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.ClassName, globalScope, [thatType]),
            };
        }

        private static ICheckedType InferObjectConstant(
            ObjectConstantSymbol objConst,
            ICheckedType? ownerType,
            CheckerState state,
            GlobalScope globalScope)
        {
            ownerType ??= TryGetOwnerTypeFromConstant(objConst, state);

            if (ownerType is not null
                && CheckerHelpers.TryGetObjectDeclaration(ownerType) is { ObjectKind: PhpTypeDeclType.Enum })
            {
                return SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.EnumCaseName, globalScope, [ownerType]);
            }

            if (ownerType is not null)
            {
                return SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.ObjectConstName, globalScope, [ownerType]);
            }

            return SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.ConstName, globalScope);
        }

        private static ICheckedType? TryGetOwnerTypeFromConstant(
            ObjectConstantSymbol objConst,
            CheckerState state)
        {
            if (state.EnclosingObject?.TryGetConstant(objConst.Name, out _) == true)
            {
                return CheckedTypes.FromSymbol(state.EnclosingObject);
            }

            return null;
        }

        private static ICheckedType InferEnclosingMemberName(
            bool isMethod,
            CheckerState state,
            GlobalScope globalScope)
        {
            if (state.EnclosingObject is null)
            {
                return isMethod
                    ? SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.FunctionName, globalScope)
                    : CheckedTypes.String;
            }

            var owner = CheckedTypes.FromSymbol(state.EnclosingObject);
            return isMethod
                ? SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.MethodName, globalScope, [owner])
                : SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.PropertyName, globalScope, [owner]);
        }

        private static ICheckedType InferMemberName(
            ICheckedType ownerType,
            string? memberName,
            bool isMethod,
            GlobalScope globalScope)
        {
            if (string.IsNullOrEmpty(memberName) || ownerType is UnresolvedCheckedType)
            {
                return CheckedTypes.String;
            }

            if (CheckerHelpers.TryGetObjectDeclaration(ownerType) is not null || ownerType is not UnresolvedCheckedType)
            {
                return isMethod
                    ? SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.MethodName, globalScope, [ownerType])
                    : SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.PropertyName, globalScope, [ownerType]);
            }

            return CheckedTypes.String;
        }

        private static ICheckedType InferClassConstant(
            ICheckedType ownerType,
            IExpression memberExpr,
            CheckerState state,
            GlobalScope globalScope)
        {
            if (memberExpr.BoundSymbol is ObjectConstantSymbol objConst)
            {
                return InferObjectConstant(objConst, ownerType, state, globalScope);
            }

            if (CheckerHelpers.TryGetObjectDeclaration(ownerType) is { ObjectKind: PhpTypeDeclType.Enum })
            {
                return SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.EnumCaseName, globalScope, [ownerType]);
            }

            if (CheckerHelpers.TryGetObjectDeclaration(ownerType) is not null)
            {
                return SymbolNameTypeHelper.MakeSymbolNameType(
                    UtilityBehavior.ObjectConstName, globalScope, [ownerType]);
            }

            return SymbolNameTypeHelper.MakeSymbolNameType(UtilityBehavior.ConstName, globalScope);
        }

        private static ICheckedType ResolveOwnerType(
            IDereferenceableBase baseNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IBase2Ast, CheckerState, ICheckedType> resolveExpressionType)
        {
            switch (baseNode)
            {
                case PhpNameAst { BoundSymbol: ObjectDeclarationSymbol obj }:
                    return CheckedTypes.FromSymbol(obj);

                case PhpNameAst name:
                    var scope = GetResolutionScope(state, globalScope);
                    var resolver = new NameResolver(symbolTree, new Domain.Diagnostics.DiagnosticBag());
                    var nameText = name.ValueString ?? string.Empty;
                    var segments = nameText.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var symbol = segments.Length switch
                    {
                        0 => null,
                        1 => resolver.ResolveSymbol(segments[0], scope)
                            ?? resolver.ResolveRelativeName(segments, scope),
                        _ => resolver.ResolveQualifiedName(segments)
                            ?? resolver.ResolveRelativeName(segments, scope),
                    };
                    if (symbol is ObjectDeclarationSymbol objectDecl)
                    {
                        return CheckedTypes.FromSymbol(objectDecl);
                    }

                    if (symbol is not null)
                    {
                        return CheckedTypes.FromSymbol(symbol);
                    }

                    break;

                case PhpVariableAst variable when variable.BoundSymbol is VariableSymbol { Name: "this" }
                    && state.EnclosingObject is not null:
                    return CheckedTypes.FromSymbol(state.EnclosingObject);

                case PhpVariableAst varAst:
                    return resolveExpressionType(varAst, state);

                case PhpDereferenceableAst chain:
                    return resolveExpressionType(chain, state);

                case IExpression expr:
                    return resolveExpressionType(expr, state);
            }

            return CheckedTypes.Unresolved;
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

        private static string? GetMethodNameFromChain(PhpDereferenceableAst deref)
        {
            for (var current = deref; current is not null; current = current.Base as PhpDereferenceableAst)
            {
                switch (current.Suffix)
                {
                    case PhpInstanceMemberAccessAst { MemberName: not null } inst:
                        return GetExpressionText(inst.MemberName);
                    case PhpStaticMemberAccessAst { Member: not null } stat:
                        return GetExpressionText(stat.Member);
                }
            }

            return null;
        }

        private static string? GetExpressionText(IExpression? expression) =>
            expression switch
            {
                TokenValueAst { ValueString: { } s } => s,
                PhpScalarAst { ValueString: { } s } => s,
                _ => expression?.ToString(),
            };
    }
}
