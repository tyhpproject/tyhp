using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker
{
    public sealed partial class TypeInferrer
    {
        private ICheckedType InferExpressionTypeCore(IBase2Ast expression, CheckerState state)
        {
            switch (expression)
            {
                case TokenValueAst token when IsIntegerToken(token):
                    return CheckedTypes.Int;

                case TokenValueAst token when IsFloatToken(token):
                    return CheckedTypes.Float;

                case PhpScalarAst scalar:
                    return InferScalar(scalar);

                case PhpEncapsListAst encapsList:
                    return InferEncapsList(encapsList);

                case PhpNameAst nameExpr:
                    return InferNamedConstant(nameExpr);

                case PhpMagicConstantAst magic:
                    return InferMagicConstant(magic);

                case PhpVariableAst variable:
                    return InferVariable(variable, state);

                case PhpBinaryOpAst binary:
                    return InferBinary(binary, state);

                case PhpUnaryOpAst unary:
                    return InferUnary(unary, state);

                case PhpTernaryOpAst ternary:
                    return InferTernary(ternary, state);

                // Bare `(expr)` from `fullyDereferenceable` is a PhpDereferenceableExpressionAst
                // (not wrapped in PhpDereferenceableAst). Without this arm, parenthesized
                // ternary/if conditions type as unresolved (Elvis/ternary audit #2).
                case PhpDereferenceableExpressionAst wrapped:
                    return InferExpressionType(wrapped.Expression!, state);

                case PhpDereferenceableAst dereferenceable:
                    return InferDereferenceable(dereferenceable, state);

                case PhpNewAst newExpr:
                    return InferNew(newExpr, state);

                case PhpArrayAst array:
                    return InferArrayLiteral(array.ArrayPairs?.GetAllNotNull().ToList() ?? [], state);

                case PhpArrayPairListAst pairList:
                    // Short-array syntax `[…]` is a bare pair-list expression (not wrapped in
                    // PhpArrayAst) via dereferenceableScalar.
                    return InferArrayLiteral(pairList.GetAllNotNull().ToList(), state);

                case PhpInlineFunctionAst closure:
                    return InferClosure(closure, state);

                case TyhpNameofAst nameofExpr:
                    return NameofTypeInferrer.Infer(
                        nameofExpr, state, _symbolTree, _globalScope,
                        InferExpressionType,
                        (typeAst, s) => ResolveTypeExpression(typeAst, s));

                case TyhpTypeofAst:
                    return CheckerHelpers.ResolveNamedType("Tyhp\\Type", _symbolTree, _globalScope);

                case TyhpDefaultAst defaultExpr:
                    return defaultExpr.TypeExpression is not null
                        ? InferDefault(ResolveTypeExpression(defaultExpr.TypeExpression, state))
                        : CheckedTypes.Unresolved;

                case TyhpVariableExistsAst:
                    return CheckedTypes.Bool;

                case PhpIssetStatementAst:
                case PhpEmptyStatementAst:
                    return CheckedTypes.Bool;

                case PhpConditionalAst conditional when conditional.IsMatchSyntax:
                    return InferMatch(conditional, state);

                case IExpression expr when expression.BoundSymbol is FunctionDeclarationSymbol func:
                    return InferCallableSymbol(func, state);

                default:
                    return CheckedTypes.Unresolved;
            }
        }

        /// <summary>
        /// Types <c>default(X)</c> from the spelled type <paramref name="spelledType"/>.
        ///
        /// Scalars and arrays fold to a real value of that type, but an object type has no
        /// author-supplied default, so the emitter produces a literal <c>null</c>. Reporting the
        /// spelled (non-nullable) class type would tell the checker it holds an instance while the
        /// runtime holds null, which defeats null safety entirely; yielding the null type instead
        /// routes the mismatch through the ordinary compatibility checks.
        ///
        /// Generic parameters are deliberately excluded — they resolve to
        /// <see cref="GenericTypeParameterSymbol"/> rather than
        /// <see cref="ObjectDeclarationSymbol"/> and are handled by runtime generic resolution.
        /// </summary>
        private static ICheckedType InferDefault(ICheckedType spelledType) =>
            DefaultsToNull(spelledType) ? CheckedTypes.Null : spelledType;

        private static bool DefaultsToNull(ICheckedType type) =>
            type switch
            {
                // Already permits null, so the spelled type describes the emitted value.
                NullableCheckedType => false,
                GenericCheckedType generic => DefaultsToNull(generic.BaseType),
                SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol } => true,
                SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol { Name: "object" } } => true,
                _ => false,
            };

        private static ICheckedType InferNamedConstant(PhpNameAst name)
        {
            // Underlying symbol names match the registered builtins (`true`/`false`), not `bool`,
            // so assignability against declared `: true` / `true $x` (SimpleCheckedType("true"))
            // succeeds via underlying equality as well as the dedicated bool-literal rules.
            return name.ValueString?.ToLowerInvariant() switch
            {
                "null" => CheckedTypes.Null,
                "true" => new LiteralCheckedType(true, new SimpleCheckedType(new BuiltInTypeSymbol("true"))),
                "false" => new LiteralCheckedType(false, new SimpleCheckedType(new BuiltInTypeSymbol("false"))),
                _ => CheckedTypes.Unresolved,
            };
        }

        private static ICheckedType InferScalar(PhpScalarAst scalar)
        {
            return scalar.ScalarType switch
            {
                PhpScalarType.Integer or PhpScalarType.OctalNumber or PhpScalarType.HexNumber or PhpScalarType.BinaryNumber
                    => scalar.ValueInt64 is long value
                        ? new LiteralCheckedType(value, new SimpleCheckedType(new BuiltInTypeSymbol("int")))
                        : CheckedTypes.Int,
                PhpScalarType.Float
                    => scalar.ValueDecimal is decimal dec
                        ? new LiteralCheckedType(dec, new SimpleCheckedType(new BuiltInTypeSymbol("float")))
                        : CheckedTypes.Float,
                PhpScalarType.String
                    => scalar.ValueString is string s
                        ? new LiteralCheckedType(s, new SimpleCheckedType(new BuiltInTypeSymbol("string")))
                        : CheckedTypes.String,
                _ => CheckedTypes.Unresolved,
            };
        }

        private static ICheckedType InferEncapsList(PhpEncapsListAst encapsList)
        {
            if (!PhpStringLiteralHelper.TryGetStaticLiteral(encapsList, out var literal))
            {
                return CheckedTypes.String;
            }

            return new LiteralCheckedType(literal, new SimpleCheckedType(new BuiltInTypeSymbol("string")));
        }

        private static ICheckedType InferMagicConstant(PhpMagicConstantAst magic)
        {
            switch (GetTokenType(magic))
            {
                case TyhpParser.T_LINE:
                    return CheckedTypes.Int;
                case TyhpParser.T_FILE:
                case TyhpParser.T_DIR:
                case TyhpParser.T_NS_C:
                case TyhpParser.T_CLASS_C:
                case TyhpParser.T_TRAIT_C:
                case TyhpParser.T_FUNC_C:
                case TyhpParser.T_PROPERTY_C:
                case TyhpParser.T_METHOD_C:
                    return CheckedTypes.String;
            }

            var text = magic.ValueString?.ToLowerInvariant();
            return text switch
            {
                "true" => new LiteralCheckedType(
                    true,
                    new SimpleCheckedType(new BuiltInTypeSymbol("true"))),
                "false" => new LiteralCheckedType(
                    false,
                    new SimpleCheckedType(new BuiltInTypeSymbol("false"))),
                "null" => CheckedTypes.Null,
                _ => CheckedTypes.String,
            };
        }

        private ICheckedType InferVariable(PhpVariableAst variable, CheckerState state)
        {
            var name = GetVariableName(variable);
            if (name is null)
            {
                return CheckedTypes.Unresolved;
            }

            if (variable.BoundSymbol is VariableSymbol symbol)
            {
                var lookedUp = state.LookupVariable(name);
                if (lookedUp is not null)
                {
                    if (lookedUp.NarrowedType is not null)
                    {
                        _checker.RecordNarrowedType(variable, lookedUp.EffectiveType);
                    }

                    return lookedUp.EffectiveType;
                }

                if (symbol.DeclaredType is not null)
                {
                    return ResolveTypeExpression(symbol.DeclaredType, state);
                }
            }

            return state.LookupVariable(name) is { } fromScope
                ? RecordNarrowedIfPresent(fromScope, variable, fromScope.EffectiveType)
                : CheckedTypes.Unresolved;
        }

        private ICheckedType RecordNarrowedIfPresent(VariableState varState, PhpVariableAst variable, ICheckedType type)
        {
            if (varState.NarrowedType is not null)
            {
                _checker.RecordNarrowedType(variable, type);
            }

            return type;
        }

        private ICheckedType InferBinary(PhpBinaryOpAst binary, CheckerState state)
        {
            var token = GetTokenType(binary.Operator);
            if (token == TyhpParser.T_TYHP_WITH)
            {
                return binary.Left is not null
                    ? InferExpressionType(binary.Left, state)
                    : CheckedTypes.Unresolved;
            }

            if (PhpAssignmentOperatorExtensions.FromToken(token) is PhpAssignmentOperator assignmentOp)
            {
                return InferAssignment(binary, assignmentOp, state);
            }

            var left = InferExpressionType(binary.Left!, state);
            var right = InferExpressionType(binary.Right!, state);
            var op = PhpBinaryOperatorExtensions.FromToken(token);

            if (op is null)
            {
                return CheckedTypes.Unresolved;
            }

            return op.Value switch
            {
                PhpBinaryOperator.InstanceOf => CheckedTypes.Bool,
                PhpBinaryOperator.Coalesce => InferNullCoalesce(left, right),
                // PHP 8.5 `|>`: result is the return type of invoking the RHS callable with the LHS.
                PhpBinaryOperator.Pipe => InferPipeResult(left, right),
                _ => InferBinaryOperatorResult(op.Value, left, right, state),
            };
        }

        /// <summary>
        /// Binary operator result: prefer a matching operator-overload return type (Story 11 §8),
        /// otherwise native PHP numeric / comparison promotion.
        /// </summary>
        private ICheckedType InferBinaryOperatorResult(
            PhpBinaryOperator op,
            ICheckedType left,
            ICheckedType right,
            CheckerState state)
        {
            var overloadable = ToOverloadableBinaryOperator(op);
            if (overloadable != OverloadableOperator.Invalid
                && TryInferBinaryOperatorOverloadReturn(overloadable, left, right, state, out var overloadReturn))
            {
                return overloadReturn;
            }

            return InferBinaryOperator(op, left, right);
        }

        /// <summary>
        /// PHP 8.5 <c>|&gt;</c>: result type is the return type of calling the RHS with one argument.
        /// Prefers an exact arity-1 callable facet; opaque <c>callable</c>/<c>\Closure</c> without
        /// a signature yields <c>mixed</c>; otherwise unresolved (checker diagnoses the RHS).
        /// Open-generic facets (e.g. <c>$xs |&gt; keep_keys(...)</c>) bind type parameters from the
        /// LHS the same way direct / first-class-callable invocation does.
        /// </summary>
        private ICheckedType InferPipeResult(ICheckedType left, ICheckedType rhsCallable)
        {
            if (CallableArityFacetBuilder.TrySelectCallableFacet(rhsCallable, argumentCount: 1, out var facet)
                && facet is not null)
            {
                return ResolveCallableFacetReturnFromArgument(facet, left);
            }

            var facets = CallableArityFacetBuilder.GetCallableFacets(rhsCallable);
            if (facets.Count > 0)
            {
                // Typed callable that cannot accept exactly one argument — leave unresolved so
                // assignment sites do not silently accept a guessed return type.
                return CheckedTypes.Unresolved;
            }

            // Bare `callable` / `\Closure` / `__invoke` objects: no arity facets → mixed.
            if (IsOpaquePipeCallable(rhsCallable))
            {
                return CheckedTypes.Mixed;
            }

            return CheckedTypes.Unresolved;
        }

        /// <summary>
        /// Binds unbound facet generics from a single pipe/call argument, then returns the
        /// substituted return type.
        /// </summary>
        private ICheckedType ResolveCallableFacetReturnFromArgument(
            CallableCheckedType facet,
            ICheckedType argumentType)
        {
            if (!CallableGenericInference.FacetNeedsArgumentInference(facet)
                || !CallableGenericInference.TryInferFacetBindings(
                    facet, [argumentType], out var bindings)
                || bindings.Count == 0)
            {
                return facet.ReturnType;
            }

            return TypeComparer.ResolveGenericTypeBySymbol(
                facet.ReturnType, bindings, _symbolTree, _globalScope);
        }

        private bool IsOpaquePipeCallable(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (Rules.CheckerHelpers.IsBuiltInName(type, "callable"))
            {
                return true;
            }

            if (CallableArityFacetBuilder.IsClosureTypeName(type))
            {
                return true;
            }

            if (TypeComparer.TryGetObjectDeclaration(type) is not { } obj)
            {
                return false;
            }

            var member = _symbolTree.ResolveMember("__invoke", obj, new Domain.Diagnostics.DiagnosticBag());
            return member is Binder.Symbols.ObjectMethodSymbol { IsStatic: false };
        }

        private ICheckedType InferAssignment(
            PhpBinaryOpAst binary,
            PhpAssignmentOperator assignmentOp,
            CheckerState state)
        {
            var rightType = InferExpressionType(binary.Right!, state);

            // Compound ops must read the pre-assignment left type before we reset narrowing /
            // write the result back into CheckerState.
            ICheckedType resultType;
            if (assignmentOp == PhpAssignmentOperator.Assign)
            {
                resultType = rightType;
            }
            else
            {
                var leftType = InferExpressionType(binary.Left!, state);
                resultType = InferCompoundAssignmentResult(assignmentOp, leftType, rightType, state);
            }

            // Plain `=` and every compound assign (`??=`, `+=`, …) must refresh tracked type /
            // narrowing so later guards (`!== null`, `instanceof`, …) see the post-assignment type.
            if (binary.Left is PhpVariableAst variable)
            {
                var name = GetVariableName(variable);
                if (name is not null)
                {
                    TypeNarrowingRule.ResetNarrowingOnAssignment(name, state);
                    state.AssignVariable(name, resultType, _diagnostics);
                }
            }
            else if (TryGetThisPropertyAssignmentTarget(binary.Left, out var propertyKey))
            {
                // `=` / `??=` are write-only / existence-probe writes — mark initialized + type.
                // Other compounds (`+=`, `.=`, …) read first; must not suppress TYHP4157 by
                // marking init here (TypeCompatibilityRule resolves the binary before children).
                if (assignmentOp is PhpAssignmentOperator.Assign or PhpAssignmentOperator.CoalesceAssign)
                {
                    state.AssignPropertyType(propertyKey!, resultType);
                }
                else if (state.LookupPropertyInit(propertyKey!) is { IsDefinitelyInitialized: true })
                {
                    state.NarrowProperty(propertyKey!, resultType);
                }
            }

            return resultType;
        }

        /// <summary>
        /// True when <paramref name="left"/> is a plain <c>$this->prop</c> write target.
        /// </summary>
        private static bool TryGetThisPropertyAssignmentTarget(IExpression? left, out string? propertyKey)
        {
            propertyKey = null;
            if (left is not PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst memberAccess } dereferenceable)
            {
                return false;
            }

            if (dereferenceable.Base is not PhpVariableAst receiver
                || !Rules.CheckerHelpers.IsThisVariable(receiver))
            {
                return false;
            }

            var memberName = GetExpressionText(memberAccess.MemberName);
            if (memberName is null || memberName.StartsWith('{'))
            {
                return false;
            }

            propertyKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            return true;
        }

        private ICheckedType InferUnary(PhpUnaryOpAst unary, CheckerState state)
        {
            // Story 14.5: keyword call forms with PhpArgumentListAst operand type from the
            // ExtCore stub (FCC → callable; invocation → return type). Unary clone stays below.
            if (TryInferKeywordConstructCall(unary, state, out var keywordCallType))
            {
                return keywordCallType;
            }

            var token = GetTokenType(unary.Operator);
            var operand = InferExpressionType(unary.Operand!, state);

            if (token == TyhpParser.T_CLONE)
            {
                return operand;
            }

            if (string.Equals(unary.Operator?.ValueString, "await", StringComparison.OrdinalIgnoreCase)
                || token == TyhpParser.T_TYHP_AWAIT)
            {
                return InferAwaitOperand(unary.Operand, state);
            }

            // PHP 8.5 `(void)` is a statement/list discard, not a value-producing cast. Operand is
            // still typed above; the cast itself is void / non-value (assignability rejects it).
            if (token == TyhpParser.T_VOID_CAST)
            {
                return CheckedTypes.Void;
            }

            if (IsCastToken(token))
            {
                return InferCastType(token);
            }

            var overloadable = ToOverloadableUnaryOperator(token);
            if (overloadable != OverloadableOperator.Invalid
                && TryInferUnaryOperatorOverloadReturn(overloadable, operand, state, out var overloadReturn))
            {
                return overloadReturn;
            }

            return token switch
            {
                TyhpParser.T_SYM_BANG => CheckedTypes.Bool,
                TyhpParser.T_SYM_PLUS or TyhpParser.T_SYM_MINUS or TyhpParser.T_SYM_TILDE
                    => InferUnaryNumeric(token, operand),
                // Prefix/postfix ++/-- without an overload keep the operand type (PHP numeric /
                // object identity); unresolved when the operand itself is unresolved.
                TyhpParser.T_INC or TyhpParser.T_DEC => operand,
                _ => CheckedTypes.Unresolved,
            };
        }

        /// <summary>
        /// Compound-assign result type: overload return when the underlying binary op matches a
        /// declared form, otherwise native promotion (same as the non-assign binary).
        /// </summary>
        private ICheckedType InferCompoundAssignmentResult(
            PhpAssignmentOperator assignmentOp,
            ICheckedType leftType,
            ICheckedType rightType,
            CheckerState state)
        {
            if (assignmentOp == PhpAssignmentOperator.CoalesceAssign)
            {
                return InferNullCoalesce(leftType, rightType);
            }

            var overloadable = ToOverloadableAssignmentOperator(assignmentOp);
            if (overloadable != OverloadableOperator.Invalid
                && TryInferBinaryOperatorOverloadReturn(
                    overloadable, leftType, rightType, state, out var overloadReturn))
            {
                return overloadReturn;
            }

            return assignmentOp switch
            {
                PhpAssignmentOperator.ConcatAssign => InferBinaryOperator(PhpBinaryOperator.Concat, leftType, rightType),
                PhpAssignmentOperator.PlusAssign => InferBinaryOperator(PhpBinaryOperator.Plus, leftType, rightType),
                PhpAssignmentOperator.MinusAssign => InferBinaryOperator(PhpBinaryOperator.Minus, leftType, rightType),
                PhpAssignmentOperator.MultiplyAssign => InferBinaryOperator(PhpBinaryOperator.Multiply, leftType, rightType),
                PhpAssignmentOperator.DivideAssign => InferBinaryOperator(PhpBinaryOperator.Divide, leftType, rightType),
                PhpAssignmentOperator.ModuloAssign => InferBinaryOperator(PhpBinaryOperator.Modulo, leftType, rightType),
                PhpAssignmentOperator.PowerAssign => InferBinaryOperator(PhpBinaryOperator.Power, leftType, rightType),
                PhpAssignmentOperator.BitwiseAndAssign => InferBinaryOperator(PhpBinaryOperator.BitwiseAnd, leftType, rightType),
                PhpAssignmentOperator.BitwiseOrAssign => InferBinaryOperator(PhpBinaryOperator.BitwiseOr, leftType, rightType),
                PhpAssignmentOperator.BitwiseXorAssign => InferBinaryOperator(PhpBinaryOperator.BitwiseXor, leftType, rightType),
                PhpAssignmentOperator.ShiftLeftAssign => InferBinaryOperator(PhpBinaryOperator.ShiftLeft, leftType, rightType),
                PhpAssignmentOperator.ShiftRightAssign => InferBinaryOperator(PhpBinaryOperator.ShiftRight, leftType, rightType),
                _ => rightType,
            };
        }

        /// <summary>
        /// Types <c>exit(...)</c> / <c>die(...)</c> / <c>clone(...)</c> call forms from their
        /// ExtCore tyhpdef symbols. Returns <c>true</c> when the node is a keyword call form.
        /// <c>clone(...)</c> preserves the type of the cloned object (like unary
        /// <c>clone $x</c>); the stub's declared <c>object</c> return is only used for
        /// arity / named-arg checking and FCC identity.
        /// </summary>
        private bool TryInferKeywordConstructCall(
            PhpUnaryOpAst unary,
            CheckerState state,
            out ICheckedType result)
        {
            result = CheckedTypes.Unresolved;
            if (unary.Operand is not PhpArgumentListAst arguments)
            {
                return false;
            }

            var name = unary.Operator?.ValueString;
            if (!CheckerHelpers.IsKeywordConstructName(name))
            {
                return false;
            }

            var function = unary.BoundSymbol as FunctionDeclarationSymbol
                ?? CheckerHelpers.ResolveKeywordConstructFunction(
                    name!, state, _symbolTree, _globalScope);
            if (function is null)
            {
                return true;
            }

            if (CheckerHelpers.IsFirstClassCallableArgumentList(arguments))
            {
                result = InferCallableSymbol(function, state);
                return true;
            }

            if (string.Equals(name, "clone", StringComparison.OrdinalIgnoreCase))
            {
                result = InferCloneCallResultType(arguments, state);
                return true;
            }

            // Keyword constructs that are also generic free functions (rare) must resolve the
            // declared return type under the callee's FunctionGenerics — same rule as
            // ResolveFunctionReturnType / Story 11 audit #5.
            result = ResolveFunctionReturnType(function, state);
            return true;
        }

        /// <summary>
        /// Result type of <c>clone($obj, …)</c> / <c>clone(object: $obj, …)</c> is the type of
        /// the cloned object argument (mirrors unary <c>clone $obj</c>).
        /// </summary>
        private ICheckedType InferCloneCallResultType(PhpArgumentListAst arguments, CheckerState state)
        {
            IExpression? objectExpr = null;

            foreach (var arg in arguments.GetAllNotNull())
            {
                if (arg.Expression is null)
                {
                    continue;
                }

                var bareName = arg.Name?.ValueString?.TrimStart('$');
                if (string.Equals(bareName, "object", StringComparison.OrdinalIgnoreCase))
                {
                    objectExpr = arg.Expression;
                    break;
                }
            }

            if (objectExpr is null)
            {
                foreach (var arg in arguments.GetAllNotNull())
                {
                    if (arg.Name is null && arg.Expression is not null)
                    {
                        objectExpr = arg.Expression;
                        break;
                    }
                }
            }

            return objectExpr is not null
                ? InferExpressionType(objectExpr, state)
                : CheckedTypes.FromSymbol(new BuiltInTypeSymbol("object"));
        }

        private ICheckedType InferTernary(PhpTernaryOpAst ternary, CheckerState state)
        {
            // Narrow each branch by its condition, mirroring if/else narrowing: the true branch
            // sees the positive narrowing of the condition and the false branch the negative. This
            // lets `$x instanceof T ? $x->m() : $x` resolve the method on the narrowed `$x`.
            // Also merge assignment state from both arms back into the parent (Prop-init #6) so
            // `$x` assigned in both arms is definitely assigned afterward.
            //
            // Also runs each arm through the full rule pipeline (`_checker.CheckNode`), not just
            // type inference: `InferExpressionType` memoizes by node identity
            // (`TyhpChecker._expressionTypes`), so this method is the SOLE processing a ternary
            // ever gets when it is only reachable via `ResolveExpressionType` (e.g. as a call
            // argument, or as the RHS of a plain assignment — `TypeCompatibilityRule.CheckBinaryOp`
            // resolves `binary.Right`'s type before `NullSafetyRule` re-walks it via `CheckNode`).
            // `ControlFlowRule.CheckTernary` now defers its own arm-walk/merge to this same cached
            // call, so exactly one caller performs the real Split/CheckNode/Merge regardless of
            // which reaches the node first — previously, whichever ran first (typically this
            // type-inference path) already merged both arms' effects into the live parent state,
            // so the second, "real" pass derived its branch states from an already-merged baseline
            // and could miss a read that happens before its own arm's assignment.
            ICheckedType trueType;
            CheckerState? trueState = null;
            if (ternary.TrueExpr is not null)
            {
                if (ternary.Condition is not null)
                {
                    trueState = state.Split(ScopeType.CodeBlock);
                    TypeNarrowingRule.ApplyConditionNarrowing(
                        ternary.Condition, trueState, this, _symbolTree, _globalScope, positive: true);
                    _checker.CheckNode(ternary.TrueExpr, trueState);
                    trueType = InferExpressionType(ternary.TrueExpr, trueState);
                }
                else
                {
                    _checker.CheckNode(ternary.TrueExpr, state);
                    trueType = InferExpressionType(ternary.TrueExpr, state);
                }
            }
            else
            {
                // Elvis form (`a ?: b`): the true value is the (truthy) condition itself.
                trueType = InferExpressionType(ternary.Condition!, state);
            }

            ICheckedType falseType;
            CheckerState? falseState = null;
            if (ternary.Condition is not null && ternary.FalseExpr is not null)
            {
                falseState = state.Split(ScopeType.CodeBlock);
                TypeNarrowingRule.ApplyConditionNarrowing(
                    ternary.Condition, falseState, this, _symbolTree, _globalScope, positive: false);
                _checker.CheckNode(ternary.FalseExpr, falseState);
                falseType = InferExpressionType(ternary.FalseExpr, falseState);
            }
            else if (ternary.FalseExpr is not null)
            {
                _checker.CheckNode(ternary.FalseExpr, state);
                falseType = InferExpressionType(ternary.FalseExpr, state);
            }
            else
            {
                falseType = InferExpressionType(ternary.FalseExpr!, state);
            }

            if (trueState is not null && falseState is not null)
            {
                trueState.Merge(falseState);
                state.AbsorbJoinedVariables(trueState);
            }
            else if (trueState is not null)
            {
                state.Merge(trueState);
            }
            else if (falseState is not null)
            {
                state.Merge(falseState);
            }

            return CheckedTypes.UnionTypes(trueType, falseType);
        }

        /// <summary>
        /// Infers an array literal as <c>array&lt;T&gt;</c> (list — no explicit keys) or
        /// <c>array&lt;K, V&gt;</c> (any explicit key). Element/key literals widen to their
        /// underlying scalar so <c>[1, 2, 3]</c> is <c>array&lt;int&gt;</c>, not a literal union.
        /// Empty <c>[]</c> is <c>array&lt;never, never&gt;</c> (bottom keys and values) so it
        /// assigns into any <c>array&lt;…&gt;</c> under covariant array variance — including
        /// narrowed keys like <c>array&lt;string, T&gt;</c>. One-arg <c>array&lt;never&gt;</c>
        /// would normalize to <c>array&lt;int|string, never&gt;</c> and fail those targets.
        /// </summary>
        private ICheckedType InferArrayLiteral(IReadOnlyList<PhpArrayPairAst> pairs, CheckerState state)
        {
            if (pairs.Count == 0)
            {
                return MakeEmptyArrayType();
            }

            var valueTypes = new List<ICheckedType>();
            var keyTypes = new List<ICheckedType>();
            var hasExplicitKey = false;

            foreach (var pair in pairs)
            {
                if (pair.IsExpansion)
                {
                    var spreadType = pair.ValueExpr is not null
                        ? WidenLiteral(InferExpressionType(pair.ValueExpr, state))
                        : CheckedTypes.Unresolved;
                    UnpackSpreadArray(spreadType, keyTypes, valueTypes, ref hasExplicitKey);
                    continue;
                }

                if (pair.KeyExpr is not null)
                {
                    hasExplicitKey = true;
                    keyTypes.Add(WidenLiteral(InferExpressionType(pair.KeyExpr, state)));
                }

                if (pair.ValueExpr is not null)
                {
                    valueTypes.Add(WidenLiteral(InferExpressionType(pair.ValueExpr, state)));
                }
            }

            if (valueTypes.Count == 0)
            {
                return MakeEmptyArrayType();
            }

            var valueType = valueTypes.Count == 1
                ? valueTypes[0]
                : CheckedTypes.UnionTypes(valueTypes);

            if (!hasExplicitKey)
            {
                return MakeArrayType([valueType]);
            }

            var keyType = keyTypes.Count == 0
                ? CheckedTypes.UnionTypes(CheckedTypes.Int, CheckedTypes.String)
                : keyTypes.Count == 1
                    ? keyTypes[0]
                    : CheckedTypes.UnionTypes(keyTypes);

            return MakeArrayType([keyType, valueType]);
        }

        private static void UnpackSpreadArray(
            ICheckedType spreadType,
            List<ICheckedType> keyTypes,
            List<ICheckedType> valueTypes,
            ref bool hasExplicitKey)
        {
            if (spreadType is GenericCheckedType generic
                && IsArrayBaseType(generic.BaseType)
                && generic.TypeArguments.Count > 0)
            {
                if (generic.TypeArguments.Count >= 2)
                {
                    hasExplicitKey = true;
                    keyTypes.Add(generic.TypeArguments[0]);
                    valueTypes.Add(generic.TypeArguments[^1]);
                }
                else
                {
                    valueTypes.Add(generic.TypeArguments[0]);
                }

                return;
            }

            // Bare `array` / unknown spread — gradual any-element.
            valueTypes.Add(CheckedTypes.Mixed);
            hasExplicitKey = true;
            keyTypes.Add(CheckedTypes.UnionTypes(CheckedTypes.Int, CheckedTypes.String));
        }

        private static ICheckedType WidenLiteral(ICheckedType type) =>
            type is LiteralCheckedType literal ? literal.UnderlyingType : type;

        /// <summary>
        /// Empty-array bottom type: both key and value are <c>never</c> so covariant
        /// assignability accepts any <c>array&lt;K, V&gt;</c> / <c>array&lt;V&gt;</c> target.
        /// </summary>
        private static ICheckedType MakeEmptyArrayType() =>
            MakeArrayType([CheckedTypes.Never, CheckedTypes.Never]);

        private static ICheckedType MakeArrayType(IReadOnlyList<ICheckedType> typeArguments)
        {
            // Match GenericTypeArgumentValidator.NormalizeArrayLikeArguments: one-arg
            // `array<V>` is the list shorthand for `array<int|string, V>`.
            var normalized = typeArguments.Count == 1
                ? (IReadOnlyList<ICheckedType>)
                    [CheckedTypes.UnionTypes(CheckedTypes.Int, CheckedTypes.String), typeArguments[0]]
                : typeArguments;

            return new GenericCheckedType(
                CheckedTypes.FromSymbol(new BuiltInTypeSymbol("array")),
                normalized);
        }

        private ICheckedType InferNew(PhpNewAst newExpr, CheckerState state)
        {
            if (newExpr.AnonymousClass is { } anonymousClass)
            {
                // An inline anonymous class is best modeled by its declared base type (if any);
                // its synthetic name is not resolvable, so otherwise fall back to an unknown object.
                return anonymousClass.Extends is ITypeExpression baseType
                    ? ResolveTypeExpression(baseType, state)
                    : CheckedTypes.Unresolved;
            }

            // Prefer resolving as a parameterized class name so `new self<T>(…)` / `new Box<T>(…)`
            // keep their type arguments. The BoundSymbol short-circuit below would otherwise yield a
            // bare class and drop the call-site `<T>` needed for constructor param substitution.
            if (newExpr.ClassName is PhpNameAst className
                && TryResolveNewClassNameWithTypeArguments(className, state, out var parameterized))
            {
                return parameterized;
            }

            if (newExpr.ClassName?.BoundSymbol is ObjectDeclarationSymbol obj)
            {
                return ApplyDefaultsForBareGenericReference(obj, newExpr.ClassName, state);
            }

            if (newExpr.ClassName is ITypeExpression typeExpr)
            {
                return ResolveTypeExpression(typeExpr, state);
            }

            // `new self(...)`/`new static(...)`/`new SomeClass(...)` reference the class by a bare
            // name that the binder does not bind; resolve it as a class type.
            if (newExpr.ClassName is PhpNameAst bareName)
            {
                var receiver = ResolveClassReceiverType(bareName, state);
                if (receiver is null)
                {
                    return CheckedTypes.Unresolved;
                }

                if (receiver is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol bareObj })
                {
                    return ApplyDefaultsForBareGenericReference(bareObj, bareName, state);
                }

                return receiver;
            }

            return CheckedTypes.Unresolved;
        }

        /// <summary>
        /// Resolves <c>new Foo&lt;T&gt;</c> / <c>new self&lt;T&gt;</c> when type arguments hang off the
        /// class-name grammar addon (or a <see cref="TyhpGenericIdentifierAst"/>).
        /// Parameterized <c>static&lt;…&gt;</c> is rejected.
        /// </summary>
        private bool TryResolveNewClassNameWithTypeArguments(
            PhpNameAst className,
            CheckerState state,
            out ICheckedType result)
        {
            result = CheckedTypes.Unresolved;
            PhpTypeExpressionListAst? typeArgList = null;
            if (className.AstGrammarAddons.TryGetValue("identifier", out var addon)
                && addon is PhpTypeExpressionListAst addonList
                && addonList.GetAllNotNull().Any())
            {
                typeArgList = addonList;
            }
            else if (className is TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst genericArgs }
                     && genericArgs.GetAllNotNull().Any())
            {
                typeArgList = genericArgs;
            }

            if (typeArgList is null)
            {
                return false;
            }

            if (IsRelativeTypeName(className.ValueString)
                && string.Equals(className.ValueString?.TrimStart('\\'), "static", StringComparison.OrdinalIgnoreCase))
            {
                ReportDiagnostic(className, state, MessageCode.CheckerParameterizedStaticForbidden);
                result = CheckedTypes.Unresolved;
                return true;
            }

            var args = typeArgList
                .GetAllNotNull()
                .Select(arg => ResolveTypeExpression(arg, state))
                .ToList();

            ICheckedType baseType;
            if (IsRelativeTypeName(className.ValueString))
            {
                baseType = ResolveClassReceiverType(className, state) ?? CheckedTypes.Unresolved;
                if (baseType is StaticCheckedType staticBase)
                {
                    // `new self<T>` uses the declaring class; parameterized `static` is already banned.
                    baseType = staticBase.DeclaringType;
                }
            }
            else if (className.BoundSymbol is ObjectDeclarationSymbol bound)
            {
                baseType = CheckedTypes.FromSymbol(bound);
            }
            else
            {
                baseType = ResolveClassReceiverType(className, state) ?? CheckedTypes.Unresolved;
            }

            if (TypeComparer.IsUnresolvedType(baseType))
            {
                return false;
            }

            result = GenericTypeArgumentValidator.ValidateInstantiation(
                baseType, args, className, state, _symbolTree, _globalScope, _diagnostics,
                ResolveTypeExpressionCore);
            return true;
        }

        private ICheckedType InferClosure(PhpInlineFunctionAst closure, CheckerState state)
        {
            var parameters = closure.Parameters?.GetAllNotNull().ToList() ?? [];
            var paramTypes = parameters
                .Select(param =>
                {
                    if (param.Type is not null)
                    {
                        return ResolveTypeExpression(param.Type, state);
                    }

                    if (state.Variables.TryGetValue(param.Name.TrimStart('$'), out var varState))
                    {
                        return varState.DeclaredType ?? CheckedTypes.Unresolved;
                    }

                    return CheckedTypes.Unresolved;
                })
                .ToList();

            var returnType = closure.ReturnType is not null
                ? ResolveTypeExpression(closure.ReturnType, state, isReturnTypePosition: true)
                : CheckedTypes.Mixed;

            // Prefer call-site / annotation contextual return when the closure omitted an authored
            // return type (mirrors ClosureParameterInference + ClosureRule).
            if (closure.ReturnType is null
                && state.ExpectedClosureType is not null
                && CallableArityFacetBuilder.TrySelectCallableFacetForClosure(
                    state.ExpectedClosureType, parameters.Count, out var expectedFacet)
                && expectedFacet is not null)
            {
                returnType = expectedFacet.ReturnType;
                for (var i = 0; i < paramTypes.Count && i < expectedFacet.ParameterTypes.Count; i++)
                {
                    if (parameters[i].Type is null
                        && (paramTypes[i].Kind == CheckedTypeKind.Unresolved || paramTypes[i].IsMixed))
                    {
                        paramTypes[i] = expectedFacet.ParameterTypes[i];
                    }
                }
            }

            return CallableArityFacetBuilder.BuildFromClosureParameters(parameters, paramTypes, returnType);
        }

        /// <summary>
        /// Infers a <c>match</c> expression's result type as the union of its arm value types.
        ///
        /// Mirrors <see cref="InferTernary"/>: each arm is checked under a split state with
        /// <see cref="TypeNarrowingRule.ApplyConditionNarrowing"/> applied to a single-condition
        /// arm (the idiomatic <c>match (true) { \is_string($x) => … }</c> form). Multi-condition
        /// arms are OR'd by PHP and are not narrowed (positive OR is unsound for one variable).
        ///
        /// Arm bodies are synthesized as <c>return &lt;expr&gt;</c> unary ops
        /// (<c>PhpParserAstVisitor.VisitMatchArm</c>). Those returns produce the match value — not
        /// the enclosing function's return — so <see cref="CheckerState.ExpectedReturnType"/> is
        /// cleared on each arm state. The value type is taken from the returned operand (a bare
        /// <c>return</c> unary types as <c>unresolved</c>, which is assignable to everything and
        /// was the Top-type #4 soundness hole).
        ///
        /// Also runs each arm through <c>_checker.CheckNode</c> so this is the sole processing a
        /// match ever gets when only reached via <c>ResolveExpressionType</c>.
        /// <c>ControlFlowRule.CheckConditional</c> defers match arms here (same memoization rationale
        /// as <c>CheckTernary</c>).
        /// </summary>
        private ICheckedType InferMatch(PhpConditionalAst conditional, CheckerState state)
        {
            var arms = conditional.Arms?.GetAllNotNull().ToList() ?? [];
            if (arms.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var armTypes = new List<ICheckedType>(arms.Count);
            CheckerState? joined = null;

            foreach (var arm in arms)
            {
                // Per-arm split (unlike switch's shared mutable switchState): an earlier arm's
                // synthetic return setting HasReturnedOnAllPaths must not leak into the next arm.
                var armState = state.Split(ScopeType.CodeBlock);
                armState.ExpectedReturnType = null;
                armState.IsTypeGuardFunction = false;
                armState.IsInSwitchContext = true;
                armState.HasReturnedOnAllPaths = false;

                if (arm.Conditions is not null)
                {
                    foreach (var condition in arm.Conditions.GetAllNotNull())
                    {
                        // Probe: progressive `&&` narrowing must not mutate the pre-match state.
                        var probe = state.Split(ScopeType.CodeBlock);
                        _checker.CheckNode(condition, probe);
                    }
                }

                ApplyMatchArmConditionNarrowing(arm, armState);

                if (arm.Body is not null)
                {
                    _checker.CheckNode(arm.Body, armState);
                }

                armTypes.Add(InferMatchArmValueType(arm, armState));

                if (joined is null)
                {
                    joined = armState;
                }
                else
                {
                    joined.Merge(armState);
                }
            }

            if (joined is not null)
            {
                state.AbsorbJoinedVariables(joined);
            }

            return CheckedTypes.UnionTypes(armTypes);
        }

        /// <summary>
        /// Applies positive condition narrowing for a single-condition match arm. Default arms and
        /// multi-condition (OR) arms are left un-narrowed.
        /// </summary>
        private void ApplyMatchArmConditionNarrowing(PhpConditionalArmAst arm, CheckerState armState)
        {
            if (arm.IsDefault || arm.Conditions is null)
            {
                return;
            }

            var conditions = arm.Conditions.GetAllNotNull().ToList();
            // Multiple arm conditions are OR'd (`cond1, cond2 => …`). Positive OR narrowing is
            // unsound for a single variable (documented on TypeNarrowingRule), so only narrow when
            // there is exactly one condition.
            if (conditions.Count != 1)
            {
                return;
            }

            TypeNarrowingRule.ApplyConditionNarrowing(
                conditions[0], armState, this, _symbolTree, _globalScope, positive: true);
        }

        /// <summary>
        /// Types the match arm's produced value. Prefers the operand of the synthetic
        /// <c>return &lt;expr&gt;</c> unary; falling back to the first body expression.
        /// </summary>
        private ICheckedType InferMatchArmValueType(PhpConditionalArmAst arm, CheckerState armState)
        {
            if (arm.Body is null)
            {
                return CheckedTypes.Unresolved;
            }

            foreach (var child in arm.Body.AstChildren)
            {
                if (child is PhpUnaryOpAst unary
                    && string.Equals(unary.Operator?.ValueString, "return", StringComparison.OrdinalIgnoreCase)
                    && unary.Operand is IExpression operand)
                {
                    return InferExpressionType(operand, armState);
                }

                if (child is IExpression expression)
                {
                    return InferExpressionType(expression, armState);
                }
            }

            return CheckedTypes.Unresolved;
        }

        private ICheckedType InferCallableSymbol(FunctionDeclarationSymbol func, CheckerState state)
        {
            // Resolve parameter/return annotations in the callee's FunctionGenerics scope so
            // `array<TKey, TValue>` / KeyIntOrString on a first-class `fn(...)` does not collapse
            // `TKey` to unresolved (Story 11 audit #5 — same as InferCallableFromFunction).
            var resolveState = state;
            if (func.GenericParameters.Count > 0)
            {
                resolveState = state.Fork();
                resolveState.FunctionGenerics = func.GenericParameters;
            }

            var paramTypes = func.Parameters
                .Select(param => param.DeclaredType is not null
                    ? ResolveTypeExpression(param.DeclaredType, resolveState)
                    : CheckedTypes.Unresolved)
                .ToList();

            var returnType = func.ReturnType is not null
                ? ResolveTypeExpression(func.ReturnType, resolveState, isReturnTypePosition: true)
                : CheckedTypes.Mixed;

            return CallableArityFacetBuilder.BuildFromParameterInfos(func.Parameters, paramTypes, returnType);
        }

        private static ICheckedType InferNullCoalesce(ICheckedType left, ICheckedType right)
        {
            var leftWithoutNull = RemoveNullFromType(left);
            // Pure-null / empty left contributes nothing — do not pollute the union with
            // `unresolved` (CheckedTypes.UnionTypes does not drop unresolved members).
            if (leftWithoutNull.Kind == CheckedTypeKind.Unresolved)
            {
                return right;
            }

            return CheckedTypes.UnionTypes(leftWithoutNull, right);
        }

        private static ICheckedType RemoveNullFromType(ICheckedType type)
        {
            if (type is NullableCheckedType nullable)
            {
                return nullable.InnerType;
            }

            if (type is UnionCheckedType union)
            {
                var members = union.Members
                    .Where(member => !IsNullLiteral(member))
                    .Select(member => member is NullableCheckedType n ? n.InnerType : member)
                    .Where(member => member.Kind != CheckedTypeKind.Unresolved)
                    .ToList();
                return members.Count == 0
                    ? CheckedTypes.Unresolved
                    : CheckedTypes.UnionTypes(members);
            }

            return IsNullLiteral(type) ? CheckedTypes.Unresolved : type;
        }

        private static bool IsNullLiteral(ICheckedType type) =>
            type is LiteralCheckedType literal && literal.Value is null;

        private static bool IsCastToken(int token) =>
            token is TyhpParser.T_INT_CAST
                or TyhpParser.T_BOOL_CAST
                or TyhpParser.T_STRING_CAST
                or TyhpParser.T_DOUBLE_CAST
                or TyhpParser.T_DECIMAL_CAST
                or TyhpParser.T_ARRAY_CAST
                or TyhpParser.T_OBJECT_CAST;

        private static ICheckedType InferCastType(int token) =>
            token switch
            {
                TyhpParser.T_INT_CAST => CheckedTypes.Int,
                TyhpParser.T_BOOL_CAST => CheckedTypes.Bool,
                TyhpParser.T_STRING_CAST => CheckedTypes.String,
                TyhpParser.T_DOUBLE_CAST => CheckedTypes.Float,
                TyhpParser.T_DECIMAL_CAST => CheckedTypes.FromSymbol(new BuiltInTypeSymbol("decimal")),
                TyhpParser.T_ARRAY_CAST => CheckedTypes.FromSymbol(new BuiltInTypeSymbol("array")),
                TyhpParser.T_OBJECT_CAST => CheckedTypes.FromSymbol(new BuiltInTypeSymbol("object")),
                _ => CheckedTypes.Unresolved,
            };

        private static ICheckedType InferUnaryNumeric(int token, ICheckedType operand)
        {
            if (token == TyhpParser.T_SYM_TILDE)
            {
                return CheckedTypes.Int;
            }

            if (IsNumericType(operand))
            {
                return operand;
            }

            return CheckedTypes.Unresolved;
        }

        private ICheckedType InferAwaitOperand(IBase2Ast? operand, CheckerState state)
        {
            if (operand is null)
            {
                return CheckedTypes.Unresolved;
            }

            var operandType = InferExpressionType(operand, state);
            if (operandType is GenericCheckedType { BaseType.DisplayName: var baseName } generic
                && baseName.Contains("Promise", StringComparison.OrdinalIgnoreCase)
                && generic.TypeArguments.Count > 0)
            {
                return generic.TypeArguments[0];
            }

            if (operandType is GenericCheckedType asyncIterable
                && asyncIterable.BaseType.DisplayName.Contains("AsyncIterable", StringComparison.OrdinalIgnoreCase)
                && asyncIterable.TypeArguments.Count > 0)
            {
                return asyncIterable.TypeArguments[^1];
            }

            return CheckedTypes.Unresolved;
        }

        private static string? GetVariableName(PhpVariableAst variable)
        {
            var raw = variable.VariableToken?.ValueString ?? variable.Identifier ?? variable.ValueString;
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            return raw.StartsWith('$') ? raw[1..] : raw;
        }

        private static bool IsIntegerToken(TokenValueAst token) =>
            token.ValueInt64 is TyhpParser.T_LNUMBER
                or TyhpParser.T_ONUMBER
                or TyhpParser.T_HNUMBER
                or TyhpParser.T_BNUMBER;

        private static bool IsFloatToken(TokenValueAst token) =>
            token.ValueInt64 is TyhpParser.T_DNUMBER;
    }
}
