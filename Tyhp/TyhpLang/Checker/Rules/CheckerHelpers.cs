using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
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
        {
            if (nameAst.BoundSymbol is FunctionDeclarationSymbol bound)
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
            var simple = raw.TrimStart('\\');

            return (resolver.ResolveSymbol(simple, fromScope)
                ?? resolver.ResolveRelativeName(new[] { simple }, fromScope)) as FunctionDeclarationSymbol;
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
                // Unpack may match any overload; keep primary for gradual checking.
                return primary;
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

        public static IEnumerable<FunctionDeclarationSymbol> EnumerateFunctionSignatures(
            FunctionDeclarationSymbol primary)
        {
            yield return primary;
            foreach (var overload in primary.Overloads)
            {
                yield return overload;
            }
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
                return (min, int.MaxValue);
            }

            return (min, parameters.Count);
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
                    _ => MemberModifier.None,
                };
            }

            return result;
        }

        public static MemberModifier ToMemberModifiers(PhpModifierListAst? modifiers) =>
            modifiers is null ? MemberModifier.None : ToMemberModifiers(modifiers.Modifiers);

        public static int CountVisibilityModifiers(MemberModifier modifiers)
        {
            var count = 0;
            if ((modifiers & MemberModifier.Public) != 0) count++;
            if ((modifiers & MemberModifier.Protected) != 0) count++;
            if ((modifiers & MemberModifier.Private) != 0) count++;
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
                PhpArrayAst arrayAst => arrayAst.ArrayPairs?.GetAllNotNull().ToList() ?? [],
                PhpArrayPairListAst pairList => pairList.GetAllNotNull().ToList(),
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

        // The right-hand side of `instanceof` is a class/type reference, not a value expression, so
        // inferring it via expression inference yields `unknown` (a bare class name is not a value).
        // Resolve it as a type instead so narrowing produces the real class type. Handles the relative
        // keywords (`self`/`static`/`parent`) and named classes, falling back to expression inference
        // for dynamic forms such as `$x instanceof $classNameVar`.
        public static ICheckedType ResolveInstanceofTargetType(
            IBase2Ast right,
            CheckerState state,
            INarrowingResolution context,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (right is PhpNameAst nameAst)
            {
                var raw = nameAst.ValueString?.TrimStart('\\');
                if (!string.IsNullOrEmpty(raw))
                {
                    ICheckedType? baseType = null;
                    if (string.Equals(raw, "self", StringComparison.OrdinalIgnoreCase))
                    {
                        if (state.EnclosingObjectType is not null)
                        {
                            baseType = state.EnclosingObjectType;
                        }
                        else if (state.EnclosingObject is not null)
                        {
                            baseType = CheckedTypes.FromSymbol(state.EnclosingObject);
                        }
                    }
                    else if (string.Equals(raw, "static", StringComparison.OrdinalIgnoreCase))
                    {
                        ICheckedType? declaring = null;
                        if (state.EnclosingObjectType is not null)
                        {
                            declaring = state.EnclosingObjectType;
                        }
                        else if (state.EnclosingObject is not null)
                        {
                            declaring = CheckedTypes.FromSymbol(state.EnclosingObject);
                        }

                        if (declaring is not null)
                        {
                            baseType = new StaticCheckedType(declaring);
                        }
                    }
                    else if (string.Equals(raw, "parent", StringComparison.OrdinalIgnoreCase)
                        && state.EnclosingObject?.ExtendsType is { } extendsType)
                    {
                        baseType = context.ResolveTypeAnnotation(extendsType, state);
                    }
                    else if (nameAst.BoundSymbol is ObjectDeclarationSymbol bound)
                    {
                        baseType = CheckedTypes.FromSymbol(bound);
                    }
                    else
                    {
                        var fromScope = state.EnclosingFunction?.ContainingScope
                            ?? state.EnclosingObject?.ContainingScope
                            ?? (IBaseScope)globalScope;
                        var resolver = new NameResolver(symbolTree, new DiagnosticBag());
                        var symbol = resolver.ResolveSymbol(raw, fromScope)
                            ?? resolver.ResolveRelativeName(raw.Split('\\'), fromScope);
                        if (symbol is not null)
                        {
                            baseType = CheckedTypes.FromSymbol(symbol);
                        }
                    }

                    if (baseType is not null)
                    {
                        return ApplyInstanceofTypeArguments(baseType, nameAst, state, context);
                    }
                }
            }

            return context.ResolveExpressionType(right, state);
        }

        /// <summary>
        /// Applies grammar type arguments on an <c>instanceof</c> RHS name
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
                    || array.ArrayPairs.GetAllNotNull().All(pair => IsConstantArrayPair(pair, state)),
                PhpArrayPairListAst arrayPairs =>
                    arrayPairs.GetAllNotNull().All(pair => IsConstantArrayPair(pair, state)),
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
                // CheckNode the dereferenceable; for calls, walk only argument expressions so the
                // callee's intermediate `->member` node is not re-checked as a property read
                // (e.g. `$accessor->get()` must not treat `->get` as private `$get`).
                case PhpDereferenceableAst deref:
                    context.CheckNode(deref, state);
                    if (deref.Suffix is PhpCallAst call && call.Arguments is not null)
                    {
                        foreach (var arg in call.Arguments.GetAllNotNull())
                        {
                            CheckCompileTimeConstructsInTree(
                                arg.Expression as IBase2Ast ?? arg,
                                state,
                                context,
                                diagnostics,
                                depth + 1);
                        }
                    }

                    return;

                // Progressive `&&` / `and` narrowing: check left, apply its positive narrowing,
                // then check right — so `\is_array($x) && \array_key_exists(0, $x)` sees `$x` as
                // `array` in the second operand. (Same pattern as TypeCompatibilityRule.CheckBinaryOp.)
                // This helper is also called directly with the ambient (non-probed) state from
                // `CheckReturn`/`CheckYield`/`CheckEcho` (ControlFlowRule.Helpers.cs) for `&&`
                // expressions that are not themselves an if/while/ternary/switch condition, so the
                // narrowing must land on a disposable probe here too — otherwise
                // `yield \is_string($x) && f($x) => $y;` (or a bare `&&` inside `echo`/`return`)
                // would leak `$x`'s narrowed type into the reachable code that follows.
                case PhpBinaryOpAst binary
                    when TypeNarrowingRule.IsLogicalAnd(binary.Operator?.ValueString ?? string.Empty):
                    var andProbe = state.Split(ScopeType.CodeBlock);
                    if (binary.Left is IExpression andLeft)
                    {
                        CheckCompileTimeConstructsInTree(
                            andLeft, andProbe, context, diagnostics, depth + 1);
                        TypeNarrowingRule.ApplyConditionNarrowing(
                            andLeft,
                            andProbe,
                            context,
                            context.SymbolTree,
                            context.GlobalScope,
                            positive: true);
                    }

                    if (binary.Right is not null)
                    {
                        CheckCompileTimeConstructsInTree(
                            binary.Right, andProbe, context, diagnostics, depth + 1);
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

                // Any other binary operator (arithmetic/bitwise/concat, comparison, `||`/`or`/`xor`)
                // inside return/echo/yield. Same gap as above for `TypeCompatibilityRule.CheckBinaryOp`
                // (mixed-narrowing restriction on arithmetic/logical operands) — `with`, simple-assign,
                // coalesce, and `&&` already have dedicated cases above.
                case PhpBinaryOpAst otherBinary:
                    context.CheckNode(otherBinary, state);
                    return;

                // Nested statements / closures are checked through their own ControlFlow /
                // ClosureRule entry points — do not descend into them from an expression walk.
                case PhpInlineFunctionAst:
                case PhpStatementBlockAst:
                case PhpIfAst:
                case PhpLoopAst:
                case PhpTryCatchAst:
                case PhpJumpStatementAst:
                case PhpReturnStatementAst:
                case PhpConditionalAst:
                case PhpEchoStatementAst:
                case PhpYieldAst:
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

        private static bool IsSimpleAssignWrite(PhpBinaryOpAst binary)
        {
            if (binary.Left is not PhpVariableAst
                && binary.Left is not PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst })
            {
                return false;
            }

            var op = PhpAssignmentOperatorExtensions.FromToken(GetAssignTokenType(binary.Operator));
            return op is PhpAssignmentOperator.Assign or PhpAssignmentOperator.UsingEqual;
        }

        private static bool IsCoalesceOrCoalesceAssign(PhpBinaryOpAst binary)
        {
            var token = GetAssignTokenType(binary.Operator);
            if (PhpBinaryOperatorExtensions.FromToken(token) == PhpBinaryOperator.Coalesce)
            {
                return true;
            }

            var assignOp = PhpAssignmentOperatorExtensions.FromToken(token);
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
        /// True for PHP <c>instanceof</c> and the Tyhp <c>is</c>/<c>isa</c>/<c>isan</c>/
        /// <c>is_a</c>/<c>is_an</c> aliases (all map to the same binary operator).
        /// </summary>
        internal static bool IsInstanceofLikeOperator(PhpBinaryOpAst binary)
        {
            var opText = binary.Operator?.ValueString;
            return string.Equals(opText, "instanceof", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "isa", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "isan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is_a", StringComparison.OrdinalIgnoreCase)
                || string.Equals(opText, "is_an", StringComparison.OrdinalIgnoreCase);
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
        /// True when a subtree uses one of <paramref name="generics"/> in a construct that needs the
        /// bound type at runtime: <c>typeof(T)</c>, <c>default(T)</c>, <c>instanceof T</c> /
        /// <c>is T</c> (and aliases), or a type argument on <c>new Foo&lt;T&gt;</c> /
        /// <c>new static&lt;T&gt;</c> (Mechanism C factory + Mechanism D binder).
        ///
        /// Scanned directly rather than inferred from rule dispatch. <see cref="CompileTimeRule"/>
        /// only observes typeof/default nodes in the expression positions
        /// <see cref="CheckCompileTimeConstructsInTree"/> covers, so a <c>typeof(T)</c> in a bare
        /// expression statement or a <c>match</c> arm is never visited and would leave the enclosing
        /// callable unflagged, emitting a lookup with nothing behind it. The same scan covers
        /// <c>instanceof</c>/<c>is</c> so Mechanism D binders and GenericObject tracking fire when
        /// the emitter rewrites those checks to <c>\Tyhp\Type::is</c>. Type-argument addons are not
        /// AstChildren, so <c>new</c>/<c>instanceof</c> parameterized forms are checked explicitly.
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

            switch (node)
            {
                case TyhpTypeofAst typeofExpr:
                    if (NamesGenericParameterIn(typeofExpr.Expression, generics))
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

            foreach (var key in (string[])["typeName", "identifier"])
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
        /// discard and suppresses the warning. No-ops until a NoDiscard attribute is present on
        /// the callee (ExtCore stub is Story 21; unbound <c>#[\NoDiscard]</c> is allowed via
        /// <c>AttributeRule</c> built-in allow-list in the interim).
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
                    statementOrExpr, state, context, out var displayName, out var declaringNode)
                || declaringNode is null
                || !HasNoDiscardAttribute(declaringNode))
            {
                return;
            }

            ReportWarning(
                diagnostics,
                state,
                statementOrExpr,
                MessageCode.CheckerNoDiscardReturnUnused,
                displayName);
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
            out IBase2Ast? declaringNode)
        {
            displayName = string.Empty;
            declaringNode = null;

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
                return declaringNode is not null;
            }

            if (deref.Base is not PhpDereferenceableAst chain || chain.Base is null)
            {
                return false;
            }

            string? methodName;
            bool staticOnly;
            ICheckedType receiverType;
            switch (chain.Suffix)
            {
                case PhpInstanceMemberAccessAst instanceAccess:
                    methodName = GetExpressionText(instanceAccess.MemberName);
                    staticOnly = false;
                    receiverType = context.ResolveExpressionType(chain.Base, state);
                    break;
                case PhpStaticMemberAccessAst staticAccess:
                    methodName = GetExpressionText(staticAccess.Member);
                    staticOnly = true;
                    receiverType = ResolveInstanceofTargetType(
                        chain.Base, state, context, context.SymbolTree, context.GlobalScope);
                    break;
                case PhpClassConstantAccessAst classConstAccess:
                    methodName = GetExpressionText(classConstAccess.Member);
                    staticOnly = true;
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
                    is not ObjectMethodSymbol method
                || (staticOnly && !method.IsStatic))
            {
                return false;
            }

            displayName = $"{objectDecl.Name}::{method.Name}";
            declaringNode = method.DeclaringAstNode;
            return declaringNode is not null;
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
