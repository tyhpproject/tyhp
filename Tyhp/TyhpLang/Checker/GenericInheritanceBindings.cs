using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Builds symbol-keyed generic parameter bindings for a receiver type by walking its
    /// <c>extends</c> chain. Shared by member substitution (<see cref="TypeInferrer"/>) and
    /// struct shape materialization.
    /// </summary>
    internal static class GenericInheritanceBindings
    {
        /// <summary>
        /// Binds every generic parameter reachable from the receiver — its own and each generic
        /// ancestor's — to a concrete type argument, keyed by parameter symbol so that same-named
        /// parameters at different levels stay distinct.
        /// </summary>
        public static bool TryBuild(
            ICheckedType receiverType,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            out Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings)
        {
            bindings = null!;
            var type = receiverType;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            ObjectDeclarationSymbol? level;
            IReadOnlyList<ICheckedType> arguments;
            if (type is GenericCheckedType { TypeArguments.Count: > 0 } generic
                && generic.BaseType is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol spelled })
            {
                level = spelled;
                arguments = generic.TypeArguments;
            }
            else if (TryGetObjectDeclaration(type, out var bare))
            {
                // A receiver written without type arguments still inherits whatever its ancestors bind
                // concretely (`class Derived extends Base<string>`).
                level = bare;
                arguments = Array.Empty<ICheckedType>();
            }
            else
            {
                return false;
            }

            bindings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
            var visited = new HashSet<ObjectDeclarationSymbol>();

            while (level is not null && visited.Add(level))
            {
                BindLevelParameters(level, arguments, bindings, state, symbolTree, globalScope, resolveType);

                var parent = TypeComparer.TryGetParentDeclaration(level, symbolTree, globalScope);
                if (parent is null || parent.GenericParameters.Count == 0)
                {
                    break;
                }

                arguments = ResolveExtendsArguments(
                    level, bindings, state, symbolTree, globalScope, resolveType);
                level = parent;
            }

            return bindings.Count > 0;
        }

        /// <summary>
        /// Type arguments on an <c>extends</c> clause. Classes and structs both spell it as a
        /// <c>className</c>, which parks the arguments on the name's <c>"identifier"</c> grammar
        /// addon. The <see cref="TyhpGenericIdentifierAst"/> fallbacks cover names built with their
        /// arguments attached directly instead (tyhpdef import declarations).
        /// </summary>
        public static IReadOnlyList<ITypeExpression>? GetExtendsTypeArguments(ObjectDeclarationSymbol level)
        {
            var extends = level.DeclaringAstNode switch
            {
                PhpObjectTypeDeclAst { Extends: IBase2Ast node } => node,
                TyhpStructDeclAst { Extends: IBase2Ast node } => node,
                TyhpdefImportObjectDeclAst { Extends: IBase2Ast node } => node,
                _ => null,
            };

            if (extends is null)
            {
                return null;
            }

            if (extends.AstGrammarAddons.TryGetValue("identifier", out var addon)
                && addon is PhpTypeExpressionListAst list
                && list.GetAllNotNull().Any())
            {
                return list.GetAllNotNull().ToList();
            }

            if (extends is TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst genArgs }
                && genArgs.GetAllNotNull().Any())
            {
                return genArgs.GetAllNotNull().ToList();
            }

            if (extends is PhpNamedTypeAst { Name: TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst nested } }
                && nested.GetAllNotNull().Any())
            {
                return nested.GetAllNotNull().ToList();
            }

            return null;
        }

        public static StructCheckedType SubstituteShape(
            StructCheckedType shape,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            (StructCheckedType)TypeComparer.ResolveGenericTypeBySymbol(
                shape, bindings, symbolTree, globalScope);

        private static void BindLevelParameters(
            ObjectDeclarationSymbol level,
            IReadOnlyList<ICheckedType> arguments,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            // Inside the open generic itself (`$this` in `Promise<TReturn>`), empty type-argument
            // lists must leave parameters unbound so ObjectGenerics keep meaning. Filling defaults
            // here would rewrite `TReturn` to `void` and break every member that mentions it.
            // Defaults still apply for *foreign* bare receivers (`$f instanceof \Fiber` →
            // `Fiber<…=mixed>` so `resume(?TResume)` becomes `resume(?mixed)`).
            var applyDefaults = !ReferenceEquals(state.EnclosingObject, level);

            if (PropertyPathSupport.IsTyhpExpressionDeclaration(level) && arguments.Count > 0)
            {
                BindExpressionCallableArityParameters(level, arguments, bindings);
                return;
            }

            for (var i = 0; i < level.GenericParameters.Count; i++)
            {
                var param = level.GenericParameters[i];
                if (i < arguments.Count
                    && arguments[i] is not null
                    && !TypeComparer.IsUnresolvedType(arguments[i]))
                {
                    bindings[param] = arguments[i];
                    continue;
                }

                if (!applyDefaults || param.DefaultType is null || bindings.ContainsKey(param))
                {
                    continue;
                }

                var defaultState = ForLevel(state, level);
                var defaultType = resolveType(param.DefaultType, defaultState, false, true);
                if (!TypeComparer.IsUnresolvedType(defaultType))
                {
                    bindings[param] = TypeComparer.ResolveGenericTypeBySymbol(
                        defaultType, bindings, symbolTree, globalScope);
                }
            }
        }

        /// <summary>
        /// Maps callable-arity <c>Expression&lt;TArgs…, TReturn&gt;</c> arguments onto the
        /// runtime class's two parameters: <c>TSource</c> is the first parameter type (or
        /// <c>mixed</c> for a zero-parameter <c>Expression&lt;R&gt;</c>), and <c>TReturn</c>
        /// is always the last type argument.
        /// </summary>
        private static void BindExpressionCallableArityParameters(
            ObjectDeclarationSymbol level,
            IReadOnlyList<ICheckedType> arguments,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings)
        {
            if (level.GenericParameters.Count == 0 || arguments.Count == 0)
            {
                return;
            }

            var tSource = level.GenericParameters[0];
            var tReturn = level.GenericParameters.Count > 1
                ? level.GenericParameters[^1]
                : tSource;

            bindings[tSource] = arguments.Count >= 2 ? arguments[0] : CheckedTypes.Mixed;
            bindings[tReturn] = arguments[^1];
        }

        private static IReadOnlyList<ICheckedType> ResolveExtendsArguments(
            ObjectDeclarationSymbol level,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            var extendsArguments = GetExtendsTypeArguments(level);
            if (extendsArguments is null || extendsArguments.Count == 0)
            {
                return Array.Empty<ICheckedType>();
            }

            var levelState = ForLevel(state, level);
            var resolved = new List<ICheckedType>(extendsArguments.Count);
            foreach (var argument in extendsArguments)
            {
                var argumentType = resolveType(argument, levelState, false, true);
                resolved.Add(TypeComparer.ResolveGenericTypeBySymbol(
                    argumentType, bindings, symbolTree, globalScope));
            }

            return resolved;
        }

        /// <summary>
        /// State for resolving a declaration written in <paramref name="level"/>'s own generic scope
        /// (its <c>extends</c> arguments, its parameter defaults). Must stay mutable — the resolver
        /// it is handed to may snapshot it.
        /// </summary>
        private static CheckerState ForLevel(CheckerState state, ObjectDeclarationSymbol level)
        {
            var levelState = state.Fork();
            levelState.EnclosingObject = level;
            levelState.EnclosingObjectType = CheckedTypes.FromSymbol(level);
            levelState.ObjectGenerics = level.GenericParameters;
            return levelState;
        }

        private static bool TryGetObjectDeclaration(
            ICheckedType receiverType,
            out ObjectDeclarationSymbol objectDecl)
        {
            objectDecl = null!;
            var unwrapped = receiverType;
            while (unwrapped is NullableCheckedType or GenericCheckedType)
            {
                unwrapped = unwrapped is NullableCheckedType nullable
                    ? nullable.InnerType
                    : ((GenericCheckedType)unwrapped).BaseType;
            }

            if (unwrapped is not SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                return false;
            }

            objectDecl = obj;
            return true;
        }
    }
}
