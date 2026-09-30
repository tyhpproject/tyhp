using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Collects yield / return evidence while checking a generator body so
    /// <see cref="GeneratorBodyInference"/> can build
    /// <c>\Generator&lt;TKey, TValue, TSend, TReturn&gt;</c> after the visit.
    /// Shared across <c>Split</c> code-block states of the same callable; function /
    /// closure / method scopes start with a fresh collector (or none).
    /// </summary>
    internal sealed class GeneratorBodyCollector
    {
        public GeneratorBodyCollector(GeneratorBodyInference.DeclaredShape shape)
        {
            Shape = shape;
        }

        /// <summary>
        /// Written declared-return shape (AST arity), not the default-filled
        /// <c>Generator&lt;mixed, mixed, mixed, mixed&gt;</c> from Layer 3.
        /// </summary>
        public GeneratorBodyInference.DeclaredShape Shape { get; }

        public static GeneratorBodyCollector ForDeclaredReturn(ITypeExpression? returnTypeAst) =>
            new(GeneratorBodyInference.ClassifyFromAst(returnTypeAst));

        public List<ICheckedType> Keys { get; } = [];

        public List<ICheckedType> Values { get; } = [];

        public List<ICheckedType> SendConstraints { get; } = [];

        public List<ICheckedType> Returns { get; } = [];

        public List<IBase2Ast> YieldSendNodes { get; } = [];

        public void AddKey(ICheckedType type) => AddIfMeaningful(Keys, type);

        public void AddValue(ICheckedType type) => AddIfMeaningful(Values, type);

        public void AddSendConstraint(ICheckedType type) => AddIfMeaningful(SendConstraints, type);

        public void AddReturn(ICheckedType type) => AddIfMeaningful(Returns, type);

        public void AddYieldSendNode(IBase2Ast node) => YieldSendNodes.Add(node);

        private static void AddIfMeaningful(List<ICheckedType> list, ICheckedType type)
        {
            if (TypeComparer.IsUnresolvedType(type))
            {
                return;
            }

            list.Add(GeneratorBodyInference.WidenLiteral(type));
        }
    }

    /// <summary>
    /// Story 21.6 Phase 6b — infer <c>\Generator&lt;TKey, TValue, TSend, TReturn&gt;</c>
    /// from a generator body's yields and returns. Reuses ControlFlowRule 4086 / 4088 /
    /// 4089 and <c>InferYield</c>; does not invent Fiber CFA.
    /// </summary>
    internal static class GeneratorBodyInference
    {
        internal enum DeclaredShape
        {
            Invalid,
            BareGenerator,
            TwoArgGenerator,
            FourArgGenerator,
            WeakerExport,
        }

        public static ICheckedType WidenLiteral(ICheckedType type)
        {
            if (type is LiteralCheckedType literal)
            {
                type = literal.UnderlyingType;
            }

            // `true`/`false` stay nominal after dropping LiteralCheckedType; TReturn/TValue
            // slots used as Generator type arguments are invariant, so widen to bool.
            if (CheckerHelpers.IsBuiltInName(type, "true")
                || CheckerHelpers.IsBuiltInName(type, "false"))
            {
                return CheckedTypes.Bool;
            }

            return type;
        }

        /// <summary>
        /// True for <c>yield</c> / <c>yield $v</c> / <c>yield $k => $v</c> (PHP
        /// <c>Generator::send()</c> sites). Not <c>yield from</c>.
        /// </summary>
        public static bool IsYieldSendExpression(IExpression? expression)
        {
            var node = UnwrapExpression(expression);
            if (node is PhpYieldAst)
            {
                return true;
            }

            return node is PhpUnaryOpAst unary
                && TyhpBinder.IsYieldOperator(unary)
                && !IsYieldFromUnary(unary);
        }

        /// <summary>
        /// TSend is unpinned when the declared return is bare <c>\Generator</c>,
        /// two-arg <c>\Generator&lt;K, V&gt;</c>, or a weaker iterable export
        /// (no TSend slot). Four-arg (or three-arg with an explicit TSend) pins it.
        /// Classification uses <em>written</em> arity: Layer 3 defaults would
        /// otherwise make every bare <c>\Generator</c> look four-arg.
        /// </summary>
        public static bool IsSendUnpinned(DeclaredShape shape) =>
            shape is DeclaredShape.BareGenerator
                or DeclaredShape.TwoArgGenerator
                or DeclaredShape.WeakerExport;

        /// <summary>
        /// When a yield-send expression is assigned or passed to a typed target inside a
        /// generator whose TSend is not pinned, record the target as a TSend constraint
        /// and skip mixed→target assignability (the yield currently types as mixed).
        /// </summary>
        public static bool TryHandleYieldSendAssignment(
            IExpression? sourceExpression,
            ICheckedType targetType,
            CheckerState state)
        {
            if (!state.IsInGeneratorContext
                || state.GeneratorInference is not { } collector
                || !IsYieldSendExpression(sourceExpression)
                || !IsSendUnpinned(collector.Shape))
            {
                return false;
            }

            if (!TypeComparer.IsUnresolvedType(targetType)
                && !TypeComparer.IsMixedType(targetType))
            {
                collector.AddSendConstraint(targetType);
            }

            return true;
        }

        public static void CollectYieldSite(
            IBase2Ast yieldNode,
            IExpression? operand,
            bool isYieldFrom,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (!state.IsInGeneratorContext || state.GeneratorInference is null)
            {
                return;
            }

            if (isYieldFrom)
            {
                if (operand is null)
                {
                    return;
                }

                MergeYieldFromOperand(
                    context.ResolveExpressionType(operand, state),
                    state,
                    context);
                return;
            }

            state.GeneratorInference.AddYieldSendNode(yieldNode);

            if (yieldNode is PhpYieldAst phpYield)
            {
                if (phpYield.KeyExpr is not null)
                {
                    state.GeneratorInference.AddKey(
                        context.ResolveExpressionType(phpYield.KeyExpr, state));
                }
                else
                {
                    state.GeneratorInference.AddKey(CheckedTypes.Int);
                }

                if (phpYield.ValueExpr is not null)
                {
                    state.GeneratorInference.AddValue(
                        context.ResolveExpressionType(phpYield.ValueExpr, state));
                }
                else
                {
                    state.GeneratorInference.AddValue(CheckedTypes.Null);
                }

                return;
            }

            // Bare `yield;` — PHP yields null with a sequential int key.
            state.GeneratorInference.AddKey(CheckedTypes.Int);
            state.GeneratorInference.AddValue(CheckedTypes.Null);
        }

        public static void CollectGeneratorReturn(ICheckedType actual, CheckerState state)
        {
            if (!state.IsInGeneratorContext || state.GeneratorInference is null)
            {
                return;
            }

            state.GeneratorInference.AddReturn(
                TypeComparer.IsVoidType(actual) ? CheckedTypes.Null : actual);
        }

        public static void FinishAfterBody(
            IBase2Ast callableNode,
            ITypeExpression? returnTypeAst,
            IBaseSymbol? callableSymbol,
            PhpInlineFunctionAst? closure,
            CheckerState funcState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!funcState.IsInGeneratorContext)
            {
                return;
            }

            object finishKey = closure ?? (object?)callableSymbol ?? callableNode;
            if (!context.TryBeginGeneratorBodyInference(finishKey))
            {
                return;
            }

            var declared = funcState.ExpectedReturnType;
            var reportNode = (IBase2Ast?)returnTypeAst ?? callableNode;
            var shape = ClassifyFromAst(returnTypeAst);
            if (shape == DeclaredShape.Invalid)
            {
                shape = ClassifyDeclaredReturn(declared);
            }

            if (shape == DeclaredShape.Invalid)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    funcState,
                    reportNode,
                    MessageCode.CheckerGeneratorInvalidReturnType,
                    "Generator, iterable, or Iterator",
                    declared?.DisplayName ?? "missing");
                return;
            }

            var collector = funcState.GeneratorInference
                ?? GeneratorBodyCollector.ForDeclaredReturn(returnTypeAst);
            var tKey = UnionOr(collector.Keys, CheckedTypes.Int, context);
            var tValue = UnionOr(collector.Values, CheckedTypes.Mixed, context);
            var tReturn = BuildReturnType(collector, !funcState.HasReturnedOnAllPaths, context);
            var tSend = BuildSendType(collector, context, funcState, reportNode, diagnostics);

            ApplyPins(
                shape,
                declared,
                ref tKey,
                ref tValue,
                ref tSend,
                ref tReturn,
                reportNode,
                funcState,
                context,
                diagnostics);

            var inferred = BuildGeneratorType(tKey, tValue, tSend, tReturn, context);
            if (inferred.Kind == CheckedTypeKind.Unresolved)
            {
                return;
            }

            foreach (var yieldNode in collector.YieldSendNodes)
            {
                context.SetExpressionType(yieldNode, tSend);
            }

            if (shape is DeclaredShape.BareGenerator or DeclaredShape.TwoArgGenerator)
            {
                context.RecordInferredGeneratorReturn(callableSymbol, closure, inferred);
            }
        }

        public static IExpression? UnwrapExpression(IExpression? expression)
        {
            while (expression is PhpDereferenceableExpressionAst { Expression: IExpression inner })
            {
                expression = inner;
            }

            return expression;
        }

        internal static bool IsNominalName(ICheckedType type, string name)
        {
            if (type is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                return string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase);
            }

            var display = type.DisplayName.TrimStart('\\');
            var angle = display.IndexOf('<');
            if (angle >= 0)
            {
                display = display[..angle];
            }

            var slash = display.LastIndexOf('\\');
            if (slash >= 0)
            {
                display = display[(slash + 1)..];
            }

            return string.Equals(display, name, StringComparison.OrdinalIgnoreCase);
        }

        internal static ICheckedType? TryGetGeneratorTypeArgument(ICheckedType? type, int index)
        {
            if (type is null)
            {
                return null;
            }

            type = UnwrapNullable(type);
            if (type is GenericCheckedType generic)
            {
                if (!IsNominalName(generic.BaseType, "Generator"))
                {
                    return null;
                }

                return generic.TypeArguments.Count > index
                    ? generic.TypeArguments[index]
                    : CheckedTypes.Mixed;
            }

            if (IsNominalName(type, "Generator"))
            {
                return CheckedTypes.Mixed;
            }

            return null;
        }

        private static void MergeYieldFromOperand(
            ICheckedType operandType,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (operandType is UnionCheckedType union)
            {
                foreach (var member in union.Members)
                {
                    MergeYieldFromOperand(member, state, context);
                }

                return;
            }

            operandType = UnwrapNullable(operandType) ?? operandType;
            var collector = state.GeneratorInference!;

            if (TryGetGeneratorTypeArgument(operandType, 0) is { } innerKey
                && TryGetGeneratorTypeArgument(operandType, 1) is { } innerValue)
            {
                collector.AddKey(innerKey);
                collector.AddValue(innerValue);
                if (TryGetGeneratorTypeArgument(operandType, 2) is { } innerSend
                    && !TypeComparer.IsMixedType(innerSend))
                {
                    collector.AddSendConstraint(innerSend);
                }

                return;
            }

            if (TryGetArrayOrIterableArgs(operandType, out var arrayKey, out var arrayValue))
            {
                collector.AddKey(arrayKey);
                collector.AddValue(arrayValue);
                return;
            }

            if (GenericInheritanceBindings.TryGetTraversableIterationTypes(
                    operandType,
                    state,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.ResolveTypeAnnotation,
                    out var travKey,
                    out var travValue))
            {
                collector.AddKey(travKey);
                collector.AddValue(travValue);
            }
        }

        private static bool TryGetArrayOrIterableArgs(
            ICheckedType iterableType,
            out ICheckedType keyType,
            out ICheckedType valueType)
        {
            keyType = CheckedTypes.Int;
            valueType = CheckedTypes.Mixed;

            if (iterableType is not GenericCheckedType { TypeArguments.Count: > 0 } generic)
            {
                return CheckerHelpers.IsBuiltInName(iterableType, "array")
                    || CheckerHelpers.IsBuiltInName(iterableType, "iterable");
            }

            var baseType = generic.BaseType;
            if (!CheckerHelpers.IsBuiltInName(baseType, "array")
                && !CheckerHelpers.IsBuiltInName(baseType, "iterable"))
            {
                return false;
            }

            valueType = generic.TypeArguments[^1];
            if (generic.TypeArguments.Count >= 2)
            {
                keyType = generic.TypeArguments[0];
            }

            return true;
        }

        /// <summary>
        /// Classify from the authored annotation, not the resolved type.
        /// Layer 3 <c>Generator&lt;TKey = mixed, …&gt;</c> fills omitted arguments, so a
        /// written <c>\Generator</c> resolves as four-arg mixed — callers must still
        /// see body inference.
        /// </summary>
        internal static DeclaredShape ClassifyFromAst(ITypeExpression? returnTypeAst)
        {
            var primary = UnwrapToPrimaryTypeAst(returnTypeAst);
            if (primary is null)
            {
                return DeclaredShape.Invalid;
            }

            var name = GetWrittenTypeName(primary);
            if (string.IsNullOrEmpty(name))
            {
                return DeclaredShape.Invalid;
            }

            if (IsTypeName(name, "Generator"))
            {
                var argCount = CountWrittenGenericArgs(primary);
                return argCount >= 3
                    ? DeclaredShape.FourArgGenerator
                    : argCount >= 2
                        ? DeclaredShape.TwoArgGenerator
                        : DeclaredShape.BareGenerator;
            }

            return IsWeakerExportName(name) ? DeclaredShape.WeakerExport : DeclaredShape.Invalid;
        }

        private static DeclaredShape ClassifyDeclaredReturn(ICheckedType? declared)
        {
            var type = UnwrapNullable(declared);
            if (type is null)
            {
                return DeclaredShape.Invalid;
            }

            if (type is GenericCheckedType generic)
            {
                if (IsNominalName(generic.BaseType, "Generator"))
                {
                    return generic.TypeArguments.Count >= 3
                        ? DeclaredShape.FourArgGenerator
                        : generic.TypeArguments.Count >= 2
                            ? DeclaredShape.TwoArgGenerator
                            : DeclaredShape.BareGenerator;
                }

                if (IsWeakerExport(generic.BaseType) || IsWeakerExport(generic))
                {
                    return DeclaredShape.WeakerExport;
                }

                return DeclaredShape.Invalid;
            }

            if (IsNominalName(type, "Generator"))
            {
                return DeclaredShape.BareGenerator;
            }

            return IsWeakerExport(type) ? DeclaredShape.WeakerExport : DeclaredShape.Invalid;
        }

        private static ITypeExpression? UnwrapToPrimaryTypeAst(ITypeExpression? typeAst)
        {
            while (typeAst is PhpTypeExpressionAst composite)
            {
                var members = composite.Types?.GetAllNotNull().ToList() ?? [];
                if (members.Count == 0)
                {
                    return typeAst;
                }

                ITypeExpression? generatorMember = null;
                foreach (var member in members)
                {
                    var innerName = GetWrittenTypeName(member);
                    if (IsTypeName(innerName, "Generator") || IsWeakerExportName(innerName))
                    {
                        generatorMember = member;
                        break;
                    }
                }

                typeAst = generatorMember ?? members[0];
            }

            return typeAst;
        }

        private static string GetWrittenTypeName(ITypeExpression? typeAst)
        {
            switch (typeAst)
            {
                case PhpBuiltinTypeAst builtin:
                    return builtin.Identifier ?? string.Empty;
                case PhpNamedTypeAst named:
                    return LastTypeNameSegment(
                        named.Name?.ValueString
                        ?? named.Name?.Identifier
                        ?? named.Identifier);
                case PhpTypeExpressionAst composite:
                {
                    var inner = UnwrapToPrimaryTypeAst(composite);
                    return ReferenceEquals(inner, composite)
                        ? composite.Identifier ?? string.Empty
                        : GetWrittenTypeName(inner);
                }
                default:
                    return typeAst?.Identifier ?? string.Empty;
            }
        }

        private static int CountWrittenGenericArgs(IBase2Ast? node)
        {
            if (node is null)
            {
                return 0;
            }

            if (TryGetWrittenArgList(node, out var list))
            {
                return list.GetAllNotNull().Count();
            }

            return node switch
            {
                PhpNamedTypeAst named => CountWrittenGenericArgs(named.Name as IBase2Ast),
                PhpTypeExpressionAst composite => CountWrittenGenericArgsOnComposite(composite),
                _ => 0,
            };
        }

        private static int CountWrittenGenericArgsOnComposite(PhpTypeExpressionAst composite)
        {
            var inner = UnwrapToPrimaryTypeAst(composite);
            return ReferenceEquals(inner, composite) ? 0 : CountWrittenGenericArgs(inner);
        }

        private static bool TryGetWrittenArgList(IBase2Ast node, out PhpTypeExpressionListAst list)
        {
            if (node is TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst fromName }
                && fromName.GetAllNotNull().Any())
            {
                list = fromName;
                return true;
            }

            foreach (var key in (string[])["typeName", "identifier"])
            {
                if (node.AstGrammarAddons.TryGetValue(key, out var addon)
                    && addon is PhpTypeExpressionListAst fromAddon
                    && fromAddon.GetAllNotNull().Any())
                {
                    list = fromAddon;
                    return true;
                }
            }

            list = null!;
            return false;
        }

        private static string LastTypeNameSegment(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            var trimmed = name.TrimStart('\\');
            var angle = trimmed.IndexOf('<');
            if (angle >= 0)
            {
                trimmed = trimmed[..angle];
            }

            var slash = trimmed.LastIndexOf('\\');
            return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        }

        private static bool IsTypeName(string? name, string expected)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return string.Equals(
                LastTypeNameSegment(name), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWeakerExportName(string? name) =>
            IsTypeName(name, "Iterator")
            || IsTypeName(name, "Traversable")
            || IsTypeName(name, "iterable");

        private static bool IsWeakerExport(ICheckedType type) =>
            IsNominalName(type, "Iterator")
            || IsNominalName(type, "Traversable")
            || CheckerHelpers.IsBuiltInName(type, "iterable")
            || (type is GenericCheckedType generic && IsWeakerExport(generic.BaseType));

        private static ICheckedType BuildSendType(
            GeneratorBodyCollector collector,
            CheckerRuleContext context,
            CheckerState state,
            IBase2Ast reportNode,
            DiagnosticBag diagnostics)
        {
            if (collector.SendConstraints.Count == 0)
            {
                return CheckedTypes.Mixed;
            }

            ICheckedType? intersection = null;
            foreach (var constraint in collector.SendConstraints)
            {
                intersection = intersection is null
                    ? constraint
                    : TypeComparer.IntersectTypes(
                        intersection, constraint, context.SymbolTree, context.GlobalScope);
            }

            intersection ??= CheckedTypes.Mixed;
            if (TypeComparer.IsNeverType(intersection))
            {
                var joined = string.Join(
                    " & ",
                    collector.SendConstraints.Select(c => c.DisplayName).Distinct(StringComparer.Ordinal));
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    reportNode,
                    MessageCode.CheckerGeneratorSendIntersectionEmpty,
                    joined);
                return CheckedTypes.Never;
            }

            return intersection;
        }

        private static ICheckedType BuildReturnType(
            GeneratorBodyCollector collector,
            bool fellOffEnd,
            CheckerRuleContext context)
        {
            var members = new List<ICheckedType>(collector.Returns);
            if (fellOffEnd)
            {
                members.Add(CheckedTypes.Null);
            }

            return UnionOr(members, CheckedTypes.Null, context);
        }

        private static void ApplyPins(
            DeclaredShape shape,
            ICheckedType? declared,
            ref ICheckedType tKey,
            ref ICheckedType tValue,
            ref ICheckedType tSend,
            ref ICheckedType tReturn,
            IBase2Ast reportNode,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            declared = UnwrapNullable(declared);
            if (declared is not GenericCheckedType generic)
            {
                return;
            }

            if (shape is DeclaredShape.TwoArgGenerator or DeclaredShape.FourArgGenerator
                or DeclaredShape.WeakerExport)
            {
                if (generic.TypeArguments.Count >= 1)
                {
                    var pinnedValue = generic.TypeArguments.Count >= 2
                        ? generic.TypeArguments[1]
                        : generic.TypeArguments[0];
                    var pinnedKey = generic.TypeArguments.Count >= 2
                        ? generic.TypeArguments[0]
                        : CheckedTypes.Int;

                    if (shape != DeclaredShape.WeakerExport || generic.TypeArguments.Count >= 2)
                    {
                        CheckPin(
                            tKey,
                            pinnedKey,
                            "TKey",
                            reportNode,
                            state,
                            context,
                            diagnostics);
                        tKey = pinnedKey;
                    }

                    if (shape != DeclaredShape.WeakerExport || generic.TypeArguments.Count >= 1)
                    {
                        CheckPin(
                            tValue,
                            pinnedValue,
                            "TValue",
                            reportNode,
                            state,
                            context,
                            diagnostics);
                        tValue = pinnedValue;
                    }
                }
            }

            if (shape != DeclaredShape.FourArgGenerator || generic.TypeArguments.Count < 3)
            {
                return;
            }

            var pinnedSend = generic.TypeArguments[2];
            // TSend is contravariant: callers may send the declared type, so the body must
            // accept it (declared TSend <: inferred TSend).
            if (!context.IsAssignable(pinnedSend, tSend, state)
                && !TypeComparer.IsMixedType(tSend))
            {
                ReportPinMismatch(tSend, pinnedSend, reportNode, state, diagnostics);
            }
            else
            {
                tSend = pinnedSend;
            }

            if (generic.TypeArguments.Count >= 4)
            {
                var pinnedReturn = generic.TypeArguments[3];
                CheckPin(
                    tReturn,
                    pinnedReturn,
                    "TReturn",
                    reportNode,
                    state,
                    context,
                    diagnostics);
                tReturn = pinnedReturn;
            }
        }

        private static void CheckPin(
            ICheckedType inferred,
            ICheckedType pinned,
            string slot,
            IBase2Ast reportNode,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            _ = slot;
            if (TypeComparer.IsUnresolvedType(inferred)
                || TypeComparer.IsMixedType(pinned)
                || context.IsAssignable(inferred, pinned, state))
            {
                return;
            }

            ReportPinMismatch(inferred, pinned, reportNode, state, diagnostics);
        }

        private static void ReportPinMismatch(
            ICheckedType inferred,
            ICheckedType pinned,
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag diagnostics) =>
            CheckerHelpers.ReportError(
                diagnostics,
                state,
                reportNode,
                MessageCode.CheckerGeneratorInvalidReturnType,
                pinned.DisplayName,
                inferred.DisplayName);

        private static ICheckedType BuildGeneratorType(
            ICheckedType tKey,
            ICheckedType tValue,
            ICheckedType tSend,
            ICheckedType tReturn,
            CheckerRuleContext context)
        {
            var generator = CheckerHelpers.ResolveNamedType(
                "Generator", context.SymbolTree, context.GlobalScope);
            if (generator.Kind == CheckedTypeKind.Unresolved)
            {
                generator = CheckerHelpers.ResolveNamedType(
                    "\\Generator", context.SymbolTree, context.GlobalScope);
            }

            if (generator.Kind == CheckedTypeKind.Unresolved)
            {
                return CheckedTypes.Unresolved;
            }

            return new GenericCheckedType(generator, [tKey, tValue, tSend, tReturn]);
        }

        private static ICheckedType UnionOr(
            IReadOnlyList<ICheckedType> members,
            ICheckedType fallback,
            CheckerRuleContext context)
        {
            if (members.Count == 0)
            {
                return fallback;
            }

            if (members.Count == 1)
            {
                return members[0];
            }

            return TypeComparer.UnionTypes(members, context.SymbolTree, context.GlobalScope);
        }

        private static ICheckedType? UnwrapNullable(ICheckedType? type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return type;
        }

        private static bool IsYieldFromUnary(PhpUnaryOpAst unary)
        {
            if (unary.Operator?.ValueInt64 == TyhpParser.T_YIELD_FROM)
            {
                return true;
            }

            var op = unary.Operator?.ValueString;
            if (string.IsNullOrEmpty(op))
            {
                return false;
            }

            if (string.Equals(op, "from", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return op.Contains("yield", StringComparison.OrdinalIgnoreCase)
                && op.Contains("from", StringComparison.OrdinalIgnoreCase);
        }
    }
}
