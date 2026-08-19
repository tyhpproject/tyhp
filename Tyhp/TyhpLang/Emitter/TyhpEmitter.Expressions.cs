using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        private string BuildExpression(IExpression? expression)
        {
            if (expression == null)
            {
                return "";
            }

            return expression switch
            {
                PhpBinaryOpAst binary => this.BuildBinaryExpression(binary),
                PhpUnaryOpAst unary => this.BuildUnaryExpression(unary),
                PhpTernaryOpAst ternary => this.BuildTernaryExpression(ternary),
                PhpVariableAst variable => this.BuildVariableExpression(variable),
                PhpScalarAst scalar => this.BuildScalarExpression(scalar),
                PhpStringAst str => this.BuildStringExpression(str),
                PhpEncapsStringAst encaps => this.BuildEncapsStringExpression(encaps),
                PhpEncapsListAst encapsList => this.BuildEncapsListExpression(encapsList),
                TyhpGenericIdentifierAst generic => generic.ValueString ?? "",
                PhpNameAst name => this.TrackAndBuildName(name),
                PhpMagicConstantAst magic => magic.ValueString ?? "",
                PhpArrayAst array => this.BuildArrayExpression(array),
                PhpNewAst newExpr => this.BuildNewExpression(newExpr),
                PhpInlineFunctionAst inlineFn => this.BuildInlineFunctionExpression(inlineFn),
                TyhpAsyncBlockAst asyncBlock => this.BuildAsyncBlockExpression(asyncBlock),
                PhpYieldAst yield => this.BuildYieldExpression(yield),
                PhpConditionalAst conditional when !conditional.IsMatchSyntax => this.BuildSwitchExpression(conditional),
                PhpConditionalAst conditional => this.BuildMatchExpression(conditional),
                PhpDereferenceableAst dereferenceable => this.BuildDereferenceableExpression(dereferenceable),
                // Preserve an explicit source grouping `( expr )` when it wraps a binary/ternary op:
                // dropping those parentheses changes operator precedence and can even produce invalid
                // PHP (e.g. `(a ? b : c) . (d ? e : f)` collapsing into an unparenthesized nested
                // ternary). Trivial groupings (variables, calls, scalars) are unwrapped to avoid noise.
                PhpDereferenceableExpressionAst paren => IsNestedBinaryOrTernary(paren.Expression)
                    ? "(" + this.BuildExpression(paren.Expression) + ")"
                    : this.BuildExpression(paren.Expression),
                PhpIssetStatementAst isset => this.TryBuildHookBackingIsset(isset)
                    ?? ("isset(" + this.FormatExpressionList(isset.Variables) + ")"),
                PhpEmptyStatementAst empty => "empty(" + this.BuildExpression(empty.Expression) + ")",
                TyhpNameofAst nameof => this.BuildNameofExpression(nameof),
                TyhpTypeofAst typeofExpr => this.BuildTypeofExpression(typeofExpr),
                TyhpDefaultAst defaultExpr => this.BuildDefaultExpression(defaultExpr),
                TyhpVariableExistsAst variableExists => this.BuildVariableExistsExpression(variableExists),
                TyhpTypedVarExprAst typedVar => this.BuildTypedVarExpression(typedVar),
                PhpExpressionListAst list => this.FormatExpressionList(list),
                PhpArrayPairListAst arrayList => this.BuildArrayPairList(arrayList),
                EmittedPhpExprAst emitted => emitted.PhpText,
                _ => this.BuildExpressionFallback(expression),
            };
        }

        private string TrackAndBuildName(PhpNameAst name, bool forceFqnForBoundSymbol = false)
        {
            var text = name.ValueString ?? "";

            // A reference to a declared class/interface/enum/trait may carry a BoundSymbol from
            // the checker (Prop-init #17). Emit a root-anchored FQN when the written form is
            // absolute, relative-qualified, or a use-aliased short name that would otherwise
            // resolve elsewhere. Unambiguous bare names in the current namespace (or global)
            // keep their written spelling — `new Widget()` stays `new Widget()`, not
            // `new \Widget()`. `self`/`static`/`parent` are contextual keywords and must be
            // preserved verbatim. Attribute names opt out via `forceFqnForBoundSymbol`: that
            // spelling was always root-anchored before Prop-init #17 introduced BoundSymbol on
            // ordinary bare class references, and attribute goldens still lock in that contract.
            if (name.BoundSymbol is ObjectDeclarationSymbol objectSymbol
                && !string.IsNullOrWhiteSpace(objectSymbol.FullyQualifiedName)
                && !IsRelativeClassKeyword(text)
                && (forceFqnForBoundSymbol || MustEmitBoundObjectFqn(text, objectSymbol)))
            {
                var fqn = EmittedFqnHelper.Format(
                    objectSymbol.FullyQualifiedName,
                    this._context.Config.NamespacePrefix,
                    objectSymbol);
                this.TrackImport(fqn.TrimStart('\\'));
                return fqn;
            }

            if (text.Contains('\\') && !text.StartsWith('\\'))
            {
                // Relative qualified names (`Exceptions\Foo` in `namespace Tyhp`) must resolve
                // against the enclosing namespace / leading `use` alias — not simply gain a
                // leading `\`. Prefer binder resolution; fall back to spelling under the source
                // namespace so emit stays PHP-correct even when BoundSymbol was never set.
                if (this.TryResolveRelativeQualifiedName(text) is { } resolved)
                {
                    var fqn = EmittedFqnHelper.Format(
                        resolved.FullyQualifiedName,
                        this._context.Config.NamespacePrefix,
                        resolved);
                    this.TrackImport(fqn.TrimStart('\\'));
                    return fqn;
                }

                var sourceNs = this._context.CurrentSourceNamespace?.Trim().TrimStart('\\');
                var binderFqn = string.IsNullOrWhiteSpace(sourceNs)
                    ? text
                    : sourceNs + "\\" + text;
                var anchored = EmittedFqnHelper.Format(
                    binderFqn,
                    this._context.Config.NamespacePrefix,
                    symbol: null);
                this.TrackImport(anchored.TrimStart('\\'));
                return anchored;
            }

            if (text.Contains('\\'))
            {
                this.TrackImport(text.TrimStart('\\'));
            }
            else if (name.BoundSymbol is UseIncludeSymbol useInclude)
            {
                this.TrackImport(useInclude.ImportedName);
            }

            return text;
        }

        /// <summary>
        /// True when a BoundSymbol-backed class name must be spelled as a root-anchored FQN.
        /// Bare names whose short name and declaring namespace already match the written form in
        /// the current emit namespace are unambiguous and keep their source spelling.
        /// </summary>
        private bool MustEmitBoundObjectFqn(string writtenText, ObjectDeclarationSymbol objectSymbol)
        {
            // Absolute (`\Foo\Bar`) or relative multi-segment (`Exceptions\Foo`) — always FQN.
            if (writtenText.Contains('\\'))
            {
                return true;
            }

            // Bare name: emit FQN unless it already denotes the bound symbol in the current
            // source namespace (short name matches and namespaces match). Use-aliased short
            // names (imported from elsewhere, or renamed via `as`) fall through to FQN.
            if (!string.Equals(objectSymbol.Name, writtenText, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var boundFqn = objectSymbol.FullyQualifiedName.Trim().TrimStart('\\');
            var lastSlash = boundFqn.LastIndexOf('\\');
            var boundNs = lastSlash < 0 ? string.Empty : boundFqn[..lastSlash];
            var sourceNs = this._context.CurrentSourceNamespace?.Trim().TrimStart('\\') ?? string.Empty;

            return !string.Equals(boundNs, sourceNs, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves a relative multi-segment name against the current source namespace (and class
        /// <c>use</c> aliases) via the binder. Returns null when the symbol tree is unavailable or
        /// the name does not denote a known type.
        /// </summary>
        private ObjectDeclarationSymbol? TryResolveRelativeQualifiedName(string relativeText)
        {
            if (this._context.GlobalScope is null)
            {
                return null;
            }

            var segments = relativeText.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            var resolver = new NameResolver(
                this._context.GetSymbolTree(),
                this._context.Diagnostics);
            return resolver.ResolveRelativeName(segments, this.GetEmitResolutionScope())
                as ObjectDeclarationSymbol;
        }

        /// <summary>
        /// Scope used for emit-time relative name resolution: prefer the namespace block that owns
        /// the file currently being emitted (so its <c>use</c> aliases are visible), else the
        /// enclosing namespace scope, else the global scope.
        /// </summary>
        private IBaseScope GetEmitResolutionScope()
        {
            var global = this._context.GlobalScope
                ?? throw new InvalidOperationException("EmitContext.GlobalScope is required.");

            var sourceFileId = this._context.CurrentOutputFile?.SourceFileAst?.Identifier
                ?? this._context.CurrentSourceFile?.Identifier;
            var sourceNs = this._context.CurrentSourceNamespace?.Trim().TrimStart('\\');

            if (!string.IsNullOrWhiteSpace(sourceNs)
                && global.FindNamespaceScope(sourceNs) is { } nsScope)
            {
                IBaseScope nsAsScope = nsScope;
                if (!string.IsNullOrEmpty(sourceFileId))
                {
                    foreach (var child in nsAsScope.GetAllChildScopes())
                    {
                        if (child.DeclarationSymbol is NamespaceBlockSymbol block
                            && MatchesEmitSourceFile(block.OwningFileScope, sourceFileId))
                        {
                            return child;
                        }
                    }
                }

                return nsAsScope.GetAllChildScopes().FirstOrDefault() ?? nsAsScope;
            }

            if (!string.IsNullOrEmpty(sourceFileId))
            {
                foreach (var child in ((IBaseScope)global).GetAllChildScopes())
                {
                    if (child is FileScope fileScope && MatchesEmitSourceFile(fileScope, sourceFileId))
                    {
                        return fileScope;
                    }
                }
            }

            return global;
        }

        private static bool MatchesEmitSourceFile(FileScope? fileScope, string sourceFileId)
        {
            if (fileScope is null)
            {
                return false;
            }

            return string.Equals(fileScope.FileName, sourceFileId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileScope.SourceFile, sourceFileId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRelativeClassKeyword(string text)
            => string.Equals(text, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "static", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "parent", StringComparison.OrdinalIgnoreCase);

        private string BuildExpressionFallback(IExpression expression)
        {
            if (expression is IDereferenceableBase dereferenceable)
            {
                return this.BuildDereferenceableBase(dereferenceable);
            }

            return "";
        }

        private string BuildEncapsStringExpression(PhpEncapsStringAst encaps)
        {
            var tokenText = encaps.TokenValue?.ValueString;
            if (!string.IsNullOrEmpty(tokenText)
                && (tokenText.StartsWith('\'') || tokenText.StartsWith('"')))
            {
                return tokenText;
            }

            var text = tokenText ?? encaps.ValueString ?? "";
            return "'" + text.Replace("'", "\\'") + "'";
        }

        private string BuildEncapsListExpression(PhpEncapsListAst encapsList)
        {
            var partList = encapsList.GetAllNotNull().ToList();
            if (encapsList.StringType == PhpStringType.SingleQuoted
                && partList.Count == 1
                && partList[0] is PhpEncapsStringAst encaps)
            {
                return this.BuildEncapsStringExpression(encaps);
            }

            var quote = encapsList.StringType == PhpStringType.SingleQuoted ? "'" : "\"";
            var parts = partList.Select(p => p switch
            {
                PhpEncapsStringAst encapsPart => encapsPart.TokenValue?.ValueString ?? encapsPart.ValueString ?? "",
                IExpression expr => this.BuildExpression(expr),
                _ => p.ValueString ?? "",
            });
            return quote + string.Join("", parts) + quote;
        }

        private EmitItem EmitExpression(IExpression expression, EmitItem parent, EmitType emitType = EmitType.SubBlockStatement)
            => EmitItem.Line(expression, emitType, this.BuildExpression(expression), parent);

        private string BuildBinaryExpression(PhpBinaryOpAst binary)
        {
            if (this.TryBuildHookBackingWrite(binary) is { } backingWrite)
            {
                return backingWrite;
            }

            // PHP 8.5 pipe `|>`: native when targeting ≥ 8.5; otherwise lower to nested calls
            // (FCC unwrap + parenthesized arrow/closure invoke). See BuildPipeExpression.
            if (IsPipeOperator(binary))
            {
                return this.BuildPipeExpression(binary);
            }

            var op = this.GetOperatorText(binary.Operator);
            var leftNeedsParens = IsNestedBinaryOrTernary(binary.Left);
            var rightNeedsParens = IsNestedBinaryOrTernary(binary.Right);
            var left = this.ParenthesizeIfNeeded(binary.Left, leftNeedsParens);
            var right = this.ParenthesizeIfNeeded(binary.Right, rightNeedsParens);

            if (binary.Operator != null
                && (binary.Operator.ValueInt64 == TyhpParser.T_TYHP_USING_EQUAL
                    || PhpAssignmentOperatorExtensions.FromToken((int)binary.Operator.ValueInt64) == PhpAssignmentOperator.UsingEqual))
            {
                if (binary.Left is PhpVariableAst varAst)
                {
                    var varName = varAst.VariableToken?.ValueString
                        ?? this.BuildExpression(varAst.VariableExpression);
                    this._context.Disposables.Track(varName.TrimStart('$'));
                }

                var leftVar = this.BuildExpression(binary.Left);
                var rightVar = this.BuildExpression(binary.Right);
                if (this._context.IsCurrentScopeDisposableTryFinallyFallback)
                {
                    return $"{leftVar} = {rightVar}";
                }

                var scopeVar = this._context.EnsureDisposableScopeForCurrentBlock();
                return $"{leftVar} = {scopeVar}->using({rightVar})";
            }

            // `$x instanceof T` / `$x is T` against an in-scope generic type parameter has no PHP
            // class named like the parameter. Reify to `\Tyhp\Type::is($x, <typeof(T)>)` using the
            // same Mechanism D binder / Mechanism C GenericObject lookup `typeof(T)` already emits (FOUND_BUGS #37).
            // Parameterized nominals (`static<T>`, `Box<int>`) likewise erase under native
            // `instanceof`, so reify those to `\Tyhp\Type::is($x, Type::generic(...))`.
            if (IsInstanceofLikeOperator(binary)
                && this.TryBuildReifiedInstanceofCheck(binary, left) is { } reified)
            {
                return reified;
            }

            return $"{left} {op} {right}";
        }

        /// <summary>
        /// Emits PHP 8.5 <c>|&gt;</c> natively when <c>output.phpVersion</c> ≥ 8.5; otherwise
        /// rewrites to a nested call so lower targets stay valid PHP.
        /// </summary>
        /// <remarks>
        /// Lowering matches the PHP manual's nested / temp equivalence for single-arg callables:
        /// <c>$a |&gt; foo(...)</c> → <c>foo($a)</c>; chains nest left-to-right
        /// (<c>$a |&gt; f(...) |&gt; g(...)</c> → <c>g(f($a))</c>). Arrow / closure RHS forms are
        /// parenthesized for both native pipe and invoke-after-lower, as PHP requires.
        /// </remarks>
        private string BuildPipeExpression(PhpBinaryOpAst binary)
        {
            if (this._context.IsPhpVersionAtLeast(8, 5))
            {
                var leftNeedsParens = IsNestedBinaryOrTernary(binary.Left);
                var left = this.ParenthesizeIfNeeded(binary.Left, leftNeedsParens);
                var right = this.BuildPipeRhsNative(binary.Right);
                return $"{left} |> {right}";
            }

            return this.BuildPipeLowering(binary.Left, binary.Right);
        }

        /// <summary>
        /// Native <c>|&gt;</c> RHS spelling. Always parenthesize arrow/closure callables — PHP
        /// forbids bare <c>fn</c>/<c>function</c> on the right of pipe, and
        /// <see cref="BuildExpression"/> otherwise drops grouping parens around non-binary RHS.
        /// </summary>
        private string BuildPipeRhsNative(IExpression? rhs)
        {
            if (UnwrapParenExpressions(rhs) is PhpInlineFunctionAst inline)
            {
                return "(" + this.BuildInlineFunctionExpression(inline) + ")";
            }

            return this.ParenthesizeIfNeeded(rhs, IsNestedBinaryOrTernary(rhs));
        }

        /// <summary>
        /// PHP &lt; 8.5 rewrite: apply the piped value as the sole argument of the RHS callable.
        /// First-class callables unwrap to a direct call (<c>foo(...)</c> → <c>foo($v)</c>);
        /// other callables become <c>$callable($v)</c>, with parentheses when PHP requires them
        /// for the invoke form (arrows, closures, operator results).
        /// </summary>
        private string BuildPipeLowering(IExpression? left, IExpression? right)
        {
            var valueText = this.BuildExpression(left);
            var rhs = UnwrapParenExpressions(right);

            if (rhs is PhpDereferenceableAst { Suffix: PhpCallAst call } deref
                && CheckerHelpers.IsFirstClassCallableArgumentList(call.Arguments))
            {
                var callee = this.BuildDereferenceableBase(deref.Base);
                return $"{callee}({valueText})";
            }

            var callableText = rhs is PhpInlineFunctionAst inline
                ? this.BuildInlineFunctionExpression(inline)
                : this.BuildExpression(rhs);

            if (PipeCallableNeedsInvokeParens(rhs))
            {
                callableText = $"({callableText})";
            }

            return $"{callableText}({valueText})";
        }

        private static bool IsPipeOperator(PhpBinaryOpAst binary)
        {
            var op = binary.Operator;
            if (op is null)
            {
                return false;
            }

            if (op.ValueInt64 == TyhpParser.T_PIPE
                || string.Equals(op.ValueString, "|>", StringComparison.Ordinal))
            {
                return true;
            }

            // TokenValue already collapses missing ValueInt64 to -1.
            return PhpBinaryOperatorExtensions.FromToken(op.TokenValue) == PhpBinaryOperator.Pipe;
        }

        /// <summary>
        /// True when invoking <paramref name="callable"/> as <c>expr(args)</c> requires wrapping
        /// <c>expr</c> in parentheses (PHP parse rules for closures, arrows, and operator results).
        /// </summary>
        private static bool PipeCallableNeedsInvokeParens(IExpression? callable)
            => callable is PhpInlineFunctionAst
                or PhpBinaryOpAst
                or PhpTernaryOpAst
                or PhpUnaryOpAst
                or PhpConditionalAst;

        private static IExpression? UnwrapParenExpressions(IExpression? expression)
        {
            while (expression is PhpDereferenceableExpressionAst paren)
            {
                expression = paren.Expression;
            }

            return expression;
        }

        /// <summary>
        /// When the RHS of <c>instanceof</c>/<c>is</c> needs a runtime type brand — a builtin
        /// scalar (<c>int</c>, <c>string</c>, …), a bare in-scope generic parameter, or a
        /// class/self/static/parent name with type arguments — returns
        /// <c>\Tyhp\Type::is(…, …)</c>; otherwise null so the caller emits native PHP
        /// <c>instanceof</c>.
        /// </summary>
        private string? TryBuildReifiedInstanceofCheck(PhpBinaryOpAst binary, string leftText)
        {
            var right = UnwrapParenExpressions(binary.Right);

            if (TryBuildBuiltinInstanceofCheck(right, leftText) is { } builtinCheck)
            {
                return builtinCheck;
            }

            if (right is not PhpNameAst name)
            {
                return null;
            }

            // `static<T>` / `Foo<Bar>` — native instanceof drops the type arguments.
            var typeArgs = GetGenericTypeArgumentAddon(name);
            if (typeArgs is { Count: > 0 })
            {
                this._context.RequirePackage("tyhp/core");
                var className = ResolveRuntimeClassName(
                    name.BoundSymbol, name, written: name.ValueString);
                var runtimeType = this.BuildRuntimeGenericFromClassAndArgs(
                    className, typeArgs, preferCtorLocals: false);
                return $"{RuntimeTypeClass}::is({leftText}, {runtimeType})";
            }

            var simpleName = (name.ValueString ?? string.Empty).Trim().TrimStart('\\');
            if (simpleName.Length == 0 || simpleName.Contains('\\'))
            {
                return null;
            }

            // A declared class of the same spelling wins (matches BuildTypeofExpression).
            if (this.TryResolveDeclaredClass(simpleName) || name.BoundSymbol is ObjectDeclarationSymbol)
            {
                return null;
            }

            string? typeExpr;
            if (this.IsVariantGenericParamName(simpleName))
            {
                typeExpr = this.BuildVariantTypeofLookup(simpleName);
            }
            else if (this.IsObjectGenericParamName(simpleName))
            {
                // Class generics live on the instance. Static members have no $this — same closed
                // fallback typeof/default use when the checker is bypassed (TYHP4156 covers
                // instanceof/is in static context; TYHP4148/4152 cover typeof/default).
                if (this._currentMemberIsStatic
                    || this.BuildGenericResolvedTypeLookupCall(simpleName) is not { } lookup)
                {
                    return "false";
                }

                typeExpr = $"$this->{lookup}";
            }
            else
            {
                return null;
            }

            return $"{RuntimeTypeClass}::is({leftText}, {typeExpr})";
        }

        /// <summary>
        /// PHP <c>instanceof</c> requires a class name. Tyhp <c>is int</c> / <c>instanceof string</c>
        /// (and the other scalar factories on <c>\Tyhp\Type</c>) reify to
        /// <c>\Tyhp\Type::is($x, \Tyhp\Type::int())</c> so the emitted file is valid PHP.
        /// </summary>
        private string? TryBuildBuiltinInstanceofCheck(IExpression? right, string leftText)
        {
            var spelling = right switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNameAst name when name.BoundSymbol is not ObjectDeclarationSymbol
                    && GetGenericTypeArgumentAddon(name) is not { Count: > 0 }
                    => (name.ValueString ?? name.Identifier ?? "").Trim().TrimStart('\\'),
                _ => null,
            };

            if (string.IsNullOrEmpty(spelling) || spelling.Contains('\\'))
            {
                return null;
            }

            // A generic parameter named like a scalar must keep the Mechanism C / D lookup.
            if (this.IsVariantGenericParamName(spelling) || this.IsObjectGenericParamName(spelling))
            {
                return null;
            }

            if (!ScalarTypeFactoryNames.Contains(spelling))
            {
                return null;
            }

            this._context.RequirePackage("tyhp/core");
            return $"{RuntimeTypeClass}::is({leftText}, {RuntimeTypeClass}::{spelling}())";
        }

        private string BuildUnaryExpression(PhpUnaryOpAst unary)
        {
            var op = this.GetOperatorText(unary.Operator);
            if (string.Equals(op, "await", StringComparison.OrdinalIgnoreCase)
                || unary.Operator?.ValueInt64 == TyhpParser.T_TYHP_AWAIT)
            {
                this._context.RequirePackage("tyhp/async");
                var awaitOperand = this.BuildExpression(unary.Operand);
                return $"\\Tyhp\\Promise::_await({awaitOperand})";
            }

            // `exit` / `die`: bare form omits parentheses; call forms carry PhpArgumentListAst.
            // Native ≥ 8.4; named / FCC lowered for older targets. See BuildExitDieExpression.
            if (unary.Operator?.ValueInt64 == TyhpParser.T_EXIT
                || string.Equals(op, "exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(op, "die", StringComparison.OrdinalIgnoreCase))
            {
                return this.BuildExitDieExpression(unary, op);
            }

            // PHP 8.5 call-shaped `clone(...)` / unary `clone $x`: native ≥ 8.5; lower call forms
            // via WithKeywordHelper (ObjectHelper / unary) for older targets. See BuildCloneExpression.
            if (StructEmissionHelper.IsCloneOperator(unary.Operator)
                || string.Equals(op, "clone", StringComparison.OrdinalIgnoreCase))
            {
                return this.BuildCloneExpression(unary, op);
            }

            // PHP 8.5 `(void)`: native cast when targeting ≥ 8.5; otherwise omit the cast and
            // emit the operand alone (statement / for-list discard — no runtime effect).
            if (CheckerHelpers.IsVoidCastUnary(unary))
            {
                return this.BuildVoidCastExpression(unary, op);
            }

            var operandText = this.BuildExpression(unary.Operand);
            if (unary.IsPrefix)
            {
                // Word-keyword prefix operators (return, yield, print, throw, clone, await, ...)
                // need a space before their operand, e.g. `return $x`. Cast operators need a space
                // after the closing paren per PSR-12 §6.1: `(int) $x`. Other symbolic operators
                // (`!`, `-`, `~`, ...) bind directly, e.g. `!$x`.
                if (op.Length > 0 && char.IsLetter(op[op.Length - 1]))
                {
                    return $"{op} {operandText}";
                }

                if (IsCastOperator(op))
                {
                    return $"{op} {operandText}";
                }

                return $"{op}{operandText}";
            }

            return $"{operandText}{op}";
        }

        /// <summary>
        /// Emits PHP 8.5 <c>(void)</c> natively when <c>output.phpVersion</c> ≥ 8.5; otherwise
        /// rewrites to the bare operand so lower targets stay valid PHP.
        /// </summary>
        /// <remarks>
        /// <c>(void)</c> has no runtime effect beyond discarding the value (and suppressing
        /// NoDiscard-style warnings in the checker). On &lt; 8.5 the cast token is unknown, so
        /// statement form <c>(void)$x;</c> becomes <c>$x;</c> and for-list items drop the cast.
        /// Native emit preserves source cast spelling and spaces after the cast per PSR-12 §6.1.
        /// </remarks>
        private string BuildVoidCastExpression(PhpUnaryOpAst unary, string op)
        {
            var operandText = this.BuildExpression(unary.Operand);
            if (this._context.IsPhpVersionAtLeast(8, 5))
            {
                return $"{op} {operandText}";
            }

            return operandText;
        }

        /// <summary>
        /// Emits <c>exit</c> / <c>die</c>: bare keyword without parentheses; call forms are
        /// native on PHP ≥ 8.4 and rewritten for lower targets (Story 14.5 Phase 5 item 4).
        /// </summary>
        /// <remarks>
        /// PHP 8.4 made <c>exit</c>/<c>die</c> proper functions (named args, variable-function /
        /// FCC use). Before that they were language constructs: only bare <c>exit;</c> and
        /// positional <c>exit($status)</c> / <c>exit()</c> were valid.
        /// <list type="bullet">
        /// <item>≥ 8.4 — pass through call spelling (positional, named, empty, FCC).</item>
        /// <item>&lt; 8.4 empty <c>()</c> — prefer bare keyword (no parens).</item>
        /// <item>&lt; 8.4 named <c>status:</c> — positional <c>exit($status)</c>.</item>
        /// <item>&lt; 8.4 FCC <c>exit(...)</c> — static arrow invoking the keyword construct
        /// (equivalent of <c>\Closure::fromCallable('exit')</c>, which only works once exit is a
        /// real function ≥ 8.4 — the native FCC path).</item>
        /// <item>Unpack / unresolvable forms keep call spelling.</item>
        /// </list>
        /// </remarks>
        private string BuildExitDieExpression(PhpUnaryOpAst unary, string op)
        {
            if (unary.Operand is null)
            {
                return op;
            }

            if (unary.Operand is not PhpArgumentListAst argumentList)
            {
                return $"{op}({this.BuildExpression(unary.Operand)})";
            }

            if (this._context.IsPhpVersionAtLeast(8, 4))
            {
                return $"{op}({this.FormatArgumentList(argumentList)})";
            }

            return this.BuildExitDieLoweredCall(op, argumentList);
        }

        /// <summary>
        /// PHP &lt; 8.4 lowering for call-shaped <c>exit(...)</c> / <c>die(...)</c>.
        /// </summary>
        private string BuildExitDieLoweredCall(string op, PhpArgumentListAst argumentList)
        {
            if (CheckerHelpers.IsFirstClassCallableArgumentList(argumentList))
            {
                // Plan allows `\Closure::fromCallable('exit')`; that only works ≥ 8.4 (native path).
                // Equivalent: static arrow matching the ExtCore tyhpdef signature.
                return $"(static fn(string | int $status = 0) => {op}($status))";
            }

            var args = argumentList.GetAllNotNull().ToList();
            if (args.Count == 0)
            {
                // Prefer bare keyword over empty `exit()` on language-construct targets.
                return op;
            }

            if (TryExtractExitDieStatusArgument(args, out var statusExpr))
            {
                return $"{op}({this.BuildExpression(statusExpr)})";
            }

            // Unpack / unknown names — keep call spelling (checker already diagnoses bad names).
            return $"{op}({this.FormatArgumentList(argumentList)})";
        }

        /// <summary>
        /// Resolves the single <c>$status</c> argument from positional or <c>status:</c> named form.
        /// Returns <c>false</c> for unpack / multi-arg / unknown names.
        /// </summary>
        private static bool TryExtractExitDieStatusArgument(
            IReadOnlyList<PhpArgumentAst> args,
            out IExpression statusExpr)
        {
            statusExpr = null!;
            if (args.Count != 1)
            {
                return false;
            }

            var arg = args[0];
            if (arg.IsVariadic || arg.Expression is null)
            {
                return false;
            }

            var name = arg.Name?.ValueString;
            if (name is not null
                && !string.Equals(name, "status", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            statusExpr = arg.Expression;
            return true;
        }

        /// <summary>
        /// Emits <c>clone</c>: unary <c>clone $x</c> always pass-through; call-shaped
        /// <c>clone(...)</c> is native on PHP ≥ 8.5 and rewritten via
        /// <see cref="WithKeywordHelper"/> on lower targets (Story 14.5 Phase 5 item 3).
        /// </summary>
        /// <remarks>
        /// Call forms carry a <see cref="PhpArgumentListAst"/> operand (trailing-comma /
        /// multi-arg / named / FCC). Parenthesized unary <c>clone($x)</c> is <em>not</em> a
        /// call form — its operand is a parenthesized expression, so it stays
        /// <c>clone $x</c> / <c>clone ($x)</c> spelling via the unary path.
        /// Lowering reuses Story 11 helpers: object-only → <c>clone $o</c>;
        /// clone-with → <c>ObjectHelper::with(clone $o, $props)</c>;
        /// FCC → static arrow wrapping the same ObjectHelper pattern.
        /// </remarks>
        private string BuildCloneExpression(PhpUnaryOpAst unary, string op)
        {
            if (unary.Operand is not PhpArgumentListAst argumentList)
            {
                var operandText = this.BuildExpression(unary.Operand);
                return string.IsNullOrEmpty(operandText) ? op : $"{op} {operandText}";
            }

            if (this._context.IsPhpVersionAtLeast(8, 5))
            {
                return $"{op}({this.FormatArgumentList(argumentList)})";
            }

            if (CheckerHelpers.IsFirstClassCallableArgumentList(argumentList))
            {
                return this.BuildExpression(
                    WithKeywordHelper.BuildCloneFirstClassCallableLowering(unary, this._context));
            }

            var rewritten = WithKeywordHelper.RewriteCloneKeywordCall(
                unary,
                argumentList,
                this._context);
            // Unrewritable forms (empty / unpack / unknown names) keep call spelling so we do not
            // re-enter BuildCloneExpression on the same node.
            if (ReferenceEquals(rewritten, unary))
            {
                return $"{op}({this.FormatArgumentList(argumentList)})";
            }

            return this.BuildExpression(rewritten);
        }

        private static bool IsCastOperator(string op)
            => op.Length >= 3
                && op[0] == '('
                && op[^1] == ')'
                && op[1..^1].All(c => char.IsLetter(c) || c == '_');

        private string BuildTernaryExpression(PhpTernaryOpAst ternary)
        {
            var condition = this.ParenthesizeIfNeeded(ternary.Condition, IsNestedBinaryOrTernary(ternary.Condition));
            if (ternary.TrueExpr == null)
            {
                var falseExpr = this.ParenthesizeIfNeeded(ternary.FalseExpr, IsNestedBinaryOrTernary(ternary.FalseExpr));
                return $"{condition} ?: {falseExpr}";
            }

            var trueExpr = this.ParenthesizeIfNeeded(ternary.TrueExpr, IsNestedBinaryOrTernary(ternary.TrueExpr));
            var falseExprFull = this.ParenthesizeIfNeeded(ternary.FalseExpr, IsNestedBinaryOrTernary(ternary.FalseExpr));
            return $"{condition} ? {trueExpr} : {falseExprFull}";
        }

        // The parser's VisitForeachVariable always wraps the real `foreach` target in an extra
        // PhpVariableAst layer (to carry the by-ref flag and unify the IForeachVariable type).
        // Rendering that wrapper through BuildVariableExpression would prepend a spurious '$',
        // turning `$item` into the variable-variable `$$item`. Unwrap exactly one layer here so the
        // inner target (which may legitimately be a real `$$dynamic` variable) renders correctly.
        private string BuildForeachVariable(IExpression? variable)
        {
            if (variable is PhpVariableAst wrapper
                && wrapper.VariableToken == null
                && wrapper.VariableExpression is IExpression inner)
            {
                var refPrefix = wrapper.IsRef ? "&" : "";
                return refPrefix + this.BuildExpression(inner);
            }

            return this.BuildExpression(variable);
        }

        private string BuildVariableExpression(PhpVariableAst variable)
        {
            if (variable.VariableToken != null)
            {
                var text = variable.VariableToken.ValueString ?? "";
                if (this._context.WeakSelfCaptureVar is { } weakVar
                    && string.Equals(text.TrimStart('$'), "this", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{weakVar}->get()";
                }

                if (this._context.ExtensionReceiverThisAlias is { } alias
                    && string.Equals(text.TrimStart('$'), "this", StringComparison.OrdinalIgnoreCase))
                {
                    return alias;
                }

                return text;
            }

            if (variable.VariableExpression != null)
            {
                return "$" + this.BuildExpression(variable.VariableExpression);
            }

            return "";
        }

        private string BuildScalarExpression(PhpScalarAst scalar)
        {
            var token = scalar.AstChildren.ElementAtOrDefault(0) as TokenValueAst;
            return scalar.ScalarType switch
            {
                PhpScalarType.Integer or PhpScalarType.OctalNumber or PhpScalarType.HexNumber or PhpScalarType.BinaryNumber
                    => token?.ValueString ?? scalar.ValueInt64?.ToString() ?? "0",
                PhpScalarType.Float => token?.ValueString ?? scalar.ValueDecimal?.ToString() ?? "0.0",
                PhpScalarType.String => FormatStringScalar(scalar, token),
                _ => token?.ValueString ?? "",
            };
        }

        private static string FormatStringScalar(PhpScalarAst scalar, TokenValueAst? token)
        {
            var value = scalar.ValueString;
            if (value is { Length: >= 2 }
                && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
            {
                return value;
            }

            var unquoted = value ?? UnquotePhpStringLiteral(token?.ValueString);
            return "'" + unquoted.Replace("'", "\\'") + "'";
        }

        private static string UnquotePhpStringLiteral(string? literal)
        {
            if (string.IsNullOrEmpty(literal))
            {
                return "";
            }

            if (literal.Length >= 2
                && ((literal[0] == '\'' && literal[^1] == '\'') || (literal[0] == '"' && literal[^1] == '"')))
            {
                return literal[1..^1].Replace("\\'", "'").Replace("\\\\", "\\");
            }

            return literal;
        }

        private string BuildStringExpression(PhpStringAst str)
        {
            if (str.Parts == null)
            {
                return "''";
            }

            var partList = str.Parts.GetAllNotNull().ToList();
            if (str.StringType == PhpStringType.SingleQuoted
                && partList.Count == 1
                && partList[0] is PhpEncapsStringAst encaps)
            {
                return this.BuildEncapsStringExpression(encaps);
            }

            var quote = str.StringType == PhpStringType.SingleQuoted ? "'" : "\"";
            var parts = partList.Select(p => this.BuildExpression(p));
            return quote + string.Join("", parts) + quote;
        }

        private string BuildArrayExpression(PhpArrayAst array)
        {
            var pairs = array.ArrayPairs?.GetAllNotNull().ToList() ?? [];
            var pairTexts = pairs.Select(this.BuildArrayPair);
            var inner = string.Join(", ", pairTexts);
            return array.IsShortSyntax ? $"[{inner}]" : $"array({inner})";
        }

        // Short array literals (`[]`, `[$a, $b => $c]`) parse to a PhpArrayPairListAst rather than a
        // PhpArrayAst, so they need their own rendering path; without it the whole literal collapsed
        // to an empty string (e.g. `$flattened = ;`).
        private string BuildArrayPairList(PhpArrayPairListAst list)
        {
            var pairs = list.GetAllNotNull().Select(this.BuildArrayPair);
            return "[" + string.Join(", ", pairs) + "]";
        }

        private string BuildArrayPair(PhpArrayPairAst pair)
        {
            if (pair.IsExpansion)
            {
                return "..." + this.BuildExpression(pair.ValueExpr);
            }

            if (pair.KeyExpr != null)
            {
                return this.BuildExpression(pair.KeyExpr) + " => " + this.BuildExpression(pair.ValueExpr);
            }

            return this.BuildExpression(pair.ValueExpr);
        }

        private string BuildNewExpression(PhpNewAst newExpr)
        {
            var formattedArgs = newExpr.Arguments != null
                ? this.FormatArgumentList(newExpr.Arguments)
                : "";
            var args = "(" + formattedArgs + ")";

            if (newExpr.AnonymousClass is { } anonymousClass)
            {
                return this.BuildAnonymousClassInline(anonymousClass, args);
            }

            if (this.TryBuildNewGenericTypeParameterExpression(newExpr, args) is { } dynamicNew)
            {
                return dynamicNew;
            }

            // A tracked generic class is instantiated through its generated factory, which binds the
            // type arguments before running the constructor.
            if (this.TryBuildGenericFactoryCall(newExpr, formattedArgs) is { } factoryCall)
            {
                return factoryCall;
            }

            // The class-name reference may be a static name (IClassName), a generic identifier
            // (generics stripped via BuildExpression), or a dynamic expression such as
            // `new $className()` / `new $obj->prop()` which is an IExpression but not an IClassName.
            var className = newExpr.ClassName switch
            {
                IClassName classNameRef => this.BuildClassName(classNameRef),
                IExpression expr => this.BuildExpression(expr),
                _ => "",
            };
            return $"new {className}{args}";
        }

        private string BuildAnonymousClassInline(PhpObjectTypeDeclAst anonymousClass, string args)
        {
            var root = EmitItem.Empty(anonymousClass, Enum.EmitType.ObjectDeclaration);
            var block = this.EmitObjectDeclaration(anonymousClass, root);
            var rendered = block.emit(0);

            const string classKeyword = "class";
            var idx = rendered.IndexOf(classKeyword, StringComparison.Ordinal);
            if (idx >= 0)
            {
                rendered = rendered.Insert(idx + classKeyword.Length, args);
            }

            return "new " + rendered;
        }

        private string BuildMethodBodyInline(PhpStatementBlockAst? body, bool compact = false)
        {
            if (body == null)
            {
                return "{}";
            }

            this._context.EnterDisposableBlockScope();
            try
            {
                var lines = new List<string>();

                // A closure/inline body that owns `:=` disposables needs its own DisposableScope
                // created at this depth so the `$__scope->using(...)` rewrite refers to a local var.
                if (ContainsUsingEqualAssignment(body))
                {
                    var scopeVar = this._context.EnsureDisposableScopeForCurrentBlock();
                    lines.Add($"{scopeVar} = \\Tyhp\\DisposableScope::create();");
                }

                foreach (var stmt in body.GetAllNotNull())
                {
                    var content = this.BuildStatementContent(stmt);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
                        {
                            lines.Add(line);
                        }
                    }
                }

                if (compact)
                {
                    return lines.Count == 0 ? "{}" : "{ " + string.Join(" ", lines) + " }";
                }

                return lines.Count == 0
                    ? "{\n}"
                    : "{\n" + string.Join("\n", lines.Select(l => "    " + l)) + "\n}";
            }
            finally
            {
                this._context.ExitDisposableBlockScope();
            }
        }

        // Renders a single statement to inline text by delegating to the full statement emitter.
        // Inline bodies (closures, match/switch arms, inline declaration bodies) previously only
        // handled bare expressions and `return`, silently dropping every other statement —
        // including typed-local declarations (`Type $x = ...;`), `if`/`foreach`/`while`/`try`,
        // etc. — which produced broken or truncated output with no diagnostic. Reusing
        // EmitStatement keeps inline bodies consistent with regular function/method bodies.
        //
        // Children are emitted at indent 0 so callers can apply their own relative indentation
        // (PSR-12 closure / switch bodies) without fighting an extra Empty-parent indent level.
        private string BuildStatementContent(IStatement statement)
        {
            var root = EmitItem.Empty(statement, EmitType.FunctionStatement);
            this.EmitStatement(statement, root, EmitType.FunctionStatement);
            var parts = new List<string>();
            foreach (var child in root.SortedChildren())
            {
                var text = child.value.emit(0);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }
            }

            return string.Join("\n", parts);
        }

        private string BuildInlineFunctionExpression(PhpInlineFunctionAst inlineFn)
        {
            if (this.IsAsyncInlineFunction(inlineFn))
            {
                return this.BuildAsyncInlineFunctionExpression(inlineFn);
            }

            var modifiers = this.FormatInlineFunctionModifiers(inlineFn.Modifiers);
            var refPrefix = inlineFn.ReturnsRef ? "&" : "";
            var paramsText = this.FormatInlineFunctionParameterList(inlineFn);
            var returnType = this.FormatInlineFunctionReturnType(inlineFn);

            if (inlineFn.IsArrowFunction)
            {
                var bodyExpr = inlineFn.Body?.GetAllNotNull().FirstOrDefault() is PhpUnaryOpAst ret
                    ? this.BuildExpression(ret.Operand)
                    : "";
                return $"{modifiers}fn{refPrefix}({paramsText}){returnType} => {bodyExpr}";
            }

            var useParts = new List<string>();
            if (inlineFn.LexicalVars?.GetAllNotNull().Any() == true)
            {
                foreach (var lexical in inlineFn.LexicalVars.GetAllNotNull())
                {
                    var rendered = this.FormatVariableListItem(lexical);
                    // Drop explicit `$this` captures — rewritten to WeakReference when active.
                    if (this._context.WeakSelfCaptureVar is not null
                        && string.Equals(rendered.TrimStart('&', '$'), "this", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    useParts.Add(rendered);
                }
            }

            if (this._context.WeakSelfCaptureVar is { } weakVar
                && !useParts.Any(p => string.Equals(p.TrimStart('&'), weakVar, StringComparison.Ordinal)))
            {
                useParts.Add(weakVar);
            }

            foreach (var capture in this.CollectVariantCapturesFor(inlineFn))
            {
                if (!useParts.Any(p => string.Equals(p.TrimStart('&'), capture, StringComparison.Ordinal)))
                {
                    useParts.Add(capture);
                }
            }

            var useClause = useParts.Count > 0 ? " use (" + string.Join(", ", useParts) + ")" : "";
            var body = this.BuildMethodBodyInline(inlineFn.Body);
            // PSR-12 §7: space after `function` (and after `&` when returning by reference).
            var functionKeyword = inlineFn.ReturnsRef ? "function &" : "function ";
            return $"{modifiers}{functionKeyword}({paramsText}){useClause}{returnType} {body}";
        }

        /// <summary>
        /// Return-type suffix for a closure / arrow: authored AST first, otherwise checker-inferred
        /// contextual type spelled with the same erasure as <see cref="BuildTypeExpression"/>.
        /// </summary>
        private string FormatInlineFunctionReturnType(PhpInlineFunctionAst inlineFn)
        {
            if (inlineFn.ReturnType != null)
            {
                var authored = this.BuildTypeExpression(inlineFn.ReturnType);
                return string.IsNullOrWhiteSpace(authored) ? "" : ": " + authored;
            }

            if (!this._context.TryGetInferredClosureSignature(inlineFn, out var inferred)
                || inferred?.ReturnType is null)
            {
                return "";
            }

            var spelled = this.BuildCheckedTypeExpression(inferred.ReturnType);
            return ShouldOmitInferredPhpTypehint(spelled) ? "" : ": " + spelled;
        }

        /// <summary>
        /// Parameter list for a closure / arrow, filling omitted param types from the checker when
        /// contextual typing recovered them.
        /// </summary>
        private string FormatInlineFunctionParameterList(PhpInlineFunctionAst inlineFn)
        {
            var parameters = inlineFn.Parameters;
            if (parameters == null)
            {
                return "";
            }

            this._context.TryGetInferredClosureSignature(inlineFn, out var inferred);
            var inferredParams = inferred?.ParameterTypes;
            var list = parameters.GetAllNotNull().ToList();
            var formatted = new List<string>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                ICheckedType? inferredType = null;
                if (inferredParams is not null && i < inferredParams.Count)
                {
                    inferredType = inferredParams[i];
                }

                formatted.Add(this.FormatParameter(list[i], inferredType));
            }

            if (formatted.Count == 0)
            {
                return "";
            }

            if (formatted.Any(p => p.Contains('\n')))
            {
                var inner = string.Join(",\n", formatted.Select(p => IndentPhpBlock(p, 4)));
                return "\n" + inner + "\n";
            }

            return string.Join(", ", formatted);
        }

        private string BuildCheckedTypeExpression(ICheckedType? type)
            => TypeSpellingHelper.SpellCheckedType(
                type,
                this._context.TypeAliasMap,
                this._context.GlobalScope,
                this._context.Config.NamespacePrefix);

        /// <summary>
        /// <c>mixed</c> / empty inferred hints add no PHP surface beyond an untyped param/return, so
        /// omit them. Authored <c>mixed</c> still emits via the AST path.
        /// </summary>
        private static bool ShouldOmitInferredPhpTypehint(string? spelling)
            => string.IsNullOrWhiteSpace(spelling)
                || string.Equals(spelling, "mixed", StringComparison.OrdinalIgnoreCase);

        private string FormatVariableListItem(PhpVariableAst variable)
        {
            var text = this.BuildVariableExpression(variable);
            // BuildVariableExpression may rewrite `$this` under WeakSelf — use the raw token for use().
            // Extension-receiver `$this`→`$this_` must remain so nested closures capture `$this_`.
            if (variable.VariableToken != null
                && this._context.WeakSelfCaptureVar is not null)
            {
                text = variable.VariableToken.ValueString ?? text;
            }

            return variable.IsRef ? "&" + text : text;
        }

        private string FormatInlineFunctionModifiers(TokenValueListAst? modifiers)
        {
            if (modifiers == null)
            {
                return "";
            }

            var parts = modifiers.GetAllNotNull()
                .Select(t => t.ValueString ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s));
            var text = string.Join(" ", parts);
            return string.IsNullOrWhiteSpace(text) ? "" : text + " ";
        }

        private string BuildYieldExpression(PhpYieldAst yield)
        {
            if (yield.KeyExpr != null && yield.ValueExpr != null)
            {
                return "yield " + this.BuildExpression(yield.KeyExpr) + " => " + this.BuildExpression(yield.ValueExpr);
            }

            if (yield.ValueExpr != null)
            {
                return "yield " + this.BuildExpression(yield.ValueExpr);
            }

            return "yield";
        }

        private string BuildSwitchExpression(PhpConditionalAst conditional)
            => this.BuildMatchExpression(conditional);

        private string BuildMatchExpression(PhpConditionalAst conditional)
        {
            var expr = this.BuildExpression(conditional.Expression);
            if (!conditional.IsMatchSyntax)
            {
                return this.BuildSwitchStatementContent(conditional);
            }

            var arms = conditional.Arms?.GetAllNotNull().ToList() ?? [];
            if (arms.Count == 0)
            {
                return $"match ({expr}) {{\n}}";
            }

            // Multiline match body so control-structure brace sniffs stay happy (and soft line
            // length improves for large arm lists).
            var lines = new List<string> { $"match ({expr}) {{" };
            for (var i = 0; i < arms.Count; i++)
            {
                var armText = this.BuildMatchArm(arms[i]);
                var comma = i < arms.Count - 1 ? "," : ",";
                var armLines = armText.Replace("\r\n", "\n").Split('\n');
                for (var j = 0; j < armLines.Length; j++)
                {
                    var suffix = j == armLines.Length - 1 ? comma : "";
                    lines.Add("    " + armLines[j] + suffix);
                }
            }

            lines.Add("}");
            return string.Join("\n", lines);
        }

        private string BuildMatchArm(PhpConditionalArmAst arm)
        {
            var armBody = this.BuildMatchArmBody(arm.Body);
            if (arm.IsDefault)
            {
                return $"default => {armBody}";
            }

            var conditions = arm.Conditions?.GetAllNotNull().Select(c => this.BuildExpression(c)) ?? [];
            return $"{string.Join(", ", conditions)} => {armBody}";
        }

        private string BuildMatchArmBody(PhpStatementBlockAst? body)
        {
            if (body == null)
            {
                return "null";
            }

            var stmts = body.GetAllNotNull().ToList();
            if (stmts.Count == 1)
            {
                // A `match` arm body is an expression, never a statement. The parser models the
                // `=> expr` arm body the same way it models arrow-function bodies: as an implicit
                // `return` (a unary `return` operator or a return statement). Emitting that verbatim
                // would produce the illegal `'x' => return ...`, so unwrap to the bare expression.
                if (stmts[0] is PhpReturnStatementAst ret)
                {
                    return ret.Expression != null ? this.BuildExpression(ret.Expression) : "null";
                }

                if (stmts[0] is IExpression expr)
                {
                    return this.BuildExpression(this.UnwrapImplicitReturn(expr));
                }
            }

            return this.BuildMethodBodyInline(body);
        }

        // Arrow-function and match-arm bodies are parsed as an implicit `return` wrapped around the
        // real expression (a prefix unary `return` operator). In contexts that require a bare
        // expression (match arms), strip that wrapper.
        private IExpression UnwrapImplicitReturn(IExpression expr)
        {
            if (expr is PhpUnaryOpAst unary
                && unary.IsPrefix
                && unary.Operand != null
                && string.Equals(unary.Operator?.ValueString, "return", StringComparison.OrdinalIgnoreCase))
            {
                return unary.Operand;
            }

            return expr;
        }

        private string BuildSwitchStatementContent(PhpConditionalAst conditional)
        {
            var expr = this.BuildExpression(conditional.Expression);
            var arms = conditional.Arms?.GetAllNotNull().ToList() ?? [];
            var caseTexts = new List<string>();
            foreach (var arm in arms)
            {
                if (arm.IsDefault)
                {
                    caseTexts.Add("default: " + this.BuildCaseBody(arm.Body));
                    continue;
                }

                foreach (var condition in arm.Conditions?.GetAllNotNull() ?? [])
                {
                    caseTexts.Add("case " + this.BuildExpression(condition) + ": " + this.BuildCaseBody(arm.Body));
                }
            }

            return $"switch ({expr}) {{ {string.Join(" ", caseTexts)} }}";
        }

        private string BuildCaseBody(PhpStatementBlockAst? body)
        {
            if (body == null)
            {
                return "break;";
            }

            var stmts = body.GetAllNotNull()
                .Select(s => this.BuildStatementContent(s))
                .Where(s => !string.IsNullOrWhiteSpace(s));
            var text = string.Join(" ", stmts);
            if (!text.Contains("break", StringComparison.Ordinal) && !text.Contains("return", StringComparison.Ordinal))
            {
                text += " break;";
            }

            return text;
        }

        private string BuildDereferenceableExpression(PhpDereferenceableAst dereferenceable)
        {
            if (this.TryBuildHookBackingRead(dereferenceable) is { } backingRead)
            {
                return backingRead;
            }

            if (this.TryBuildParentPropertyHookAccess(dereferenceable) is { } parentHook)
            {
                return parentHook;
            }

            // WeakReference rewrite: `$this->member` → `$__weakSelf->get()?->member`
            // (and `$this?->member` → `$__weakSelf->get()?->member`, without doubling `?`).
            if (this._context.WeakSelfCaptureVar is { } weakVar
                && dereferenceable.Base is PhpVariableAst baseVar
                && IsThisVariable(baseVar)
                && dereferenceable.Suffix is PhpInstanceMemberAccessAst or PhpMemberAccessAst)
            {
                var suffixText = this.BuildDereferenceableSuffix(dereferenceable.Suffix);
                if (suffixText.StartsWith("?->", StringComparison.Ordinal))
                {
                    return $"{weakVar}->get(){suffixText}";
                }

                if (suffixText.StartsWith("->", StringComparison.Ordinal))
                {
                    return $"{weakVar}->get()?{suffixText}";
                }

                return $"{weakVar}->get(){suffixText}";
            }

            if (dereferenceable.Suffix is PhpCallAst callSuffix
                && this.TryBuildGenericVariantCall(dereferenceable, callSuffix) is { } variantCall)
            {
                return variantCall;
            }

            var baseText = this.BuildDereferenceableBase(dereferenceable.Base);
            var suffix = this.BuildDereferenceableSuffix(dereferenceable.Suffix);
            return baseText + suffix;
        }

        private string BuildDereferenceableBase(IDereferenceableBase? baseExpr)
        {
            return baseExpr switch
            {
                null => "",
                PhpDereferenceableAst chain => this.BuildDereferenceableExpression(chain),
                PhpDereferenceableExpressionAst paren => "(" + this.BuildExpression(paren.Expression) + ")",
                PhpVariableAst variable => this.BuildVariableExpression(variable),
                TyhpGenericIdentifierAst generic => generic.ValueString ?? "",
                PhpNameAst name => this.ApplyPendingVariantCallSuffix(name, this.TrackAndBuildName(name)),
                PhpMagicConstantAst magic => magic.ValueString ?? "",
                PhpNewAst newExpr => this.BuildNewExpression(newExpr),
                PhpArrayPairListAst arrayList => this.BuildArrayPairList(arrayList),
                PhpEncapsStringAst encaps => this.BuildEncapsStringExpression(encaps),
                PhpEncapsListAst encapsList => this.BuildEncapsListExpression(encapsList),
                _ => "",
            };
        }

        private string BuildDereferenceableSuffix(IDereferenceableSuffix? suffix)
        {
            return suffix switch
            {
                null => "",
                PhpInstanceMemberAccessAst instance => this.BuildInstanceMemberAccess(instance),
                PhpStaticMemberAccessAst staticAccess => "::"
                    + this.ApplyPendingVariantCallSuffix(
                        staticAccess.Member, this.BuildExpression(staticAccess.Member)),
                PhpClassConstantAccessAst constant => "::"
                    + this.ApplyPendingVariantCallSuffix(
                        constant.Member, this.BuildExpression(constant.Member)),
                PhpArrayAccessAst arrayAccess => this.BuildArrayAccess(arrayAccess),
                PhpCallAst call => "(" + this.FormatArgumentList(call.Arguments) + ")",
                PhpMemberAccessAst memberAccess => this.BuildMemberAccess(memberAccess),
                PhpArgumentListAst argList => "(" + this.FormatArgumentList(argList) + ")",
                _ => "",
            };
        }

        /// <summary>
        /// Extracts the short name from a potentially fully-qualified name.
        /// Used for nameof and typeof emission where only the root identifier matters.
        /// Strips leading backslash and returns only the last segment after the final backslash.
        /// </summary>
        private static string GetShortName(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text ?? "";
            }

            text = text.StartsWith('\\') ? text[1..] : text;
            var idx = text.LastIndexOf('\\');
            return idx >= 0 ? text[(idx + 1)..] : text;
        }

        /// <summary>
        /// Returns the bare member name from an instance member access AST node,
        /// without any name resolution or import tracking.
        /// </summary>
        private string BuildInstanceMemberAccessName(IExpression memberName)
        {
            return memberName.ValueString ?? "";
        }

        private string BuildInstanceMemberAccess(PhpInstanceMemberAccessAst instance)
        {
            var accessor = instance.Accessor?.ValueString ?? "->";

            // A bare identifier after `->` is a property/method name and must be emitted
            // verbatim. It must never run through class-name resolution: PHP identifiers are
            // case-insensitive, so a member like `$this->promise` can otherwise be bound to a
            // same-named class (`Promise`) and miscompile to `$this->\Tyhp\Promise`.
            if (instance.MemberName is PhpNameAst name)
            {
                return accessor + this.ApplyPendingVariantCallSuffix(name, name.ValueString ?? "");
            }

            return accessor + this.BuildExpression(instance.MemberName);
        }

        private string BuildArrayAccess(PhpArrayAccessAst arrayAccess)
        {
            if (arrayAccess.IndexExpression == null)
            {
                return "[]";
            }

            return "[" + this.BuildExpression(arrayAccess.IndexExpression) + "]";
        }

        private string BuildMemberAccess(PhpMemberAccessAst memberAccess)
        {
            var accessor = memberAccess.Accessor?.ValueString ?? "->";
            if (memberAccess.Target != null)
            {
                return this.BuildExpression(memberAccess.Target) + accessor + this.BuildExpression(memberAccess.Key);
            }

            return accessor + this.BuildExpression(memberAccess.Key);
        }

        private string FormatArgumentList(PhpArgumentListAst? arguments)
        {
            if (arguments == null)
            {
                return "";
            }

            return string.Join(", ", arguments.GetAllNotNull().Select(this.FormatArgument));
        }

        private string FormatArgument(PhpArgumentAst argument)
        {
            if (argument.IsVariadic)
            {
                return "..." + this.BuildExpression(argument.Expression);
            }

            if (argument.Name != null)
            {
                var name = argument.Name.ValueString ?? "";
                return name + ": " + this.BuildExpression(argument.Expression);
            }

            return this.BuildExpression(argument.Expression);
        }

        private string BuildNameofExpression(TyhpNameofAst nameof)
        {
            var expr = nameof.Expression;
            var name = expr switch
            {
                PhpVariableAst var => var.VariableToken?.ValueString?.TrimStart('$') ?? "",
                TyhpGenericIdentifierAst g => g.ValueString ?? "",
                PhpNameAst n => GetShortName(n.ValueString),
                PhpInstanceMemberAccessAst m => this.BuildInstanceMemberAccessName(m.MemberName),
                PhpStaticMemberAccessAst s => GetShortName(this.BuildExpression(s.Member)),
                PhpClassConstantAccessAst c => GetShortName(this.BuildExpression(c.Member)),
                PhpDereferenceableAst deref when deref.Base is PhpInstanceMemberAccessAst instance => this.BuildInstanceMemberAccessName(instance.MemberName),
                PhpDereferenceableAst deref when deref.Suffix is PhpStaticMemberAccessAst staticAccess => GetShortName(this.BuildExpression(staticAccess.Member)),
                // nameof($o->name): the member access is the dereferenceable suffix, not the base.
                PhpDereferenceableAst deref when deref.Suffix is PhpInstanceMemberAccessAst instanceMember => this.BuildInstanceMemberAccessName(instanceMember.MemberName),
                // nameof(C::A): a class constant access is the suffix.
                PhpDereferenceableAst deref when deref.Suffix is PhpClassConstantAccessAst constantAccess => GetShortName(this.BuildExpression(constantAccess.Member)),
                PhpInlineFunctionAst fn when PropertyPathSupport.TryGetNameofPropertyPathLastSegment(fn, out var lastSegment) => lastSegment,
                _ => this.BuildExpression(expr),
            };

            return "'" + name.Replace("'", "\\'") + "'";
        }

        /// <summary>
        /// Fully-qualified name of the runtime <c>Type</c> reflection class provided by the core package.
        /// </summary>
        private const string RuntimeTypeClass = "\\Tyhp\\Type";

        private static readonly HashSet<string> ScalarTypeFactoryNames = new(StringComparer.Ordinal)
        {
            "string", "int", "float", "bool", "null", "void",
            "mixed", "never", "array", "object", "callable", "iterable", "resource",
        };

        /// <summary>
        /// Materializes <c>typeof(T)</c> into a runtime <c>\Tyhp\Type</c> value.
        ///
        /// A bareword reference to an in-scope generic type parameter (which the binder intentionally
        /// leaves unbound, see <c>CompileTimeRule.CheckTypeof</c>) is read from the runtime
        /// generic-tracking lookup supplied by the <c>GenericObject</c> trait. Classes that use
        /// <c>typeof(T)</c> are flagged <c>RequiresRuntimeGenericTracking</c> so the emitter injects
        /// that trait and constructor initialization (Story 11 Phase 8).
        ///
        /// A reference to a declared type emits <c>\Tyhp\Type::fromClassName(Foo::class)</c>, and a
        /// built-in scalar keyword emits the matching <c>\Tyhp\Type</c> factory (e.g. <c>::string()</c>).
        /// </summary>
        private string BuildTypeofExpression(TyhpTypeofAst typeofExpr)
        {
            var expr = typeofExpr.Expression;

            if (expr is PhpNameAst name)
            {
                var simpleName = (name.ValueString ?? string.Empty).Trim();

                // typeof of a declared class -> \Tyhp\Type::fromClassName(ShortName::class).
                // The binder intentionally leaves typeof args unbound (see CompileTimeRule.CheckTypeof),
                // so a bound symbol is not available; resolve the name against the bound global scope
                // to tell a real declared class apart from a generic type parameter / unbound bareword.
                // With no bound scope (parse-only emit) nothing resolves and the generic-lookup path
                // below is taken, matching type parameters and unbound names.
                if (simpleName.Length > 0
                    && !simpleName.Contains('\\')
                    && this.TryResolveDeclaredClass(simpleName))
                {
                    return $"{RuntimeTypeClass}::fromClassName('{simpleName}'::class)";
                }

                if (name.BoundSymbol is ObjectDeclarationSymbol)
                {
                    return $"{RuntimeTypeClass}::fromClassName('{GetShortName(name.ValueString)}'::class)";
                }

                if (name.BoundSymbol is null
                    && simpleName.Length > 0
                    && !simpleName.Contains('\\'))
                {
                    // A generic the callable declares itself arrives as a binder-captured parameter of the
                    // Mechanism D variant, which is what makes it work in a static method or a free
                    // function where there is no instance to read from.
                    if (this.IsVariantGenericParamName(simpleName))
                    {
                        return this.BuildVariantTypeofLookup(simpleName);
                    }

                    // A class generic parameter is recorded on the instance, so the lookup cannot run
                    // without `$this`. CompileTimeRule rejects that shape (TYHP4148); fall back to
                    // `mixed` here so a path that bypasses the checker still emits valid PHP.
                    if (this._currentMemberIsStatic && this.IsObjectGenericParamName(simpleName))
                    {
                        return $"{RuntimeTypeClass}::mixed()";
                    }

                    // Parenthesized: a bare method-call lookup is fine under most operators, but
                    // cast prefixes bind tighter than `->` in some mental models and tests assert
                    // a grouped form for typeof(T).
                    return this.BuildGenericResolvedTypeLookupCall(simpleName) is { } lookup
                        ? $"($this->{lookup})"
                        : $"{RuntimeTypeClass}::mixed()";
                }
            }

            if (expr is PhpBuiltinTypeAst builtin
                && builtin.Identifier is { } identifier
                && ScalarTypeFactoryNames.Contains(identifier))
            {
                return $"{RuntimeTypeClass}::{identifier}()";
            }

            return $"{RuntimeTypeClass}::mixed()";
        }

        private string BuildDefaultExpression(TyhpDefaultAst defaultExpr)
        {
            var typeText = this.BuildTypeExpression(defaultExpr.TypeExpression);
            if (typeText.StartsWith('?'))
            {
                return "null";
            }

            if (!typeText.Contains('\\'))
            {
                // `default(T)` on a generic the callable declares itself resolves through the
                // Mechanism D binder parameter: the zero value of whatever type the call site bound, or
                // null when the caller passed nothing.
                if (this.IsVariantGenericParamName(typeText))
                {
                    return this.BuildVariantDefaultLookup(typeText);
                }

                if (this.IsObjectGenericParamName(typeText))
                {
                    // A class generic parameter is recorded on the instance, so the lookup cannot run
                    // without `$this`. CompileTimeRule rejects that shape (TYHP4152); fall back to the
                    // erased answer here so a path that bypasses the checker still emits valid PHP.
                    if (this._currentMemberIsStatic
                        || this.BuildGenericDefaultValueLookupCall(typeText) is not { } lookup)
                    {
                        return "null";
                    }

                    return $"$this->{lookup}";
                }
            }

            return typeText.ToLowerInvariant() switch
            {
                "int" => "0",
                "float" => "0.0",
                "string" => "''",
                "bool" => "false",
                "array" => "[]",
                _ => "null",
            };
        }

        /// <summary>
        /// Emits a runtime existence check for <c>variable_exists($v)</c> when the checker did not
        /// fold it to a boolean literal. The variable name is taken from the AST as a string
        /// literal (never evaluated), and <c>\array_key_exists</c> is used instead of
        /// <c>isset</c> so a defined variable holding <c>null</c> still counts as present.
        /// </summary>
        private string BuildVariableExistsExpression(TyhpVariableExistsAst variableExists)
        {
            var name = TryExtractVariableExistsName(variableExists.Expression);
            if (name is null)
            {
                // Recovery only — CompileTimeRule should have rejected non-constant forms.
                // Do not emit EmitterTyhpConstructNotImplemented (5008): the construct *is*
                // implemented; the argument was invalid.
                return "false";
            }

            return $"\\array_key_exists('{name.Replace("'", "\\'")}', \\get_defined_vars())";
        }

        /// <summary>
        /// Extracts the compile-time variable name from a <c>variable_exists</c> argument.
        /// Accepts a simple variable (<c>$v</c>) or a string / token literal (<c>'v'</c>).
        /// Returns <c>null</c> for forms that cannot be resolved to a fixed name (e.g. <c>$$v</c>).
        /// </summary>
        private static string? TryExtractVariableExistsName(IExpression? expression)
        {
            switch (expression)
            {
                case PhpVariableAst variable:
                    return TryExtractSimpleVariableName(variable);

                case PhpEncapsListAst encapsList:
                    return TryExtractConstantEncapsName(encapsList);

                case PhpEncapsStringAst encaps:
                    return NormalizeVariableExistsName(
                        UnquotePhpStringLiteral(encaps.ValueString ?? encaps.TokenValue?.ValueString));

                case PhpScalarAst { ScalarType: PhpScalarType.String } scalar:
                {
                    var literal = UnquotePhpStringLiteral(scalar.ValueString);
                    if (string.IsNullOrEmpty(literal))
                    {
                        var token = scalar.AstChildren.ElementAtOrDefault(0) as TokenValueAst;
                        literal = UnquotePhpStringLiteral(token?.ValueString);
                    }

                    return NormalizeVariableExistsName(literal);
                }

                case TokenValueAst token:
                    return NormalizeVariableExistsName(UnquotePhpStringLiteral(token.ValueString));

                default:
                    return null;
            }
        }

        /// <summary>
        /// Returns the unquoted contents of a single-part constant encaps string list
        /// (e.g. <c>'customHandler'</c>), or <c>null</c> for interpolated / multi-part strings.
        /// </summary>
        private static string? TryExtractConstantEncapsName(PhpEncapsListAst encapsList)
        {
            var parts = encapsList.GetAllNotNull().ToList();
            if (parts.Count != 1 || parts[0] is not PhpEncapsStringAst encaps)
            {
                return null;
            }

            return NormalizeVariableExistsName(
                UnquotePhpStringLiteral(encaps.ValueString ?? encaps.TokenValue?.ValueString));
        }

        private static string? TryExtractSimpleVariableName(PhpVariableAst variable)
        {
            var raw = variable.VariableToken?.ValueString;
            if (string.IsNullOrEmpty(raw))
            {
                // Variable-variables ($$x) have an expression child that is itself a variable —
                // the target name is not a compile-time constant, so leave those for the caller
                // to reject. A TokenValueAst child is the ordinary `$name` form without a token
                // on the outer node.
                if (variable.VariableExpression is PhpVariableAst)
                {
                    return null;
                }

                if (variable.VariableExpression is TokenValueAst token
                    && !string.IsNullOrEmpty(token.ValueString))
                {
                    raw = token.ValueString;
                }
                else
                {
                    raw = variable.ValueString;
                }
            }

            return NormalizeVariableExistsName(raw);
        }

        private static string? NormalizeVariableExistsName(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            var name = raw.StartsWith('$') ? raw[1..] : raw;
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>
        /// Reports whether <paramref name="simpleName"/> resolves to a declared
        /// class/interface/enum/trait in the bound global scope. Used by <c>typeof</c> emission to
        /// distinguish a real declared class from a generic type parameter, which the binder
        /// intentionally leaves unbound (see <c>CompileTimeRule.CheckTypeof</c>). When no binding
        /// is available (parse-only emit) the scope is empty and this returns false, so the
        /// generic-lookup fallback path is taken.
        /// </summary>
        private bool TryResolveDeclaredClass(string simpleName)
        {
            if (string.IsNullOrEmpty(simpleName))
            {
                return false;
            }

            return ScopeContainsObjectDeclarationNamed(this._context.GlobalScope, simpleName);
        }

        private static bool ScopeContainsObjectDeclarationNamed(IBaseScope scope, string name, int depth = 0)
        {
            if (depth > 500)
            {
                return false;
            }

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is ObjectDeclarationSymbol obj
                    && string.Equals(obj.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                if (ScopeContainsObjectDeclarationNamed(child, name, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildTypedVarExpression(TyhpTypedVarExprAst typedVar)
        {
            var varText = this.BuildExpression(typedVar.Variable);
            if (typedVar.AssignedExpression != null)
            {
                var assignOp = typedVar.IsRef ? " =& " : " = ";
                return varText + assignOp + this.BuildExpression(typedVar.AssignedExpression);
            }

            return varText;
        }
    }
}
