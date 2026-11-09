using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Mechanism D — Closure binders for function and method generics.
    ///
    /// A class generic is recorded on the instance and read back through the <c>GenericObject</c>
    /// trait, which needs <c>$this</c>. A generic declared by a function or method itself has no
    /// such home. A non-erased callable is emitted as one method under its declared name. That
    /// method is the binder: type arguments only, returning a <c>\Closure</c> with the author's
    /// value signature (<c>Json::decode($T)($json)</c>). <c>#[\Tyhp\InferGeneric]</c> keeps the
    /// value signature under the declared name and emits <c>name__tyhpGeneric</c> as the companion.
    /// Layout-1 libraries are still called as <c>name__tyhpGeneric(types)(args)</c>.
    /// </summary>
    public partial class TyhpEmitter
    {
        internal const string GenericVariantSuffix = GeneratedNames.GenericVariantSuffix;

        private static string GenericVariantParamName(string genericName) =>
            "$" + genericName;

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
        /// One binder parameter per type parameter, named <c>$T</c>, typed
        /// <c>\Tyhp\TypeLike|string|null</c>, default <c>null</c>. The binder normalizes each
        /// with <c>\Tyhp\Type::arg</c>. A null stays null so the value Closure can infer it.
        /// </summary>
        private IReadOnlyList<string> BuildVariantHiddenParameters()
        {
            if (this._currentVariantGenericParams.Count == 0)
            {
                return Array.Empty<string>();
            }

            this._context.RequirePackage("tyhp/core");
            return this._currentVariantGenericParams
                .Select(gp => $"\\Tyhp\\TypeLike|string|null {GenericVariantParamName(gp.Name)} = null")
                .ToList();
        }

        /// <summary>The binder's name for a declared identifier, with generics stripped.</summary>
        private string BuildVariantName(string? identifier) =>
            this.StripGenericsFromName(identifier) + GenericVariantSuffix;

        /// <summary>
        /// The emitted name for a declaration. Layout 2 keeps the declared name on the binder.
        /// <see cref="BuildVariantName"/> remains for a layout-1 companion.
        /// </summary>
        private string ApplyVariantNaming(string? identifier) =>
            this.StripGenericsFromName(identifier);

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
        /// Emits the binder signature of a bodyless declaration — an <c>interface</c> member or an
        /// <c>abstract</c> method. Implementors declare that one method. An
        /// <c>#[\Tyhp\InferGeneric]</c> contract declares the value signature and the companion.
        /// </summary>
        private EmitItem EmitGenericVariantContract(
            PhpMethodDeclAst method,
            EmitItem parent,
            IReadOnlyList<GenericTypeParameterSymbol> variantGenerics)
        {
            var emitType = this.GetMethodEmitType(method);
            var previous = this._currentVariantGenericParams;
            var previousCompanion = this._inferGenericCompanionName;
            this._currentVariantGenericParams = variantGenerics;
            var infer = InferGenericAttributeSupport.HasInferGeneric(method);
            try
            {
                EmitItem? valueLine = null;
                if (infer)
                {
                    valueLine = this.ApplyDocComment(
                        method,
                        EmitItem.Line(
                            method,
                            emitType,
                            this.BuildInferGenericValueSignature(method) + ";",
                            parent));
                    this.AttachAttributes(method, valueLine);
                    this._inferGenericCompanionName = true;
                }

                var binderLine = EmitItem.Line(
                    method,
                    emitType,
                    this.BuildVariantBinderMethodSignature(method) + ";",
                    parent);
                EmitItem.AttachDocComment(this.BuildVariantBinderDocComment(method), binderLine);
                this.AttachAttributes(method, binderLine);
                return valueLine ?? binderLine;
            }
            finally
            {
                this._currentVariantGenericParams = previous;
                this._inferGenericCompanionName = previousCompanion;
            }
        }

        /// <summary>
        /// Emits the binder under the declared name. <c>#[\Tyhp\InferGeneric]</c> emits the
        /// value signature under that name and the binder as <c>name__tyhpGeneric</c>.
        /// Bodyless declarations go through <see cref="EmitGenericVariantContract"/> instead.
        /// </summary>
        private EmitItem EmitGenericVariantPair(
            PhpMethodDeclAst method,
            EmitItem parent,
            IReadOnlyList<GenericTypeParameterSymbol> variantGenerics)
        {
            var previous = this._currentVariantGenericParams;
            var previousCompanion = this._inferGenericCompanionName;
            var previousSuppress = this._suppressCompanionStamp;
            this._currentVariantGenericParams = variantGenerics;
            var infer = InferGenericAttributeSupport.HasInferGeneric(method);
            try
            {
                if (infer)
                {
                    this.EmitInferGenericWrapper(method, parent);
                    this._inferGenericCompanionName = true;
                    this._suppressCompanionStamp = true;
                }

                return this.EmitGenericVariantBinderMethod(method, parent);
            }
            finally
            {
                this._currentVariantGenericParams = previous;
                this._inferGenericCompanionName = previousCompanion;
                this._suppressCompanionStamp = previousSuppress;
            }
        }

        /// <summary>
        /// Free-function counterpart of <see cref="EmitGenericVariantPair(PhpMethodDeclAst, EmitItem, IReadOnlyList{GenericTypeParameterSymbol})"/>.
        /// </summary>
        private EmitItem EmitGenericVariantPair(
            PhpFunctionDeclAst function,
            EmitItem parent,
            IReadOnlyList<GenericTypeParameterSymbol> variantGenerics)
        {
            var previous = this._currentVariantGenericParams;
            var previousCompanion = this._inferGenericCompanionName;
            var previousSuppress = this._suppressCompanionStamp;
            this._currentVariantGenericParams = variantGenerics;
            var infer = InferGenericAttributeSupport.HasInferGeneric(function);
            try
            {
                if (infer)
                {
                    this.EmitInferGenericWrapper(function, parent);
                    this._inferGenericCompanionName = true;
                    this._suppressCompanionStamp = true;
                }

                return this.EmitGenericVariantBinderFunction(function, parent);
            }
            finally
            {
                this._currentVariantGenericParams = previous;
                this._inferGenericCompanionName = previousCompanion;
                this._suppressCompanionStamp = previousSuppress;
            }
        }

        /// <summary>
        /// Extension-method Mechanism D pair: same Closure binder as a free function, emitted as
        /// <c>public static</c> members of the extension backer class. Delegation uses
        /// <c>static::</c> so a subclass of the backer (unusual, but consistent with static methods)
        /// still reaches the binder.
        /// </summary>
        private void EmitExtensionGenericVariantPair(
            PhpFunctionDeclAst function,
            EmitItem parent,
            IReadOnlyList<GenericTypeParameterSymbol> variantGenerics)
        {
            var previous = this._currentVariantGenericParams;
            var previousCompanion = this._inferGenericCompanionName;
            var previousSuppress = this._suppressCompanionStamp;
            this._currentVariantGenericParams = variantGenerics;
            var infer = InferGenericAttributeSupport.HasInferGeneric(function);
            try
            {
                if (infer)
                {
                    this.EmitInferGenericExtensionWrapper(function, parent);
                    this._inferGenericCompanionName = true;
                    this._suppressCompanionStamp = true;
                }

                this.EmitGenericVariantBinderExtensionMethod(function, parent);
            }
            finally
            {
                this._currentVariantGenericParams = previous;
                this._inferGenericCompanionName = previousCompanion;
                this._suppressCompanionStamp = previousSuppress;
            }
        }

        private void EmitExtensionVariantDelegationBody(
            PhpFunctionDeclAst function,
            IReadOnlyList<GenericTypeParameterSymbol> variantGenerics,
            EmitItem wrapperBlock)
        {
            var typeArgs = this.BuildWrapperDelegatingTypeArguments(function.Parameters, variantGenerics);
            var binderCall = "static::"
                + this.BuildVariantName(function.Identifier)
                + "(" + string.Join(", ", typeArgs) + ")";
            var valueArgs = this.BuildDeclaredForwardedArguments(function.Parameters);

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

        private void EmitGenericVariantBinderExtensionMethod(PhpFunctionDeclAst function, EmitItem parent)
        {
            var binderSig = "public static " + this.BuildVariantBinderFunctionSignature(function);
            var binderBlock = EmitItem.BlockBraceNextLine(
                function,
                EmitType.ObjectStaticMethods,
                binderSig,
                "}",
                parent);
            EmitItem.AttachDocComment(this.BuildVariantBinderDocComment(function), binderBlock);
            this.AttachVariantBinderAttributes(function, binderBlock);

            this.EmitVariantTypeArgPrologue(function, binderBlock);

            var closureBlock = this.OpenVariantValueClosure(
                function,
                binderBlock,
                function.Parameters,
                function.ReturnType,
                function.ReturnsRef,
                this.IsAsyncModifiers(function));
            this.EmitVariantClosureInference(function, closureBlock, function.Parameters);

            if (this.IsAsyncModifiers(function))
            {
                this.EmitAsyncWrappedBody(function, closureBlock, captureThis: false);
            }
            else
            {
                this.EmitClosureTreePrologue(function.Parameters, function, closureBlock);
                this.EmitFunctionBody(function.Body, closureBlock);
            }
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

            var name = this.VariantBinderDeclaredName(method.Identifier);
            var paramsText = string.Join(", ", this.BuildVariantHiddenParameters());
            return $"{modifiers}function {name}({paramsText}): \\Closure";
        }

        private string BuildVariantBinderFunctionSignature(PhpFunctionDeclAst function)
        {
            var name = this.VariantBinderDeclaredName(function.Identifier);
            var paramsText = string.Join(", ", this.BuildVariantHiddenParameters());
            return $"function {name}({paramsText}): \\Closure";
        }

        /// <summary>
        /// Binder docblock: the two-step call, the author's docblock, then <c>@template</c>,
        /// <c>@param</c>, and <c>@return \Closure(...)</c>.
        /// </summary>
        private string BuildVariantBinderDocComment(PhpMethodDeclAst method)
        {
            var lines = new List<string> { "/**" };
            lines.Add(" * " + this.BuildVariantTwoStepCall(
                this.VariantMethodCallHead(method),
                method.Parameters));
            AppendAuthorDocBody(method.DocComment, lines);
            this.AppendVariantBinderDocTags(
                lines,
                method.Parameters,
                method.ReturnType,
                method.ReturnsRef,
                this.IsAsyncModifiers(method));
            lines.Add(" */");
            return string.Join("\n", lines);
        }

        private string BuildVariantBinderDocComment(PhpFunctionDeclAst function)
        {
            var lines = new List<string> { "/**" };
            lines.Add(" * " + this.BuildVariantTwoStepCall(
                this.VariantBinderDeclaredName(function.Identifier),
                function.Parameters));
            AppendAuthorDocBody(function.DocComment, lines);
            this.AppendVariantBinderDocTags(
                lines,
                function.Parameters,
                function.ReturnType,
                function.ReturnsRef,
                this.IsAsyncModifiers(function));
            lines.Add(" */");
            return string.Join("\n", lines);
        }

        private void AppendVariantBinderDocTags(
            List<string> lines,
            PhpParameterListAst? parameters,
            ITypeExpression? returnType,
            bool returnsRef,
            bool isAsync)
        {
            foreach (var gp in this._currentVariantGenericParams)
            {
                lines.Add($" * @template {gp.Name}");
            }

            foreach (var gp in this._currentVariantGenericParams)
            {
                lines.Add(
                    $" * @param \\Tyhp\\Type<{gp.Name}>|\\Tyhp\\BuiltIn|class-string<{gp.Name}>|null {GenericVariantParamName(gp.Name)}");
            }

            lines.Add($" * @return {this.BuildVariantClosureReturnDoc(parameters, returnType, returnsRef, isAsync)}");
        }

        /// <summary>
        /// Declared name, or <c>name__tyhpGeneric</c> while the InferGeneric companion is emitted.
        /// </summary>
        private string VariantBinderDeclaredName(string? identifier) =>
            this._inferGenericCompanionName
                ? this.BuildVariantName(identifier)
                : this.StripGenericsFromName(identifier);

        private string VariantMethodCallHead(PhpMethodDeclAst method)
        {
            var name = this.VariantBinderDeclaredName(method.Identifier);
            var isStatic = method.Modifiers?.Modifiers.Contains(PhpModifier.Static) == true;
            if (!isStatic)
            {
                return "$this->" + name;
            }

            var className = "static";
            if (method.BoundSymbol is ObjectMethodSymbol symbol)
            {
                for (var scope = symbol.ContainingScope; scope is not null; scope = scope.ParentScope)
                {
                    if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj
                        && !string.IsNullOrEmpty(obj.Name))
                    {
                        className = obj.Name;
                        break;
                    }
                }
            }

            return className + "::" + name;
        }

        private string BuildVariantTwoStepCall(string head, PhpParameterListAst? parameters)
        {
            var typeArgs = string.Join(
                ", ",
                this._currentVariantGenericParams.Select(gp => "$" + gp.Name));
            var valueArgs = new List<string>();
            foreach (var parameter in parameters?.GetAllNotNull() ?? [])
            {
                if (string.IsNullOrWhiteSpace(parameter.Name))
                {
                    continue;
                }

                var name = parameter.Name.StartsWith('$') ? parameter.Name : "$" + parameter.Name;
                valueArgs.Add(parameter.IsVariadic ? "..." + name : name);
            }

            return head + "(" + typeArgs + ")(" + string.Join(", ", valueArgs) + ")";
        }

        private static void AppendAuthorDocBody(string? doc, List<string> lines)
        {
            if (string.IsNullOrWhiteSpace(doc))
            {
                return;
            }

            var text = doc.Trim();
            if (text.StartsWith("/**", StringComparison.Ordinal))
            {
                text = text[3..];
            }

            if (text.EndsWith("*/", StringComparison.Ordinal))
            {
                text = text[..^2];
            }

            var any = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith('*'))
                {
                    line = line[1..].TrimStart();
                }

                if (line.Length == 0 && !any)
                {
                    continue;
                }

                lines.Add(" * " + line);
                any = true;
            }

            if (any)
            {
                lines.Add(" *");
            }
        }

        private string BuildVariantClosureReturnDoc(
            PhpParameterListAst? parameters,
            ITypeExpression? returnType,
            bool returnsRef,
            bool isAsync)
        {
            var paramParts = new List<string>();
            if (!string.IsNullOrEmpty(this._extensionReceiverParameterText))
            {
                paramParts.Add(this._extensionReceiverParameterText);
            }

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

        private void EmitInferGenericWrapper(PhpMethodDeclAst method, EmitItem parent)
        {
            var emitType = this.GetMethodEmitType(method);
            var block = this.ApplyDocComment(
                method,
                EmitItem.BlockBraceNextLine(
                    method,
                    emitType,
                    this.BuildInferGenericValueSignature(method),
                    "}",
                    parent));
            this.AttachVariantBinderAttributes(method, block);
            this.EmitVariantDelegationBody(method, this._currentVariantGenericParams, block);
        }

        private void EmitInferGenericWrapper(PhpFunctionDeclAst function, EmitItem parent)
        {
            var signature = "function " + this.BuildInferGenericValueSignature(function);
            var block = this.ApplyDocComment(
                function,
                EmitItem.BlockBraceNextLine(function, EmitType.RootStatement, signature, "}", parent));
            this.AttachVariantBinderAttributes(function, block);
            this.EmitVariantDelegationBody(function, this._currentVariantGenericParams, block);
        }

        private void EmitInferGenericExtensionWrapper(PhpFunctionDeclAst function, EmitItem parent)
        {
            var signature = "public static function " + this.BuildInferGenericValueSignature(function);
            var block = this.ApplyDocComment(
                function,
                EmitItem.BlockBraceNextLine(
                    function,
                    EmitType.ObjectStaticMethods,
                    signature,
                    "}",
                    parent));
            this.AttachVariantBinderAttributes(function, block);
            this.EmitExtensionVariantDelegationBody(function, this._currentVariantGenericParams, block);
        }

        /// <summary>
        /// The declared value signature. Building it while variant parameters are current would
        /// replace the value parameters with the companion's type arguments.
        /// </summary>
        private string BuildInferGenericValueSignature(PhpMethodDeclAst method)
        {
            var saved = this._currentVariantGenericParams;
            this._currentVariantGenericParams = Array.Empty<GenericTypeParameterSymbol>();
            try
            {
                return this.BuildWrapperFacingSignature(method);
            }
            finally
            {
                this._currentVariantGenericParams = saved;
            }
        }

        private string BuildInferGenericValueSignature(PhpFunctionDeclAst function)
        {
            var saved = this._currentVariantGenericParams;
            this._currentVariantGenericParams = Array.Empty<GenericTypeParameterSymbol>();
            try
            {
                return this.BuildWrapperFacingSignature(function);
            }
            finally
            {
                this._currentVariantGenericParams = saved;
            }
        }

        /// <summary>
        /// The wrapper's body: invoke the binder with type args (null, or a callable-return
        /// inference expression when a value parameter is typed <c>callable(...): T</c>), then
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
                var sources = this.CollectVariantInferenceSources(parameters, gp.Name);
                if (sources.Count == 1
                    && sources[0].Kind == VariantInferenceKind.CallableReturn
                    && !sources[0].IsVariadic)
                {
                    this._context.RequirePackage("tyhp/core");
                    var paramName = sources[0].ParameterName;
                    parts.Add(sources[0].AllowsNull
                        ? $"({paramName} !== null ? {RuntimeTypeClassFq}::fromCallableReturn({paramName}) : null)"
                        : $"{RuntimeTypeClassFq}::fromCallableReturn({paramName})");
                }
                else
                {
                    // Several sources, a promise, a class-string, or a value: pass null and let
                    // the companion Closure union them. A single expression here would drop the rest.
                    parts.Add("null");
                }
            }

            return parts;
        }

        /// <summary>
        /// Return type of a <c>callable(…): R</c> shape. For <c>\Closure&lt;C&gt;</c>, <c>C</c>
        /// is the callable shape — not Closure's last class type argument.
        /// </summary>
        private static bool TryGetCallableReturnLastTypeArgument(
            ITypeExpression type,
            out ITypeExpression? returnArg)
        {
            if (TyhpCallableShapeAst.Find(type) is { } shape)
            {
                returnArg = shape.ReturnType;
                return returnArg is not null;
            }

            returnArg = null;
            if (TryGetCallableOrClosureTypeArguments(type) is not { Count: > 0 } typeArgs)
            {
                return false;
            }

            if (IsClosureTypeExpression(type))
            {
                return TryGetCallableReturnLastTypeArgument(typeArgs[0], out returnArg);
            }

            returnArg = typeArgs[^1];
            return true;
        }

        private static bool IsClosureTypeExpression(ITypeExpression type)
        {
            while (type is PhpTypeExpressionAst { IsNullable: false, Types: { } members })
            {
                var only = members.GetAllNotNull().ToList();
                if (only.Count != 1 || only[0] is not ITypeExpression inner)
                {
                    break;
                }

                type = inner;
            }

            return type switch
            {
                PhpNamedTypeAst named => IsClosureTypeSpelling(named),
                TyhpGenericIdentifierAst g => IsClosureTypeSpelling(g),
                PhpNameAst name => IsClosureTypeSpelling(name),
                _ => false,
            };
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
            if (!string.IsNullOrEmpty(this._extensionReceiverParameterText)
                && this._context.ExtensionReceiverThisAlias is { } receiverAlias)
            {
                parts.Add(receiverAlias);
            }

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
        /// Emits the binder under the declared name. It returns a Closure with the value signature.
        /// </summary>
        private EmitItem EmitGenericVariantBinderMethod(PhpMethodDeclAst method, EmitItem parent)
        {
            var emitType = this.GetMethodEmitType(method);

            var binderSig = this.BuildVariantBinderMethodSignature(method);
            var binderBlock = EmitItem.BlockBraceNextLine(method, emitType, binderSig, "}", parent);
            EmitItem.AttachDocComment(this.BuildVariantBinderDocComment(method), binderBlock);
            this.AttachVariantBinderAttributes(method, binderBlock);

            this.EmitVariantTypeArgPrologue(method, binderBlock);

            var closureBlock = this.OpenVariantValueClosure(
                method,
                binderBlock,
                method.Parameters,
                method.ReturnType,
                method.ReturnsRef,
                this.IsAsyncModifiers(method));
            this.EmitVariantClosureInference(method, closureBlock, method.Parameters);

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
                    this.EmitMethodBody(method, closureBlock);
                }
                finally
                {
                    this._currentMethodGenericReturnCheck = previousReturnCheck;
                }
            }

            return binderBlock;
        }

        private EmitItem EmitGenericVariantBinderFunction(PhpFunctionDeclAst function, EmitItem parent)
        {
            var binderSig = this.BuildVariantBinderFunctionSignature(function);
            var binderBlock = EmitItem.BlockBraceNextLine(
                function,
                EmitType.RootStatement,
                binderSig,
                "}",
                parent);
            EmitItem.AttachDocComment(this.BuildVariantBinderDocComment(function), binderBlock);
            this.AttachVariantBinderAttributes(function, binderBlock);

            this.EmitVariantTypeArgPrologue(function, binderBlock);

            var closureBlock = this.OpenVariantValueClosure(
                function,
                binderBlock,
                function.Parameters,
                function.ReturnType,
                function.ReturnsRef,
                this.IsAsyncModifiers(function));
            this.EmitVariantClosureInference(function, closureBlock, function.Parameters);

            if (this.IsAsyncModifiers(function))
            {
                this.EmitAsyncWrappedBody(function, closureBlock, captureThis: false);
            }
            else
            {
                this.EmitClosureTreePrologue(function.Parameters, function, closureBlock);
                this.EmitFunctionBody(function.Body, closureBlock);
            }

            return binderBlock;
        }

        /// <summary>
        /// Opens <c>return function [&](...$params) use ($T, ...)[: R] {</c> inside the binder.
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

        private void AttachVariantBinderAttributes(IBase2Ast source, EmitItem target)
        {
            if (this._suppressCompanionStamp)
            {
                this.AttachAttributes(source, target);
                return;
            }

            var previous = this._allowVariantBinderStamp;
            this._allowVariantBinderStamp = true;
            try
            {
                this.AttachAttributes(source, target);
            }
            finally
            {
                this._allowVariantBinderStamp = previous;
            }
        }

        /// <summary>
        /// Normalizes each binder argument with <c>\Tyhp\Type::arg</c>. Null stays null; the value
        /// Closure infers it on every call.
        /// </summary>
        private void EmitVariantTypeArgPrologue(IBase2Ast node, EmitItem binderBlock)
        {
            this._context.RequirePackage("tyhp/core");
            foreach (var gp in this._currentVariantGenericParams)
            {
                var name = GenericVariantParamName(gp.Name);
                EmitItem.Line(
                    node,
                    EmitType.FunctionStatement,
                    $"{name} = {RuntimeTypeClassFq}::arg({name});",
                    binderBlock);
            }
        }

        /// <summary>
        /// When a captured type argument is null, infer it from this call's value arguments.
        /// Method-level parameters only. No applicable source uses the declared default, or
        /// <c>Type::mixed()</c> when the parameter declares none.
        /// </summary>
        private void EmitVariantClosureInference(
            IBase2Ast node,
            EmitItem closureBlock,
            PhpParameterListAst? parameters)
        {
            foreach (var gp in this._currentVariantGenericParams)
            {
                var name = GenericVariantParamName(gp.Name);
                var guard = EmitItem.Block(
                    node,
                    EmitType.FunctionStatement,
                    $"if ({name} === null) {{",
                    "}",
                    closureBlock);
                var sources = this.CollectVariantInferenceSources(parameters, gp.Name);
                var fallback = this.VariantUnboundFallback(gp);
                if (sources.Count == 0)
                {
                    EmitItem.Line(
                        node,
                        EmitType.FunctionStatement,
                        $"{name} = {fallback};",
                        guard);
                    continue;
                }

                var infer = "$__tyhpInfer_" + gp.Name;
                EmitItem.Line(node, EmitType.FunctionStatement, $"{infer} = null;", guard);
                for (var index = 0; index < sources.Count; index++)
                {
                    this.EmitVariantInferenceSource(node, guard, infer, sources[index], gp.Name, index);
                }

                EmitItem.Line(
                    node,
                    EmitType.FunctionStatement,
                    $"{name} = {infer} ?? {fallback};",
                    guard);
            }
        }

        private string VariantUnboundFallback(GenericTypeParameterSymbol generic)
        {
            this._context.RequirePackage("tyhp/core");
            return generic.DefaultType is { } defaultType
                ? this.BuildRuntimeTypeExpression(defaultType, preferCtorLocals: false)
                : $"{RuntimeTypeClassFq}::mixed()";
        }

        private void EmitVariantInferenceSource(
            IBase2Ast node,
            EmitItem guard,
            string infer,
            VariantInferenceSource source,
            string genericName,
            int index)
        {
            var param = source.ParameterName;
            var target = guard;

            // A variadic parameter holds the arguments as an array. Each argument is a source.
            if (source.IsVariadic)
            {
                var item = "$__tyhpItem_" + genericName + "_" + index;
                target = EmitItem.Block(
                    node,
                    EmitType.FunctionStatement,
                    $"foreach ({param} as {item}) {{",
                    "}",
                    guard);
                param = item;
            }

            switch (source.Kind)
            {
                case VariantInferenceKind.Promise:
                {
                    this.EmitPromiseResultInference(node, target, infer, param, genericName, index);
                    break;
                }

                case VariantInferenceKind.ValueOrPromise:
                {
                    // `null|T|Promise<T>`: a promise gives its result type, any other value its own type.
                    var promiseClass = this.PromiseArmClassName(source.PromiseArm);
                    var isPromise = EmitItem.Block(
                        node,
                        EmitType.FunctionStatement,
                        $"if ({param} instanceof {promiseClass}) {{",
                        "}",
                        target);
                    this.EmitPromiseResultInference(node, isPromise, infer, param, genericName, index);

                    var isValue = EmitItem.Block(
                        node,
                        EmitType.FunctionStatement,
                        $"if ({param} !== null && !({param} instanceof {promiseClass})) {{",
                        "}",
                        target);
                    this.EmitVariantInferenceUnion(
                        node,
                        isValue,
                        infer,
                        $"{RuntimeTypeClassFq}::of({param})");
                    break;
                }

                case VariantInferenceKind.PromiseElements:
                {
                    target = EmitItem.Block(
                        node,
                        EmitType.FunctionStatement,
                        $"if ({param} !== null) {{",
                        "}",
                        target);
                    var element = "$__tyhpPromise_" + genericName + "_" + index;
                    var loop = EmitItem.Block(
                        node,
                        EmitType.FunctionStatement,
                        $"foreach ({param} as {element}) {{",
                        "}",
                        target);
                    this.EmitPromiseResultInference(node, loop, infer, element, genericName, index);
                    break;
                }

                case VariantInferenceKind.CallableReturn:
                case VariantInferenceKind.ClassName:
                {
                    // `fromCallableReturn` and `fromClassName` do not accept null.
                    if (source.AllowsNull)
                    {
                        target = EmitItem.Block(
                            node,
                            EmitType.FunctionStatement,
                            $"if ({param} !== null) {{",
                            "}",
                            target);
                    }

                    if (source.Kind == VariantInferenceKind.ClassName)
                    {
                        this.EmitVariantInferenceUnion(
                            node,
                            target,
                            infer,
                            $"{RuntimeTypeClassFq}::fromClassName({param})");
                        break;
                    }

                    var temp = "$__tyhpCall_" + genericName + "_" + index;
                    EmitItem.Line(
                        node,
                        EmitType.FunctionStatement,
                        $"{temp} = {RuntimeTypeClassFq}::fromCallableReturn({param});",
                        target);
                    var present = EmitItem.Block(
                        node,
                        EmitType.FunctionStatement,
                        $"if ({temp} !== null) {{",
                        "}",
                        target);
                    this.EmitVariantInferenceUnion(node, present, infer, temp);
                    break;
                }

                default:
                {
                    var present = EmitItem.Block(
                        node,
                        EmitType.FunctionStatement,
                        $"if ({param} !== null) {{",
                        "}",
                        target);
                    this.EmitVariantInferenceUnion(
                        node,
                        present,
                        infer,
                        $"{RuntimeTypeClassFq}::of({param})");
                    break;
                }
            }
        }

        /// <summary>
        /// Fully qualified PHP name of the <c>Promise&lt;T&gt;</c> arm of a value-or-promise union, for
        /// <c>instanceof</c>.
        /// </summary>
        private string PromiseArmClassName(ITypeExpression? arm)
        {
            var type = arm is null ? null : UnwrapNullableUnion(arm);
            var resolved = type switch
            {
                PhpNamedTypeAst { Name: TyhpGenericIdentifierAst generic } named =>
                    this.ResolveRuntimeClassName(
                        named.BoundSymbol ?? generic.BoundSymbol,
                        generic,
                        generic.ValueString ?? generic.Identifier),
                PhpNamedTypeAst { Name: PhpNameAst name } named =>
                    this.ResolveRuntimeClassName(
                        named.BoundSymbol ?? name.BoundSymbol,
                        name,
                        name.ValueString ?? name.Identifier),
                TyhpGenericIdentifierAst generic =>
                    this.ResolveRuntimeClassName(generic.BoundSymbol, generic, generic.ValueString ?? generic.Identifier),
                _ => null,
            };

            return "\\" + (string.IsNullOrWhiteSpace(resolved) ? "Tyhp\\Promise" : resolved.TrimStart('\\'));
        }

        /// <summary>
        /// <c>Type::of</c> on a promise is <c>Promise&lt;T&gt;</c>. The type parameter is that
        /// object's first type argument. A null promise is skipped and does not throw.
        /// </summary>
        private void EmitPromiseResultInference(
            IBase2Ast node,
            EmitItem target,
            string infer,
            string param,
            string genericName,
            int index)
        {
            var present = EmitItem.Block(
                node,
                EmitType.FunctionStatement,
                $"if ({param} !== null) {{",
                "}",
                target);
            var extracted = "$__tyhpPromiseType_" + genericName + "_" + index;
            EmitItem.Line(
                node,
                EmitType.FunctionStatement,
                $"{extracted} = {RuntimeTypeClassFq}::of({param})->typeArgument(0);",
                present);
            var typed = EmitItem.Block(
                node,
                EmitType.FunctionStatement,
                $"if ({extracted} !== null) {{",
                "}",
                present);
            this.EmitVariantInferenceUnion(node, typed, infer, extracted);
        }

        private void EmitVariantInferenceUnion(IBase2Ast node, EmitItem block, string infer, string typeExpr)
        {
            EmitItem.Line(
                node,
                EmitType.FunctionStatement,
                $"{infer} = {infer} === null ? {typeExpr} : {infer}->or({typeExpr});",
                block);
        }

        private List<VariantInferenceSource> CollectVariantInferenceSources(
            PhpParameterListAst? parameters,
            string genericName)
        {
            var sources = new List<VariantInferenceSource>();
            foreach (var parameter in parameters?.GetAllNotNull() ?? [])
            {
                // An out-parameter holds no argument value to read a type from.
                if (string.IsNullOrWhiteSpace(parameter.Name) || parameter.Type is null || parameter.IsRef)
                {
                    continue;
                }

                var kind = ClassifyInference(parameter.Type, genericName);

                if (kind is { } found)
                {
                    ITypeExpression? promiseArm = null;
                    if (found == VariantInferenceKind.ValueOrPromise)
                    {
                        TryGetValueOrPromiseArms(parameter.Type, genericName, out promiseArm);
                    }

                    sources.Add(new VariantInferenceSource(
                        found,
                        parameter.Name,
                        parameter.IsVariadic,
                        TypeAllowsNull(parameter.Type),
                        promiseArm));
                }
            }

            return sources;
        }

        /// <summary>
        /// True when some argument can supply <paramref name="genericName"/>. Shared by the
        /// TYHP4407 check and the companion Closure so the two cannot drift.
        /// </summary>
        internal static bool TypeParameterIsInferable(string genericName, PhpParameterListAst? parameters)
        {
            foreach (var parameter in parameters?.GetAllNotNull() ?? [])
            {
                if (!parameter.IsRef
                    && parameter.Type is { } type
                    && ClassifyInference(type, genericName) is not null)
                {
                    return true;
                }
            }

            return false;
        }

        private static VariantInferenceKind? ClassifyInference(ITypeExpression type, string genericName)
        {
            if (ParameterInfersCallableReturn(type, genericName))
            {
                return VariantInferenceKind.CallableReturn;
            }

            if (ParameterInfersClassName(type, genericName))
            {
                return VariantInferenceKind.ClassName;
            }

            if (ParameterInfersPromise(type, genericName))
            {
                return VariantInferenceKind.Promise;
            }

            if (ParameterInfersPromiseElements(type, genericName))
            {
                return VariantInferenceKind.PromiseElements;
            }

            if (ParameterTypeIsTypeParameter(type, genericName))
            {
                return VariantInferenceKind.Value;
            }

            if (TryGetValueOrPromiseArms(type, genericName, out _))
            {
                return VariantInferenceKind.ValueOrPromise;
            }

            return null;
        }

        /// <summary>
        /// A union whose non-null members are exactly <c>T</c> and <c>Promise&lt;T&gt;</c>, such as
        /// <c>null|T|Promise&lt;T&gt;</c>. Each arm is an existing source: a promise argument gives its
        /// result type, any other argument gives its own type. A union with any other member is not
        /// a source.
        /// </summary>
        private static bool TryGetValueOrPromiseArms(
            ITypeExpression type,
            string genericName,
            out ITypeExpression? promiseArm)
        {
            promiseArm = null;
            type = UnwrapSingleType(type);
            if (type is not PhpTypeExpressionAst { IsNullable: false, Types: { } members })
            {
                return false;
            }

            var arms = members.GetAllNotNull().OfType<ITypeExpression>().Where(part => !IsNullTypeName(part)).ToList();
            if (arms.Count != 2)
            {
                return false;
            }

            var hasValue = false;
            foreach (var arm in arms)
            {
                if (IsPromiseOf(arm, genericName))
                {
                    if (promiseArm is not null)
                    {
                        promiseArm = null;
                        return false;
                    }

                    promiseArm = arm;
                }
                else if (ParameterTypeIsTypeParameter(arm, genericName))
                {
                    hasValue = true;
                }
                else
                {
                    promiseArm = null;
                    return false;
                }
            }

            if (hasValue && promiseArm is not null)
            {
                return true;
            }

            promiseArm = null;
            return false;
        }

        private static bool ParameterInfersCallableReturn(ITypeExpression type, string genericName)
        {
            type = UnwrapSingleType(type);
            if (!TryGetCallableReturnLastTypeArgument(type, out var returnArg) || returnArg is null)
            {
                return false;
            }

            return string.Equals(
                Checker.Rules.CheckerHelpers.SoleTypeName(returnArg),
                genericName,
                StringComparison.Ordinal);
        }

        private static bool ParameterInfersPromise(ITypeExpression type, string genericName) =>
            IsPromiseOf(type, genericName);

        private static bool ParameterInfersPromiseElements(ITypeExpression type, string genericName)
        {
            type = UnwrapNullableUnion(type);
            if (!IsArrayType(type))
            {
                return false;
            }

            var args = TypeArgumentsOf(type);
            return args is { Count: > 0 } && IsPromiseOf(args[^1], genericName);
        }

        /// <summary>
        /// <c>Promise&lt;T&gt;</c>, <c>?Promise&lt;T&gt;</c>, or <c>Promise&lt;T&gt;|null</c> whose
        /// first type argument is <paramref name="genericName"/>. The class may be
        /// <c>\Tyhp\Promise</c> or a fixture with the same simple name.
        /// </summary>
        private static bool IsPromiseOf(ITypeExpression type, string genericName)
        {
            type = UnwrapNullableUnion(type);
            if (!IsPromiseTypeName(SimpleTypeSpelling(type)))
            {
                return false;
            }

            var args = TypeArgumentsOf(type);
            if (args is not { Count: > 0 })
            {
                return false;
            }

            return string.Equals(
                Checker.Rules.CheckerHelpers.SoleTypeName(args[0]),
                genericName,
                StringComparison.Ordinal);
        }

        private static bool IsPromiseTypeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var trimmed = name.Trim().TrimStart('\\');
            var separator = trimmed.LastIndexOf('\\');
            var simple = separator < 0 ? trimmed : trimmed[(separator + 1)..];
            return simple.Equals("Promise", StringComparison.Ordinal);
        }

        private static bool IsArrayType(ITypeExpression type)
        {
            var name = SimpleTypeSpelling(type)?.Trim().TrimStart('\\');
            return name is not null && name.Equals("array", StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<ITypeExpression>? TypeArgumentsOf(ITypeExpression type)
        {
            var args = GetGenericTypeArgumentAddon(type);
            if ((args is null || args.Count == 0) && type is PhpNamedTypeAst { Name: IBase2Ast nameNode })
            {
                args = GetGenericTypeArgumentAddon(nameNode);
            }

            return args;
        }

        private static ITypeExpression UnwrapNullableUnion(ITypeExpression type)
        {
            type = UnwrapSingleType(type);
            if (type is PhpTypeExpressionAst { IsNullable: false, Types: { } members })
            {
                var parts = members.GetAllNotNull().OfType<ITypeExpression>().ToList();
                var nonNull = parts.Where(part => !IsNullTypeName(part)).ToList();
                if (nonNull.Count == 1 && nonNull.Count != parts.Count)
                {
                    return UnwrapSingleType(nonNull[0]);
                }
            }

            return type;
        }

        private static bool ParameterInfersClassName(ITypeExpression type, string genericName)
        {
            if (!TryUnwrapToClassNameBrand(type, out var argument) || argument is null)
            {
                return false;
            }

            return string.Equals(
                Checker.Rules.CheckerHelpers.SoleTypeName(argument),
                genericName,
                StringComparison.Ordinal);
        }

        private static bool TryUnwrapToClassNameBrand(ITypeExpression type, out ITypeExpression? argument)
        {
            argument = null;
            type = UnwrapSingleType(type);
            if (!IsClassNameBrand(type))
            {
                return false;
            }

            var args = type is IBase2Ast node ? GetGenericTypeArgumentAddon(node) : null;
            if ((args is null || args.Count == 0) && type is PhpNamedTypeAst { Name: IBase2Ast nameNode })
            {
                args = GetGenericTypeArgumentAddon(nameNode);
            }

            if (args is not { Count: > 0 })
            {
                return false;
            }

            argument = args[0];
            return argument is not null;
        }

        private static bool IsClassNameBrand(ITypeExpression type)
        {
            if (type is PhpNamedTypeAst { BoundSymbol: BuiltInUtilityTypeSymbol utility }
                && utility.Behavior is UtilityBehavior.ClassName
                    or UtilityBehavior.InterfaceName
                    or UtilityBehavior.EnumName)
            {
                return true;
            }

            var name = SimpleTypeSpelling(type)?.Trim().TrimStart('\\');
            return name is "__ClassName" or "__InterfaceName" or "__EnumName" or "class-string";
        }

        private static ITypeExpression UnwrapSingleType(ITypeExpression type)
        {
            type = UnwrapNullableType(type);
            while (type is PhpTypeExpressionAst { IsNullable: false, Types: { } members })
            {
                var only = members.GetAllNotNull().OfType<ITypeExpression>().ToList();
                if (only.Count != 1)
                {
                    break;
                }

                type = only[0];
            }

            return type;
        }

        private static bool ParameterTypeIsTypeParameter(ITypeExpression type, string genericName)
        {
            type = UnwrapSingleType(type);
            if (type is PhpTypeExpressionAst { IsNullable: false, Types: { } members })
            {
                var parts = members.GetAllNotNull().OfType<ITypeExpression>().ToList();
                var nonNull = parts.Where(part => !IsNullTypeName(part)).ToList();
                if (nonNull.Count == 1 && nonNull.Count != parts.Count)
                {
                    type = nonNull[0];
                }
            }

            return string.Equals(
                Checker.Rules.CheckerHelpers.SoleTypeName(type),
                genericName,
                StringComparison.Ordinal);
        }

        private static ITypeExpression UnwrapNullableType(ITypeExpression type)
        {
            while (type is PhpTypeExpressionAst { IsNullable: true, Types: { } members })
            {
                var only = members.GetAllNotNull().OfType<ITypeExpression>().ToList();
                if (only.Count != 1)
                {
                    break;
                }

                type = only[0];
            }

            return type;
        }

        private static bool IsNullTypeName(ITypeExpression type)
        {
            var name = SimpleTypeSpelling(type);
            return string.Equals(name, "null", StringComparison.OrdinalIgnoreCase);
        }

        private static string? SimpleTypeSpelling(ITypeExpression type) =>
            type switch
            {
                PhpNamedTypeAst { Name: TyhpGenericIdentifierAst generic } =>
                    generic.ValueString ?? generic.Identifier,
                PhpNamedTypeAst { Name: PhpNameAst name } => name.ValueString ?? name.Identifier,
                TyhpGenericIdentifierAst generic => generic.ValueString ?? generic.Identifier,
                PhpNameAst name => name.ValueString ?? name.Identifier,
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                _ => null,
            };

        private enum VariantInferenceKind
        {
            CallableReturn,
            ClassName,
            Value,
            Promise,
            PromiseElements,
            ValueOrPromise,
        }

        private readonly record struct VariantInferenceSource(
            VariantInferenceKind Kind,
            string ParameterName,
            bool IsVariadic,
            bool AllowsNull,
            ITypeExpression? PromiseArm = null);

        /// <summary>True for <c>?X</c>, <c>X|null</c>, and a bare <c>null</c> member.</summary>
        private static bool TypeAllowsNull(ITypeExpression type)
        {
            if (type is PhpTypeExpressionAst composite)
            {
                if (composite.IsNullable)
                {
                    return true;
                }

                if (composite.Types is { } members)
                {
                    var parts = members.GetAllNotNull().OfType<ITypeExpression>().ToList();
                    if (parts.Any(IsNullTypeName))
                    {
                        return true;
                    }

                    if (parts.Count == 1)
                    {
                        return TypeAllowsNull(parts[0]);
                    }
                }

                return false;
            }

            return IsNullTypeName(type);
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
            var firstClassCallable = Checker.Rules.CheckerHelpers.IsFirstClassCallableArgumentList(call.Arguments);

            if (!this._context.GenericCallTargets.TryGetValue(call, out var callee) || callee is null)
            {
                // Extension instance calls are rewritten to FQ static calls with a fresh PhpCallAst
                // that is not in GenericCallTargets. The rewrite stamps BoundSymbol on the
                // dereferenceable. A free function's symbol sits on the callee name.
                callee = dereferenceable.BoundSymbol
                    ?? (dereferenceable.Base as PhpNameAst)?.BoundSymbol;
            }

            if (callee is not (ObjectMethodSymbol or FunctionDeclarationSymbol))
            {
                return null;
            }

            var info = GenericRuntimeAttributeSupport.TryRead(callee);
            var foreign = this._context.HasForeignGenericRuntime(callee);
            if (info is { Erased: true })
            {
                return firstClassCallable
                    ? null
                    : this.TryBuildErasedForeignGenericBind(dereferenceable, call, callee, foreign);
            }

            if (!foreign && !this._context.RequiresGenericVariantFor(callee))
            {
                return null;
            }

            var genericParams = callee switch
            {
                ObjectMethodSymbol method => method.GenericParameters,
                FunctionDeclarationSymbol function => function.GenericParameters,
                _ => (IReadOnlyList<GenericTypeParameterSymbol>)Array.Empty<GenericTypeParameterSymbol>(),
            };

            if (genericParams.Count == 0)
            {
                return null;
            }

            if (!this.GenericRuntimeLayoutIsSupported(callee, call))
            {
                return null;
            }

            var layout2 = !InferGenericAttributeSupport.HasInferGeneric(callee)
                && CallableDispatchLayout(info) == GenericRuntimeAttributeSupport.CallableLayout;
            var typeArgs = TryGetCallSiteTypeArguments(dereferenceable.Base);
            if (firstClassCallable && !layout2 && typeArgs is not { Count: > 0 })
            {
                // `f(...)` on a layout-1 library with no explicit type arguments is the
                // value-signature wrapper under the declared name. With `f<int>(...)` the
                // type arguments must reach the binder, so it falls through below.
                return null;
            }

            IReadOnlyList<string>? asyncInferredArgs = null;
            if (!firstClassCallable && typeArgs is not { Count: > 0 })
            {
                // An async closure is emitted returning \Tyhp\Promise, so reflecting its PHP
                // return type would bind T to Promise. Spell the awaited type at the call.
                asyncInferredArgs = this.TryBuildAsyncCallableReturnTypeArguments(callee, genericParams, call);
                if (!layout2 && asyncInferredArgs is null)
                {
                    // A layout-1 library still has a value-signature wrapper for a bare call.
                    return null;
                }
            }

            var nameNode = FindCalleeNameNode(dereferenceable.Base);
            if (nameNode is null)
            {
                return null;
            }

            var leadingArgs = asyncInferredArgs ?? this.BuildVariantTypeArguments(genericParams, typeArgs);
            var typeArgsList = string.Join(", ", leadingArgs);
            var declaredArgs = firstClassCallable ? "" : this.FormatArgumentList(call.Arguments);

            if (!layout2)
            {
                var storedBinder = info?.Binder?.Trim();
                if (!string.IsNullOrEmpty(storedBinder)
                    && storedBinder.Contains("::", StringComparison.Ordinal))
                {
                    return firstClassCallable
                        ? this.FormatStoredGenericBinder(callee, storedBinder) + "(" + typeArgsList + ")"
                        : this.FormatStoredGenericBinder(callee, storedBinder)
                            + "(" + typeArgsList + ")(" + declaredArgs + ")";
                }
            }

            var previous = this._pendingVariantCallName;
            this._pendingVariantCallName = layout2 ? null : nameNode;
            string baseText;
            try
            {
                baseText = this.BuildDereferenceableBase(dereferenceable.Base);
            }
            finally
            {
                this._pendingVariantCallName = previous;
            }

            // The binder returns the value Closure, which is what `name(...)` denotes.
            return firstClassCallable
                ? baseText + "(" + typeArgsList + ")"
                : baseText + "(" + typeArgsList + ")(" + declaredArgs + ")";
        }

        /// <summary>
        /// Layout 2 when the callee was compiled here or its stamp lists 2. Layout 1 when the
        /// stamp lists 1 and not 2.
        /// </summary>
        private static int CallableDispatchLayout(GenericRuntimeInfo? info)
        {
            if (info is null || info.Layouts.Count == 0)
            {
                return GenericRuntimeAttributeSupport.CallableLayout;
            }

            if (info.Layouts.Contains(GenericRuntimeAttributeSupport.CallableLayout))
            {
                return GenericRuntimeAttributeSupport.CallableLayout;
            }

            if (info.Layouts.Contains(GenericRuntimeAttributeSupport.ClassLayout))
            {
                return GenericRuntimeAttributeSupport.ClassLayout;
            }

            return info.Layouts[0];
        }

        /// <summary>
        /// An erased foreign callable with explicit type arguments still goes through
        /// <c>Generic::bind</c>. The plain method does not take type arguments.
        /// </summary>
        private string? TryBuildErasedForeignGenericBind(
            PhpDereferenceableAst dereferenceable,
            PhpCallAst call,
            IBaseSymbol callee,
            bool foreign)
        {
            if (!foreign || GenericRuntimeAttributeSupport.TryRead(callee) is not { Erased: true })
            {
                return null;
            }

            var genericParams = callee switch
            {
                ObjectMethodSymbol method => method.GenericParameters,
                FunctionDeclarationSymbol function => function.GenericParameters,
                _ => (IReadOnlyList<GenericTypeParameterSymbol>)Array.Empty<GenericTypeParameterSymbol>(),
            };
            var typeArgs = TryGetCallSiteTypeArguments(dereferenceable.Base);
            if (typeArgs is not { Count: > 0 })
            {
                return null;
            }

            if (!this.GenericRuntimeLayoutIsSupported(callee, call))
            {
                return null;
            }

            var leadingArgs = this.BuildVariantTypeArguments(genericParams, typeArgs);
            var typeArgsList = string.Join(", ", leadingArgs);
            var declaredArgs = this.FormatArgumentList(call.Arguments);
            this._context.RequirePackage("tyhp/core");
            var savedPending = this._pendingVariantCallName;
            this._pendingVariantCallName = null;
            string fccBase;
            try
            {
                fccBase = this.BuildDereferenceableBase(dereferenceable.Base);
            }
            finally
            {
                this._pendingVariantCallName = savedPending;
            }

            var bindTarget = fccBase + "(...)";
            var bindArgs = string.IsNullOrEmpty(typeArgsList)
                ? bindTarget
                : bindTarget + ", " + typeArgsList;
            return "\\Tyhp\\Generic::bind(" + bindArgs + ")(" + declaredArgs + ")";
        }

        /// <summary>
        /// One <c>\Tyhp\Type</c> expression per declared type parameter, in declaration order. A
        /// parameter the call site left out falls back to its declared default type, then to null.
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
        /// Type arguments for a call with no <c>&lt;T&gt;</c> list whose arguments include an
        /// <c>async</c> closure or arrow function literal, or null when the call should keep using
        /// the declared-name wrapper.
        ///
        /// The wrapper binds a generic read from <c>callable(): T</c> through
        /// <c>Type::fromCallableReturn($param)</c>, which reflects the callable's PHP return type.
        /// An <c>async</c> closure is emitted returning <c>\Tyhp\Promise</c>, so the wrapper would
        /// bind <c>T</c> to <c>Promise</c> while the checker inferred the awaited type. Here each
        /// generic read from a <c>callable(): T</c> parameter takes the declared return type of the
        /// closure literal passed for it (null when the literal declares none, which the binder
        /// settles to the generic's default). When such a parameter receives anything other than a
        /// closure literal its type cannot be spelled statically, so the call stays on the wrapper.
        /// </summary>
        private IReadOnlyList<string>? TryBuildAsyncCallableReturnTypeArguments(
            IBaseSymbol callee,
            IReadOnlyList<GenericTypeParameterSymbol> genericParams,
            PhpCallAst call)
        {
            if (genericParams.Count == 0 || call.Arguments is null)
            {
                return null;
            }

            var declared = callee switch
            {
                ObjectMethodSymbol method => method.Parameters,
                FunctionDeclarationSymbol function => function.Parameters,
                _ => null,
            };
            if (declared is null)
            {
                return null;
            }

            var arguments = call.Arguments.GetAllNotNull().ToList();
            if (arguments.Any(a => a.IsVariadic))
            {
                return null;
            }

            var parts = new List<string>(genericParams.Count);
            var boundFromAsync = false;
            foreach (var generic in genericParams)
            {
                var part = "null";
                for (var index = 0; index < declared.Count; index++)
                {
                    var parameter = declared[index];
                    if (parameter.DeclaredType is null
                        || !TryGetCallableReturnLastTypeArgument(parameter.DeclaredType, out var returnArg)
                        || returnArg is null
                        || !string.Equals(
                            Checker.Rules.CheckerHelpers.SoleTypeName(returnArg),
                            generic.Name,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var argumentExpression = FindArgumentForParameter(arguments, declared, index)?.Expression;
                    if (argumentExpression is PhpInlineFunctionAst closure)
                    {
                        if (closure.ReturnType is { } closureReturn)
                        {
                            part = this.BuildRuntimeTypeExpression(closureReturn, preferCtorLocals: false);
                        }

                        boundFromAsync |= this.IsAsyncInlineFunction(closure);
                        break;
                    }

                    if (argumentExpression is not null
                        && Checker.AsyncClosureArgumentFacts.TryGet(argumentExpression, out var awaitedReturn))
                    {
                        if (awaitedReturn is not null)
                        {
                            part = this.BuildRuntimeTypeExpression(awaitedReturn, preferCtorLocals: false);
                        }

                        boundFromAsync = true;
                        break;
                    }

                    return null;
                }

                parts.Add(part);
            }

            return boundFromAsync ? parts : null;
        }

        /// <summary>
        /// The argument bound to the declared parameter at <paramref name="parameterIndex"/>: a named
        /// argument matching its name, else the positional argument at that index.
        /// </summary>
        private static PhpArgumentAst? FindArgumentForParameter(
            IReadOnlyList<PhpArgumentAst> arguments,
            IReadOnlyList<ParameterInfo> declared,
            int parameterIndex)
        {
            var parameterName = declared[parameterIndex].Name.TrimStart('$');
            var named = arguments.FirstOrDefault(a =>
                a.Name is { } name
                && string.Equals(name.ValueString?.TrimStart('$'), parameterName, StringComparison.Ordinal));
            if (named is not null)
            {
                return named;
            }

            var positional = arguments.Where(a => a.Name is null).ToList();
            return parameterIndex < positional.Count ? positional[parameterIndex] : null;
        }

        /// <summary>
        /// Compiled-library extension binders are stored as <c>Backer::name__tyhpGeneric</c>
        /// (the mapping class does not host the Closure). Qualify a namespace-relative backer
        /// with the callee's owning namespace.
        /// </summary>
        private string FormatStoredGenericBinder(IBaseSymbol callee, string binder)
        {
            var split = binder.Split(["::"], 2, StringSplitOptions.None);
            var className = split[0].Trim();
            var member = split.Length > 1 ? split[1].Trim() : "";
            var qualifiedClass = this.QualifyStoredBinderClass(callee, className);
            return string.IsNullOrEmpty(member) ? qualifiedClass : qualifiedClass + "::" + member;
        }

        private string QualifyStoredBinderClass(IBaseSymbol callee, string className)
        {
            if (className.Contains('\\', StringComparison.Ordinal))
            {
                return className.StartsWith('\\') ? className : "\\" + className;
            }

            var ownerFqn = callee switch
            {
                ObjectMethodSymbol method when method.ContainingScope is ObjectDeclarationScope obj =>
                    obj.DeclarationSymbol?.FullyQualifiedName ?? "",
                _ => callee.FullyQualifiedName ?? "",
            };

            var owner = ownerFqn.Trim().TrimStart('\\');
            var slash = owner.LastIndexOf('\\');
            var prefix = slash >= 0 ? owner[..slash] : "";
            return string.IsNullOrEmpty(prefix)
                ? "\\" + className.TrimStart('\\')
                : "\\" + prefix + "\\" + className.TrimStart('\\');
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
