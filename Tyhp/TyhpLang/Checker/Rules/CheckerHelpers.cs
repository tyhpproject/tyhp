using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    internal static class CheckerHelpers
    {
        // The binder only binds type references on declarations; it does not bind call-site
        // function-name references inside bodies, so `PhpNameAst.BoundSymbol` is null for free
        // function calls (e.g. `isFoo($x)`). Resolve the declaration by name from the enclosing
        // scope so call/return-type inference and type-guard narrowing can recognize the function.
        public static FunctionDeclarationSymbol? ResolveFreeFunction(
            PhpNameAst nameAst,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
            => ResolveCallSiteFunctionSymbol(nameAst, state, symbolTree, globalScope)
                as FunctionDeclarationSymbol;

        /// <summary>
        /// Call-site free constant names (e.g. <c>\PHP_URL_HOST</c>) are often unbound by the
        /// binder. Resolve the declaration so tyhpdef <c>?? N</c> start values can type as
        /// integer literals at overload selection.
        /// </summary>
        public static ConstantSymbol? ResolveFreeConstant(
            PhpNameAst nameAst,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (nameAst.BoundSymbol is ConstantSymbol bound)
            {
                return bound;
            }

            var raw = nameAst.ValueString;
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            var fromScope = state.EnclosingFunction?.ContainingScope
                ?? state.EnclosingObject?.ContainingScope
                ?? (IBaseScope)globalScope;

            var resolver = new NameResolver(symbolTree, new DiagnosticBag());
            var isFullyQualified = raw.StartsWith('\\');
            var trimmed = raw.TrimStart('\\');
            if (string.IsNullOrEmpty(trimmed))
            {
                return null;
            }

            IBaseSymbol? resolved;
            if (isFullyQualified)
            {
                resolved = resolver.ResolveQualifiedName(trimmed.Split('\\'));
            }
            else if (trimmed.Contains('\\'))
            {
                resolved = resolver.ResolveRelativeName(trimmed.Split('\\'), fromScope);
            }
            else
            {
                resolved = resolver.ResolveSymbol(trimmed, fromScope)
                    ?? resolver.ResolveRelativeName([trimmed], fromScope);
            }

            return resolved as ConstantSymbol;
        }

        /// <summary>
        /// True when the call-site name resolves to a declared function, tyhpdef stub,
        /// source type-alias factory, or compile-time builtin (<c>nameof</c> / <c>typeof</c> /
        /// <c>default</c> / <c>variable_exists</c>). Used to decide whether a missing-function
        /// diagnostic is warranted without treating builtins as
        /// <see cref="FunctionDeclarationSymbol"/>.
        /// </summary>
        public static bool FreeFunctionCallResolves(
            PhpNameAst nameAst,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
            => ResolveCallSiteFunctionSymbol(nameAst, state, symbolTree, globalScope)
                is FunctionDeclarationSymbol or BuiltInFunctionSymbol or TypeAliasSymbol;

        /// <summary>
        /// Call-site <c>UserId()</c> / imported class-kind <c>use App\Types\UserId</c> used as a
        /// factory. Tyhpdef aliases stay type-only and do not resolve here.
        /// </summary>
        public static TypeAliasSymbol? ResolveTypeAliasFactory(
            PhpNameAst nameAst,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
            => ResolveCallSiteFunctionSymbol(nameAst, state, symbolTree, globalScope) as TypeAliasSymbol;

        /// <summary>
        /// Class-level alias factory invoked as a static method (<c>UserService::NameType()</c>).
        /// </summary>
        public static ObjectTypeAliasSymbol? TryResolveObjectTypeAliasFactory(
            ICheckedType receiverType,
            string memberName,
            SymbolTree symbolTree)
        {
            var objectDecl = TryGetObjectDeclaration(UnwrapMemberAccessReceiver(receiverType));
            if (objectDecl is null
                || string.IsNullOrEmpty(memberName)
                || !objectDecl.Members.TryGetValue(memberName, out var member)
                || member is not ObjectTypeAliasSymbol objectAlias
                || !IsSourceTypeAliasFactory(objectAlias))
            {
                return null;
            }

            _ = symbolTree;
            return objectAlias;
        }

        /// <summary>
        /// Source <c>.tyhp</c> aliases emit a PHP factory. Tyhpdef aliases do not, unless
        /// <c>#[\Tyhp\GenericRuntime(aliasFactory: …)]</c> stamps a compiled-library factory.
        /// </summary>
        public static bool IsSourceTypeAliasFactory(IBaseSymbol? symbol)
        {
            if (symbol is not TypeAliasSymbol and not ObjectTypeAliasSymbol)
            {
                return false;
            }

            if (GenericRuntimeAttributeSupport.TryRead(symbol)?.HasAliasFactory == true)
            {
                return true;
            }

            var file = symbol.SourceFile ?? "";
            if (file.Contains("<tyhpdef:", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return symbol is not BaseSymbol { DeclaringAstNode.LanguageMode: "tyhpdef" };
        }

        public static IReadOnlyList<GenericTypeParameterSymbol> GetAliasFactoryGenericParameters(
            IBaseSymbol alias) =>
            alias switch
            {
                TypeAliasSymbol fileAlias => fileAlias.GenericParameters,
                ObjectTypeAliasSymbol objectAlias => objectAlias.GenericParameters,
                _ => Array.Empty<GenericTypeParameterSymbol>(),
            };

        /// <summary>
        /// True when a factory call was written with type arguments (<c>Optional&lt;int&gt;()</c>).
        /// Arguments hang off the callee name and/or the call's <c>genericTypeArguments</c> addon.
        /// </summary>
        public static bool TypeAliasFactoryCallHasTypeArguments(
            IDereferenceableBase? callBase,
            PhpCallAst call)
        {
            if (HasNonEmptyTypeArgumentList(TypeInferrer.TryGetCallSiteTypeArgumentList(callBase)))
            {
                return true;
            }

            if (call.AstGrammarAddons.TryGetValue("genericTypeArguments", out var addon)
                && HasNonEmptyTypeArgumentList(addon as PhpTypeExpressionListAst))
            {
                return true;
            }

            return callBase is TyhpGenericIdentifierAst generic
                && HasNonEmptyTypeArgumentList(generic.GenericArguments as PhpTypeExpressionListAst);
        }

        private static bool HasNonEmptyTypeArgumentList(PhpTypeExpressionListAst? list)
            => list is not null && list.GetAllNotNull().Any();

        private static IBaseSymbol? ResolveCallSiteFunctionSymbol(
            PhpNameAst nameAst,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            // Alias-name-as-call: `UserId()` binds the `type UserId` symbol, not a user
            // function. Ordinary methods/functions are never factories just because names match.
            if (nameAst.BoundSymbol is TypeAliasSymbol boundAlias
                && IsSourceTypeAliasFactory(boundAlias))
            {
                return boundAlias;
            }

            if (nameAst.BoundSymbol is FunctionDeclarationSymbol bound)
            {
                return bound;
            }

            if (nameAst.BoundSymbol is BuiltInFunctionSymbol builtin)
            {
                return builtin;
            }

            var raw = nameAst.ValueString;
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            // Top-level statements have no enclosing function/object. Falling back to
            // GlobalScope skips the current namespace, so unqualified `twice()` in
            // `namespace App` would miss `App\twice` (TYHP4182). FindCallSiteScope
            // selects this file's NamespaceBlockScope when CurrentNamespaceName is set.
            var lexical = state.NameResolutionScope
                ?? state.EnclosingCallable?.ContainingScope
                ?? state.EnclosingFunction?.ContainingScope
                ?? state.EnclosingObject?.ContainingScope;

            var resolver = new NameResolver(symbolTree, new DiagnosticBag());
            var fromScope = resolver.FindCallSiteScope(
                nameAst, lexical, state.CurrentNamespaceName);
            var isFullyQualified = raw.StartsWith('\\');
            var trimmed = raw.TrimStart('\\');
            if (string.IsNullOrEmpty(trimmed))
            {
                return null;
            }

            // Match type-name resolution: `\Ns\fn` is global-qualified; `Ns\fn` is relative
            // to the enclosing namespace (then global). A single-segment string containing
            // backslashes would never match a namespaced function symbol.
            if (isFullyQualified)
            {
                return AsCallSiteFunction(resolver.ResolveQualifiedName(trimmed.Split('\\')));
            }

            if (trimmed.Contains('\\'))
            {
                return AsCallSiteFunction(resolver.ResolveRelativeName(trimmed.Split('\\'), fromScope));
            }

            return AsCallSiteFunction(
                    resolver.ResolveSymbol(trimmed, fromScope, IsCallSiteFunctionOrFactory))
                ?? AsCallSiteFunction(resolver.ResolveRelativeName([trimmed], fromScope));
        }

        /// <summary>
        /// Free-function / file-level factory lookup ignores class members. A static method
        /// named <c>string</c> is not the alias factory and must not hide a namespace function
        /// or file-level <c>type</c> factory. Class-likes and file-level aliases are still
        /// accepted so <see cref="AsCallSiteFunction"/> can recover a sibling function or
        /// treat a source alias as the factory.
        /// </summary>
        private static bool IsCallSiteFunctionOrFactory(IBaseSymbol symbol)
            => symbol is FunctionDeclarationSymbol
                or BuiltInFunctionSymbol
                or ObjectDeclarationSymbol
                or TypeAliasSymbol;

        /// <summary>
        /// Generic name lookup prefers class-likes over same-named functions. A call site needs
        /// the function table, so when a class/struct/enum won, look for a sibling function.
        /// A class-kind import (or same-namespace name) whose target is a source
        /// <see cref="TypeAliasSymbol"/> is the alias factory of the same short name.
        /// </summary>
        private static IBaseSymbol? AsCallSiteFunction(IBaseSymbol? resolved)
        {
            if (resolved is FunctionDeclarationSymbol or BuiltInFunctionSymbol)
            {
                return resolved;
            }

            if (resolved is TypeAliasSymbol alias && IsSourceTypeAliasFactory(alias))
            {
                return alias;
            }

            if (resolved?.ContainingScope is not { } scope || string.IsNullOrEmpty(resolved.Name))
            {
                return null;
            }

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is FunctionDeclarationSymbol or BuiltInFunctionSymbol
                    && string.Equals(symbol.Name, resolved.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return symbol;
                }
            }

            return null;
        }

        /// <summary>
        /// True for Story 14.5 reserved keyword constructs that have ExtCore tyhpdef stubs
        /// (<c>exit</c> / <c>die</c> / <c>clone</c>).
        /// </summary>
        public static bool IsKeywordConstructName(string? name) =>
            string.Equals(name, "exit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "die", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "clone", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Resolves an ExtCore keyword-construct stub by name. Prefer
        /// <see cref="IBase2Ast.BoundSymbol"/> from the binder when present; this is the checker
        /// fallback when binding was skipped.
        /// </summary>
        public static FunctionDeclarationSymbol? ResolveKeywordConstructFunction(
            string name,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (string.IsNullOrEmpty(name) || !IsKeywordConstructName(name))
            {
                return null;
            }

            var fromScope = state.EnclosingFunction?.ContainingScope
                ?? state.EnclosingObject?.ContainingScope
                ?? (IBaseScope)globalScope;

            var resolver = new NameResolver(symbolTree, new DiagnosticBag());
            return (resolver.ResolveSymbol(name, fromScope)
                ?? resolver.ResolveRelativeName([name], fromScope)
                ?? resolver.ResolveRelativeName([name], globalScope)) as FunctionDeclarationSymbol;
        }

        /// <summary>
        /// True for PHP 8.1 first-class callable syntax whose argument list is solely a bare
        /// ellipsis (no unpacked expression) — used for normal calls and keyword construct calls.
        /// </summary>
        public static bool IsFirstClassCallableArgumentList(PhpArgumentListAst? arguments)
        {
            var args = arguments?.GetAllNotNull().ToList();
            return args is { Count: 1 }
                && args[0].IsVariadic
                && args[0].Expression is null;
        }

        /// <summary>
        /// Picks a tyhpdef overload whose parameter arity fits the call. The primary symbol is the
        /// first declaration; additional signatures live on <see cref="FunctionDeclarationSymbol.Overloads"/>.
        /// Same-arity candidates are refined by <see cref="FunctionOverloadSelector"/> using argument
        /// types (named vs positional bags for <c>call_user_func_array</c>). Arity matching still
        /// unblocks calls like <c>call_user_func($cb, $a, $b)</c> that would otherwise be checked
        /// only against a 1-parameter primary and falsely report TYHP4143.
        /// When no signature fits, returns the widest (for too-many) or narrowest-min (for too-few)
        /// candidate so arity diagnostics cite a sensible expected bound.
        /// </summary>
        public static FunctionDeclarationSymbol SelectFunctionOverloadForCall(
            FunctionDeclarationSymbol primary,
            PhpArgumentListAst? arguments)
        {
            if (primary.Overloads.Count == 0)
            {
                return primary;
            }

            var args = arguments?.GetAllNotNull().ToList() ?? [];
            if (args.Any(a => a.IsVariadic))
            {
                return SelectSpreadCompatibleOverload(
                    primary,
                    EnumerateFunctionSignatures(primary),
                    CountArgumentsBeforeSpread(args),
                    static c => c.Parameters);
            }

            var argCount = args.Count;
            FunctionDeclarationSymbol? exact = null;
            FunctionDeclarationSymbol? inRange = null;
            var signatures = EnumerateFunctionSignatures(primary).ToList();
            foreach (var candidate in signatures)
            {
                var (min, max) = GetParameterArityRange(candidate.Parameters);
                if (argCount < min || argCount > max)
                {
                    continue;
                }

                // Prefer a signature whose declared parameter count equals the call arity
                // (call_user_func's ladder is one required param per slot, no defaults).
                if (candidate.Parameters.Count == argCount
                    || (candidate.Parameters.Count > 0
                        && candidate.Parameters[^1].IsVariadic
                        && candidate.Parameters.Count - 1 <= argCount))
                {
                    exact ??= candidate;
                }
                else
                {
                    inRange ??= candidate;
                }
            }

            if (exact is not null || inRange is not null)
            {
                return exact ?? inRange!;
            }

            // No fit: pick a signature that makes the arity diagnostic mention a useful bound.
            var widest = signatures[0];
            var widestMax = GetParameterArityRange(widest.Parameters).Max;
            var narrowest = signatures[0];
            var narrowestMin = GetParameterArityRange(narrowest.Parameters).Min;
            foreach (var candidate in signatures.Skip(1))
            {
                var (min, max) = GetParameterArityRange(candidate.Parameters);
                if (max > widestMax)
                {
                    widest = candidate;
                    widestMax = max;
                }

                if (min < narrowestMin)
                {
                    narrowest = candidate;
                    narrowestMin = min;
                }
            }

            return argCount > widestMax ? widest : narrowest;
        }

        /// <summary>
        /// Same arity pick as <see cref="SelectFunctionOverloadForCall"/> for
        /// <see cref="ObjectMethodSymbol.Overloads"/> (Story 21.6 Phase 5 — <c>bindTo</c>
        /// omitted / <c>'static'</c> vs explicit scope).
        /// </summary>
        public static ObjectMethodSymbol SelectMethodOverloadForCall(
            ObjectMethodSymbol primary,
            PhpArgumentListAst? arguments)
        {
            if (primary.Overloads.Count == 0)
            {
                return primary;
            }

            var args = arguments?.GetAllNotNull().ToList() ?? [];
            if (args.Any(a => a.IsVariadic))
            {
                return SelectSpreadCompatibleOverload(
                    primary,
                    EnumerateMethodSignatures(primary),
                    CountArgumentsBeforeSpread(args),
                    static c => GetCallSiteParameters(c));
            }

            var argCount = args.Count;
            ObjectMethodSymbol? exact = null;
            ObjectMethodSymbol? inRange = null;
            var signatures = EnumerateMethodSignatures(primary).ToList();
            foreach (var candidate in signatures)
            {
                var parameters = GetCallSiteParameters(candidate);
                var (min, max) = GetParameterArityRange(parameters);
                if (argCount < min || argCount > max)
                {
                    continue;
                }

                if (parameters.Count == argCount
                    || (parameters.Count > 0
                        && parameters[^1].IsVariadic
                        && parameters.Count - 1 <= argCount))
                {
                    exact ??= candidate;
                }
                else
                {
                    inRange ??= candidate;
                }
            }

            if (exact is not null || inRange is not null)
            {
                return exact ?? inRange!;
            }

            var widest = signatures[0];
            var widestMax = GetParameterArityRange(GetCallSiteParameters(widest)).Max;
            var narrowest = signatures[0];
            var narrowestMin = GetParameterArityRange(GetCallSiteParameters(narrowest)).Min;
            foreach (var candidate in signatures.Skip(1))
            {
                var (min, max) = GetParameterArityRange(GetCallSiteParameters(candidate));
                if (max > widestMax)
                {
                    widest = candidate;
                    widestMax = max;
                }

                if (min < narrowestMin)
                {
                    narrowest = candidate;
                    narrowestMin = min;
                }
            }

            return argCount > widestMax ? widest : narrowest;
        }

        public static IEnumerable<ObjectMethodSymbol> EnumerateMethodSignatures(ObjectMethodSymbol primary)
        {
            yield return primary;
            foreach (var overload in primary.Overloads)
            {
                yield return overload;
            }
        }

        /// <summary>
        /// Parameters visible at an instance call site. Extension methods declare
        /// <c>extends T $this</c> (or <c>extends T $str</c>) as a real first parameter; the
        /// receiver is not in the argument list, so overload arity/scoring skip it.
        /// Same skip as <see cref="ExcludeExtensionReceiver"/>.
        /// </summary>
        public static IReadOnlyList<ParameterInfo> GetCallSiteParameters(ObjectMethodSymbol method) =>
            ExcludeExtensionReceiver(method.Parameters, method);

        public static IEnumerable<FunctionDeclarationSymbol> EnumerateFunctionSignatures(
            FunctionDeclarationSymbol primary)
        {
            yield return primary;
            foreach (var overload in primary.Overloads)
            {
                yield return overload;
            }
        }

        /// <summary>
        /// Count of arguments before the first <c>...$xs</c> unpack. That prefix is the only
        /// statically known arity; the spread may contribute zero or more extra values.
        /// </summary>
        private static int CountArgumentsBeforeSpread(IReadOnlyList<PhpArgumentAst> args)
        {
            var count = 0;
            foreach (var arg in args)
            {
                if (arg.IsVariadic)
                {
                    break;
                }

                count++;
            }

            return count;
        }

        /// <summary>
        /// Spread unpack is not a static arity, so the previous "keep primary" policy left
        /// <c>max($a, $b, ...$rest)</c> on <c>max(array&lt;T&gt;)</c> (first overlay) and the
        /// call's return type stayed unbound <c>T</c>. Keep signatures that can accept the
        /// known prefix, then prefer a trailing variadic because unpack typically supplies
        /// extra values.
        /// </summary>
        private static T SelectSpreadCompatibleOverload<T>(
            T primary,
            IEnumerable<T> signatures,
            int prefixCount,
            Func<T, IReadOnlyList<ParameterInfo>> parametersOf)
            where T : class
        {
            T? firstFitting = null;
            T? firstVariadic = null;
            foreach (var candidate in signatures)
            {
                var max = GetParameterArityRange(parametersOf(candidate)).Max;
                if (prefixCount > max)
                {
                    continue;
                }

                firstFitting ??= candidate;
                if (firstVariadic is null && max == int.MaxValue)
                {
                    firstVariadic = candidate;
                }
            }

            return firstVariadic ?? firstFitting ?? primary;
        }

        public static (int Min, int Max) GetParameterArityRange(IReadOnlyList<ParameterInfo> parameters)
        {
            var min = 0;
            foreach (var param in parameters)
            {
                if (param.IsVariadic)
                {
                    break;
                }

                if (param.DefaultValue is null)
                {
                    min++;
                }
            }

            if (parameters.Count > 0 && parameters[^1].IsVariadic)
            {
                var minFromSlice = GetTrailingSliceMin(parameters[^1].DeclaredType);
                return (min + minFromSlice, int.MaxValue);
            }

            return (min, parameters.Count);
        }

        /// <summary>
        /// <c>array&lt;Slice&lt;T, start, TMin&gt;&gt; ...$arrays</c> requires at least
        /// <c>TMin</c> extra arguments so a 1-array call does not match the zip overload.
        /// </summary>
        private static int GetTrailingSliceMin(ITypeExpression? declaredType)
        {
            var inner = declaredType;
            if (declaredType is PhpNamedTypeAst named
                && string.Equals(named.Identifier, "array", StringComparison.OrdinalIgnoreCase)
                && named.AstGrammarAddons.TryGetValue("typeName", out var addon)
                && addon is PhpTypeExpressionListAst list)
            {
                var args = list.GetAllNotNull().ToList();
                if (args.Count > 0)
                {
                    inner = args[^1];
                }
            }

            if (inner is not PhpNamedTypeAst sliceNamed
                || sliceNamed.Identifier is null
                || !sliceNamed.Identifier.Contains("CallableParametersSlice", StringComparison.Ordinal))
            {
                return 0;
            }

            if (!sliceNamed.AstGrammarAddons.TryGetValue("typeName", out var sliceAddon)
                || sliceAddon is not PhpTypeExpressionListAst sliceArgs)
            {
                return 0;
            }

            var typeArgs = sliceArgs.GetAllNotNull().ToList();
            if (typeArgs.Count < 3)
            {
                return 0;
            }

            return TyhpdefConstIntLiteral.TryGetIntegerFromExpression(
                typeArgs[2] as IExpression, out var min)
                || TryReadTypeAstIntLiteral(typeArgs[2], out min)
                ? (int)Math.Max(0, min)
                : 0;
        }

        private static bool TryReadTypeAstIntLiteral(ITypeExpression typeAst, out long value)
        {
            value = 0;
            while (typeAst is PhpTypeExpressionAst { Types: { } types })
            {
                var inner = types.GetAllNotNull().OfType<ITypeExpression>().FirstOrDefault();
                if (inner is null)
                {
                    break;
                }

                typeAst = inner;
            }

            if (typeAst is IExpression expr
                && TyhpdefConstIntLiteral.TryGetIntegerFromExpression(expr, out value)
                && value >= 0)
            {
                return true;
            }

            var text = typeAst switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNamedTypeAst named => named.Identifier,
                PhpScalarAst scalar => scalar.ValueString,
                _ => null,
            };
            return !string.IsNullOrEmpty(text) && long.TryParse(text, out value) && value >= 0;
        }

        public static void ReportError(
            CheckerRuleContext context,
            CheckerState state,
            IBase2Ast node,
            MessageCode code,
            params object[] args)
        {
            context.ReportError(state, node, code, args);
        }

        public static void ReportError(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast node,
            MessageCode code,
            params object[] args)
        {
            var fileName = ResolveDiagnosticFileName(state, node);
            DiagnosticExtensions.GetOptionalEnd(node, out var endLine, out var endColumn);
            diagnostics.Add(Diagnostic.Error(code, fileName, node.Line, node.Column, args, endLine, endColumn));
        }

        /// <summary>
        /// TYHP4358: <c>object</c> / <c>mixed</c> is not assignable to an object shape without a
        /// shape guard. Returns true when that diagnostic was reported.
        /// </summary>
        public static bool TryReportObjectShapeRequiresGuard(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast node,
            ICheckedType source,
            ICheckedType target)
        {
            if (!IsUnnarrowedObjectOrMixed(source) || !IsObjectShapeAssignmentTarget(target))
            {
                return false;
            }

            ReportError(
                diagnostics,
                state,
                node,
                MessageCode.CheckerObjectShapeRequiresGuard,
                source.DisplayName,
                target.DisplayName);
            return true;
        }

        /// <summary>
        /// TYHP4360: bare <c>callable</c> / <c>mixed</c> is not assignable to a callable
        /// shape without a shape guard. Returns true when that diagnostic was reported.
        /// </summary>
        public static bool TryReportCallableShapeRequiresGuard(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast node,
            ICheckedType source,
            ICheckedType target)
        {
            if (!IsUnnarrowedBareCallableOrMixed(source) || !IsCallableShapeAssignmentTarget(target))
            {
                return false;
            }

            ReportError(
                diagnostics,
                state,
                node,
                MessageCode.CheckerCallableShapeRequiresGuard,
                source.DisplayName,
                target.DisplayName);
            return true;
        }

        /// <summary>
        /// TYHP4352 / 4353 / 4354 when <paramref name="source"/> fails a <c>__New&lt;Shape&gt;</c>
        /// bound for a constructability reason. Instance-shape mismatch stays the generic
        /// incompatible-type diagnostic.
        /// </summary>
        public static bool TryReportNewConstraintFailure(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast node,
            ICheckedType source,
            ICheckedType target,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (!TypeComparer.TryDescribeNewConstraintFailure(
                    source,
                    target,
                    symbolTree,
                    globalScope,
                    out var failure,
                    out var sourceDisplay,
                    out var shapeDisplay,
                    out var kind))
            {
                return false;
            }

            switch (failure)
            {
                case NewConstraintFailure.NotConstructableKind:
                    ReportError(
                        diagnostics,
                        state,
                        node,
                        MessageCode.CheckerNewConstraintNotConstructable,
                        sourceDisplay,
                        shapeDisplay,
                        kind);
                    return true;
                case NewConstraintFailure.NonPublicConstructor:
                    ReportError(
                        diagnostics,
                        state,
                        node,
                        MessageCode.CheckerNewConstraintNonPublicConstructor,
                        sourceDisplay,
                        shapeDisplay);
                    return true;
                case NewConstraintFailure.ConstructorMismatch:
                    ReportError(
                        diagnostics,
                        state,
                        node,
                        MessageCode.CheckerNewConstraintConstructorMismatch,
                        sourceDisplay,
                        shapeDisplay);
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsUnnarrowedObjectOrMixed(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return TypeComparer.IsMixedType(type) || TypeComparer.IsBuiltInName(type, "object");
        }

        private static bool IsUnnarrowedBareCallableOrMixed(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (TypeComparer.IsMixedType(type))
            {
                return true;
            }

            return type is not CallableCheckedType
                && TypeComparer.IsBuiltInName(type, "callable");
        }

        private static bool IsObjectShapeAssignmentTarget(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (TypeComparer.IsObjectShapeType(type))
            {
                return true;
            }

            if (type is IntersectionCheckedType intersection)
            {
                // Any shape member (`\Psr\Log\LoggerInterface & ClockShape`) needs the guard: an
                // unnarrowed `object`/`mixed` source can never satisfy that member structurally,
                // regardless of the intersection's other, non-shape members.
                return intersection.Members.Any(member =>
                    TypeComparer.IsObjectShapeType(member)
                    || TypeComparer.IsBuiltInName(member, "object"));
            }

            return false;
        }

        private static bool IsCallableShapeAssignmentTarget(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (TypeComparer.IsCallableShapeType(type))
            {
                return true;
            }

            if (type is IntersectionCheckedType intersection)
            {
                return intersection.Members.Any(IsCallableShapeAssignmentTarget);
            }

            return false;
        }

        /// <summary>
        /// Reports an error and attaches a Levenshtein "did you mean" suggestion when a close
        /// candidate exists (Story 14 Phase 3).
        /// </summary>
        public static void ReportErrorWithDidYouMean(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast node,
            MessageCode code,
            string unknownName,
            IEnumerable<string> candidates,
            params object[] args)
        {
            var fileName = ResolveDiagnosticFileName(state, node);
            DiagnosticExtensions.GetOptionalEnd(node, out var endLine, out var endColumn);
            var diagnostic = Diagnostic.Error(code, fileName, node.Line, node.Column, args, endLine, endColumn);
            diagnostic = DidYouMean.Attach(diagnostic, unknownName, candidates);
            diagnostics.Add(diagnostic);
        }

        public static void ReportWarning(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast node,
            MessageCode code,
            params object[] args)
        {
            var fileName = ResolveDiagnosticFileName(state, node);
            DiagnosticExtensions.GetOptionalEnd(node, out var endLine, out var endColumn);
            diagnostics.Add(Diagnostic.Warning(code, fileName, node.Line, node.Column, args, endLine, endColumn));
        }

        public static void ReportInfo(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast node,
            MessageCode code,
            params object[] args)
        {
            var fileName = ResolveDiagnosticFileName(state, node);
            DiagnosticExtensions.GetOptionalEnd(node, out var endLine, out var endColumn);
            diagnostics.Add(Diagnostic.Info(code, fileName, node.Line, node.Column, args, endLine, endColumn));
        }

        /// <summary>
        /// Prefer the AST node's owning file when present so diagnostics on foreign nodes
        /// (e.g. a base method signature while checking an override) keep line/column aligned
        /// with the file that actually contains that span.
        /// </summary>
        internal static string ResolveDiagnosticFileName(CheckerState state, IBase2Ast node) =>
            node.OwningFile?.FileName ?? state.CurrentFileName ?? string.Empty;

        public static MemberModifier ToMemberModifiers(IEnumerable<PhpModifier>? modifiers)
        {
            if (modifiers is null)
            {
                return MemberModifier.None;
            }

            MemberModifier result = MemberModifier.None;
            foreach (var modifier in modifiers)
            {
                result |= modifier switch
                {
                    PhpModifier.Public => MemberModifier.Public,
                    PhpModifier.Protected => MemberModifier.Protected,
                    PhpModifier.Private => MemberModifier.Private,
                    PhpModifier.Static => MemberModifier.Static,
                    PhpModifier.Abstract => MemberModifier.Abstract,
                    PhpModifier.Final => MemberModifier.Final,
                    PhpModifier.Readonly => MemberModifier.Readonly,
                    PhpModifier.Var => MemberModifier.Var,
                    PhpModifier.Internal => MemberModifier.Internal,
                    _ => MemberModifier.None,
                };
            }

            return result;
        }

        public static MemberModifier ToMemberModifiers(PhpModifierListAst? modifiers)
        {
            if (modifiers is null)
            {
                return MemberModifier.None;
            }

            var result = ToMemberModifiers(modifiers.Modifiers);
            if (modifiers.AstGrammarAddons.ContainsKey("isInternal"))
            {
                result |= MemberModifier.Internal;
            }

            return result;
        }

        public static int CountVisibilityModifiers(MemberModifier modifiers)
        {
            var count = 0;
            if ((modifiers & MemberModifier.Public) != 0) count++;
            if ((modifiers & MemberModifier.Protected) != 0) count++;
            if ((modifiers & MemberModifier.Private) != 0) count++;
            if ((modifiers & MemberModifier.Internal) != 0) count++;
            return count;
        }

        public static bool IsBoolType(ICheckedType type) =>
            type is LiteralCheckedType { Value: bool }
            || IsBuiltInName(type, "bool")
            || (type is UnionCheckedType union && union.Members.All(IsBoolType));

        public static bool IsScalarType(ICheckedType type) =>
            type is LiteralCheckedType
            || NormalizeTypeName(type.DisplayName) is "int" or "float" or "string" or "bool";

        public static bool IsIterableType(
            ICheckedType type,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            // `array<K, V>` / `iterable<V>` are `GenericCheckedType` wrappers around the plain
            // `array`/`iterable` built-in — unwrap before the name check, mirroring the equivalent
            // (but private, TypeComparer-only) `IsArrayLikeType`/`IsIterableType` helpers in
            // TypeComparer.BuiltInTypes.cs. Without this, spreading a declared/parameter-typed
            // `array<int, Foo>` (e.g. a variadic parameter's array type inside its own function body)
            // was rejected as non-iterable even though a plain untyped `array` was accepted.
            var unwrapped = type is GenericCheckedType generic ? generic.BaseType : type;
            if (IsBuiltInName(unwrapped, "array") || IsBuiltInName(unwrapped, "iterable"))
            {
                return true;
            }

            return ImplementsInterface(type, "Traversable", symbolTree, globalScope);
        }

        public static bool IsArrayOrStringType(ICheckedType type) =>
            IsBuiltInName(type, "array") || IsBuiltInName(type, "string");

        /// <summary>
        /// Recognizes the classic PHP "array callable" literal shape <c>[$receiver, 'method']</c> —
        /// a two-element positional array literal whose first element could be an object (or
        /// class-name string) and whose second is a method-name string — when the *target*
        /// parameter/variable type is <c>callable</c>. General <c>array</c> values remain rejected
        /// as callable (most arrays are not valid callables — see the plain-`string`/`array`
        /// rejection in <c>TypeComparer.BuiltInTypes.TryCheckCallableAssignability</c>), but this
        /// exact literal shape is the idiomatic two-element callable form used throughout the PHP
        /// ecosystem (framework dispatchers, <c>\Tyhp\Generic::bindCallable</c>'s own
        /// runtime-checked <c>[$obj, 'method']</c> handling, etc.), so a literal written in this
        /// shape is accepted structurally without requiring a separate runtime guard first.
        /// </summary>
        public static bool IsArrayCallableLiteral(
            IExpression? expression,
            ICheckedType targetType,
            CheckerRuleContext context,
            CheckerState state)
        {
            if (!IsCallableLikeType(targetType))
            {
                return false;
            }

            IReadOnlyList<PhpArrayPairAst> pairs = expression switch
            {
                PhpArrayAst arrayAst => arrayAst.ArrayPairs?.GetAllExcludingSkippedSlots().ToList() ?? [],
                PhpArrayPairListAst pairList => pairList.GetAllExcludingSkippedSlots().ToList(),
                _ => [],
            };

            if (pairs.Count != 2
                || pairs.Any(pair => pair.IsExpansion || pair.KeyExpr is not null || pair.ValueExpr is null))
            {
                return false;
            }

            var receiverType = context.ResolveExpressionType(pairs[0].ValueExpr!, state);
            var methodNameType = context.ResolveExpressionType(pairs[1].ValueExpr!, state);

            return IsCallableArrayReceiverType(receiverType) && IsCallableArrayMethodNameType(methodNameType);
        }

        private static bool IsCallableLikeType(ICheckedType type)
        {
            if (type is CallableCheckedType)
            {
                return true;
            }

            var unwrapped = type is GenericCheckedType generic ? generic.BaseType : type;
            return IsBuiltInName(unwrapped, "callable");
        }

        private static bool IsCallableArrayReceiverType(ICheckedType type) =>
            TryGetObjectDeclaration(type) is not null
            || IsBuiltInName(type, "object")
            || IsBuiltInName(type, "string")
            || (type is LiteralCheckedType literal && IsBuiltInName(literal.UnderlyingType, "string"));

        private static bool IsCallableArrayMethodNameType(ICheckedType type) =>
            IsBuiltInName(type, "string")
            || (type is LiteralCheckedType literal && IsBuiltInName(literal.UnderlyingType, "string"));

        public static bool IsBuiltInName(ICheckedType type, string name) =>
            string.Equals(NormalizeTypeName(type.DisplayName), name, StringComparison.OrdinalIgnoreCase)
            || (type is SimpleCheckedType simple
                && string.Equals(NormalizeTypeName(simple.ResolvedSymbol.Name), name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// True when <paramref name="type"/> is bare <c>mixed</c> (or <c>?mixed</c>) that still
        /// requires narrowing before type-specific operations. Unresolved (error-recovery) is never
        /// treated as mixed here — it stays permissive to avoid cascading diagnostics.
        /// </summary>
        public static bool IsUnnarrowedMixed(ICheckedType type)
        {
            if (type is UnresolvedCheckedType)
            {
                return false;
            }

            var current = type;
            while (current is NullableCheckedType nullable)
            {
                current = nullable.InnerType;
            }

            if (TypeComparer.IsMixedType(current))
            {
                return true;
            }

            // `mixed|T` must still require narrowing: UnionCheckedType.IsMixed only looks at
            // `.IsMixed` on members, so a BuiltIn SimpleCheckedType("mixed") leaves the union
            // unmarked and would otherwise bypass TYHP4160 (FOUND #1g).
            if (current is UnionCheckedType union)
            {
                return union.Members.Any(IsUnnarrowedMixed);
            }

            return false;
        }

        /// <summary>
        /// Reports TYHP4160 when <paramref name="type"/> is unnarrowed <c>mixed</c>, unless the
        /// current state is an existence-probe context (<c>isset</c>/<c>??</c>/…).
        /// </summary>
        public static bool ReportMixedRequiresNarrowing(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast node,
            ICheckedType type)
        {
            if (state.IsExistenceProbeContext || !IsUnnarrowedMixed(type))
            {
                return false;
            }

            ReportError(diagnostics, state, node, MessageCode.CheckerMixedRequiresNarrowing);
            return true;
        }

        /// <summary>
        /// Reports TYHP4197 when <paramref name="type"/> is the checker recovery
        /// <c>Unresolved</c> marker and <paramref name="node"/> (the receiver) does not already
        /// carry an error. Returns <see langword="true"/> when the use site is handled — either
        /// reported or suppressed — so callers skip member/index resolution. Assignability of
        /// Unresolved is unchanged; unnarrowed <c>mixed</c> stays on TYHP4160.
        /// </summary>
        public static bool ReportUnresolvedReceiver(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast node,
            ICheckedType type)
        {
            if (state.IsExistenceProbeContext || !TypeComparer.IsUnresolvedType(type))
            {
                return false;
            }

            if (SubtreeHasError(diagnostics, state, node))
            {
                return true;
            }

            ReportError(diagnostics, state, node, MessageCode.CheckerUnresolvedReceiver);
            return true;
        }

        /// <summary>
        /// True when an error-severity diagnostic already sits on <paramref name="node"/> or
        /// inside its source span (cascade suppression for Unresolved member access).
        /// </summary>
        internal static bool SubtreeHasError(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast node)
        {
            if (!diagnostics.HasErrors)
            {
                return false;
            }

            var fileName = ResolveDiagnosticFileName(state, node);
            foreach (var error in diagnostics.Errors)
            {
                if (!string.Equals(error.FileName, fileName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (NodeContainsPosition(node, error.Line, error.Column))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NodeContainsPosition(IBase2Ast node, int line, int column)
        {
            if (node.Line == line && node.Column == column)
            {
                return true;
            }

            DiagnosticExtensions.GetOptionalEnd(node, out var endLine, out var endColumn);
            if (endLine is int el && endColumn is int ec
                && CompareSourcePos(line, column, node.Line, node.Column) >= 0
                && CompareSourcePos(line, column, el, ec) < 0)
            {
                return true;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null && NodeContainsPosition(child, line, column))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareSourcePos(int line1, int column1, int line2, int column2)
        {
            var cmp = line1.CompareTo(line2);
            return cmp != 0 ? cmp : column1.CompareTo(column2);
        }

        private static string NormalizeTypeName(string name) =>
            name.TrimStart('\\');

        public static bool ImplementsInterface(
            ICheckedType type,
            string interfaceName,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (type is not SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol objectDecl })
            {
                return false;
            }

            return TypeComparer.IsSubtypeOf(
                type,
                ResolveNamedType(interfaceName, symbolTree, globalScope),
                symbolTree,
                globalScope);
        }

        /// <summary>
        /// Types PHP will stringify: scalars, and objects/interfaces that implement
        /// <c>\Stringable</c> or declare instance <c>__toString</c> (engine auto-implement).
        /// Unions are stringable when every member is. Nullable wrappers are not unwrapped —
        /// <c>?T</c> is not stringable even when <c>T</c> is.
        /// </summary>
        public static bool IsStringableType(
            ICheckedType type,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (type is UnionCheckedType union)
            {
                return union.Members.Count > 0
                    && union.Members.All(member => IsStringableType(member, symbolTree, globalScope));
            }

            if (IsScalarType(type) || IsBuiltInName(type, "string"))
            {
                return true;
            }

            var stringable = ResolveNamedType("Stringable", symbolTree, globalScope);
            if (stringable is UnresolvedCheckedType)
            {
                return false;
            }

            return TypeComparer.IsSubtypeOf(type, stringable, symbolTree, globalScope);
        }

        /// <summary>
        /// Interpolation holes stringify like <c>echo</c> / concat. Literal chunks are skipped.
        /// </summary>
        public static void CheckEncapsListStringable(
            PhpEncapsListAst encaps,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            foreach (var part in encaps.GetAllNotNull())
            {
                if (part is PhpEncapsStringAst || part is not IExpression hole)
                {
                    continue;
                }

                var holeType = context.ResolveExpressionType(hole, state);
                if (!IsStringableType(holeType, context.SymbolTree, context.GlobalScope))
                {
                    ReportError(
                        diagnostics, state, hole, MessageCode.CheckerConcatNonStringable, holeType.DisplayName);
                }
            }
        }

        public static bool IsThrowableType(
            ICheckedType type,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (type is UnionCheckedType union)
            {
                return union.Members.All(m => IsThrowableType(m, symbolTree, globalScope));
            }

            var throwable = ResolveNamedType("Throwable", symbolTree, globalScope);
            if (throwable is UnresolvedCheckedType)
            {
                return TryGetObjectDeclaration(type) is not null
                    && !IsBuiltInName(type, "int")
                    && !IsBuiltInName(type, "string")
                    && !IsBuiltInName(type, "bool")
                    && !IsBuiltInName(type, "float")
                    && !IsBuiltInName(type, "array");
            }

            return TypeComparer.IsSubtypeOf(type, throwable, symbolTree, globalScope);
        }

        public static bool IsScalarOrStructOrEnum(ICheckedType type) =>
            IsBuiltInName(type, "int")
            || IsBuiltInName(type, "float")
            || IsBuiltInName(type, "string")
            || IsBuiltInName(type, "bool")
            || IsBuiltInName(type, "array")
            || type is StructCheckedType
            || (TryGetObjectDeclaration(type) is { ObjectKind: PhpTypeDeclType.Enum });

        public static ObjectDeclarationSymbol? TryGetObjectDeclaration(ICheckedType type) =>
            type switch
            {
                SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj } => obj,
                GenericCheckedType { BaseType: SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj } } => obj,
                StaticCheckedType staticType => TryGetObjectDeclaration(staticType.DeclaringType),
                _ => null,
            };

        /// <summary>
        /// Scope used for file-local <c>use extension</c> hide / alias / insteadof at a call site.
        /// </summary>
        public static IBaseScope GetCallSiteScope(CheckerState state, GlobalScope globalScope)
        {
            if (state.NameResolutionScope is { } overrideScope)
            {
                return overrideScope;
            }

            var lexical = state.EnclosingCallable?.ContainingScope
                ?? state.EnclosingFunction?.ContainingScope
                ?? state.EnclosingObject?.ContainingScope;
            if (lexical is not null)
            {
                return lexical;
            }

            var resolver = new NameResolver(globalScope, new DiagnosticBag());
            return resolver.FindCallSiteScope(
                node: null, lexicalScope: null, state.CurrentNamespaceName);
        }

        /// <summary>
        /// Peel nullability, <c>static</c>, generic instantiation, and literal wrappers so member
        /// lookup sees the underlying receiver symbol.
        /// </summary>
        public static ICheckedType UnwrapMemberAccessReceiver(ICheckedType type)
        {
            while (true)
            {
                switch (type)
                {
                    case NullableCheckedType nullable:
                        type = nullable.InnerType;
                        break;
                    case StaticCheckedType staticType:
                        type = staticType.DeclaringType;
                        break;
                    case GenericCheckedType generic:
                        type = generic.BaseType;
                        break;
                    case LiteralCheckedType literal:
                        type = literal.UnderlyingType;
                        break;
                    default:
                        return type;
                }
            }
        }

        /// <summary>
        /// Receivers that cannot grow real PHP instance methods: scalar builtins plus
        /// <c>\Closure</c>. An unresolved member on these is always a compile-time mistake
        /// (hidden or never-declared extension), unlike an ordinary class (<c>__call</c>,
        /// undeclared trait requirements, uncompiled dependencies).
        /// </summary>
        public static bool IsScalarPseudoObjectReceiver(ICheckedType type)
        {
            type = UnwrapMemberAccessReceiver(type);
            if (TypeComparer.TryGetBuiltInName(type, out var name))
            {
                return name is "string" or "int" or "float" or "bool" or "array";
            }

            if (TryGetObjectDeclaration(type) is { } obj)
            {
                var fq = (obj.FullyQualifiedName ?? obj.Name).TrimStart('\\');
                return string.Equals(fq, "Closure", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// Receivers whose missing instance members are always a compile-time mistake:
        /// scalar pseudo-objects plus structs (array-backed; no <c>__call</c> / dynamic
        /// properties). Ordinary classes stay gradual.
        /// </summary>
        public static bool IsClosedMemberReceiver(ICheckedType type)
        {
            if (IsScalarPseudoObjectReceiver(type))
            {
                return true;
            }

            type = UnwrapMemberAccessReceiver(type);
            if (type is StructCheckedType || TypeComparer.IsBuiltInName(type, "struct"))
            {
                return true;
            }

            return TryGetObjectDeclaration(type) is { IsStruct: true };
        }

        /// <summary>
        /// <c>self</c> / <c>parent</c> / <c>static</c> as a <c>::</c> receiver — PHP instance
        /// forwarding with <c>$this</c> bound, not a named-class static call.
        /// </summary>
        public static bool IsRelativeClassKeyword(IExpression? expression)
        {
            var text = GetExpressionText(expression);
            return !string.IsNullOrEmpty(text)
                && (string.Equals(text, "self", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "parent", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, "static", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Own members first (inheritance / traits), then in-scope extension methods using the
        /// call-site file so local <c>use extension</c> hide / alias apply on builtin receivers.
        /// <paramref name="allowInstanceForwarding"/> lets <c>self::</c>/<c>parent::</c>/
        /// <c>static::</c> resolve a non-static own method (PHP forwarding). Named-class
        /// <c>Foo::instanceMethod()</c> stays static-only. Extension lookup is never used for
        /// <c>::</c> calls.
        /// </summary>
        public static bool TryResolveInstanceOrExtensionMethod(
            ICheckedType ownerType,
            string methodName,
            bool staticOnly,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out ObjectMethodSymbol? method,
            bool allowInstanceForwarding = false)
        {
            method = null;
            var unwrapped = UnwrapMemberAccessReceiver(ownerType);
            var objectDecl = TryGetObjectDeclaration(unwrapped);
            if (objectDecl is not null
                && symbolTree.ResolveMember(methodName, objectDecl, new DiagnosticBag())
                    is ObjectMethodSymbol own
                && (!staticOnly || own.IsStatic || allowInstanceForwarding))
            {
                method = own;
                return true;
            }

            // Scalar / class extension methods are instance-call syntax (`$s->length()`), not
            // `string::length()`. Static lookup stays on declared static members only, including
            // relative-keyword forwarding (own instance members are handled above).
            if (staticOnly)
            {
                return false;
            }

            // A union block target matches a receiver only when every type that receiver
            // might be is a member of the union. The binder stores the union's first
            // member, so the plain symbol lookup below would treat `string|int` as
            // `string` and would also accept `?string` after nullability is peeled.
            //
            // `ExtensionMethodIndex` (and so `HasUnionTargetMethod`) is keyed by the
            // *declared* method name — a call site that only knows a
            // `use extension { Ext::real as alias; }` rename would never match here, so
            // a non-first-member receiver calling through the alias would silently fail
            // to resolve. Resolve the alias back to its declared name first.
            var callSite = GetCallSiteScope(state, globalScope);
            var declaredMethodName = ResolveDeclaredExtensionMethodName(
                methodName, objectDecl, callSite, globalScope);
            if (ExtensionBlockTargetChecks.HasUnionTargetMethod(symbolTree, declaredMethodName))
            {
                method = ResolveUnionAwareExtensionMethod(
                    ownerType, unwrapped, objectDecl, methodName, declaredMethodName, state, symbolTree, globalScope);
                return method is not null;
            }

            IBaseSymbol? onType = CanonicalExtensionReceiverSymbol(unwrapped, objectDecl, globalScope);
            if (onType is null)
            {
                return false;
            }

            var resolver = new NameResolver(symbolTree, new DiagnosticBag());
            if (resolver.ResolveExtensionMethod(
                    methodName, onType, GetCallSiteScope(state, globalScope))
                is ObjectMethodSymbol extension)
            {
                method = extension;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Extension lookup keys off the receiver symbol. Literal types, operator results, and
        /// <see cref="CheckedTypes"/> singletons use a distinct <see cref="BuiltInTypeSymbol"/>
        /// instance from the global <c>int</c>/<c>string</c>/… builtin, so map those to the
        /// scope-registered symbol before <see cref="NameResolver.ResolveExtensionMethod"/>.
        /// </summary>
        private static IBaseSymbol? CanonicalExtensionReceiverSymbol(
            ICheckedType unwrapped,
            ObjectDeclarationSymbol? objectDecl,
            GlobalScope globalScope)
        {
            if (TypeComparer.TryGetBuiltInName(unwrapped, out var builtinName)
                && TypeComparer.ResolveBuiltIn(builtinName, globalScope) is { } canonical)
            {
                return canonical;
            }

            if (unwrapped is TemplateStringCheckedType
                && TypeComparer.ResolveBuiltIn("string", globalScope) is { } stringBuiltin)
            {
                return stringBuiltin;
            }

            return objectDecl ?? (unwrapped as SimpleCheckedType)?.ResolvedSymbol;
        }

        /// <summary>
        /// A method name with a union-target candidate anywhere in the program still has
        /// most call sites resolved correctly by the ordinary
        /// <see cref="NameResolver.ResolveExtensionMethod"/> — it already implements
        /// aliasing (<c>as</c>), <c>hide</c> / <c>insteadof</c>, tyhpdef auto-activation,
        /// and precedence ranking, and its receiver-symbol fallback happens to agree with
        /// the union's first member whenever the receiver actually *is* that first member
        /// with no other nullability. This only falls back to the union-aware full scan
        /// (<see cref="SelectUnionAwareExtensionMethod"/>, which re-checks every candidate
        /// but does not implement that richer precedence/aliasing behavior) when the
        /// ordinary result is missing or its target does not really cover
        /// <paramref name="receiverType"/> once nullability and the other union members
        /// are considered — e.g. a receiver that is the union's second member, or that is
        /// nullable when the union has no <c>null</c> member.
        /// </summary>
        /// <param name="methodName">
        /// The name written at the call site — passed to <see cref="NameResolver"/> as-is
        /// so its own <c>as</c> / <c>hide</c> handling applies.
        /// </param>
        /// <param name="declaredMethodName">
        /// <paramref name="methodName"/> resolved past any call-site rename. The union
        /// scan falls back on this — not <paramref name="methodName"/> — because
        /// <see cref="SymbolTree.ExtensionMethodIndex"/> is keyed by the declared name, so
        /// an aliased call would otherwise never find the union's own candidates.
        /// </param>
        private static ObjectMethodSymbol? ResolveUnionAwareExtensionMethod(
            ICheckedType receiverType,
            ICheckedType unwrapped,
            ObjectDeclarationSymbol? objectDecl,
            string methodName,
            string declaredMethodName,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var onType = CanonicalExtensionReceiverSymbol(unwrapped, objectDecl, globalScope);
            if (onType is not null)
            {
                var ordinaryResolver = new NameResolver(symbolTree, new DiagnosticBag());
                var callSite = GetCallSiteScope(state, globalScope);
                if (ordinaryResolver.ResolveExtensionMethod(methodName, onType, callSite)
                        is ObjectMethodSymbol ordinary
                    && (!ExtensionBlockTargetChecks.TryGetUnionTargetMembers(
                            ordinary, symbolTree, out var ordinaryMembers)
                        || ExtensionBlockTargetChecks.UnionTargetCoversReceiver(
                            ordinaryMembers, receiverType)))
                {
                    return ordinary;
                }
            }

            return SelectUnionAwareExtensionMethod(
                receiverType, declaredMethodName, state, symbolTree, globalScope);
        }

        /// <summary>
        /// Resolves a call-site extension method name past a
        /// <c>use extension { Ext::real as alias; }</c> rename, to the declared name that
        /// <see cref="SymbolTree.ExtensionMethodIndex"/> is keyed by. Mirrors
        /// <see cref="NameResolver"/>'s alias precedence (receiver, then file, then
        /// global) over the same public dictionaries it populates, so
        /// <see cref="ExtensionBlockTargetChecks.HasUnionTargetMethod"/> can be checked
        /// before deciding whether a call needs the union-aware path at all.
        /// </summary>
        private static string ResolveDeclaredExtensionMethodName(
            string methodName,
            ObjectDeclarationSymbol? objectDecl,
            IBaseScope callSite,
            GlobalScope globalScope)
        {
            if (objectDecl?.ExtensionUseMethodAliases?.TryGetValue(methodName, out var fromReceiver) == true)
            {
                return fromReceiver.Item2;
            }

            if (GetOwningFileScope(callSite)?.ExtensionUseMethodAliases?.TryGetValue(methodName, out var fromFile)
                == true)
            {
                return fromFile.Item2;
            }

            if (globalScope.ExtensionUseMethodAliases?.TryGetValue(methodName, out var fromGlobal) == true)
            {
                return fromGlobal.Item2;
            }

            return methodName;
        }

        /// <summary>
        /// Nearest enclosing <see cref="FileScope"/>, following a namespace block back to
        /// the file that declared it (mirrors <c>NameResolver.GetOwningFileScope</c>).
        /// </summary>
        private static FileScope? GetOwningFileScope(IBaseScope? scope)
        {
            for (var current = scope; current is not null; current = current.ParentScope)
            {
                if (current is FileScope fileScope)
                {
                    return fileScope;
                }

                if (current.DeclarationSymbol is NamespaceBlockSymbol { OwningFileScope: { } owning })
                {
                    return owning;
                }
            }

            return null;
        }

        private static ObjectMethodSymbol? SelectUnionAwareExtensionMethod(
            ICheckedType receiverType,
            string methodName,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (!symbolTree.ExtensionMethodIndex.TryGetValue(methodName, out var candidates))
            {
                return null;
            }

            var callSite = GetCallSiteScope(state, globalScope);
            var unwrapped = UnwrapMemberAccessReceiver(receiverType);
            var onType = CanonicalExtensionReceiverSymbol(
                unwrapped, TryGetObjectDeclaration(unwrapped), globalScope);
            if (IsExtensionMethodNameHidden(methodName, onType, callSite, globalScope))
            {
                return null;
            }

            ObjectMethodSymbol? best = null;
            var bestRank = int.MinValue;
            foreach (var candidate in candidates)
            {
                var owner = FindDeclaringExtension(candidate);
                if (!IsExtensionActive(owner, callSite, globalScope))
                {
                    continue;
                }

                var applies = ExtensionBlockTargetChecks.TryGetUnionTargetMembers(
                        candidate, symbolTree, out var members)
                    ? ExtensionBlockTargetChecks.UnionTargetCoversReceiver(members, receiverType)
                    : NonUnionExtensionApplies(candidate, onType);
                if (!applies)
                {
                    continue;
                }

                var rank = RankExtension(owner, callSite, globalScope);
                if (rank > bestRank)
                {
                    bestRank = rank;
                    best = candidate;
                }
            }

            return best;
        }

        private static bool NonUnionExtensionApplies(ObjectMethodSymbol method, IBaseSymbol? onType)
        {
            if (onType is null)
            {
                return false;
            }

            for (var scope = method.ContainingScope; scope is not null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is not ObjectDeclarationSymbol block
                    || block.ExtensionBlockTargetSymbol is not { } target)
                {
                    continue;
                }

                return TypeComparer.SymbolsMatch(target, onType);
            }

            return method.Parameters.FirstOrDefault()?.DeclaredType is IBase2Ast declared
                && declared.BoundSymbol is { } bound
                && TypeComparer.SymbolsMatch(bound, onType);
        }

        private static ObjectDeclarationSymbol? FindDeclaringExtension(ObjectMethodSymbol method)
        {
            for (var scope = method.ContainingScope; scope is not null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol extension
                    && extension.IsExtension
                    && !extension.IsExtensionTargetGroup)
                {
                    return extension;
                }
            }

            return null;
        }

        private static bool IsExtensionActive(
            ObjectDeclarationSymbol? owner,
            IBaseScope callSite,
            GlobalScope globalScope)
        {
            if (owner is null || !owner.IsExtension || owner.IsCompilerGenerated)
            {
                return true;
            }

            var source = owner.SourceFile ?? "";
            if (source.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase)
                && !source.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            for (var scope = callSite; scope is not null; scope = scope.ParentScope)
            {
                if (scope is FileScope file
                    && (file.DeclaredExtensions.Contains(owner) || file.ImportedExtensions.Contains(owner)))
                {
                    return true;
                }
            }

            return globalScope.GloballyActivatedExtensions.Contains(owner);
        }

        private static int RankExtension(
            ObjectDeclarationSymbol? owner,
            IBaseScope callSite,
            GlobalScope globalScope)
        {
            if (owner is null)
            {
                return 0;
            }

            for (var scope = callSite; scope is not null; scope = scope.ParentScope)
            {
                if (scope is not FileScope file)
                {
                    continue;
                }

                if (file.DeclaredExtensions.Contains(owner))
                {
                    return 300;
                }

                if (file.ImportedExtensions.Contains(owner))
                {
                    return 200;
                }
            }

            if (globalScope.GloballyActivatedExtensions.Contains(owner) || owner.IsCompilerGenerated)
            {
                return 100;
            }

            return 50;
        }

        private static bool IsExtensionMethodNameHidden(
            string methodName,
            IBaseSymbol? onType,
            IBaseScope callSite,
            GlobalScope globalScope)
        {
            if (onType is ObjectDeclarationSymbol receiver
                && receiver.ExtensionUseHiddenMembers is { } onReceiver
                && onReceiver.Contains(methodName))
            {
                return true;
            }

            for (var scope = callSite; scope is not null; scope = scope.ParentScope)
            {
                if (scope is FileScope { ExtensionUseHiddenMembers: { } hidden }
                    && hidden.Contains(methodName))
                {
                    return true;
                }
            }

            return globalScope.ExtensionUseHiddenMembers is { } globalHidden
                && globalHidden.Contains(methodName);
        }

        // The right-hand side of `instanceof` is a class/type reference, not a value expression, so
        // inferring it via expression inference yields `unknown` (a bare class name is not a value).
        // Resolve it as a type instead so narrowing produces the real class type. Handles the relative
        // keywords (`self`/`static`/`parent`) and named classes, falling back to expression inference
        // for dynamic forms such as `$x instanceof $classNameVar`.
        //
        // A type-position name that resolves to nothing is not a value expression: pass
        // <paramref name="diagnostics"/> from the instanceof check site so TYHP3003 (or
        // relative-type codes) fire once. Other callers (narrowing, `Foo::bar` receivers)
        // omit it to avoid duplicate reports.
        public static ICheckedType ResolveInstanceofTargetType(
            IBase2Ast right,
            CheckerState state,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag? diagnostics = null)
        {
            // `$x is ?T` (TyhpParser.g4 phpExprBinaryOpGrammarAddon002Handler): the RHS is a
            // synthetic prefix `?` unary wrapping the real target so the nullable marker survives
            // parsing (FOUND_BUGS #21). Resolve the wrapped target and make the result nullable
            // instead of falling through to expression inference, which does not know a bare `?`
            // prefix and would type the whole RHS as unresolved.
            if (IsNullableInstanceofMarker(right, out var nullableOperand))
            {
                var innerType = ResolveInstanceofTargetType(
                    nullableOperand, state, context, symbolTree, globalScope, diagnostics);
                return innerType.IsNullable ? innerType : new NullableCheckedType(innerType);
            }

            if (right is PhpBuiltinTypeAst builtin)
            {
                var ident = builtin.Identifier ?? string.Empty;
                if (TryResolveRelativeInstanceofTarget(
                        ident, builtin, state, context, diagnostics, out var relativeBuiltin))
                {
                    return relativeBuiltin;
                }

                if (TryResolveBuiltinInstanceofTarget(ident, globalScope) is { } builtinType)
                {
                    return builtinType;
                }

                ReportUnresolvedInstanceofTarget(ident, builtin, state, diagnostics, globalScope);
                return CheckedTypes.Unresolved;
            }

            if (right is PhpNameAst nameAst)
            {
                var written = nameAst.ValueString ?? nameAst.Identifier ?? string.Empty;
                var raw = written.TrimStart('\\');
                if (!string.IsNullOrEmpty(raw))
                {
                    if (TryResolveRelativeInstanceofTarget(
                            raw, nameAst, state, context, diagnostics, out var relative))
                    {
                        return FinishInstanceofTargetType(
                            relative, nameAst, state, context, symbolTree, globalScope);
                    }

                    ICheckedType? baseType = null;
                    if (nameAst.BoundSymbol is ObjectDeclarationSymbol bound)
                    {
                        baseType = CheckedTypes.FromSymbol(bound);
                    }
                    else if (nameAst.BoundSymbol is TypeAliasSymbol or ObjectTypeAliasSymbol)
                    {
                        baseType = ResolveAliasInstanceofTarget(nameAst.BoundSymbol, state, context);
                    }
                    else if (nameAst.BoundSymbol is GenericTypeParameterSymbol boundGeneric)
                    {
                        baseType = CheckedTypes.FromSymbol(boundGeneric);
                    }
                    else if (TryResolveInScopeGenericParameter(raw, state, out var genericType))
                    {
                        baseType = genericType;
                    }
                    else
                    {
                        var fromScope = GetInstanceofResolutionScope(state, globalScope);
                        var resolver = new NameResolver(symbolTree, new DiagnosticBag());
                        var symbol = ResolveInstanceofTypeSymbol(resolver, written, raw, fromScope);
                        if (symbol is not null)
                        {
                            resolver.RecordResolution(nameAst, symbol);
                        }

                        if (symbol is TypeAliasSymbol or ObjectTypeAliasSymbol)
                        {
                            baseType = ResolveAliasInstanceofTarget(symbol, state, context);
                        }
                        else if (NameResolver.IsTypePositionSymbol(symbol))
                        {
                            baseType = CheckedTypes.FromSymbol(symbol);
                        }
                        else if (TryResolveBuiltinInstanceofTarget(raw, globalScope) is { } builtinName)
                        {
                            baseType = builtinName;
                        }
                    }

                    if (baseType is not null)
                    {
                        return FinishInstanceofTargetType(
                            baseType, nameAst, state, context, symbolTree, globalScope);
                    }

                    ReportUnresolvedInstanceofTarget(raw, nameAst, state, diagnostics, fromScope: GetInstanceofResolutionScope(state, globalScope));
                    return CheckedTypes.Unresolved;
                }
            }

            return context.ResolveExpressionType(right, state);
        }

        private static bool TryResolveRelativeInstanceofTarget(
            string raw,
            IBase2Ast reportNode,
            CheckerState state,
            INarrowingResolution context,
            DiagnosticBag? diagnostics,
            out ICheckedType type)
        {
            type = CheckedTypes.Unresolved;
            if (string.Equals(raw, "self", StringComparison.OrdinalIgnoreCase))
            {
                if (state.EnclosingObjectType is not null)
                {
                    type = state.EnclosingObjectType;
                    return true;
                }

                if (state.EnclosingObject is not null)
                {
                    type = CheckedTypes.FromSymbol(state.EnclosingObject);
                    return true;
                }

                ReportRelativeInstanceofOutsideClass(reportNode, state, diagnostics);
                return true;
            }

            if (string.Equals(raw, "static", StringComparison.OrdinalIgnoreCase))
            {
                ICheckedType? declaring = state.EnclosingObjectType
                    ?? (state.EnclosingObject is not null
                        ? CheckedTypes.FromSymbol(state.EnclosingObject)
                        : null);
                if (declaring is not null)
                {
                    type = new StaticCheckedType(declaring);
                    return true;
                }

                ReportRelativeInstanceofOutsideClass(reportNode, state, diagnostics);
                return true;
            }

            if (string.Equals(raw, "parent", StringComparison.OrdinalIgnoreCase))
            {
                if (state.EnclosingObject is null)
                {
                    ReportRelativeInstanceofOutsideClass(reportNode, state, diagnostics);
                    return true;
                }

                if (state.EnclosingObject.ExtendsType is { } extendsType)
                {
                    type = context.ResolveTypeAnnotation(extendsType, state);
                    return true;
                }

                if (diagnostics is not null)
                {
                    ReportError(
                        diagnostics,
                        state,
                        reportNode,
                        MessageCode.CheckerParentWithoutParent,
                        state.EnclosingObject.Name);
                }

                return true;
            }

            return false;
        }

        private static void ReportRelativeInstanceofOutsideClass(
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag? diagnostics)
        {
            if (diagnostics is null)
            {
                return;
            }

            ReportError(diagnostics, state, reportNode, MessageCode.CheckerRelativeTypeOutsideClass);
        }

        private static void ReportUnresolvedInstanceofTarget(
            string display,
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag? diagnostics,
            IBaseScope fromScope)
        {
            if (diagnostics is null || string.IsNullOrEmpty(display))
            {
                return;
            }

            ReportErrorWithDidYouMean(
                diagnostics,
                state,
                reportNode,
                MessageCode.BinderSymbolNotFound,
                display,
                InScopeNameCandidates.CollectTypeNames(fromScope),
                display);
        }

        private static ICheckedType? TryResolveBuiltinInstanceofTarget(string raw, GlobalScope globalScope)
        {
            if (string.IsNullOrEmpty(raw) || raw.Contains('\\'))
            {
                return null;
            }

            if (TypeComparer.ResolveBuiltIn(raw, globalScope) is { } builtin)
            {
                return CheckedTypes.FromSymbol(builtin);
            }

            return raw.ToLowerInvariant() switch
            {
                "void" => CheckedTypes.Void,
                "never" => CheckedTypes.Never,
                "mixed" => CheckedTypes.Mixed,
                "null" => CheckedTypes.Null,
                "true" => new LiteralCheckedType(true, new SimpleCheckedType(new BuiltInTypeSymbol("true"))),
                "false" => new LiteralCheckedType(false, new SimpleCheckedType(new BuiltInTypeSymbol("false"))),
                "bool" => CheckedTypes.Bool,
                "int" => CheckedTypes.Int,
                "float" => CheckedTypes.Float,
                "string" => CheckedTypes.String,
                _ => null,
            };
        }

        private static bool TryResolveInScopeGenericParameter(
            string raw,
            CheckerState state,
            out ICheckedType type)
        {
            type = CheckedTypes.Unresolved;
            GenericTypeParameterSymbol? param =
                state.FunctionGenerics.FirstOrDefault(gp =>
                    string.Equals(gp.Name, raw, StringComparison.Ordinal))
                ?? state.ObjectGenerics.FirstOrDefault(gp =>
                    string.Equals(gp.Name, raw, StringComparison.Ordinal))
                ?? state.EnclosingObject?.GenericParameters.FirstOrDefault(gp =>
                    string.Equals(gp.Name, raw, StringComparison.Ordinal));
            if (param is null)
            {
                return false;
            }

            type = CheckedTypes.FromSymbol(param);
            return true;
        }

        private static IBaseSymbol? ResolveInstanceofTypeSymbol(
            NameResolver resolver,
            string written,
            string raw,
            IBaseScope fromScope)
        {
            var segments = raw.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            var isFullyQualified = written.StartsWith('\\') || raw.StartsWith('\\');
            if (segments.Length > 1)
            {
                var objectAlias = resolver.TryResolveObjectTypeAlias(
                    segments, fromScope, isFullyQualified);
                if (objectAlias is not null)
                {
                    return objectAlias;
                }
            }

            IBaseSymbol? symbol;
            if (isFullyQualified)
            {
                symbol = resolver.ResolveQualifiedName(segments);
            }
            else if (segments.Length > 1)
            {
                symbol = resolver.ResolveRelativeName(segments, fromScope)
                    ?? resolver.ResolveQualifiedName(segments);
            }
            else
            {
                symbol = resolver.ResolveSymbol(raw, fromScope, NameResolver.IsTypePositionSymbol)
                    ?? resolver.ResolveRelativeName(segments, fromScope);
            }

            return NameResolver.IsTypePositionSymbol(symbol) ? symbol : null;
        }

        private static IBaseScope GetInstanceofResolutionScope(CheckerState state, GlobalScope globalScope)
        {
            if (state.NameResolutionScope is { } overrideScope)
            {
                return overrideScope;
            }

            if (state.EnclosingCallable?.ContainingScope is IBaseScope callableScope)
            {
                return callableScope;
            }

            if (state.EnclosingObject?.ContainingScope is IBaseScope objectScope)
            {
                return objectScope;
            }

            return globalScope;
        }

        /// <summary>
        /// Narrow <c>$x is Alias</c> to the alias body (non-generic) so a transparent alias of
        /// <c>int</c> or a class behaves like the underlying type after the guard. Generic alias
        /// uses stay the alias symbol so <see cref="ApplyInstanceofTypeArguments"/> can attach args;
        /// <see cref="FinishInstanceofTargetType"/> then expands
        /// <c>Predicate&lt;int&gt;</c> to the callable shape (and other generic aliases to their bodies).
        /// </summary>
        private static ICheckedType ResolveAliasInstanceofTarget(
            IBaseSymbol alias,
            CheckerState state,
            INarrowingResolution context)
        {
            var body = alias switch
            {
                TypeAliasSymbol fileAlias => fileAlias.AliasedType,
                ObjectTypeAliasSymbol objectAlias => objectAlias.AliasedType,
                _ => null,
            };

            var generics = alias switch
            {
                TypeAliasSymbol fileAlias => fileAlias.GenericParameters,
                ObjectTypeAliasSymbol objectAlias => objectAlias.GenericParameters,
                _ => (IReadOnlyList<GenericTypeParameterSymbol>)[],
            };

            if (body is not null && generics.Count == 0)
            {
                return context.ResolveTypeAnnotation(body, WithAliasBodyContext(state, alias));
            }

            return CheckedTypes.FromSymbol(alias);
        }

        /// <summary>
        /// Applies grammar type arguments on an <c>is</c> / <c>instanceof</c> RHS, then expands
        /// type aliases so <c>$fn is Predicate&lt;int&gt;</c> narrows to the callable shape
        /// (and <c>$x is Optional&lt;int&gt;</c> to the alias body). Nominal classes keep their
        /// parameterized identity.
        /// </summary>
        private static ICheckedType FinishInstanceofTargetType(
            ICheckedType baseType,
            PhpNameAst nameAst,
            CheckerState state,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var withArgs = ApplyInstanceofTypeArguments(baseType, nameAst, state, context);
            return TypeComparer.ExpandTypeAliases(
                withArgs,
                symbolTree,
                globalScope,
                (ast, alias) => context.ResolveTypeAnnotation(
                    ast,
                    WithAliasBodyContext(state, alias)));
        }

        /// <summary>
        /// Applies grammar type arguments on an <c>is</c> / <c>instanceof</c> RHS name
        /// (<c>self&lt;T&gt;</c>, <c>Box&lt;int&gt;</c>) so narrowing matches the parameterized type
        /// the emitter reifies via <c>\Tyhp\Type::is</c>. Parameterized <c>static&lt;…&gt;</c> is
        /// rejected elsewhere during type resolution.
        /// </summary>
        private static ICheckedType ApplyInstanceofTypeArguments(
            ICheckedType baseType,
            PhpNameAst nameAst,
            CheckerState state,
            INarrowingResolution context)
        {
            PhpTypeExpressionListAst? list = null;
            foreach (var key in (string[])["typeName", "identifier"])
            {
                if (nameAst.AstGrammarAddons.TryGetValue(key, out var addon)
                    && addon is PhpTypeExpressionListAst candidate)
                {
                    list = candidate;
                    break;
                }
            }

            if (list is null)
            {
                return baseType;
            }

            // Parameterized `static<…>` is forbidden even on instanceof RHS.
            if (baseType is StaticCheckedType
                || (nameAst.ValueString is { } staticName
                    && string.Equals(staticName.TrimStart('\\'), "static", StringComparison.OrdinalIgnoreCase)))
            {
                if (context is CheckerRuleContext ruleContext)
                {
                    ReportError(
                        ruleContext.Diagnostics,
                        state,
                        nameAst,
                        MessageCode.CheckerParameterizedStaticForbidden);
                }

                return baseType is StaticCheckedType staticBase
                    ? staticBase
                    : CheckedTypes.Unresolved;
            }

            var raw = list.GetAllNotNull().ToList();
            // instanceof / classNameIdentifier addon often wraps args in one PhpTypeExpressionAst.
            if (raw.Count == 1
                && raw[0] is PhpTypeExpressionAst { Types: PhpTypeExpressionListAst inner })
            {
                var innerArgs = inner.GetAllNotNull().ToList();
                if (innerArgs.Count > 0)
                {
                    raw = innerArgs;
                }
            }

            var args = raw
                .Select(arg => context.ResolveTypeAnnotation(arg, state))
                .ToList();
            if (args.Count == 0)
            {
                return baseType;
            }

            var bareBase = baseType is GenericCheckedType generic
                ? generic.BaseType
                : baseType is StaticCheckedType staticDecl
                    ? staticDecl.DeclaringType
                    : baseType;
            return new GenericCheckedType(bareBase, args);
        }

        public static ICheckedType ResolveNamedType(
            string name,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            // Built-in global types (e.g. \Throwable) live under the global namespace and are not
            // found by a bare lexical lookup; resolve them as a qualified name from global scope.
            var symbol = symbolTree.ResolveSymbol(name, globalScope, new DiagnosticBag())
                ?? symbolTree.ResolveQualifiedName(name.TrimStart('\\').Split('\\'), globalScope, new DiagnosticBag());
            return symbol is null ? CheckedTypes.Unresolved : CheckedTypes.FromSymbol(symbol);
        }

        /// <summary>
        /// True when <paramref name="expression"/> is a compile-time constant and therefore legal in
        /// a constant-required context (property / parameter default, class constant, enum case
        /// value, attribute argument).
        /// </summary>
        /// <param name="state">
        /// Enclosing checker state, used only to tell <c>default(&lt;concrete type&gt;)</c> — which
        /// folds to a literal — apart from <c>default(&lt;generic parameter&gt;)</c>, whose value is
        /// not known until runtime. Omitting it treats every <c>default()</c> as non-constant.
        /// </param>
        public static bool IsConstantExpression(IExpression? expression, CheckerState? state = null) =>
            expression switch
            {
                null => false,
                PhpMagicConstantAst magic => IsConstantMagic(magic),
                PhpScalarAst => true,
                TokenValueAst => true,
                PhpArrayAst array => array.ArrayPairs is null
                    || array.ArrayPairs.GetAllExcludingSkippedSlots().All(pair => IsConstantArrayPair(pair, state)),
                PhpArrayPairListAst arrayPairs =>
                    arrayPairs.GetAllExcludingSkippedSlots().All(pair => IsConstantArrayPair(pair, state)),
                PhpEncapsListAst encaps =>
                    encaps.GetAllNotNull().All(item => item is PhpEncapsStringAst),
                PhpBinaryOpAst binary => IsConstantBinary(binary, state),
                PhpUnaryOpAst unary => IsConstantExpression(unary.Operand as IExpression, state),
                PhpTernaryOpAst ternary =>
                    IsConstantExpression(ternary.Condition as IExpression, state)
                    && IsConstantExpression(ternary.TrueExpr as IExpression, state)
                    && IsConstantExpression(ternary.FalseExpr as IExpression, state),
                // `default(int)` folds to `0`, `default(Foo)` to `null` — both valid PHP constant
                // initializers. `default(T)` cannot fold, because the value depends on the type
                // argument bound at construction time.
                TyhpDefaultAst defaultExpr => IsConstantDefault(defaultExpr, state),
                // Class-constant and enum-case access (`Foo::BAR`, `Color::Red`) are constant
                // expressions and valid in property/parameter defaults and other const contexts.
                PhpDereferenceableAst { Suffix: PhpClassConstantAccessAst } => true,
                PhpDereferenceableAst { Base: PhpNameAst name, Suffix: null } =>
                    name.BoundSymbol is ConstantSymbol,
                // PHP 8.1+: `new ClassName(...constant args)` is a constant expression (property /
                // parameter defaults, statics, attributes). Anonymous / dynamic class names are not.
                PhpNewAst newExpr => IsConstantNew(newExpr, state),
                PhpVariableAst => false,
                PhpInlineFunctionAst => false,
                TyhpAsyncBlockAst => false,
                _ => expression.BoundSymbol is ConstantSymbol,
            };

        public static string? GetVariableName(PhpVariableAst variable)
        {
            var raw = variable.VariableToken?.ValueString ?? variable.Identifier ?? variable.ValueString;
            if (string.IsNullOrEmpty(raw))
            {
                // `foreach` value/key variables (and `&$ref`-wrapped variables) carry the real
                // variable nested in VariableExpression rather than on the token directly, so the
                // outer node has no name of its own. Recurse into the wrapped variable to recover it.
                if (variable.VariableExpression is PhpVariableAst inner && !ReferenceEquals(inner, variable))
                {
                    return GetVariableName(inner);
                }

                if (variable.VariableExpression is TokenValueAst token
                    && !string.IsNullOrEmpty(token.ValueString))
                {
                    raw = token.ValueString;
                }
                else
                {
                    return null;
                }
            }

            return raw.StartsWith('$') ? raw[1..] : raw;
        }

        /// <summary>
        /// Name shown in TYHP4016 for a variable, parameter, or property. Parameter AST names
        /// already include a leading <c>$</c>; locals from <see cref="GetVariableName"/> and some
        /// property identifiers do not. Always a single leading <c>$</c> so the message template
        /// can wrap <c>{0}</c> in backticks without doubling the sigil.
        /// </summary>
        public static string FormatTypeRequiredName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name ?? string.Empty;
            }

            return name[0] == '$' ? name : "$" + name;
        }

        public static bool IsThisVariable(PhpVariableAst variable) =>
            string.Equals(GetVariableName(variable), "this", StringComparison.OrdinalIgnoreCase);

        public static bool IsInStaticContext(CheckerState state)
        {
            for (var scope = state; scope is not null; scope = scope.Parent)
            {
                if (scope.ScopeType == ScopeType.StaticMethodDeclaration)
                {
                    return true;
                }

                if ((scope.Modifiers & MemberModifier.Static) != 0
                    && scope.ScopeType is ScopeType.InstanceMethodDeclaration
                        or ScopeType.StaticMethodDeclaration
                        or ScopeType.AnonymousFunctionDeclaration
                        or ScopeType.CodeBlock
                        or ScopeType.Statement)
                {
                    return true;
                }

                if (scope.ScopeType is ScopeType.FunctionDeclaration
                    or ScopeType.InstanceMethodDeclaration
                    or ScopeType.StaticMethodDeclaration
                    or ScopeType.AnonymousFunctionDeclaration)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// True when <c>$this</c> is the extension-method receiver parameter
        /// (<c>extends T $this</c>). Extension methods lower to static PHP methods, so
        /// <see cref="IsInStaticContext"/> is true, but the name is an ordinary parameter —
        /// not PHP's special instance <c>$this</c>. Static closures nested inside extension
        /// methods still reject <c>$this</c> (stop at a static anonymous-function boundary).
        /// </summary>
        public static bool IsExtensionReceiverThis(CheckerState state)
        {
            for (var scope = state; scope is not null; scope = scope.Parent)
            {
                if (scope.ScopeType == ScopeType.AnonymousFunctionDeclaration
                    && (scope.Modifiers & MemberModifier.Static) != 0)
                {
                    return false;
                }

                if (scope.ScopeType == ScopeType.StaticMethodDeclaration
                    && scope.EnclosingObject?.IsExtension == true
                    && scope.Variables.TryGetValue("this", out var thisVar)
                    && thisVar.IsParameter)
                {
                    return true;
                }

                if (scope.ScopeType is ScopeType.FunctionDeclaration
                    or ScopeType.InstanceMethodDeclaration
                    or ScopeType.StaticMethodDeclaration
                    or ScopeType.AnonymousFunctionDeclaration)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Call-site argument lists never include the extension receiver
        /// (<c>extends T $this</c>, another receiver name on an <c>extension</c> member,
        /// or a class-body thin-mapping implicit <c>$this</c>).
        /// Matches splice / reference-tracking / overload arity.
        /// </summary>
        public static IReadOnlyList<ParameterInfo> ExcludeExtensionReceiver(
            IReadOnlyList<ParameterInfo> parameters,
            ObjectMethodSymbol? method)
        {
            if (parameters.Count == 0)
            {
                return parameters;
            }

            if (FindExtensionOwner(method) is not null
                || IsExtensionReceiverParameterName(parameters[0].Name))
            {
                return parameters.Skip(1).ToList();
            }

            return parameters;
        }

        /// <summary>
        /// Walks from the method scope to the declaring object so a
        /// <see cref="StaticMethodDeclarationScope"/> still sees <c>extension Foo</c>.
        /// </summary>
        private static ObjectDeclarationSymbol? FindExtensionOwner(ObjectMethodSymbol? method)
        {
            for (var scope = method?.ContainingScope; scope is not null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj)
                {
                    return obj.IsExtension ? obj : null;
                }
            }

            return null;
        }

        private static bool IsExtensionReceiverParameterName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.StartsWith('$') ? name[1..] : name;
            return string.Equals(trimmed, "this", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsConstantMagic(PhpMagicConstantAst magic)
        {
            var text = magic.ValueString?.ToLowerInvariant();
            return text is "true" or "false" or "null"
                || magic.ValueInt64 is not null;
        }

        /// <summary>
        /// Walks an expression tree and dispatches rules for compile-time constructs
        /// (<c>nameof</c>/<c>typeof</c>/<c>default</c>/<c>variable_exists</c>), Tyhp
        /// <c>with</c> binaries, and <c>await</c> operators found inside. Used from
        /// <c>ControlFlowRule</c> sites that suppress child traversal (return / if / echo / yield)
        /// without performing a full <c>CheckNode</c> (which re-enters statement rules and can hang
        /// on complex trees).
        ///
        /// Generic-parameter <c>instanceof</c>/<c>is</c> is handled by the emitter (reify to
        /// <c>\Tyhp\Type::is</c>) and flagged for Mechanism D binders / Mechanism C GenericObject via
        /// <see cref="UsesGenericAtRuntime"/> — no checker reject.
        /// </summary>
        public static void CheckCompileTimeConstructsInTree(
            IBase2Ast? node,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics,
            int depth = 0)
        {
            if (node is null || depth > 500)
            {
                return;
            }

            switch (node)
            {
                case TyhpNameofAst:
                case TyhpTypeofAst:
                case TyhpDefaultAst:
                case TyhpVariableExistsAst:
                    context.CheckNode(node, state);
                    return;

                // Existence probes are not reads — dispatch so NullSafetyRule suppresses operands.
                case PhpIssetStatementAst:
                case PhpEmptyStatementAst:
                    context.CheckNode(node, state);
                    return;

                // Variable reads inside return/if/echo/yield (ControlFlowRule suppresses those
                // statements' children). Dispatches NullSafetyRule for 4014/4015.
                case PhpVariableAst:
                    context.CheckNode(node, state);
                    return;

                // Bare names (`return FOO`, `echo OLD_CONST`) — DeprecationRule / free-constant
                // resolution would otherwise miss tyhpdef `deprecated` and `#[\Deprecated]` consts.
                case PhpNameAst:
                    context.CheckNode(node, state);
                    return;

                // Ternary arms need split-state checking for definite assignment (Prop-init #6).
                case PhpTernaryOpAst:
                    context.CheckNode(node, state);
                    return;

                // `await` inside return/if/echo expressions would otherwise skip AsyncRule because
                // ControlFlowRule suppresses child traversal on those statement nodes.
                case PhpUnaryOpAst unary when IsAwaitOperator(unary):
                    context.CheckNode(unary, state);
                    return;

                // `with` is a binary op; check it then continue into override values / left expr.
                case PhpBinaryOpAst binary when IsWithOperator(binary.Operator):
                    context.CheckNode(binary, state);
                    return;

                // Bare `new Struct()` (and class new) inside return/if/echo — required struct
                // properties and abstract/trait/interface instantiation checks live on CheckNew.
                case PhpNewAst:
                    context.CheckNode(node, state);
                    return;

                // Simple `$x = …` / `$this->prop = …` — CheckNode runs TypeCompatibilityRule
                // (AssignVariable / AssignProperty) and NullSafetyRule (skips the write-target left).
                // Do not walk into the left as a read.
                case PhpBinaryOpAst binary when IsSimpleAssignWrite(binary):
                    context.CheckNode(binary, state);
                    return;

                // `??` / `??=` — NullSafetyRule treats the left as an existence probe (no 4014/4157).
                case PhpBinaryOpAst binary when IsCoalesceOrCoalesceAssign(binary):
                    context.CheckNode(binary, state);
                    return;

                // Member / call / class-constant access inside return/if/echo/yield. Without this,
                // TypeCompatibilityRule never sees `return Owner::SECRET` (or `$x->privateProp`)
                // because ControlFlowRule suppresses child traversal on those statements.
                // CheckNode the dereferenceable; CheckCall CheckNodes non-closure argument
                // expressions (yield / await / nested operators) so they are not walked again
                // here — a second CheckNode would duplicate TYHP4086 / 4088 / 4089.
                // The callee's intermediate `->member` node is not re-checked as a property read
                // (e.g. `$accessor->get()` must not treat `->get` as private `$get`).
                case PhpDereferenceableAst deref:
                    context.CheckNode(deref, state);
                    // `(fn() => $x)()` — the closure is the callee (often wrapped in
                    // PhpDereferenceableExpressionAst for the parentheses), not a call argument,
                    // so CheckCall never reaches ClosureRule. Check it with the enclosing
                    // (possibly narrowed) state so captured variables keep their post-guard types.
                    if (FindParenthesizedInlineFunction(deref.Base) is { } calleeClosure)
                    {
                        CheckCompileTimeConstructsInTree(
                            calleeClosure, state, context, diagnostics, depth + 1);
                    }

                    return;

                // Progressive short-circuit narrowing: check left, apply its polarity
                // (`&&` positive / `||` negative), then check right — so
                // `\is_array($x) && \array_key_exists(0, $x)` sees `$x` as `array`, and
                // `!\class_exists($n) || \is_subclass_of($n, $base)` sees `$n` as `__ClassName`.
                // (Same pattern as TypeCompatibilityRule.CheckBinaryOp.)
                // This helper is also called directly with the ambient (non-probed) state from
                // `CheckReturn`/`CheckYield`/`CheckEcho` (ControlFlowRule.Helpers.cs) for these
                // expressions that are not themselves an if/while/ternary/switch condition, so the
                // narrowing must land on a disposable probe here too — otherwise
                // `yield \is_string($x) && f($x) => $y;` (or a bare `&&`/`||` inside `echo`/`return`)
                // would leak `$x`'s narrowed type into the reachable code that follows.
                case PhpBinaryOpAst binary
                    when TypeNarrowingRule.IsShortCircuitLogical(binary.Operator?.ValueString ?? string.Empty):
                    var shortCircuitProbe = state.Split(ScopeType.CodeBlock);
                    if (binary.Left is IExpression shortCircuitLeft)
                    {
                        CheckCompileTimeConstructsInTree(
                            shortCircuitLeft, shortCircuitProbe, context, diagnostics, depth + 1);
                        TypeNarrowingRule.ApplyConditionNarrowing(
                            shortCircuitLeft,
                            shortCircuitProbe,
                            context,
                            context.SymbolTree,
                            context.GlobalScope,
                            positive: TypeNarrowingRule.IsLogicalAnd(
                                binary.Operator?.ValueString ?? string.Empty));
                    }

                    if (binary.Right is not null)
                    {
                        CheckCompileTimeConstructsInTree(
                            binary.Right, shortCircuitProbe, context, diagnostics, depth + 1);
                    }

                    return;

                // Any other unary operator (`+`/`-`/`~`/`++`/`--`/`!`, casts, `@`) inside
                // return/echo/yield. Without this, `TypeCompatibilityRule.CheckUnaryOp` never sees
                // e.g. `return !$value;` / `echo -$value;` for a `mixed $value` — the mixed-narrowing
                // restriction (TYHP4160) and clone-non-object (TYHP4073) checks were silently skipped
                // because ControlFlowRule suppresses child traversal on those statements and this
                // walk's default recursion (below) only re-enters *children*, never the operator node
                // itself. `await`/`return`/`throw` operators are excluded — they have their own case
                // above, or (return/throw) are never passed into this helper as the node itself.
                case PhpUnaryOpAst unary
                    when unary.Operator?.ValueString is not ("return" or "throw"):
                    context.CheckNode(unary, state);
                    return;

                // Any other binary operator (arithmetic/bitwise/concat, comparison, `xor`)
                // inside return/echo/yield. Same gap as above for `TypeCompatibilityRule.CheckBinaryOp`
                // (mixed-narrowing restriction on arithmetic/logical operands) — `with`, simple-assign,
                // coalesce, and short-circuit `&&`/`||` already have dedicated cases above.
                case PhpBinaryOpAst otherBinary:
                    context.CheckNode(otherBinary, state);
                    return;

                // Nested statements are checked through their own ControlFlow entry points —
                // do not descend into them from an expression walk. Closures inside
                // return/echo/if expressions are otherwise skipped (ControlFlowRule suppresses
                // those statements' children), so ClosureRule must run here.
                case PhpInlineFunctionAst:
                    context.CheckNode(node, state);
                    return;

                case TyhpAsyncBlockAst:
                    context.CheckNode(node, state);
                    return;

                // Unlike the statement types below, `yield` is an expression and can be buried
                // inside another expression's manually-walked subtree (e.g. `return 1 + yield 2`).
                // Call-argument yield is CheckNode'd by CheckCall. PhpYieldAst children stay
                // suppressed by ControlFlowRule and are walked by `CheckYield`.
                case PhpYieldAst:
                    context.CheckNode(node, state);
                    return;

                // Interpolation inside return/echo/yield. ControlFlowRule suppresses those
                // statements' children, so TypeCompatibilityRule would never see the list.
                case PhpEncapsListAst:
                    context.CheckNode(node, state);
                    return;

                case PhpStatementBlockAst:
                case PhpIfAst:
                case PhpLoopAst:
                case PhpTryCatchAst:
                case PhpJumpStatementAst:
                case PhpReturnStatementAst:
                case PhpConditionalAst:
                case PhpEchoStatementAst:
                    return;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    CheckCompileTimeConstructsInTree(child, state, context, diagnostics, depth + 1);
                }
            }
        }

        /// <summary>
        /// Unwraps <c>(fn() => …)</c> grouping around an immediately-invoked closure.
        /// </summary>
        private static PhpInlineFunctionAst? FindParenthesizedInlineFunction(IDereferenceableBase? node)
        {
            while (node is not null)
            {
                switch (node)
                {
                    case PhpInlineFunctionAst closure:
                        return closure;
                    case PhpDereferenceableExpressionAst paren
                        when paren.Expression is PhpInlineFunctionAst innerClosure:
                        return innerClosure;
                    case PhpDereferenceableExpressionAst paren
                        when paren.Expression is IDereferenceableBase inner:
                        node = inner;
                        continue;
                    default:
                        return null;
                }
            }

            return null;
        }

        private static bool IsSimpleAssignWrite(PhpBinaryOpAst binary)
        {
            if (binary.Left is not PhpVariableAst
                && binary.Left is not PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst })
            {
                return false;
            }

            var op = PhpAssignmentOperatorExtensions.FromToken(
                GetAssignTokenType(binary.Operator),
                binary.Operator?.ValueString);
            return op is PhpAssignmentOperator.Assign or PhpAssignmentOperator.UsingEqual;
        }

        private static bool IsCoalesceOrCoalesceAssign(PhpBinaryOpAst binary)
        {
            var token = GetAssignTokenType(binary.Operator);
            var text = binary.Operator?.ValueString;
            if (PhpBinaryOperatorExtensions.FromToken(token, text) == PhpBinaryOperator.Coalesce)
            {
                return true;
            }

            var assignOp = PhpAssignmentOperatorExtensions.FromToken(token, text);
            return assignOp == PhpAssignmentOperator.CoalesceAssign;
        }

        private static int GetAssignTokenType(TokenValueAst? token) =>
            token?.ValueInt64 is long value ? (int)value : Parser.TyhpParser.Eof;

        private static bool IsAwaitOperator(PhpUnaryOpAst unary) =>
            string.Equals(unary.Operator?.ValueString, "await", StringComparison.OrdinalIgnoreCase)
            || (unary.Operator?.ValueInt64 is long tokenType
                && tokenType == Tyhp.TyhpLang.Parser.TyhpParser.T_TYHP_AWAIT);

        private static bool IsWithOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (op.ValueInt64 is long tokenType
                && tokenType == Tyhp.TyhpLang.Parser.TyhpParser.T_TYHP_WITH)
            {
                return true;
            }

            return string.Equals(op.ValueString, "with", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op.Identifier, "with", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when <paramref name="right"/> is the synthetic prefix <c>?</c> unary the visitor
        /// wraps around the RHS of <c>$x is ?T</c> (<see cref="PhpParserAstVisitor"/>
        /// <c>VisitPhpExprBinaryOpGrammarAddon002Handler</c>, FOUND_BUGS #21). This spelling never
        /// occurs elsewhere as a real unary operator — <c>?</c> is otherwise only the ternary
        /// operator or a type-annotation nullable prefix, neither of which parses as
        /// <see cref="PhpUnaryOpAst"/>.
        /// </summary>
        internal static bool IsNullableInstanceofMarker(IBase2Ast? right, out IBase2Ast operand)
        {
            operand = null!;
            if (right is not PhpUnaryOpAst { Operand: { } unaryOperand } unary)
            {
                return false;
            }

            var op = unary.Operator;
            var isQuestion = op?.ValueInt64 == Parser.TyhpParser.T_SYM_QUESTION
                || string.Equals(op?.ValueString, "?", StringComparison.Ordinal);
            if (!isQuestion)
            {
                return false;
            }

            operand = unaryOperand;
            return true;
        }

        /// <summary>
        /// True for PHP <c>instanceof</c> and the Tyhp <c>is</c> alias (same binary operator).
        /// </summary>
        internal static bool IsInstanceofLikeOperator(PhpBinaryOpAst binary)
        {
            var opText = binary.Operator?.ValueString;
            return string.Equals(opText, "instanceof", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsConstantArrayPair(PhpArrayPairAst arrayPair, CheckerState? state) =>
            (arrayPair.KeyExpr is null || IsConstantExpression(arrayPair.KeyExpr, state))
            && IsConstantExpression(arrayPair.ValueExpr, state);

        /// <summary>
        /// PHP 8.1 <c>new in initializers</c>: a compile-time class name with constant arguments.
        /// Rejects anonymous classes and dynamic names (<c>new $class</c>, <c>new (expr)</c>).
        /// </summary>
        private static bool IsConstantNew(PhpNewAst newExpr, CheckerState? state)
        {
            if (newExpr.AnonymousClass is not null || newExpr.ClassName is null)
            {
                return false;
            }

            // Static class name only — PhpNameAst covers unqualified / qualified / fully-qualified
            // identifiers used as `new Foo` / `new \A\B`.
            if (newExpr.ClassName is not PhpNameAst)
            {
                return false;
            }

            foreach (var arg in newExpr.Arguments?.GetAllNotNull() ?? [])
            {
                if (arg.Expression is not IExpression expr || !IsConstantExpression(expr, state))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsConstantBinary(PhpBinaryOpAst binary, CheckerState? state)
        {
            var left = binary.Left as IExpression;
            var right = binary.Right as IExpression;
            return IsConstantExpression(left, state) && IsConstantExpression(right, state);
        }

        /// <summary>
        /// True when <c>default(...)</c> folds to a compile-time literal. Only the top-level spelled
        /// type matters: <c>default(array&lt;T&gt;)</c> is the empty array whatever <c>T</c> is, while
        /// <c>default(T)</c> itself is resolved from the runtime type argument.
        /// </summary>
        private static bool IsConstantDefault(TyhpDefaultAst defaultExpr, CheckerState? state)
        {
            if (defaultExpr.TypeExpression is null || state is null)
            {
                return false;
            }

            return !NamesGenericParameterIn(defaultExpr.TypeExpression, state.FunctionGenerics)
                && !NamesGenericParameterIn(defaultExpr.TypeExpression, state.ObjectGenerics);
        }

        /// <summary>
        /// True when a <c>default(X)</c> / <c>typeof(X)</c> type spelling names one of
        /// <paramref name="generics"/>. Callers pass <see cref="CheckerState.FunctionGenerics"/> or
        /// <see cref="CheckerState.ObjectGenerics"/> to distinguish a parameter the callable declares
        /// itself (served by the Mechanism D binder) from one inherited from the enclosing class
        /// (served by <c>GenericObject</c> instance tracking).
        /// </summary>
        public static bool NamesGenericParameterIn(
            IBase2Ast? typeExpr,
            IReadOnlyList<GenericTypeParameterSymbol> generics) =>
            SoleTypeName(typeExpr) is { } simple
            && generics.Any(gp => string.Equals(gp.Name, simple, StringComparison.Ordinal));

        /// <summary>
        /// The lone unqualified type name a <c>default(X)</c> / <c>typeof(X)</c> spelling denotes, or
        /// <c>null</c> when it is nullable, composite or qualified — none of which can name a generic
        /// parameter.
        /// </summary>
        public static string? SoleTypeName(IBase2Ast? typeExpr)
        {
            // `default(X)` wraps X in a single-member type-expression list (see VisitTypeExpr). A
            // nullable or composite spelling always defaults to null, so only the lone simple
            // member can name a generic parameter.
            if (typeExpr is PhpTypeExpressionAst composite)
            {
                if (composite.IsNullable || composite.Types is null)
                {
                    return null;
                }

                var members = composite.Types.GetAllNotNull().ToList();
                return members.Count == 1 && members[0] is ITypeExpression inner
                    ? SoleTypeName(inner)
                    : null;
            }

            var name = typeExpr switch
            {
                PhpNamedTypeAst { Name: PhpNameAst named } => named.ValueString,
                PhpNameAst bare => bare.ValueString,
                _ => null,
            };

            var simple = name?.TrimStart('\\');
            return string.IsNullOrEmpty(simple) || simple.Contains('\\') ? null : simple;
        }

        /// <summary>
        /// Flags a callable for Mechanism D binder emission when its body uses one of its own generic
        /// parameters in a construct that needs the bound type at runtime.
        /// </summary>
        public static void FlagGenericVariantIfNeeded(
            IBase2Ast? body,
            IBaseSymbol? symbol,
            IReadOnlyList<GenericTypeParameterSymbol> generics,
            CheckerRuleContext context)
        {
            if (symbol is not null && generics.Count > 0 && UsesGenericAtRuntime(body, generics))
            {
                context.MarkRequiresGenericVariant(symbol);
            }
        }

        /// <summary>
        /// True when a subtree uses one of <paramref name="generics"/> in a construct that needs the
        /// bound type at runtime: <c>typeof(T)</c>, <c>default(T)</c>, <c>instanceof T</c> /
        /// <c>is T</c> (and aliases), <c>new T()</c> (a type parameter as the class name), a type
        /// argument on <c>new Foo&lt;T&gt;</c> / <c>new static&lt;T&gt;</c> (Mechanism C factory),
        /// or a type argument on a call (<c>decode&lt;T&gt;()</c> / <c>Type::isType&lt;T&gt;()</c>)
        /// so a forwarding wrapper can pass the bound type into a Mechanism D binder.
        ///
        /// Scanned directly rather than inferred from rule dispatch. <see cref="CompileTimeRule"/>
        /// only observes typeof/default nodes in the expression positions
        /// <see cref="CheckCompileTimeConstructsInTree"/> covers, so a <c>typeof(T)</c> in a bare
        /// expression statement or a <c>match</c> arm is never visited and would leave the enclosing
        /// callable unflagged, emitting a lookup with nothing behind it. The same scan covers
        /// <c>instanceof</c>/<c>is</c> so Mechanism D binders and GenericObject tracking fire when
        /// the emitter rewrites those checks to <c>\Tyhp\Type::is</c>. Type-argument addons are not
        /// AstChildren, so <c>new</c>/<c>instanceof</c>/call parameterized forms are checked on the
        /// name node (including <c>memberName</c> for <c>::</c>/<c>-&gt;</c> callees).
        /// </summary>
        public static bool UsesGenericAtRuntime(
            IBase2Ast? node,
            IReadOnlyList<GenericTypeParameterSymbol> generics,
            int depth = 0)
        {
            if (node is null || generics.Count == 0 || depth > 500)
            {
                return false;
            }

            // Type-arg lists live in grammar addons on the name, not AstChildren. Checking every
            // node covers `new Foo<T>`, `instanceof Foo<T>`, `foo<T>()`, and `Bar::decode<T>()`.
            if (ClassReferenceTypeArgsUseGeneric(node, generics, depth))
            {
                return true;
            }

            switch (node)
            {
                case TyhpTypeofAst typeofExpr:
                    if (NamesGenericParameterIn(typeofExpr.TypeExpression, generics))
                    {
                        return true;
                    }

                    break;

                case TyhpDefaultAst defaultExpr:
                    if (NamesGenericParameterIn(defaultExpr.TypeExpression, generics))
                    {
                        return true;
                    }

                    break;

                case PhpNewAst newExpr:
                    // `new T()` — the class name itself is the generic parameter (Mechanism C/D).
                    if (NamesGenericParameterIn(newExpr.ClassName, generics))
                    {
                        return true;
                    }

                    // `new static<T>(…)` / `new Box<T>(…)` — type args ride on the class-name
                    // addon (or TyhpGenericIdentifierAst.GenericArguments), not AstChildren.
                    if (ClassReferenceTypeArgsUseGeneric(newExpr.ClassName, generics, depth))
                    {
                        return true;
                    }

                    break;

                case PhpBinaryOpAst binary when IsInstanceofLikeOperator(binary):
                    if (NamesGenericParameterIn(binary.Right as IBase2Ast, generics))
                    {
                        return true;
                    }

                    // `instanceof static<T>` / `Foo<T>` — the generic lives in type-argument
                    // addons (`identifier` or `typeName`), not the sole RHS name (`static` / `Foo`).
                    if (ClassReferenceTypeArgsUseGeneric(binary.Right as IBase2Ast, generics, depth))
                    {
                        return true;
                    }

                    break;
            }

            foreach (var child in node.AstChildren)
            {
                if (UsesGenericAtRuntime(child, generics, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when a class-name / type-name node carries type-argument addons (or an explicit
        /// <see cref="TyhpGenericIdentifierAst.GenericArguments"/> list) that name or further use
        /// one of <paramref name="generics"/>.
        /// </summary>
        private static bool ClassReferenceTypeArgsUseGeneric(
            IBase2Ast? classOrTypeRef,
            IReadOnlyList<GenericTypeParameterSymbol> generics,
            int depth)
        {
            if (classOrTypeRef is null)
            {
                return false;
            }

            if (classOrTypeRef is TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst ga }
                && TypeArgumentListUsesGenericAtRuntime(ga, generics, depth))
            {
                return true;
            }

            foreach (var key in (string[])["typeName", "identifier", "memberName"])
            {
                if (!classOrTypeRef.AstGrammarAddons.TryGetValue(key, out var typeArgAddon)
                    || typeArgAddon is not PhpTypeExpressionListAst typeArgList)
                {
                    continue;
                }

                if (TypeArgumentListUsesGenericAtRuntime(typeArgList, generics, depth))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TypeArgumentListUsesGenericAtRuntime(
            PhpTypeExpressionListAst typeArgList,
            IReadOnlyList<GenericTypeParameterSymbol> generics,
            int depth)
        {
            foreach (var arg in typeArgList.GetAllNotNull())
            {
                if (NamesGenericParameterIn(arg, generics)
                    || UsesGenericAtRuntime(arg, generics, depth + 1))
                {
                    return true;
                }

                // Wrapped single PhpTypeExpressionAst (instanceof / new classNameIdentifier shape).
                if (arg is PhpTypeExpressionAst { Types: PhpTypeExpressionListAst nested })
                {
                    foreach (var nestedArg in nested.GetAllNotNull())
                    {
                        if (NamesGenericParameterIn(nestedArg, generics)
                            || UsesGenericAtRuntime(nestedArg, generics, depth + 1))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Reports each <c>instanceof T</c> / <c>is T</c> (and aliases) in <paramref name="node"/>
        /// whose RHS names one of <paramref name="generics"/> — used to reject that shape from a
        /// <c>static</c> member, where emitter reification (Prop-init #37) has nothing on the instance
        /// to read the bound type from. A declared class of the same spelling as the generic parameter
        /// takes precedence (matches <c>TyhpEmitter.TryBuildReifiedInstanceofCheck</c>) and is skipped,
        /// so shadowing a generic name with a real class is unaffected.
        /// </summary>
        public static void ForEachStaticContextGenericInstanceof(
            IBase2Ast? node,
            IReadOnlyList<GenericTypeParameterSymbol> generics,
            Action<PhpBinaryOpAst, string> report,
            int depth = 0)
        {
            if (node is null || generics.Count == 0 || depth > 500)
            {
                return;
            }

            if (node is PhpBinaryOpAst binary
                && IsInstanceofLikeOperator(binary)
                && binary.Right is PhpNameAst name
                && name.BoundSymbol is not ObjectDeclarationSymbol
                && SoleTypeName(name) is { } simple
                && generics.FirstOrDefault(gp => string.Equals(gp.Name, simple, StringComparison.Ordinal)) is { } matched)
            {
                report(binary, matched.Name);
            }

            foreach (var child in node.AstChildren)
            {
                ForEachStaticContextGenericInstanceof(child, generics, report, depth + 1);
            }
        }

        /// <summary>
        /// PHP 8.5 <c>(void)</c> cast operator token on a prefix <see cref="PhpUnaryOpAst"/>.
        /// </summary>
        public static bool IsVoidCastUnary(PhpUnaryOpAst unary) =>
            unary.Operator?.ValueInt64 is long value
            && (int)value == Parser.TyhpParser.T_VOID_CAST;

        /// <summary>
        /// True when <paramref name="node"/> is a <c>(void) expr</c> discard form.
        /// </summary>
        public static bool IsVoidCast(IBase2Ast? node) =>
            node is PhpUnaryOpAst unary && IsVoidCastUnary(unary);

        /// <summary>
        /// When a discarded expression (statement / non-final for-list item) is a call to a
        /// <c>#[\NoDiscard]</c>-marked callable, emits TYHP4165. <c>(void)</c> is intentional
        /// discard and suppresses the warning. PHP warns from the invoked declaration
        /// only: interface and abstract method callees do not warn; overrides do not inherit
        /// the attribute. Trait-imported methods still warn when the trait method is marked
        /// (PHP copies the attribute onto the using class). A string-literal
        /// <c>$message</c> is appended as <c>{1}</c> (empty when omitted).
        /// </summary>
        public static void ReportNoDiscardIfDiscarded(
            IBase2Ast statementOrExpr,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (IsVoidCast(statementOrExpr))
            {
                return;
            }

            if (!TryGetCallCalleeDeclaringNode(
                    statementOrExpr,
                    state,
                    context,
                    out var displayName,
                    out var declaringNode,
                    out var callee)
                || declaringNode is null
                || !HasNoDiscardAttribute(declaringNode)
                || SuppressesNoDiscardWarning(callee))
            {
                return;
            }

            // WARNING_TYHP4165 is `…(void)`{1}. {1} is empty when there is no literal
            // #[\NoDiscard] $message so existing one-arg wording is preserved.
            var messageSuffix = TryGetLiteralNoDiscardMessage(declaringNode, out var message)
                ? ": " + message
                : string.Empty;
            ReportWarning(
                diagnostics,
                state,
                statementOrExpr,
                MessageCode.CheckerNoDiscardReturnUnused,
                displayName,
                messageSuffix);
        }

        /// <summary>
        /// True when the declaration carries <c>#[\NoDiscard]</c> / <c>#[NoDiscard]</c>
        /// (name match; class need not resolve yet).
        /// </summary>
        public static bool HasNoDiscardAttribute(IBase2Ast declaringNode)
        {
            foreach (var attribute in declaringNode.AstAttributes.OfType<PhpAttributeAst>())
            {
                if (IsNoDiscardAttributeName(GetAttributeSimpleName(attribute.Name)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// PHP does not warn for interface or abstract method declarations — the invoked
        /// method is the implementor/override. Trait methods are not skipped (PHP copies
        /// the attribute onto the using class).
        /// </summary>
        private static bool SuppressesNoDiscardWarning(IBaseSymbol? callee)
        {
            if (callee is not ObjectMethodSymbol method)
            {
                return false;
            }

            if (method.IsAbstract)
            {
                return true;
            }

            return method.ContainingScope?.DeclarationSymbol is ObjectDeclarationSymbol
            {
                ObjectKind: PhpTypeDeclType.Interface
            };
        }

        private static bool TryGetLiteralNoDiscardMessage(IBase2Ast declaringNode, out string message)
        {
            message = "";
            foreach (var attribute in declaringNode.AstAttributes.OfType<PhpAttributeAst>())
            {
                if (!IsNoDiscardAttributeName(GetAttributeSimpleName(attribute.Name)))
                {
                    continue;
                }

                if (TryReadNoDiscardMessageArgument(attribute, out message))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadNoDiscardMessageArgument(PhpAttributeAst attribute, out string message)
        {
            message = "";
            var arguments = attribute.Arguments?.GetAllNotNull();
            if (arguments is null)
            {
                return false;
            }

            PhpArgumentAst? namedMessage = null;
            PhpArgumentAst? firstPositional = null;
            foreach (var argument in arguments)
            {
                var argName = NormalizeAttributeArgumentName(argument.Name?.ValueString);
                if (string.IsNullOrEmpty(argName))
                {
                    firstPositional ??= argument;
                    continue;
                }

                if (string.Equals(argName, "message", StringComparison.OrdinalIgnoreCase))
                {
                    namedMessage = argument;
                    break;
                }
            }

            var chosen = namedMessage ?? firstPositional;
            if (chosen?.Expression is null
                || !TryReadNoDiscardStringLiteral(chosen.Expression, out message)
                || string.IsNullOrWhiteSpace(message))
            {
                message = "";
                return false;
            }

            return true;
        }

        private static string? NormalizeAttributeArgumentName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            return name[0] == '$' ? name[1..] : name;
        }

        private static bool TryReadNoDiscardStringLiteral(IExpression expression, out string value)
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

                    value = UnquotePhpStringLiteral(scalar.ValueString);
                    return !string.IsNullOrEmpty(value);

                case PhpEncapsStringAst encaps:
                    value = UnquotePhpStringLiteral(encaps.ValueString ?? encaps.TokenValue?.ValueString);
                    return !string.IsNullOrEmpty(value);

                case PhpEncapsListAst list:
                    // Only a genuine literal when every part is a plain string segment — a part
                    // that is itself a variable / member / expression (real string interpolation)
                    // is not a compile-time literal. Same rule as EngineDeprecatedAttribute.
                    var parts = list.GetAllNotNull().ToList();
                    if (!parts.All(part => part is PhpEncapsStringAst))
                    {
                        return false;
                    }

                    value = string.Concat(parts.Select(part => UnquotePhpStringLiteral(part.ValueString)));
                    return !string.IsNullOrEmpty(value);

                default:
                    return false;
            }
        }

        private static string UnquotePhpStringLiteral(string? literal)
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

        private static bool IsNoDiscardAttributeName(string? name) =>
            name is not null
            && (string.Equals(name, "NoDiscard", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("\\NoDiscard", StringComparison.OrdinalIgnoreCase));

        private static string? GetAttributeSimpleName(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString ?? name.Identifier,
                TokenValueAst token => token.ValueString,
                _ => null,
            };

        /// <summary>
        /// Resolves a discarded call expression to its callee declaration for NoDiscard checks.
        /// Supports free-function and instance/static method call shapes used by
        /// <c>TypeCompatibilityRule.CheckCall</c>.
        /// </summary>
        private static bool TryGetCallCalleeDeclaringNode(
            IBase2Ast node,
            CheckerState state,
            CheckerRuleContext context,
            out string displayName,
            out IBase2Ast? declaringNode,
            out IBaseSymbol? callee)
        {
            displayName = string.Empty;
            declaringNode = null;
            callee = null;

            if (node is not PhpDereferenceableAst { Suffix: PhpCallAst } deref)
            {
                return false;
            }

            if (deref.Base is PhpNameAst nameAst)
            {
                var function = ResolveFreeFunction(
                    nameAst, state, context.SymbolTree, context.GlobalScope);
                if (function is null)
                {
                    return false;
                }

                displayName = function.FullyQualifiedName ?? function.Name ?? "function";
                declaringNode = function.DeclaringAstNode;
                callee = function;
                return declaringNode is not null;
            }

            if (deref.Base is not PhpDereferenceableAst chain || chain.Base is null)
            {
                return false;
            }

            // `staticOnly` is intentionally not enforced here. CheckCall now also
            // forwards `self`/`parent`/`static` to instance methods for argument
            // checking, but still rejects `Foo::instanceMethod()`. NoDiscard only
            // needs the invoked declaration, so named-class `::` to a non-static
            // method is still resolved here even when CheckCall does not treat it
            // as a legal forwarding call.
            string? methodName;
            ICheckedType receiverType;
            switch (chain.Suffix)
            {
                case PhpInstanceMemberAccessAst instanceAccess:
                    methodName = GetExpressionText(instanceAccess.MemberName);
                    receiverType = context.ResolveExpressionType(chain.Base, state);
                    break;
                case PhpStaticMemberAccessAst staticAccess:
                    methodName = GetExpressionText(staticAccess.Member);
                    receiverType = ResolveInstanceofTargetType(
                        chain.Base, state, context, context.SymbolTree, context.GlobalScope);
                    break;
                case PhpClassConstantAccessAst classConstAccess:
                    methodName = GetExpressionText(classConstAccess.Member);
                    receiverType = ResolveInstanceofTargetType(
                        chain.Base, state, context, context.SymbolTree, context.GlobalScope);
                    break;
                default:
                    return false;
            }

            if (methodName is null
                || TryGetObjectDeclaration(UnwrapNullable(receiverType)) is not { } objectDecl)
            {
                return false;
            }

            if (context.SymbolTree.ResolveMember(methodName, objectDecl, new DiagnosticBag())
                    is not ObjectMethodSymbol method)
            {
                return false;
            }

            displayName = $"{objectDecl.Name}::{method.Name}";
            declaringNode = method.DeclaringAstNode;
            callee = method;
            return declaringNode is not null;
        }

        /// <summary>
        /// Resolves an alias body in the alias's declaration context: class-level aliases treat
        /// <c>self</c>/<c>static</c>/<c>parent</c> as the owning class, and alias generic
        /// parameters (<c>type Row&lt;T&gt;</c>) are in scope.
        /// </summary>
        internal static CheckerState WithAliasBodyContext(CheckerState state, IBaseSymbol alias)
        {
            IReadOnlyList<GenericTypeParameterSymbol>? aliasGenerics = alias switch
            {
                TypeAliasSymbol fileAlias => fileAlias.GenericParameters,
                ObjectTypeAliasSymbol objectAlias => objectAlias.GenericParameters,
                _ => null,
            };

            var owner = alias is ObjectTypeAliasSymbol
                ? alias.ContainingScope?.DeclarationSymbol as ObjectDeclarationSymbol
                : null;

            if (owner is null && (aliasGenerics is null || aliasGenerics.Count == 0))
            {
                return state;
            }

            var forked = state.Fork();
            if (owner is not null)
            {
                forked.EnclosingObject = owner;
                forked.EnclosingObjectType = CheckedTypes.FromSymbol(owner);
                if (owner.GenericParameters.Count > 0)
                {
                    forked.ObjectGenerics = owner.GenericParameters;
                }
            }

            if (aliasGenerics is { Count: > 0 })
            {
                forked.FunctionGenerics = aliasGenerics;
            }

            return forked;
        }

        private static ICheckedType UnwrapNullable(ICheckedType type) =>
            type is NullableCheckedType nullable ? nullable.InnerType : type;

        private static string? GetExpressionText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString ?? name.Identifier,
                TokenValueAst token => token.ValueString,
                PhpVariableAst variable => GetVariableName(variable),
                _ => null,
            };
    }
}
