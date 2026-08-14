using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Mechanism D — Closure-binder variants for function and method generics (FOUND_BUGS
    /// Mechanism D; supersedes Mechanism A).
    ///
    /// A class generic is recorded on the instance and read back through the <c>GenericObject</c>
    /// trait, which needs <c>$this</c>. A generic declared by a function or method itself has no
    /// such home: a free function has no receiver and a static method has no instance. So a callable
    /// that uses its own generic in a construct requiring the bound type at runtime is emitted as a
    /// pair — the declared name keeps the declared signature and delegates with null type args, while
    /// a <c>__tyhpGeneric</c> binder takes only the type arguments and returns a <c>\Closure</c> with
    /// the author's value signature (body + prologue inside that Closure).
    /// </summary>
    public partial class TyhpEmitter
    {
        internal const string GenericVariantSuffix = GeneratedNames.GenericVariantSuffix;

        private static string GenericVariantParamName(string genericName) =>
            "$" + GeneratedNames.GenericVariantParameterPrefix + genericName;

        /// <summary>
        /// Generic parameters the checker flagged for Mechanism D emission on this declaration, or
        /// an empty list when the declaration needs no variant. Callables whose generics only erase
        /// are not flagged and keep emitting as a single symbol.
        /// </summary>
        private IReadOnlyList<GenericTypeParameterSymbol> ResolveVariantGenericParams(IBase2Ast decl)
        {
            if (this._currentVariantGenericParams.Count > 0)
            {
                // Already inside the variant; re-entering would recurse forever.
                return Array.Empty<GenericTypeParameterSymbol>();
            }

            var symbol = decl.BoundSymbol;
            if (!this._context.RequiresGenericVariantFor(symbol))
            {
                return Array.Empty<GenericTypeParameterSymbol>();
            }

            return symbol switch
            {
                ObjectMethodSymbol method => method.GenericParameters,
                FunctionDeclarationSymbol function => function.GenericParameters,
                _ => Array.Empty<GenericTypeParameterSymbol>(),
            };
        }

        /// <summary>
        /// The <c>?\Tyhp\Type $__generic_*</c> parameters of the binder. They accept null so the
        /// delegating wrapper (and a PHP-mode caller reaching the binder) can pass nothing, in which
        /// case the Closure prologue falls back to a declared default or <c>mixed</c>. They carry no
        /// default value: every caller passes them explicitly.
        /// </summary>
        private IReadOnlyList<string> BuildVariantHiddenParameters()
        {
            if (this._currentVariantGenericParams.Count == 0)
            {
                return Array.Empty<string>();
            }

            this._context.RequirePackage("tyhp/core");
            return this._currentVariantGenericParams
                .Select(gp => $"?{RuntimeTypeClassFq} {GenericVariantParamName(gp.Name)}")
                .ToList();
        }

        /// <summary>The binder's name for a declared identifier, with generics stripped.</summary>
        private string BuildVariantName(string? identifier) =>
            this.StripGenericsFromName(identifier) + GenericVariantSuffix;

        /// <summary>
        /// The emitted name for a declaration: the declared name, suffixed while emitting the binder.
        /// </summary>
        private string ApplyVariantNaming(string? identifier) =>
            this._currentVariantGenericParams.Count > 0
                ? this.BuildVariantName(identifier)
                : this.StripGenericsFromName(identifier);

        /// <summary>
        /// Hidden parameter names an emitter-generated closure must capture to keep the binder's type
        /// arguments reachable — the body of an <c>async</c> variant runs inside such a closure, and
        /// the Mechanism D value Closure captures them via <c>use</c>.
        /// </summary>
        private IReadOnlyList<string> BuildVariantCaptureNames() =>
            this._currentVariantGenericParams
                .Select(gp => GenericVariantParamName(gp.Name))
                .ToList();

        /// <summary>
        /// Emits both signatures of a bodyless declaration — an <c>interface</c> member or an
        /// <c>abstract</c> method. The binder has to be part of the contract: a call through the
        /// contract type targets the binder, so every implementation must be required to declare it.
        /// </summary>
        private EmitItem EmitGenericVariantContract(
            PhpMethodDeclAst method,
            EmitItem parent,
            IReadOnlyList<GenericTypeParameterSymbol> variantGenerics)
        {
            var emitType = this.GetMethodEmitType(method);

            var declaration = this.ApplyDocComment(
                method,
                EmitItem.Line(method, emitType, this.BuildWrapperFacingSignature(method) + ";", parent));
            this.AttachAttributes(method, declaration);

            var previous = this._currentVariantGenericParams;
            this._currentVariantGenericParams = variantGenerics;
            try
            {
                var binderLine = EmitItem.Line(
                    method,
                    emitType,
                    this.BuildVariantBinderMethodSignature(method) + ";",
                    parent);
                EmitItem.AttachDocComment(this.BuildVariantBinderDocComment(method), binderLine);
            }
            finally
            {
                this._currentVariantGenericParams = previous;
            }

            return declaration;
        }

        /// <summary>
        /// Emits the delegating wrapper under the declared name followed by the binder that returns
        /// a value-signature Closure. Bodyless declarations go through
        /// <see cref="EmitGenericVariantContract"/> instead.
        /// </summary>
        private EmitItem EmitGenericVariantPair(
            PhpMethodDeclAst method,
            EmitItem parent,
            IReadOnlyList<GenericTypeParameterSymbol> variantGenerics)
        {
            var emitType = this.GetMethodEmitType(method);

            var wrapperSignature = this.BuildWrapperFacingSignature(method);
            var wrapperBlock = this.ApplyDocComment(
                method,
                EmitItem.BlockBraceNextLine(method, emitType, wrapperSignature, "}", parent));
            this.AttachAttributes(method, wrapperBlock);
            this.EmitVariantDelegationBody(method, variantGenerics, wrapperBlock);

            var previous = this._currentVariantGenericParams;
            this._currentVariantGenericParams = variantGenerics;
            try
            {
                this.EmitGenericVariantBinderMethod(method, parent);
            }
            finally
            {
                this._currentVariantGenericParams = previous;
            }

            return wrapperBlock;
        }

        /// <summary>
        /// Free-function counterpart of <see cref="EmitGenericVariantPair(PhpMethodDeclAst, EmitItem, IReadOnlyList{GenericTypeParameterSymbol})"/>.
        /// </summary>
        private EmitItem EmitGenericVariantPair(
            PhpFunctionDeclAst function,
            EmitItem parent,
            IReadOnlyList<GenericTypeParameterSymbol> variantGenerics)
        {
            var wrapperSignature = "function " + this.BuildWrapperFacingSignature(function);
            var wrapperBlock = this.ApplyDocComment(
                function,
                EmitItem.BlockBraceNextLine(function, EmitType.RootStatement, wrapperSignature, "}", parent));
            this.AttachAttributes(function, wrapperBlock);
            this.EmitVariantDelegationBody(function, variantGenerics, wrapperBlock);

            var previous = this._currentVariantGenericParams;
            this._currentVariantGenericParams = variantGenerics;
            try
            {
                this.EmitGenericVariantBinderFunction(function, parent);
            }
            finally
            {
                this._currentVariantGenericParams = previous;
            }

            return wrapperBlock;
        }

        /// <summary>
        /// The signature the wrapper (or bodyless declaration) presents under the declared name. An
        /// <c>async</c> callable's outward-facing form returns a <c>\Tyhp\Promise</c> rather than the
        /// declared type, so both halves of the pair have to be built by the same rule the
        /// non-generic path uses — except the binder half returns <c>\Closure</c>.
        /// </summary>
        private string BuildWrapperFacingSignature(PhpMethodDeclAst method) =>
            this.IsAsyncModifiers(method)
                ? this.BuildAsyncOuterMethodSignature(method)
                : this.BuildMethodSignature(method);

        private string BuildWrapperFacingSignature(PhpFunctionDeclAst function) =>
            this.IsAsyncModifiers(function)
                ? this.BuildAsyncOuterSignature(function)
                : this.BuildFunctionSignature(function);

        /// <summary>
        /// Binder signature: type arguments only, returns <c>\Closure</c>. No return-by-ref on the
        /// binder itself — by-ref lives on the returned Closure.
        /// </summary>
        private string BuildVariantBinderMethodSignature(PhpMethodDeclAst method)
        {
            var modifiers = this.EnsureMethodVisibility(this.FormatModifiers(method.Modifiers));
            modifiers = System.Text.RegularExpressions.Regex.Replace(
                modifiers,
                @"\basync\b",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            modifiers = string.Join(" ", modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (!string.IsNullOrEmpty(modifiers))
            {
                modifiers += " ";
            }

            var name = this.BuildVariantName(method.Identifier);
            var paramsText = string.Join(", ", this.BuildVariantHiddenParameters());
            return $"{modifiers}function {name}({paramsText}): \\Closure";
        }

        private string BuildVariantBinderFunctionSignature(PhpFunctionDeclAst function)
        {
            var name = this.BuildVariantName(function.Identifier);
            var paramsText = string.Join(", ", this.BuildVariantHiddenParameters());
            return $"function {name}({paramsText}): \\Closure";
        }

        /// <summary>
        /// Synthetic docblock for a binder: <c>@param</c> for each type arg and
        /// <c>@return \Closure(...)</c> describing the value signature PHP cannot enforce.
        /// </summary>
        private string BuildVariantBinderDocComment(PhpMethodDeclAst method)
        {
            var lines = new List<string> { "/**" };
            foreach (var gp in this._currentVariantGenericParams)
            {
                lines.Add($" * @param ?{RuntimeTypeClassFq} {GenericVariantParamName(gp.Name)}");
            }

            lines.Add($" * @return {this.BuildVariantClosureReturnDoc(method.Parameters, method.ReturnType, method.ReturnsRef, this.IsAsyncModifiers(method))}");
            lines.Add(" */");
            return string.Join("\n", lines);
        }

        private string BuildVariantBinderDocComment(PhpFunctionDeclAst function)
        {
            var lines = new List<string> { "/**" };
            foreach (var gp in this._currentVariantGenericParams)
            {
                lines.Add($" * @param ?{RuntimeTypeClassFq} {GenericVariantParamName(gp.Name)}");
            }

            lines.Add($" * @return {this.BuildVariantClosureReturnDoc(function.Parameters, function.ReturnType, function.ReturnsRef, this.IsAsyncModifiers(function))}");
            lines.Add(" */");
            return string.Join("\n", lines);
        }

        private string BuildVariantClosureReturnDoc(
            PhpParameterListAst? parameters,
            ITypeExpression? returnType,
            bool returnsRef,
            bool isAsync)
        {
            var paramParts = new List<string>();
            foreach (var parameter in parameters?.GetAllNotNull() ?? [])
            {
                var type = parameter.Type != null
                    ? this.BuildTypeExpression(parameter.Type)
                    : "mixed";
                if (string.IsNullOrWhiteSpace(type))
                {
                    type = "mixed";
                }

                // Include `$name` (and `=` when optional) so Psalm/PHPStan/PhpStorm callable shapes
                // accept named arguments at call sites like `$fn(state: …, value: …)`.
                var name = parameter.Name.TrimStart('$');
                var refMark = parameter.IsRef ? "&" : "";
                var variadic = parameter.IsVariadic ? "..." : "";
                var optional = parameter.DefaultValue != null ? "=" : "";
                paramParts.Add(
                    string.IsNullOrEmpty(name)
                        ? $"{type}{optional}"
                        : $"{type} {refMark}{variadic}${name}{optional}");
            }

            var result = isAsync
                ? "\\Tyhp\\Promise"
                : returnType != null
                    ? this.BuildTypeExpression(returnType)
                    : "mixed";
            if (string.IsNullOrWhiteSpace(result))
            {
                result = "mixed";
            }

            // PHPStan/Psalm-style Closure shape; return-by-ref is not expressible here — the emitted
            // Closure uses `function &(...)` and callers take `&$fn()`.
            _ = returnsRef;
            return $"\\Closure({string.Join(", ", paramParts)}): {result}";
        }

        /// <summary>
        /// The wrapper's body: invoke the binder with type args (null, or a callable-return
        /// inference expression when a value parameter is typed <c>callable&lt;…, T&gt;</c>), then
        /// apply the declared value arguments to the returned Closure. Return-by-ref uses a temporary
        /// so the reference is not broken by a by-value return of a nested call expression.
        /// </summary>
        private void EmitVariantDelegationBody(
            PhpMethodDeclAst method,
            IReadOnlyList<GenericTypeParameterSymbol> variantGenerics,
            EmitItem wrapperBlock)
        {
            var isStatic = method.Modifiers?.Modifiers.Contains(PhpModifier.Static) == true;
            var receiver = isStatic ? "static::" : "$this->";
            var typeArgs = this.BuildWrapperDelegatingTypeArguments(method.Parameters, variantGenerics);
            var binderCall = receiver
                + this.BuildVariantName(method.Identifier)
                + "(" + string.Join(", ", typeArgs) + ")";
            var valueArgs = BuildDeclaredForwardedArguments(method.Parameters);

            if (method.ReturnsRef)
            {
                EmitItem.Line(
                    method,
                    EmitType.FunctionStatement,
                    $"$fn = {binderCall};",
                    wrapperBlock);
                EmitItem.Line(
                    method,
                    EmitType.FunctionStatement,
                    $"return $fn({valueArgs});",
                    wrapperBlock);
                return;
            }

            var invoke = $"{binderCall}({valueArgs})";
            EmitItem.Line(
                method,
                EmitType.FunctionStatement,
                !this.IsAsyncModifiers(method) && ReturnsNoValue(method.ReturnType)
                    ? invoke + ";"
                    : "return " + invoke + ";",
                wrapperBlock);
        }

        private void EmitVariantDelegationBody(
            PhpFunctionDeclAst function,
            IReadOnlyList<GenericTypeParameterSymbol> variantGenerics,
            EmitItem wrapperBlock)
        {
            var typeArgs = this.BuildWrapperDelegatingTypeArguments(function.Parameters, variantGenerics);
            var binderCall = this.BuildVariantName(function.Identifier)
                + "(" + string.Join(", ", typeArgs) + ")";
            var valueArgs = BuildDeclaredForwardedArguments(function.Parameters);

            if (function.ReturnsRef)
            {
                EmitItem.Line(
                    function,
                    EmitType.FunctionStatement,
                    $"$fn = {binderCall};",
                    wrapperBlock);
                EmitItem.Line(
                    function,
                    EmitType.FunctionStatement,
                    $"return $fn({valueArgs});",
                    wrapperBlock);
                return;
            }

            var invoke = $"{binderCall}({valueArgs})";
            EmitItem.Line(
                function,
                EmitType.FunctionStatement,
                !this.IsAsyncModifiers(function) && ReturnsNoValue(function.ReturnType)
                    ? invoke + ";"
                    : "return " + invoke + ";",
                wrapperBlock);
        }

        /// <summary>
        /// Type arguments the declared-name wrapper passes into the binder. Explicit call sites with
        /// <c>&lt;T&gt;</c> go through <see cref="BuildVariantTypeArguments"/> instead; the wrapper
        /// has no type args, so each parameter is either <c>null</c> (prologue default) or
        /// <c>Type::fromCallableReturn($param)</c> when a value parameter's callable return type is
        /// that generic — inference has to happen here because <c>$param</c> is not in scope on the
        /// binder until the value Closure runs, and defaults/inference on the binder must stay above
        /// that Closure.
        /// </summary>
        private IReadOnlyList<string> BuildWrapperDelegatingTypeArguments(
            PhpParameterListAst? parameters,
            IReadOnlyList<GenericTypeParameterSymbol> variantGenerics)
        {
            var parts = new List<string>(variantGenerics.Count);
            foreach (var gp in variantGenerics)
            {
                if (TryFindCallableReturnInferenceParameter(parameters, gp.Name) is { } paramName)
                {
                    this._context.RequirePackage("tyhp/core");
                    parts.Add($"{RuntimeTypeClassFq}::fromCallableReturn({paramName})");
                }
                else
                {
                    parts.Add("null");
                }
            }

            return parts;
        }

        /// <summary>
        /// Finds a value parameter typed as <c>callable&lt;…, T&gt;</c> / <c>Closure&lt;…, T&gt;</c>
        /// whose last type argument names <paramref name="genericName"/> — the PHP return type of
        /// that callable is then a runtime source for <paramref name="genericName"/>.
        /// </summary>
        private static string? TryFindCallableReturnInferenceParameter(
            PhpParameterListAst? parameters,
            string genericName)
        {
            if (parameters is null)
            {
                return null;
            }

            foreach (var parameter in parameters.GetAllNotNull())
            {
                if (string.IsNullOrWhiteSpace(parameter.Name) || parameter.Type is null)
                {
                    continue;
                }

                if (TryGetCallableOrClosureTypeArguments(parameter.Type) is not { Count: > 0 } typeArgs)
                {
                    continue;
                }

                if (string.Equals(
                        Checker.Rules.CheckerHelpers.SoleTypeName(typeArgs[^1]),
                        genericName,
                        StringComparison.Ordinal))
                {
                    return parameter.Name;
                }
            }

            return null;
        }

        private static IReadOnlyList<ITypeExpression>? TryGetCallableOrClosureTypeArguments(
            ITypeExpression type)
        {
            // Unwrap a single-member composite (parameter types often arrive that way).
            while (type is PhpTypeExpressionAst { IsNullable: false, Types: { } members })
            {
                var only = members.GetAllNotNull().ToList();
                if (only.Count != 1 || only[0] is not ITypeExpression inner)
                {
                    break;
                }

                type = inner;
            }

            var isCallableOrClosure = type switch
            {
                PhpBuiltinTypeAst { Identifier: { } id }
                    => string.Equals(id, "callable", StringComparison.OrdinalIgnoreCase),
                PhpNamedTypeAst named => IsClosureTypeSpelling(named),
                // TyhpGenericIdentifierAst subclasses PhpNameAst — derived arm first.
                TyhpGenericIdentifierAst g => IsClosureTypeSpelling(g),
                PhpNameAst name => IsClosureTypeSpelling(name),
                _ => false,
            };

            if (!isCallableOrClosure)
            {
                return null;
            }

            return type is IBase2Ast node ? GetGenericTypeArgumentAddon(node) : null;
        }

        private static bool IsClosureTypeSpelling(IBase2Ast node)
        {
            var text = node switch
            {
                PhpNamedTypeAst { Name: TyhpGenericIdentifierAst g } => g.ValueString,
                PhpNamedTypeAst { Name: PhpNameAst n } => n.ValueString,
                TyhpGenericIdentifierAst g => g.ValueString,
                PhpNameAst n => n.ValueString,
                _ => null,
            };
            var simple = text?.TrimStart('\\');
            return string.Equals(simple, "Closure", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Forwards the declared value parameters positionally into the Closure invoke. A variadic
        /// is spread so it keeps its arity; by-reference parameters forward without special syntax.
        /// </summary>
        private string BuildDeclaredForwardedArguments(PhpParameterListAst? parameters)
        {
            var parts = new List<string>();
            foreach (var parameter in parameters?.GetAllNotNull() ?? [])
            {
                if (string.IsNullOrWhiteSpace(parameter.Name))
                {
                    continue;
                }

                parts.Add(parameter.IsVariadic
                    ? "..." + this.EmitParameterVariableName(parameter.Name)
                    : this.EmitParameterVariableName(parameter.Name));
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Emits the <c>__tyhpGeneric</c> binder method that returns a Closure with the declared
        /// value signature, capturing the type-argument parameters.
        /// </summary>
        private void EmitGenericVariantBinderMethod(PhpMethodDeclAst method, EmitItem parent)
        {
            var emitType = this.GetMethodEmitType(method);

            var binderSig = this.BuildVariantBinderMethodSignature(method);
            var binderBlock = EmitItem.BlockBraceNextLine(method, emitType, binderSig, "}", parent);
            EmitItem.AttachDocComment(this.BuildVariantBinderDocComment(method), binderBlock);
            this.AttachAttributes(method, binderBlock);

            // Resolve type args on the binder itself (before the value Closure) so defaults /
            // inference run once per binder invoke and the Closure captures the settled values.
            this.EmitVariantTypeArgPrologue(method, binderBlock);

            var closureBlock = this.OpenVariantValueClosure(
                method,
                binderBlock,
                method.Parameters,
                method.ReturnType,
                method.ReturnsRef,
                this.IsAsyncModifiers(method));

            if (this.IsAsyncModifiers(method))
            {
                this.EmitAsyncWrappedMethodBody(method, closureBlock);
            }
            else
            {
                var previousReturnCheck = this._currentMethodGenericReturnCheck;
                try
                {
                    this._currentMethodGenericReturnCheck = this.ResolveMethodGenericReturnCheck(
                        method,
                        isConstructor: false);
                    this.EmitFunctionBody(method.Body, closureBlock);
                }
                finally
                {
                    this._currentMethodGenericReturnCheck = previousReturnCheck;
                }
            }
        }

        private void EmitGenericVariantBinderFunction(PhpFunctionDeclAst function, EmitItem parent)
        {
            var binderSig = this.BuildVariantBinderFunctionSignature(function);
            var binderBlock = EmitItem.BlockBraceNextLine(
                function,
                EmitType.RootStatement,
                binderSig,
                "}",
                parent);
            EmitItem.AttachDocComment(this.BuildVariantBinderDocComment(function), binderBlock);
            this.AttachAttributes(function, binderBlock);

            this.EmitVariantTypeArgPrologue(function, binderBlock);

            var closureBlock = this.OpenVariantValueClosure(
                function,
                binderBlock,
                function.Parameters,
                function.ReturnType,
                function.ReturnsRef,
                this.IsAsyncModifiers(function));

            if (this.IsAsyncModifiers(function))
            {
                this.EmitAsyncWrappedBody(function, closureBlock, captureThis: false);
            }
            else
            {
                this.EmitFunctionBody(function.Body, closureBlock);
            }
        }

        /// <summary>
        /// Opens <c>return function [&](...$params) use ($__generic_*)[: R] {</c> inside the binder.
        /// </summary>
        private EmitItem OpenVariantValueClosure(
            IBase2Ast node,
            EmitItem binderBlock,
            PhpParameterListAst? parameters,
            ITypeExpression? returnType,
            bool returnsRef,
            bool isAsync)
        {
            var paramsText = this.FormatParameterList(parameters);
            var captures = this.BuildVariantCaptureNames();
            var useClause = captures.Count > 0
                ? " use (" + string.Join(", ", captures) + ")"
                : "";

            string innerReturn;
            if (isAsync)
            {
                this._context.RequirePackage("tyhp/async");
                innerReturn = ": \\Tyhp\\Promise";
            }
            else if (returnType != null)
            {
                var spelled = this.BuildTypeExpression(returnType);
                innerReturn = string.IsNullOrWhiteSpace(spelled) ? "" : ": " + spelled;
            }
            else
            {
                innerReturn = "";
            }

            var refPrefix = returnsRef ? "&" : "";
            var open = $"return function {refPrefix}({paramsText}){useClause}{innerReturn} {{";
            return EmitItem.Block(node, EmitType.FunctionStatement, open, "};", binderBlock);
        }

        /// <summary>
        /// Resolves unbound type arguments on the binder (above the value Closure): explicit non-null
        /// keeps; otherwise declared default, else mixed. Callable-return inference for omitted type
        /// args is supplied by the declared-name wrapper via
        /// <c>Type::fromCallableReturn($param)</c> so this prologue stays outside the Closure and
        /// still sees a concrete binding (or null → default) before <c>use</c> captures it.
        /// </summary>
        private void EmitVariantTypeArgPrologue(IBase2Ast node, EmitItem binderBlock)
        {
            foreach (var gp in this._currentVariantGenericParams)
            {
                var name = GenericVariantParamName(gp.Name);
                var fallback = gp.DefaultType is { } defaultType
                    ? this.BuildRuntimeTypeExpression(defaultType, preferCtorLocals: false)
                    : $"{RuntimeTypeClassFq}::mixed()";
                EmitItem.Line(
                    node,
                    EmitType.FunctionStatement,
                    $"{name} ??= {fallback};",
                    binderBlock);
            }
        }

        /// <summary>
        /// True when a declared return type forbids returning a value, so the delegating wrapper must
        /// call the binder as a statement.
        /// </summary>
        private static bool ReturnsNoValue(ITypeExpression? returnType)
        {
            if (returnType is null)
            {
                return false;
            }

            var spelled = ExtractSoleTypeName(returnType);
            return string.Equals(spelled, "void", StringComparison.OrdinalIgnoreCase)
                || string.Equals(spelled, "never", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ExtractSoleTypeName(IBase2Ast? typeExpr)
        {
            if (typeExpr is PhpTypeExpressionAst composite)
            {
                if (composite.IsNullable || composite.Types is null)
                {
                    return null;
                }

                var members = composite.Types.GetAllNotNull().ToList();
                return members.Count == 1 && members[0] is ITypeExpression inner
                    ? ExtractSoleTypeName(inner)
                    : null;
            }

            return typeExpr switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNamedTypeAst { Name: PhpNameAst named } => named.ValueString?.TrimStart('\\'),
                PhpNameAst name => name.ValueString?.TrimStart('\\'),
                _ => null,
            };
        }

        /// <summary>
        /// Hidden variant parameters a user-authored classic closure must capture. PHP's
        /// <c>function () {}</c> captures nothing implicitly, while arrow functions inherit the
        /// parent scope. Every binder generic is captured into every nested classic closure —
        /// detecting only <c>typeof</c>/<c>default</c>/<c>instanceof</c> misses type-argument
        /// sites such as <c>new Foo&lt;T&gt;()</c> that still emit <c>$__generic_T</c>.
        /// </summary>
        private IReadOnlyList<string> CollectVariantCapturesFor(PhpInlineFunctionAst closure)
        {
            if (this._currentVariantGenericParams.Count == 0 || closure.IsArrowFunction)
            {
                return Array.Empty<string>();
            }

            return this.BuildVariantCaptureNames();
        }

        /// <summary>
        /// True when <paramref name="simpleName"/> names a generic parameter of the binder currently
        /// being emitted, meaning <c>typeof</c>/<c>default</c> must read the hidden parameter.
        /// </summary>
        private bool IsVariantGenericParamName(string simpleName) =>
            this._currentVariantGenericParams.Any(gp =>
                string.Equals(gp.Name, simpleName, StringComparison.Ordinal));

        /// <summary>
        /// <c>typeof(T)</c> for a binder generic: the bound <c>\Tyhp\Type</c> from the captured
        /// parameter. The binder prologue (<c>$__generic_T ??= …</c>) settles nulls before the value
        /// Closure, so no <c>?? mixed()</c> inside the body.
        /// </summary>
        private string BuildVariantTypeofLookup(string simpleName) =>
            GenericVariantParamName(simpleName);

        /// <summary>
        /// <c>default(T)</c> for a binder generic: zero value of the bound type. Same settled-local
        /// assumption as <see cref="BuildVariantTypeofLookup"/> — no nullsafe call.
        /// </summary>
        private string BuildVariantDefaultLookup(string simpleName) =>
            $"{GenericVariantParamName(simpleName)}->defaultValue()";

        /// <summary>
        /// The callee name node whose emitted name currently needs the <c>__tyhpGeneric</c> suffix.
        /// Set while <see cref="BuildDereferenceableExpression"/> builds the base of a call it is
        /// routing to the binder, because the callee name is emitted one level below the call.
        /// </summary>
        private IBase2Ast? _pendingVariantCallName;

        /// <summary>
        /// Rewrites a call to a Mechanism D callable so it reaches the binder: the callee name gains
        /// the suffix, type arguments bind first, and declared value arguments apply to the returned
        /// Closure (<c>binder(types...)(values...)</c>). Returns null when the call needs no rewrite.
        /// </summary>
        private string? TryBuildGenericVariantCall(PhpDereferenceableAst dereferenceable, PhpCallAst call)
        {
            if (!this._context.GenericCallTargets.TryGetValue(call, out var callee)
                || !this._context.RequiresGenericVariantFor(callee))
            {
                return null;
            }

            var genericParams = callee switch
            {
                ObjectMethodSymbol method => method.GenericParameters,
                FunctionDeclarationSymbol function => function.GenericParameters,
                _ => (IReadOnlyList<GenericTypeParameterSymbol>)Array.Empty<GenericTypeParameterSymbol>(),
            };

            var nameNode = FindCalleeNameNode(dereferenceable.Base);
            if (nameNode is null)
            {
                return null;
            }

            var typeArgs = TryGetCallSiteTypeArguments(dereferenceable.Base);
            var leadingArgs = this.BuildVariantTypeArguments(genericParams, typeArgs);

            var previous = this._pendingVariantCallName;
            this._pendingVariantCallName = nameNode;
            string baseText;
            try
            {
                baseText = this.BuildDereferenceableBase(dereferenceable.Base);
            }
            finally
            {
                this._pendingVariantCallName = previous;
            }

            var declaredArgs = this.FormatArgumentList(call.Arguments);
            var typeArgsList = string.Join(", ", leadingArgs);
            return baseText + "(" + typeArgsList + ")(" + declaredArgs + ")";
        }

        /// <summary>
        /// One <c>\Tyhp\Type</c> expression per declared type parameter, in declaration order. A
        /// parameter the call site left out falls back to its declared default type, then to null,
        /// which the Closure prologue reads as unbound.
        /// </summary>
        private IReadOnlyList<string> BuildVariantTypeArguments(
            IReadOnlyList<GenericTypeParameterSymbol> genericParams,
            IReadOnlyList<ITypeExpression>? typeArgs)
        {
            var parts = new List<string>(genericParams.Count);
            for (var i = 0; i < genericParams.Count; i++)
            {
                var typeArg = typeArgs is not null && i < typeArgs.Count
                    ? typeArgs[i]
                    : genericParams[i].DefaultType;

                parts.Add(typeArg is null
                    ? "null"
                    : this.BuildRuntimeTypeExpression(typeArg, preferCtorLocals: false));
            }

            return parts;
        }

        /// <summary>
        /// The node holding the callee's name: the bare name for a free function, or the member name
        /// on the access that precedes the argument list for a method.
        /// </summary>
        private static IBase2Ast? FindCalleeNameNode(IDereferenceableBase? baseNode) =>
            baseNode switch
            {
                PhpNameAst name => name,
                PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst instance } =>
                    instance.MemberName,
                PhpDereferenceableAst { Suffix: PhpStaticMemberAccessAst staticAccess } =>
                    staticAccess.Member,
                PhpDereferenceableAst { Suffix: PhpClassConstantAccessAst classConst } =>
                    classConst.Member,
                _ => null,
            };

        /// <summary>
        /// Call-site type arguments hang off the callee name rather than the argument list: a free
        /// function carries them under the <c>identifier</c> addon, a <c>::</c>/<c>-&gt;</c> member
        /// under <c>memberName</c>.
        /// </summary>
        private static IReadOnlyList<ITypeExpression>? TryGetCallSiteTypeArguments(
            IDereferenceableBase? baseNode)
        {
            if (FindCalleeNameNode(baseNode) is not { } nameNode)
            {
                return null;
            }

            foreach (var key in new[] { "memberName", "identifier" })
            {
                if (nameNode.AstGrammarAddons.TryGetValue(key, out var addon)
                    && addon is PhpTypeExpressionListAst list)
                {
                    return list.GetAllNotNull().ToList();
                }
            }

            return null;
        }

        /// <summary>
        /// True when <paramref name="node"/> is the callee name of the call being routed to a binder,
        /// so its emitted name takes the suffix.
        /// </summary>
        private string ApplyPendingVariantCallSuffix(IBase2Ast? node, string emittedName) =>
            this._pendingVariantCallName is not null && ReferenceEquals(this._pendingVariantCallName, node)
                ? emittedName + GenericVariantSuffix
                : emittedName;
    }
}
