using System.Text;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// Rewrites object-form Tyhp <c>with</c> expressions into PHP (ObjectHelper, PHP 8.5
    /// <c>clone()</c>, anonymous-class wrappers, or direct property assignments). Also lowers
    /// PHP 8.5 call-shaped <c>clone(...)</c> for targets &lt; 8.5 (Story 14.5).
    /// Struct <c>with</c> is handled separately by <see cref="StructEmissionHelper"/>.
    /// </summary>
    internal static class WithKeywordHelper
    {
        public enum WithForm
        {
            Clone,
            New,
            InPlace,
        }

        public static WithForm DetectWithForm(IExpression? left)
        {
            if (left is PhpUnaryOpAst unary && StructEmissionHelper.IsCloneOperator(unary.Operator))
            {
                return WithForm.Clone;
            }

            if (left is PhpNewAst)
            {
                return WithForm.New;
            }

            return WithForm.InPlace;
        }

        public static bool IsSimpleAssignmentOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (op.ValueInt64 is long tokenType && tokenType == TyhpParser.T_SYM_EQUAL)
            {
                return true;
            }

            return string.Equals(op.ValueString, "=", StringComparison.Ordinal);
        }

        public static bool IsSimpleVariable(IExpression? expression)
            => expression is PhpVariableAst { VariableToken: not null, VariableExpression: null };

        /// <summary>
        /// True when any override property is declared <c>readonly</c> on <paramref name="objectDecl"/>.
        /// Missing declarations are ignored (checker reports those separately).
        /// </summary>
        public static bool HasReadonlyOverride(
            ObjectDeclarationSymbol? objectDecl,
            PhpArrayPairListAst pairList)
        {
            if (objectDecl is null)
            {
                return false;
            }

            foreach (var pair in pairList.GetAllNotNull())
            {
                var name = GetPropertyName(pair.KeyExpr);
                if (name is null)
                {
                    continue;
                }

                if (TryGetProperty(objectDecl, name) is { } property
                    && (property.Visibility & MemberModifier.Readonly) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static ObjectPropertySymbol? TryGetProperty(
            ObjectDeclarationSymbol objectDecl,
            string propertyName)
        {
            var bare = propertyName.StartsWith('$') ? propertyName[1..] : propertyName;
            var withDollar = "$" + bare;

            if (objectDecl.Members.TryGetValue(withDollar, out var member)
                && member is ObjectPropertySymbol byDollar)
            {
                return byDollar;
            }

            if (objectDecl.Members.TryGetValue(bare, out member)
                && member is ObjectPropertySymbol byBare)
            {
                return byBare;
            }

            // Fallback: scan members (covers odd binder key spellings).
            foreach (var candidate in objectDecl.Members.Values.OfType<ObjectPropertySymbol>())
            {
                var candidateBare = candidate.Name.StartsWith('$') ? candidate.Name[1..] : candidate.Name;
                if (string.Equals(candidateBare, bare, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        public static string? GetPropertyName(IExpression? keyExpr) =>
            keyExpr switch
            {
                PhpNameAst name => FirstNonEmpty(name.ValueString, name.Identifier),
                TokenValueAst token => FirstNonEmpty(token.ValueString, token.Identifier),
                PhpScalarAst scalar => Unquote(scalar.ValueString),
                PhpEncapsStringAst encaps => Unquote(encaps.ValueString ?? encaps.TokenValue?.ValueString),
                PhpBuiltinTypeAst builtin => FirstNonEmpty(builtin.Identifier, builtin.ValueString),
                IExpression expr => string.IsNullOrWhiteSpace(expr.Identifier) ? null : expr.Identifier,
                _ => null,
            };

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds <c>['prop' => value, ...]</c> with bare property-name string keys (no struct alias remap).
        /// </summary>
        public static PhpArrayAst CreateOverrideArray(PhpArrayPairListAst pairList, Base2Ast context)
        {
            var pairs = new List<PhpArrayPairAst>();
            foreach (var pair in pairList.GetAllNotNull())
            {
                if (pair.ValueExpr is null)
                {
                    continue;
                }

                var name = GetPropertyName(pair.KeyExpr) ?? "";
                name = name.StartsWith('$') ? name[1..] : name;
                var keyExpr = PhpScalarAst.CreateStringFromContext(context, name);
                pairs.Add(PhpArrayPairAst.CreateFromContext(keyExpr, pair.ValueExpr, pair.IsExpansion, context));
            }

            return PhpArrayAst.CreateFromContext(
                PhpArrayPairListAst.Create(pairs, context),
                isShortSyntax: true,
                context);
        }

        public static IReadOnlyList<(string Name, IExpression Value)> GetOverrideProperties(
            PhpArrayPairListAst pairList)
        {
            var result = new List<(string, IExpression)>();
            foreach (var pair in pairList.GetAllNotNull())
            {
                if (pair.ValueExpr is null)
                {
                    continue;
                }

                var name = GetPropertyName(pair.KeyExpr);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                name = name.StartsWith('$') ? name[1..] : name;
                result.Add((name, pair.ValueExpr));
            }

            return result;
        }

        /// <summary>
        /// Rewrites an object <c>with</c> binary as a single expression (not statement expansion).
        /// </summary>
        public static IExpression RewriteAsExpression(
            PhpBinaryOpAst withBinary,
            EmitContext context,
            ObjectDeclarationSymbol? objectDecl)
        {
            if (withBinary.Right is not PhpArrayPairListAst pairList)
            {
                return withBinary;
            }

            var form = DetectWithForm(withBinary.Left);
            var isReadonly = HasReadonlyOverride(objectDecl, pairList);
            var overrides = CreateOverrideArray(pairList, withBinary);
            var php85 = context.IsPhpVersionAtLeast(8, 5);

            return (form, isReadonly, php85) switch
            {
                (WithForm.Clone, _, true) =>
                    BuildNativeCloneCall(GetCloneOperand(withBinary.Left)!, overrides, withBinary),

                (WithForm.Clone, false, false) =>
                    BuildObjectHelperWith(
                        BuildCloneUnary(GetCloneOperand(withBinary.Left)!, withBinary),
                        overrides,
                        withBinary,
                        context),

                (WithForm.Clone, true, false) =>
                    BuildReadonlyCloneIife(
                        GetCloneOperand(withBinary.Left)!,
                        overrides,
                        objectDecl,
                        withBinary),

                (WithForm.New, false, _) =>
                    BuildObjectHelperWith(withBinary.Left!, overrides, withBinary, context),

                (WithForm.New, true, true) =>
                    BuildNativeCloneCall(withBinary.Left!, overrides, withBinary),

                (WithForm.New, true, false) =>
                    BuildReadonlyNewCloneWrapper(withBinary.Left as PhpNewAst, pairList, objectDecl, withBinary),

                (WithForm.InPlace, _, _) =>
                    BuildObjectHelperWith(withBinary.Left!, overrides, withBinary, context),

                _ => withBinary,
            };
        }

        /// <summary>
        /// True when this object <c>with</c> can expand to direct property-assignment statements.
        /// </summary>
        public static bool CanExpandToStatements(
            PhpBinaryOpAst withBinary,
            EmitContext context,
            ObjectDeclarationSymbol? objectDecl)
        {
            if (withBinary.Right is not PhpArrayPairListAst pairList)
            {
                return false;
            }

            if (HasReadonlyOverride(objectDecl, pairList))
            {
                return false;
            }

            var form = DetectWithForm(withBinary.Left);
            // PHP 8.5 clone(... with) always uses native clone() even in statement context.
            if (form == WithForm.Clone && context.IsPhpVersionAtLeast(8, 5))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Expands a statement-context object <c>with</c> into one or more statements.
        /// </summary>
        public static IReadOnlyList<IBase2Ast> ExpandToStatements(
            IBase2Ast statement,
            PhpBinaryOpAst withBinary,
            EmitContext context,
            ObjectDeclarationSymbol? objectDecl)
        {
            if (withBinary.Right is not PhpArrayPairListAst pairList)
            {
                return [statement];
            }

            var form = DetectWithForm(withBinary.Left);
            var overrides = GetOverrideProperties(pairList);
            var statements = new List<IBase2Ast>();

            if (statement is TyhpTypedVarExprAst typedVar)
            {
                ExpandTypedVarAssignment(typedVar, withBinary, form, overrides, context, statements);
                return statements;
            }

            if (statement is PhpBinaryOpAst assign
                && IsSimpleAssignmentOperator(assign.Operator)
                && ReferenceEquals(assign.Right, withBinary))
            {
                ExpandAssignment(assign, withBinary, form, overrides, context, statements);
                return statements;
            }

            // Bare `with` statement (typically in-place).
            if (ReferenceEquals(statement, withBinary))
            {
                ExpandBareWith(withBinary, form, overrides, context, statements);
                return statements;
            }

            return [statement];
        }

        private static void ExpandTypedVarAssignment(
            TyhpTypedVarExprAst typedVar,
            PhpBinaryOpAst withBinary,
            WithForm form,
            IReadOnlyList<(string Name, IExpression Value)> overrides,
            EmitContext context,
            List<IBase2Ast> statements)
        {
            var lhs = typedVar.Variable;
            if (lhs is null)
            {
                statements.Add(typedVar);
                return;
            }

            var baseExpr = GetBaseExpression(withBinary, form);
            // Replace assigned expression with the base (clone/new/in-place source).
            if (typedVar.AssignedExpression is IBase2Ast oldAssigned)
            {
                typedVar.ReplaceChild(oldAssigned, baseExpr);
            }

            statements.Add(typedVar);
            AppendPropertyAssignments(lhs, overrides, withBinary, statements);
        }

        private static void ExpandAssignment(
            PhpBinaryOpAst assign,
            PhpBinaryOpAst withBinary,
            WithForm form,
            IReadOnlyList<(string Name, IExpression Value)> overrides,
            EmitContext context,
            List<IBase2Ast> statements)
        {
            var lhs = assign.Left;
            var baseExpr = GetBaseExpression(withBinary, form);

            if (IsSimpleVariable(lhs))
            {
                assign.ReplaceChild(withBinary, baseExpr);
                statements.Add(assign);
                AppendPropertyAssignments(lhs!, overrides, withBinary, statements);
                return;
            }

            // Complex LHS: assign through a unique temp, then copy to the real target.
            var tempName = context.GenerateUniqueVarName("__with");
            var tempVar = PhpVariableAst.CreateFromContext(tempName, withBinary);
            statements.Add(CreateAssignment(tempVar, baseExpr, withBinary));
            AppendPropertyAssignments(tempVar, overrides, withBinary, statements);
            statements.Add(CreateAssignment(lhs!, tempVar, withBinary));
        }

        private static void ExpandBareWith(
            PhpBinaryOpAst withBinary,
            WithForm form,
            IReadOnlyList<(string Name, IExpression Value)> overrides,
            EmitContext context,
            List<IBase2Ast> statements)
        {
            if (form == WithForm.InPlace)
            {
                AppendPropertyAssignments(withBinary.Left!, overrides, withBinary, statements);
                return;
            }

            // clone/new as a bare statement: materialize into a temp (result discarded).
            var tempName = context.GenerateUniqueVarName("__with");
            var tempVar = PhpVariableAst.CreateFromContext(tempName, withBinary);
            statements.Add(CreateAssignment(tempVar, GetBaseExpression(withBinary, form), withBinary));
            AppendPropertyAssignments(tempVar, overrides, withBinary, statements);
        }

        private static void AppendPropertyAssignments(
            IExpression target,
            IReadOnlyList<(string Name, IExpression Value)> overrides,
            Base2Ast context,
            List<IBase2Ast> statements)
        {
            foreach (var (name, value) in overrides)
            {
                statements.Add(CreatePropertyAssignment(target, name, value, context));
            }
        }

        private static IExpression GetBaseExpression(PhpBinaryOpAst withBinary, WithForm form) =>
            form switch
            {
                WithForm.Clone => BuildCloneUnary(GetCloneOperand(withBinary.Left)!, withBinary),
                WithForm.New => withBinary.Left!,
                _ => withBinary.Left!,
            };

        private static IExpression? GetCloneOperand(IExpression? left)
        {
            if (left is PhpUnaryOpAst unary && StructEmissionHelper.IsCloneOperator(unary.Operator))
            {
                return unary.Operand;
            }

            return left;
        }

        public static PhpBinaryOpAst CreateAssignment(IExpression left, IExpression right, Base2Ast context)
        {
            var op = TokenValueAst.CreateFromContext("=", TyhpParser.T_SYM_EQUAL, context);
            return PhpBinaryOpAst.CreateFromContext(op, left, right, context);
        }

        public static PhpBinaryOpAst CreatePropertyAssignment(
            IExpression target,
            string propertyName,
            IExpression value,
            Base2Ast context)
        {
            if (target is not IDereferenceableBase targetBase)
            {
                throw new InvalidOperationException("Property assignment target must be dereferenceable.");
            }

            var accessor = TokenValueAst.CreateFromContext("->", TyhpParser.T_OBJECT_OPERATOR, context);
            var member = PhpInstanceMemberAccessAst.CreateFromContext(
                accessor,
                PhpNameAst.CreateFromContext(propertyName, context),
                context);
            var memberAccess = PhpDereferenceableAst.CreateFromContext(targetBase, member, context);
            return CreateAssignment(memberAccess, value, context);
        }

        public static IExpression BuildCloneUnary(IExpression operand, Base2Ast context)
        {
            var op = TokenValueAst.CreateFromContext("clone", TyhpParser.T_CLONE, context);
            return PhpUnaryOpAst.CreateFromContext(op, operand, context);
        }

        public static IExpression BuildNativeCloneCall(
            IExpression objectExpr,
            IExpression overridesArray,
            Base2Ast context)
        {
            var args = PhpArgumentListAst.Create(
                [
                    PhpArgumentAst.CreateFromContext(objectExpr, context),
                    PhpArgumentAst.CreateFromContext(overridesArray, context),
                ],
                context);

            return PhpDereferenceableAst.CreateFromContext(
                PhpNameAst.CreateFromContext("clone", context),
                PhpCallAst.CreateFromContext(args, context),
                context);
        }

        /// <summary>
        /// Story 14.5: lower PHP 8.5 call-shaped <c>clone(...)</c> for targets &lt; 8.5.
        /// Reuses <see cref="BuildCloneUnary"/> / <see cref="BuildObjectHelperWith"/> (same
        /// Story 11 paths as Tyhp <c>clone … with</c>). First-class <c>clone(...)</c> is
        /// handled by <see cref="BuildCloneFirstClassCallableLowering"/>.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>Object only (<c>clone($o,)</c> / <c>clone(object: $o)</c>) → unary <c>clone $o</c></item>
        /// <item>Object + withProperties → <c>ObjectHelper::with(clone $o, $props)</c></item>
        /// <item>Unpack / unresolvable forms keep a best-effort ObjectHelper shape when an
        /// object expression is available; otherwise the call AST is left unchanged for emit</item>
        /// </list>
        /// Readonly IIFE lowering remains the Tyhp <c>with</c> / AliasConverter path when the
        /// class and override keys are known; bare PHP <c>clone($o, $props)</c> uses ObjectHelper
        /// (same as non-readonly Story 11 clone-with).
        /// </remarks>
        public static IExpression RewriteCloneKeywordCall(
            PhpUnaryOpAst cloneCall,
            PhpArgumentListAst arguments,
            EmitContext emitContext)
        {
            if (!TryExtractCloneCallArguments(arguments, out var objectExpr, out var withProperties))
            {
                return cloneCall;
            }

            if (objectExpr is null)
            {
                return cloneCall;
            }

            if (withProperties is null)
            {
                return BuildCloneUnary(objectExpr, cloneCall);
            }

            return BuildObjectHelperWith(
                BuildCloneUnary(objectExpr, cloneCall),
                withProperties,
                cloneCall,
                emitContext);
        }

        /// <summary>
        /// PHP &lt; 8.5 lowering for first-class callable <c>clone(...)</c> — a static arrow that
        /// mirrors the tyhpdef signature via <c>ObjectHelper::with(clone $object, $withProperties)</c>.
        /// </summary>
        public static IExpression BuildCloneFirstClassCallableLowering(
            Base2Ast context,
            EmitContext emitContext)
        {
            emitContext.RequirePackage("tyhp/core");
            return EmittedPhpExprAst.Create(
                "(static fn(object $object, array $withProperties = []) => "
                + @"\Tyhp\ObjectHelper::with(clone $object, $withProperties))",
                context);
        }

        /// <summary>
        /// Resolves <c>object</c> / <c>withProperties</c> from positional or named clone call
        /// arguments. Returns <c>false</c> for FCC / sole-unpack forms that need a different path.
        /// </summary>
        private static bool TryExtractCloneCallArguments(
            PhpArgumentListAst arguments,
            out IExpression? objectExpr,
            out IExpression? withProperties)
        {
            objectExpr = null;
            withProperties = null;

            var args = arguments.GetAllNotNull().ToList();
            if (args.Count == 0)
            {
                return true;
            }

            // Bare FCC `clone(...)` — caller handles via BuildCloneFirstClassCallableLowering.
            if (args.Count == 1 && args[0].IsVariadic && args[0].Expression is null)
            {
                return false;
            }

            foreach (var arg in args)
            {
                if (arg.IsVariadic)
                {
                    // `clone(...$pack)` — no stable object/props split without evaluating the pack.
                    objectExpr = null;
                    withProperties = null;
                    return false;
                }

                var name = arg.Name?.ValueString;
                if (string.Equals(name, "object", StringComparison.OrdinalIgnoreCase)
                    || (name is null && objectExpr is null))
                {
                    objectExpr = arg.Expression;
                    continue;
                }

                if (string.Equals(name, "withProperties", StringComparison.OrdinalIgnoreCase)
                    || (name is null && objectExpr is not null && withProperties is null))
                {
                    withProperties = arg.Expression;
                    continue;
                }

                // Unknown named arg — checker already diagnoses; leave unrewritten.
                objectExpr = null;
                withProperties = null;
                return false;
            }

            return true;
        }

        public static IExpression BuildObjectHelperWith(
            IExpression objectExpr,
            IExpression overridesArray,
            Base2Ast context,
            EmitContext emitContext)
        {
            emitContext.RequirePackage("tyhp/core");

            var staticMember = PhpStaticMemberAccessAst.CreateFromContext(
                PhpNameAst.CreateFromContext("with", context),
                context);
            var classBase = PhpDereferenceableAst.CreateFromContext(
                PhpNameAst.CreateFromContext(@"\Tyhp\ObjectHelper", context),
                staticMember,
                context);
            var args = PhpArgumentListAst.Create(
                [
                    PhpArgumentAst.CreateFromContext(objectExpr, context),
                    PhpArgumentAst.CreateFromContext(overridesArray, context),
                ],
                context);
            return PhpDereferenceableAst.CreateFromContext(
                classBase,
                PhpCallAst.CreateFromContext(args, context),
                context);
        }

        /// <summary>
        /// PHP &lt; 8.5 readonly <c>clone ... with</c> — reflection IIFE per Story 11 plan.
        /// </summary>
        public static IExpression BuildReadonlyCloneIife(
            IExpression sourceExpr,
            PhpArrayAst overridesArray,
            ObjectDeclarationSymbol? objectDecl,
            Base2Ast context)
        {
            var classFqn = NormalizeFqn(objectDecl?.FullyQualifiedName ?? objectDecl?.Name ?? "object");
            var sourceText = RenderExpression(sourceExpr);
            var overridesText = RenderExpression(overridesArray);

            var php = new StringBuilder();
            php.Append("(static function (").Append(classFqn).Append(" $__src, array $__overrides): ")
                .Append(classFqn).Append(" {\n");
            php.Append("    $__wrapper = (new \\ReflectionClass(new class extends ").Append(classFqn).Append(" {\n");
            php.Append("        /** @internal */ public array $__tyhp_overrides = [];\n");
            php.Append("\n");
            php.Append("        public function __clone(): void\n");
            php.Append("        {\n");
            php.Append("            if (\\method_exists(parent::class, '__clone')) {\n");
            php.Append("                parent::__clone();\n");
            php.Append("            }\n");
            php.Append("            foreach ($this->__tyhp_overrides as $__k => $__v) {\n");
            php.Append("                $this->$__k = $__v;\n");
            php.Append("            }\n");
            php.Append("            $this->__tyhp_overrides = [];\n");
            php.Append("        }\n");
            php.Append("    }))->newInstanceWithoutConstructor();\n");
            php.Append("\n");
            php.Append("    foreach ((new \\ReflectionObject($__src))->getProperties() as $__prop) {\n");
            php.Append("        $__prop->setAccessible(true);\n");
            php.Append("        if ($__prop->isInitialized($__src)) {\n");
            php.Append("            $__prop->setValue($__wrapper, $__prop->getValue($__src));\n");
            php.Append("        }\n");
            php.Append("    }\n");
            php.Append("\n");
            php.Append("    $__wrapper->__tyhp_overrides = $__overrides;\n");
            php.Append("    return clone $__wrapper;\n");
            php.Append("})(").Append(sourceText).Append(", ").Append(overridesText).Append(')');

            return EmittedPhpExprAst.Create(php.ToString(), context);
        }

        /// <summary>
        /// PHP &lt; 8.5 readonly <c>new ... with</c> — anonymous class + <c>__clone</c> with inlined assignments.
        /// </summary>
        public static IExpression BuildReadonlyNewCloneWrapper(
            PhpNewAst? newExpr,
            PhpArrayPairListAst pairList,
            ObjectDeclarationSymbol? objectDecl,
            Base2Ast context)
        {
            var classFqn = NormalizeFqn(objectDecl?.FullyQualifiedName ?? GetNewClassName(newExpr) ?? "object");
            var argsText = RenderArgumentList(newExpr?.Arguments);
            var overrides = GetOverrideProperties(pairList);

            var php = new StringBuilder();
            php.Append("clone (new class(").Append(argsText).Append(") extends ").Append(classFqn).Append(" {\n");
            php.Append("    public function __clone(): void\n");
            php.Append("    {\n");
            php.Append("        if (\\method_exists(parent::class, '__clone')) {\n");
            php.Append("            parent::__clone();\n");
            php.Append("        }\n");
            foreach (var (name, value) in overrides)
            {
                php.Append("        $this->").Append(name).Append(" = ")
                    .Append(RenderExpression(value)).Append(";\n");
            }

            php.Append("    }\n");
            php.Append("})");

            return EmittedPhpExprAst.Create(php.ToString(), context);
        }

        private static string? GetNewClassName(PhpNewAst? newExpr) =>
            newExpr?.ClassName switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                IClassName reference => reference.Identifier,
                _ => null,
            };

        private static string NormalizeFqn(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "\\object";
            }

            return name.StartsWith('\\') ? name : "\\" + name;
        }

        private static string Unquote(string? text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 2)
            {
                return text ?? "";
            }

            if ((text[0] == '\'' && text[^1] == '\'') || (text[0] == '"' && text[^1] == '"'))
            {
                return text[1..^1];
            }

            return text;
        }

        private static string RenderArgumentList(PhpArgumentListAst? args)
        {
            if (args is null)
            {
                return "";
            }

            return string.Join(", ", args.GetAllNotNull().Select(a => RenderExpression(a.Expression)));
        }

        /// <summary>
        /// Minimal expression renderer for embedding AST fragments into <see cref="EmittedPhpExprAst"/> text.
        /// </summary>
        internal static string RenderExpression(IExpression? expression)
        {
            if (expression is null)
            {
                return "";
            }

            return expression switch
            {
                EmittedPhpExprAst emitted => emitted.PhpText,
                PhpVariableAst variable => RenderVariable(variable),
                PhpNameAst name => name.ValueString ?? "",
                PhpScalarAst scalar => RenderScalar(scalar),
                PhpStringAst str => str.ValueString ?? "\"\"",
                PhpEncapsStringAst encaps => encaps.ValueString ?? encaps.TokenValue?.ValueString ?? "\"\"",
                PhpArrayAst array => RenderArray(array),
                PhpNewAst newExpr => "new " + RenderExpression(newExpr.ClassName as IExpression)
                    + "(" + RenderArgumentList(newExpr.Arguments) + ")",
                PhpUnaryOpAst unary => RenderUnary(unary),
                PhpBinaryOpAst binary =>
                    RenderExpression(binary.Left) + " " + (binary.Operator?.ValueString ?? "") + " "
                    + RenderExpression(binary.Right),
                PhpDereferenceableAst deref => RenderDereferenceable(deref),
                PhpDereferenceableExpressionAst paren => "(" + RenderExpression(paren.Expression) + ")",
                _ => expression.ValueString ?? expression.Identifier ?? "",
            };
        }

        private static string RenderVariable(PhpVariableAst variable)
        {
            if (variable.VariableToken?.ValueString is { } tokenText)
            {
                return tokenText.StartsWith('$') ? tokenText : "$" + tokenText;
            }

            if (variable.VariableExpression is not null)
            {
                return "$" + RenderExpression(variable.VariableExpression);
            }

            return variable.Identifier.StartsWith('$') ? variable.Identifier : "$" + variable.Identifier;
        }

        private static string RenderScalar(PhpScalarAst scalar)
        {
            var token = scalar.AstChildren.ElementAtOrDefault(0) as TokenValueAst;
            if (scalar.ScalarType == PhpScalarType.String
                || (token?.ValueInt64 is long t && t == TyhpParser.T_CONSTANT_ENCAPSED_STRING))
            {
                var value = scalar.ValueString;
                if (value is { Length: >= 2 }
                    && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"')))
                {
                    return value;
                }

                if (!string.IsNullOrEmpty(token?.ValueString)
                    && token.ValueString[0] is '\'' or '"')
                {
                    return token.ValueString;
                }

                var unquoted = value ?? "";
                return "'" + unquoted.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
            }

            return token?.ValueString
                ?? scalar.ValueString
                ?? scalar.ValueInt64?.ToString()
                ?? scalar.ValueBoolean?.ToString()?.ToLowerInvariant()
                ?? "null";
        }

        private static string RenderArray(PhpArrayAst array)
        {
            var pairs = array.ArrayPairs?.GetAllNotNull().Select(RenderArrayPair) ?? [];
            return array.IsShortSyntax
                ? "[" + string.Join(", ", pairs) + "]"
                : "array(" + string.Join(", ", pairs) + ")";
        }

        private static string RenderArrayPair(PhpArrayPairAst pair)
        {
            if (pair.IsExpansion)
            {
                return "..." + RenderExpression(pair.ValueExpr);
            }

            if (pair.KeyExpr is not null)
            {
                return RenderExpression(pair.KeyExpr) + " => " + RenderExpression(pair.ValueExpr);
            }

            return RenderExpression(pair.ValueExpr);
        }

        private static string RenderUnary(PhpUnaryOpAst unary)
        {
            var op = unary.Operator?.ValueString ?? "";
            var operand = RenderExpression(unary.Operand);
            if (unary.IsPrefix)
            {
                if (op.Length > 0 && char.IsLetter(op[^1]))
                {
                    return op + " " + operand;
                }

                return op + operand;
            }

            return operand + op;
        }

        private static string RenderDereferenceable(PhpDereferenceableAst deref)
        {
            var baseText = deref.Base switch
            {
                IExpression expr => RenderExpression(expr),
                _ => deref.Base?.Identifier ?? "",
            };

            return deref.Suffix switch
            {
                PhpCallAst call => baseText + "(" + RenderArgumentList(call.Arguments) + ")",
                PhpInstanceMemberAccessAst instance =>
                    baseText + (instance.Accessor?.ValueString ?? "->")
                    + RenderExpression(instance.MemberName),
                PhpStaticMemberAccessAst staticAccess =>
                    baseText + "::" + RenderExpression(staticAccess.Member),
                PhpArrayAccessAst arrayAccess =>
                    baseText + "[" + RenderExpression(arrayAccess.IndexExpression) + "]",
                _ => baseText,
            };
        }
    }
}
