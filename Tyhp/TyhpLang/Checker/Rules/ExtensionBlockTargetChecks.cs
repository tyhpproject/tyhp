using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Emitter.Splice;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Block-target rules for <c>extension Name extends Type</c> and nested <c>extends</c> groups.
    /// </summary>
    internal static class ExtensionBlockTargetChecks
    {
        public static void CheckSurface(
            IBase2Ast declaration,
            string name,
            ITypeExpression? headerTarget,
            TyhpGenericsTypeArgumentListAst? headerGenerics,
            ObjectDeclarationSymbol? extensionSymbol,
            IReadOnlyList<IBase2Ast> members,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var groups = new List<TyhpExtensionDeclAst>();
            var loose = new List<IBase2Ast>();
            foreach (var member in members)
            {
                if (member is TyhpExtensionDeclAst { IsTargetGroup: true } group)
                {
                    groups.Add(group);
                }
                else if (IsLooseMember(member))
                {
                    loose.Add(member);
                }
            }

            if (headerTarget is not null && groups.Count > 0)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    groups[0],
                    MessageCode.CheckerExtensionHeaderAndNestedTargets,
                    name);
            }

            if (groups.Count > 0 && loose.Count > 0)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    loose[0],
                    MessageCode.CheckerExtensionLooseMemberBesideGroup,
                    name);
            }

            if (headerTarget is null && groups.Count == 0 && loose.Count > 0)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    declaration,
                    MessageCode.CheckerExtensionMissingExtends,
                    name);
            }

            var blocks = new List<TargetBlock>();
            if (headerTarget is not null)
            {
                var headerType = ResolveTarget(
                    headerTarget, extensionSymbol, extensionSymbol, state, context, diagnostics);
                blocks.Add(new TargetBlock(extensionSymbol, headerType, ContributedMembers(loose)));
            }

            foreach (var group in groups)
            {
                var groupSymbol = group.BoundSymbol as ObjectDeclarationSymbol;
                ICheckedType? targetType = null;
                if (group.TargetType is not null)
                {
                    targetType = ResolveTarget(
                        group.TargetType, groupSymbol, extensionSymbol, state, context, diagnostics);
                }

                blocks.Add(new TargetBlock(
                    groupSymbol,
                    targetType,
                    ContributedMembers(group.FunctionList?.GetAllNotNull().Where(IsLooseMember).ToList() ?? [])));
            }

            CheckUnusedTypeParameters(
                headerGenerics,
                extensionSymbol,
                headerTarget,
                members,
                state,
                diagnostics);
            foreach (var group in groups)
            {
                CheckUnusedTypeParameters(
                    group.GenericParameters,
                    group.BoundSymbol as ObjectDeclarationSymbol,
                    group.TargetType,
                    group.FunctionList?.GetAllNotNull().ToList() ?? [],
                    state,
                    diagnostics);
            }

            CheckOverlap(blocks, state, context, diagnostics);
            CheckConvertFromAmbiguity(blocks, state, diagnostics);
        }

        public static void CheckMember(
            IBase2Ast member,
            ObjectDeclarationSymbol? blockSymbol,
            ObjectDeclarationSymbol? extensionSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            CheckRelativeTypes(member, state, diagnostics);
            CheckTypeParameterShadow(member, blockSymbol, extensionSymbol, state, diagnostics);
            CheckByRefReceiver(member, blockSymbol, state, context, diagnostics);
        }

        /// <summary>
        /// True for a member of a user-authored <c>extension</c> block or nested group.
        /// Class-body tyhpdef thin mappings live on a compiler-generated synthetic extension.
        /// </summary>
        public static bool IsUserExtensionBlock(IBaseSymbol? symbol)
        {
            for (var scope = (symbol as BaseSymbol)?.ContainingScope; scope is not null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is not ObjectDeclarationSymbol obj)
                {
                    continue;
                }

                if (obj.IsExtensionTargetGroup)
                {
                    continue;
                }

                if (obj.IsExtension)
                {
                    return !obj.IsCompilerGenerated;
                }
            }

            return false;
        }

        public static IReadOnlyList<GenericTypeParameterSymbol> InScopeTypeParameters(
            ObjectDeclarationSymbol? block,
            ObjectDeclarationSymbol? extension)
        {
            var list = new List<GenericTypeParameterSymbol>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Add(block);
            if (!ReferenceEquals(block, extension))
            {
                Add(extension);
            }

            return list;

            void Add(ObjectDeclarationSymbol? symbol)
            {
                if (symbol is null)
                {
                    return;
                }

                foreach (var parameter in symbol.GenericParameters)
                {
                    if (seen.Add(parameter.Name))
                    {
                        list.Add(parameter);
                    }
                }
            }
        }

        private static void CheckUnusedTypeParameters(
            TyhpGenericsTypeArgumentListAst? declarations,
            ObjectDeclarationSymbol? owner,
            ITypeExpression? target,
            IReadOnlyList<IBase2Ast> members,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (owner is null || owner.GenericParameters.Count == 0)
            {
                return;
            }

            var tracked = owner.GenericParameters.Select(parameter => parameter.Name)
                .ToHashSet(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var shadowed = new HashSet<string>(StringComparer.Ordinal);
            if (target is not null)
            {
                CollectTypeParameterUses(target, tracked, shadowed, used, []);
            }

            foreach (var parameter in owner.GenericParameters)
            {
                CollectTypeParameterUses(parameter.Constraint, tracked, shadowed, used, []);
                CollectTypeParameterUses(parameter.DefaultType, tracked, shadowed, used, []);
            }

            foreach (var member in members)
            {
                CollectMemberUses(member, owner, tracked, shadowed, used);
            }

            foreach (var parameter in owner.GenericParameters)
            {
                if (used.Contains(parameter.Name))
                {
                    continue;
                }

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    parameter.DeclaringAstNode ?? declarations ?? owner.DeclaringAstNode!,
                    MessageCode.CheckerExtensionUnusedTypeParameter,
                    parameter.Name);
            }
        }

        private static void CollectMemberUses(
            IBase2Ast member,
            ObjectDeclarationSymbol? owner,
            HashSet<string> tracked,
            HashSet<string> shadowed,
            HashSet<string> used)
        {
            if (member is TyhpExtensionDeclAst { IsTargetGroup: true } group)
            {
                var groupSymbol = group.BoundSymbol as ObjectDeclarationSymbol;
                var groupShadow = new HashSet<string>(shadowed, StringComparer.Ordinal);
                if (groupSymbol is not null)
                {
                    foreach (var parameter in groupSymbol.GenericParameters)
                    {
                        groupShadow.Add(parameter.Name);
                    }
                }

                CollectTypeParameterUses(group.TargetType, tracked, groupShadow, used, []);
                if (groupSymbol is not null)
                {
                    foreach (var parameter in groupSymbol.GenericParameters)
                    {
                        CollectTypeParameterUses(parameter.Constraint, tracked, groupShadow, used, []);
                        CollectTypeParameterUses(parameter.DefaultType, tracked, groupShadow, used, []);
                    }
                }

                foreach (var child in group.FunctionList?.GetAllNotNull() ?? [])
                {
                    CollectMemberUses(child, groupSymbol, tracked, groupShadow, used);
                }

                return;
            }

            var method = MethodSymbol(member, owner);
            var methodShadow = new HashSet<string>(shadowed, StringComparer.Ordinal);
            if (method is not null)
            {
                foreach (var parameter in method.GenericParameters)
                {
                    methodShadow.Add(parameter.Name);
                    CollectTypeParameterUses(parameter.Constraint, tracked, methodShadow, used, []);
                    CollectTypeParameterUses(parameter.DefaultType, tracked, methodShadow, used, []);
                }
            }

            CollectTypeParameterUses(member, tracked, methodShadow, used, [], skipGenericAddon: true);
        }

        private static void CollectTypeParameterUses(
            IBase2Ast? node,
            HashSet<string> tracked,
            HashSet<string> shadowed,
            HashSet<string> used,
            HashSet<IBase2Ast> visited,
            bool skipGenericAddon = false)
        {
            if (node is null || !visited.Add(node))
            {
                return;
            }

            if (SimpleName(node) is { } name
                && tracked.Contains(name)
                && !shadowed.Contains(name))
            {
                used.Add(name);
            }

            foreach (var child in node.AstChildren)
            {
                CollectTypeParameterUses(child, tracked, shadowed, used, visited);
            }

            foreach (var addon in node.AstGrammarAddons)
            {
                if (skipGenericAddon && addon.Key == "identifier")
                {
                    continue;
                }

                CollectTypeParameterUses(addon.Value, tracked, shadowed, used, visited);
            }
        }

        private static void CheckTypeParameterShadow(
            IBase2Ast member,
            ObjectDeclarationSymbol? blockSymbol,
            ObjectDeclarationSymbol? extensionSymbol,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var method = MethodSymbol(member, blockSymbol) ?? MethodSymbol(member, extensionSymbol);
            if (method is null || method.GenericParameters.Count == 0)
            {
                return;
            }

            var inScope = InScopeTypeParameters(blockSymbol, extensionSymbol);
            if (inScope.Count == 0)
            {
                return;
            }

            var inScopeNames = inScope.Select(parameter => parameter.Name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var parameter in method.GenericParameters)
            {
                if (!inScopeNames.Contains(parameter.Name))
                {
                    continue;
                }

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    parameter.DeclaringAstNode ?? member,
                    MessageCode.CheckerExtensionTypeParameterShadowed,
                    parameter.Name);
            }
        }

        private static void CheckRelativeTypes(
            IBase2Ast member,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            WalkRelative(member, isRoot: true, state, diagnostics, []);
        }

        private static void WalkRelative(
            IBase2Ast? node,
            bool isRoot,
            CheckerState state,
            DiagnosticBag diagnostics,
            HashSet<IBase2Ast> visited)
        {
            if (node is null || !visited.Add(node))
            {
                return;
            }

            if (!isRoot && node is PhpFunctionDeclAst or PhpMethodDeclAst or PhpObjectTypeDeclAst)
            {
                return;
            }

            if (node is PhpDereferenceableAst deref
                && deref.Suffix is PhpStaticMemberAccessAst or PhpClassConstantAccessAst
                && Qualifier(deref.Base) is { } qualifier
                && (string.Equals(qualifier, "static", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(qualifier, "parent", StringComparison.OrdinalIgnoreCase)))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    deref,
                    MessageCode.CheckerExtensionRelativeType,
                    qualifier);
            }

            foreach (var child in node.AstChildren)
            {
                WalkRelative(child, isRoot: false, state, diagnostics, visited);
            }
        }

        private static void CheckByRefReceiver(
            IBase2Ast member,
            ObjectDeclarationSymbol? blockSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (blockSymbol?.PendingExtensionBlockTarget is null || BodyOf(member) is not { } body)
            {
                return;
            }

            // Object / class / interface / enum targets share a handle: a property write is
            // visible to the caller without `&$this` (same as a mutating method call). Scalar /
            // array / struct targets are by-value PHP parameters — a write through `$this->prop`
            // or `$this[$k]` only mutates the local copy, exactly like reassigning `$this` itself,
            // so it needs the same annotation (decision 4 — do not confuse object vs scalar here).
            var valueTypeTarget = state.EnclosingObjectType is { } targetType
                && CheckerHelpers.IsClosedMemberReceiver(targetType);
            var writes = WritesThis(body, state, context, valueTypeTarget);
            var annotated = HasByRefReceiver(member);
            var memberName = member.Identifier ?? "";
            if (writes && !annotated)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    member,
                    MessageCode.CheckerExtensionByRefReceiverRequired,
                    memberName);
            }
            else if (annotated && !writes)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    member,
                    MessageCode.CheckerExtensionByRefReceiverUnused,
                    memberName);
            }
        }

        private static bool WritesThis(
            IBase2Ast body,
            CheckerState state,
            CheckerRuleContext context,
            bool valueTypeTarget)
        {
            var written = false;
            CollectThisWrites(body, state, context, valueTypeTarget, ref written, []);
            return written;
        }

        private static void CollectThisWrites(
            IBase2Ast? node,
            CheckerState state,
            CheckerRuleContext context,
            bool valueTypeTarget,
            ref bool written,
            HashSet<IBase2Ast> visited)
        {
            if (node is null || written || !visited.Add(node))
            {
                return;
            }

            if (node is PhpObjectTypeDeclAst or PhpFunctionDeclAst or PhpMethodDeclAst)
            {
                return;
            }

            switch (node)
            {
                case PhpBinaryOpAst binary when SpliceAst.IsAssignmentOperator(binary.Operator):
                    if (IsThisExpression(binary.Left)
                        || (valueTypeTarget && IsThisRootedDereference(binary.Left)))
                    {
                        written = true;
                    }

                    CollectThisWrites(binary.Right, state, context, valueTypeTarget, ref written, visited);
                    if (SpliceAst.IsCompoundAssignmentOperator(binary.Operator))
                    {
                        CollectThisWrites(binary.Left, state, context, valueTypeTarget, ref written, visited);
                    }

                    return;

                case PhpUnaryOpAst unary when SpliceAst.IsIncrementDecrement(unary.Operator):
                    if (IsThisExpression(unary.Operand)
                        || (valueTypeTarget && IsThisRootedDereference(unary.Operand)))
                    {
                        written = true;
                    }

                    return;

                case PhpDereferenceableAst deref when deref.Suffix is PhpCallAst call:
                    if (CallWritesThis(deref, call, state, context))
                    {
                        written = true;
                        return;
                    }

                    CollectThisWrites(deref.Base, state, context, valueTypeTarget, ref written, visited);
                    foreach (var argument in call.Arguments?.GetAllNotNull() ?? [])
                    {
                        CollectThisWrites(argument, state, context, valueTypeTarget, ref written, visited);
                    }

                    return;
            }

            foreach (var child in node.AstChildren)
            {
                CollectThisWrites(child, state, context, valueTypeTarget, ref written, visited);
            }
        }

        /// <summary>
        /// True when <paramref name="expression"/> is a member/array-access chain rooted at
        /// <c>$this</c> (<c>$this-&gt;prop</c>, <c>$this[$k]</c>, <c>$this[]</c>, or a chain of
        /// those). Callers gate this on a value-type target
        /// (<see cref="CheckerHelpers.IsClosedMemberReceiver"/>) — for an object target the same
        /// write is visible to the caller through the shared handle and must not count.
        /// </summary>
        private static bool IsThisRootedDereference(IExpression? expression)
        {
            var current = SpliceAst.UnwrapParens(expression) as IDereferenceableBase;
            while (current is PhpDereferenceableAst deref)
            {
                current = deref.Base;
            }

            return current is PhpVariableAst variable && CheckerHelpers.IsThisVariable(variable);
        }

        private static bool CallWritesThis(
            PhpDereferenceableAst deref,
            PhpCallAst call,
            CheckerState state,
            CheckerRuleContext context)
        {
            var parameters = ResolveCallParameters(deref, state, context);
            if (parameters is null || call.Arguments is null)
            {
                return false;
            }

            var method = deref.BoundSymbol as ObjectMethodSymbol
                ?? (deref.Base as PhpDereferenceableAst)?.BoundSymbol as ObjectMethodSymbol
                ?? (deref.Base as PhpNameAst)?.BoundSymbol as ObjectMethodSymbol;
            var slots = CheckerHelpers.ExcludeExtensionReceiver(parameters, method);
            var positional = 0;
            foreach (var argument in call.Arguments.GetAllNotNull())
            {
                ParameterInfo? slot = null;
                if (argument.Name?.ValueString is { } named)
                {
                    slot = slots.FirstOrDefault(parameter =>
                        string.Equals(
                            NormalizeParameterName(parameter.Name),
                            NormalizeParameterName(named),
                            StringComparison.OrdinalIgnoreCase));
                }
                else if (positional < slots.Count)
                {
                    slot = slots[positional];
                    positional++;
                }

                if (slot is { IsByReference: true } && IsThisExpression(argument.Expression))
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<ParameterInfo>? ResolveCallParameters(
            PhpDereferenceableAst deref,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (SpliceAst.ParametersOf(deref.BoundSymbol) is { } bound)
            {
                return bound;
            }

            if (deref.Base is PhpNameAst name)
            {
                if (SpliceAst.ParametersOf(name.BoundSymbol) is { } fromName)
                {
                    return fromName;
                }

                return CheckerHelpers.ResolveFreeFunction(
                    name, state, context.SymbolTree, context.GlobalScope)?.Parameters;
            }

            if (deref.Base is PhpDereferenceableAst inner)
            {
                return SpliceAst.ParametersOf(inner.BoundSymbol);
            }

            return null;
        }

        private static ICheckedType? ResolveTarget(
            ITypeExpression target,
            ObjectDeclarationSymbol? block,
            ObjectDeclarationSymbol? extension,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (TryDescribeIllegalShape(target, out var found))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    target,
                    MessageCode.CheckerExtensionTargetNotSingleType,
                    found);
                return null;
            }

            var resolveState = state.Fork();
            resolveState.EnclosingObject = extension;
            resolveState.ObjectGenerics = InScopeTypeParameters(block, extension);
            GenericConstraintResolver.ResolveAll(resolveState.ObjectGenerics, resolveState, context);
            context.MarkImportNames(target, state);
            context.CheckNode(target, resolveState);
            var resolved = context.ResolveTypeAnnotation(target, resolveState);
            if (TypeComparer.IsUnresolvedType(resolved))
            {
                return null;
            }

            if (IsRejectedExtensionTarget(resolved))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    target,
                    MessageCode.CheckerExtensionTargetNotSingleType,
                    resolved.DisplayName);
                return null;
            }

            return resolved;
        }

        /// <summary>
        /// After alias expansion. A union is a legal block target when every member is
        /// itself a legal target — a class / interface / enum / builtin / generic
        /// application (checked recursively so a member that came in through a type
        /// alias, e.g. <c>type X = A&amp;B; extends X|string</c>, is still caught).
        /// <c>?T</c> is the inner target plus null.
        /// An intersection, an <c>object { }</c> shape, <c>void</c>,
        /// <c>never</c>, <c>mixed</c>, or a type parameter is not a block target,
        /// whether written directly or as one member of a union.
        /// </summary>
        private static bool IsRejectedExtensionTarget(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is UnionCheckedType union)
            {
                return union.Members.Count == 0 || union.Members.Any(IsRejectedExtensionTarget);
            }

            if (type is IntersectionCheckedType or ObjectShapeCheckedType)
            {
                return true;
            }

            if (TypeComparer.IsVoidType(type)
                || TypeComparer.IsNeverType(type)
                || TypeComparer.IsMixedType(type))
            {
                return true;
            }

            return TypeComparer.TryGetNominalSymbol(type) is GenericTypeParameterSymbol;
        }

        /// <summary>
        /// Nested <c>extends</c> groups share one PHP backer, so convert-from forms share
        /// <c>__from</c> even when their targets do not overlap. Class-body
        /// <c>ValidateOperatorOverloadSet</c> never sees those groups together.
        /// A short <c>=&gt; expr</c> extension operator is erased entirely — never added to the
        /// emitted <c>__from</c> dispatcher (see <c>TyhpEmitter.EmitExtensionMembers</c>) — so two
        /// such forms with the same source type never share a runtime branch and must not be
        /// flagged here.
        /// </summary>
        private static void CheckConvertFromAmbiguity(
            IReadOnlyList<TargetBlock> blocks,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var operators = new List<TyhpOperatorOverloadAst>();
            foreach (var block in blocks)
            {
                foreach (var member in block.Members)
                {
                    if (member is TyhpOperatorOverloadAst { IsShortSyntax: false } op)
                    {
                        operators.Add(op);
                    }
                }
            }

            DeclarationRule.ReportAmbiguousConvertFromForms(operators, state, diagnostics);
        }

        private static void CheckOverlap(
            IReadOnlyList<TargetBlock> blocks,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                for (var j = i + 1; j < blocks.Count; j++)
                {
                    var left = blocks[i];
                    var right = blocks[j];
                    if (left.Target is null || right.Target is null)
                    {
                        continue;
                    }

                    if (!TargetsOverlap(left.Target, right.Target, context))
                    {
                        continue;
                    }

                    var rightMembers = IndexMembers(right.Members);
                    foreach (var member in left.Members)
                    {
                        if (MemberKey(member) is not { } key || !rightMembers.TryGetValue(key, out var other))
                        {
                            continue;
                        }

                        CheckerHelpers.ReportError(
                            diagnostics,
                            state,
                            other,
                            MessageCode.CheckerExtensionOverlappingMember,
                            MemberLabel(other),
                            left.Target.DisplayName,
                            right.Target.DisplayName);
                    }
                }
            }
        }

        /// <summary>
        /// True when some extension method of <paramref name="methodName"/> is declared on a
        /// union block target. Call-site lookup then uses
        /// <see cref="UnionTargetCoversReceiver"/> instead of the first union member.
        /// </summary>
        internal static bool HasUnionTargetMethod(SymbolTree symbolTree, string methodName)
        {
            if (string.IsNullOrEmpty(methodName)
                || !symbolTree.ExtensionMethodIndex.TryGetValue(methodName, out var candidates))
            {
                return false;
            }

            foreach (var candidate in candidates)
            {
                if (TryGetUnionTargetMembers(candidate, symbolTree, out _))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Members of a union block target, aliases followed through to the union.
        /// False for a single type, including <c>?T</c>.
        /// </summary>
        internal static bool TryGetUnionTargetMembers(
            ObjectMethodSymbol method,
            SymbolTree symbolTree,
            out IReadOnlyList<ICheckedType> members)
        {
            members = [];
            var target = UnionTargetAst(method);
            if (target is null)
            {
                return false;
            }

            var resolver = new NameResolver(symbolTree, new DiagnosticBag());
            var scope = BlockScope(method) ?? symbolTree.GlobalScope;
            var shape = UnwrapAliasTarget(target, resolver, scope, []);
            if (!IsUnionShape(shape))
            {
                return false;
            }

            var collected = new List<ICheckedType>();
            CollectUnionMembers(shape, resolver, scope, collected, []);
            if (collected.Count < 2)
            {
                return false;
            }

            members = collected;
            return true;
        }

        /// <summary>
        /// A union target applies when every type <paramref name="receiver"/> might be is one of
        /// <paramref name="targetMembers"/>. A receiver that is only <c>null</c> does not match.
        /// </summary>
        internal static bool UnionTargetCoversReceiver(
            IReadOnlyList<ICheckedType> targetMembers,
            ICheckedType receiver)
        {
            var target = new List<ICheckedType>();
            foreach (var member in targetMembers)
            {
                CollectConstituents(member, target);
            }

            if (target.Count < 2)
            {
                return false;
            }

            var parts = new List<ICheckedType>();
            CollectConstituents(receiver, parts);
            if (parts.Count == 0 || parts.All(IsNullConstituent))
            {
                return false;
            }

            foreach (var part in parts)
            {
                if (!target.Any(member => SameConstituent(member, part)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TargetsOverlap(
            ICheckedType left,
            ICheckedType right,
            CheckerRuleContext context)
        {
            if (TypeComparer.IsUnresolvedType(left) || TypeComparer.IsUnresolvedType(right))
            {
                return false;
            }

            if (IsUnionTarget(left) || IsUnionTarget(right))
            {
                return UnionTargetsShareReceiver(left, right, context);
            }

            return NonUnionTargetsOverlap(left, right, context);
        }

        private static bool NonUnionTargetsOverlap(
            ICheckedType left,
            ICheckedType right,
            CheckerRuleContext context)
        {
            // `?T` applies to `T`, so nullability does not keep two targets apart.
            left = PeelNullable(left);
            right = PeelNullable(right);
            if (TypeComparer.IsUnresolvedType(left) || TypeComparer.IsUnresolvedType(right))
            {
                return false;
            }

            if (WideTargetOverlaps(left, right))
            {
                return true;
            }

            if (left is GenericCheckedType leftGeneric && right is GenericCheckedType rightGeneric)
            {
                if (!SameNominal(leftGeneric.BaseType, rightGeneric.BaseType))
                {
                    return false;
                }

                if (leftGeneric.TypeArguments.Count != rightGeneric.TypeArguments.Count)
                {
                    return false;
                }

                for (var i = 0; i < leftGeneric.TypeArguments.Count; i++)
                {
                    if (!ArgumentsOverlap(
                            leftGeneric.TypeArguments[i],
                            rightGeneric.TypeArguments[i],
                            context))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (left is GenericCheckedType || right is GenericCheckedType)
            {
                return false;
            }

            return SameNominal(left, right);
        }

        private static bool ArgumentsOverlap(
            ICheckedType left,
            ICheckedType right,
            CheckerRuleContext context)
        {
            if (TypeComparer.IsUnresolvedType(left) || TypeComparer.IsUnresolvedType(right))
            {
                return false;
            }

            if (TypeParameter(left) is { } leftParameter && TypeParameter(right) is { } rightParameter)
            {
                return TypeParametersOverlap(leftParameter, rightParameter, context);
            }

            if (TypeParameter(left) is { } onlyLeft)
            {
                return TypeParameterMatches(onlyLeft, right, context);
            }

            if (TypeParameter(right) is { } onlyRight)
            {
                return TypeParameterMatches(onlyRight, left, context);
            }

            if (left is GenericCheckedType && right is GenericCheckedType)
            {
                return TargetsOverlap(left, right, context);
            }

            return SameNominal(left, right);
        }

        private static bool TypeParametersOverlap(
            GenericTypeParameterSymbol left,
            GenericTypeParameterSymbol right,
            CheckerRuleContext context)
        {
            if (left.ResolvedConstraint is null || right.ResolvedConstraint is null)
            {
                return true;
            }

            return TypeComparer.IsAssignableTo(
                    left.ResolvedConstraint, right.ResolvedConstraint, context.SymbolTree, context.GlobalScope)
                || TypeComparer.IsAssignableTo(
                    right.ResolvedConstraint, left.ResolvedConstraint, context.SymbolTree, context.GlobalScope);
        }

        private static bool TypeParameterMatches(
            GenericTypeParameterSymbol parameter,
            ICheckedType concrete,
            CheckerRuleContext context)
        {
            if (parameter.ResolvedConstraint is null)
            {
                return true;
            }

            return TypeComparer.IsAssignableTo(
                concrete, parameter.ResolvedConstraint, context.SymbolTree, context.GlobalScope);
        }

        private static bool SameNominal(ICheckedType left, ICheckedType right)
        {
            var leftSymbol = TypeComparer.TryGetNominalSymbol(left);
            var rightSymbol = TypeComparer.TryGetNominalSymbol(right);
            if (leftSymbol is null || rightSymbol is null)
            {
                return TypeComparer.AreTypesEqual(left, right);
            }

            if (ReferenceEquals(leftSymbol, rightSymbol))
            {
                return true;
            }

            if (leftSymbol is BuiltInTypeSymbol && rightSymbol is BuiltInTypeSymbol)
            {
                return string.Equals(leftSymbol.Name, rightSymbol.Name, StringComparison.OrdinalIgnoreCase);
            }

            var leftName = string.IsNullOrEmpty(leftSymbol.FullyQualifiedName)
                ? leftSymbol.Name
                : leftSymbol.FullyQualifiedName;
            var rightName = string.IsNullOrEmpty(rightSymbol.FullyQualifiedName)
                ? rightSymbol.Name
                : rightSymbol.FullyQualifiedName;
            return string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnionTarget(ICheckedType type) =>
            PeelNullable(type) is UnionCheckedType;

        /// <summary>
        /// Two targets overlap when one receiver is in both match sets. A union shares a
        /// receiver with another union when they have a non-null member in common, and with
        /// a single target when that target applies to one of the union's non-null members.
        /// </summary>
        private static bool UnionTargetsShareReceiver(
            ICheckedType left,
            ICheckedType right,
            CheckerRuleContext context)
        {
            var leftUnion = IsUnionTarget(left);
            var rightUnion = IsUnionTarget(right);
            if (leftUnion && rightUnion)
            {
                return UnionsShareNonNullConstituent(left, right);
            }

            var union = leftUnion ? left : right;
            var single = leftUnion ? right : left;
            foreach (var member in FlattenConstituents(union))
            {
                if (IsNullConstituent(member))
                {
                    continue;
                }

                if (NonUnionTargetsOverlap(single, member, context))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool UnionsShareNonNullConstituent(ICheckedType left, ICheckedType right)
        {
            var rightMembers = FlattenConstituents(right);
            foreach (var member in FlattenConstituents(left))
            {
                if (IsNullConstituent(member))
                {
                    continue;
                }

                if (rightMembers.Any(other => !IsNullConstituent(other) && SameConstituent(member, other)))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<ICheckedType> FlattenConstituents(ICheckedType type)
        {
            var parts = new List<ICheckedType>();
            CollectConstituents(type, parts);
            return parts;
        }

        private static void CollectConstituents(ICheckedType type, List<ICheckedType> into)
        {
            switch (type)
            {
                case UnionCheckedType union:
                    foreach (var member in union.Members)
                    {
                        CollectConstituents(member, into);
                    }

                    return;
                case NullableCheckedType nullable:
                    into.Add(CheckedTypes.Null);
                    CollectConstituents(nullable.InnerType, into);
                    return;
                case LiteralCheckedType literal:
                    CollectConstituents(literal.UnderlyingType, into);
                    return;
                case StaticCheckedType staticType:
                    CollectConstituents(staticType.DeclaringType, into);
                    return;
                case GenericCheckedType generic:
                    CollectConstituents(generic.BaseType, into);
                    return;
                default:
                    into.Add(type);
                    return;
            }
        }

        private static bool SameConstituent(ICheckedType left, ICheckedType right) =>
            (IsNullConstituent(left) && IsNullConstituent(right))
            || TypeComparer.AreTypesEqual(left, right);

        private static bool IsNullConstituent(ICheckedType type) =>
            TypeComparer.IsNullLiteral(type) || TypeComparer.IsBuiltInName(type, "null");

        private static ITypeExpression? UnionTargetAst(ObjectMethodSymbol method)
        {
            for (var scope = method.ContainingScope; scope is not null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol block
                    && block.PendingExtensionBlockTarget is not null)
                {
                    return block.PendingExtensionBlockTarget;
                }
            }

            return null;
        }

        private static IBaseScope? BlockScope(ObjectMethodSymbol method)
        {
            var block = FindBlock(method);
            return block?.ContainingScope;
        }

        private static ObjectDeclarationSymbol? FindBlock(ObjectMethodSymbol method)
        {
            for (var scope = method.ContainingScope; scope is not null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol block
                    && block.PendingExtensionBlockTarget is not null)
                {
                    return block;
                }
            }

            return null;
        }

        private static ITypeExpression UnwrapAliasTarget(
            ITypeExpression target,
            NameResolver resolver,
            IBaseScope scope,
            HashSet<IBaseSymbol> seenAliases)
        {
            var current = target;
            var seenAst = new HashSet<IBase2Ast>(ReferenceEqualityComparer.Instance);
            while (seenAst.Add(current))
            {
                if (IsUnionShape(current))
                {
                    return current;
                }

                var symbol = SymbolOf(current, resolver, scope);
                if (symbol is TypeAliasSymbol or ObjectTypeAliasSymbol
                    && seenAliases.Add(symbol)
                    && ObjectShapeSupport.GetAliasedType(symbol) is { } body)
                {
                    current = body;
                    continue;
                }

                if (current is PhpTypeExpressionAst { TypeKind: PhpTypeKind.Simple, IsNullable: false } simple
                    && FirstTypeChild(simple) is { } inner)
                {
                    current = inner;
                    continue;
                }

                return current;
            }

            return current;
        }

        private static bool IsUnionShape(ITypeExpression target)
        {
            if (target is not PhpTypeExpressionAst expression)
            {
                return false;
            }

            if (expression.TypeKind is PhpTypeKind.Union)
            {
                return true;
            }

            return expression.IsNullable
                && FirstTypeChild(expression) is PhpTypeExpressionAst { TypeKind: PhpTypeKind.Union };
        }

        private static void CollectUnionMembers(
            ITypeExpression target,
            NameResolver resolver,
            IBaseScope scope,
            List<ICheckedType> members,
            HashSet<IBaseSymbol> seenAliases)
        {
            if (target is PhpTypeExpressionAst { TypeKind: PhpTypeKind.Union } union)
            {
                foreach (var child in union.Types?.GetAllNotNull().OfType<ITypeExpression>() ?? [])
                {
                    CollectUnionMembers(child, resolver, scope, members, seenAliases);
                }

                if (union.IsNullable)
                {
                    members.Add(CheckedTypes.Null);
                }

                return;
            }

            if (target is PhpTypeExpressionAst { IsNullable: true } nullable
                && FirstTypeChild(nullable) is { } inner)
            {
                members.Add(CheckedTypes.Null);
                CollectUnionMembers(inner, resolver, scope, members, seenAliases);
                return;
            }

            var symbol = SymbolOf(target, resolver, scope);
            if (symbol is TypeAliasSymbol or ObjectTypeAliasSymbol
                && seenAliases.Add(symbol)
                && ObjectShapeSupport.GetAliasedType(symbol) is { } body)
            {
                CollectUnionMembers(body, resolver, scope, members, seenAliases);
                return;
            }

            if (symbol is not null)
            {
                members.Add(CheckedTypes.FromSymbol(symbol));
            }
        }

        private static IBaseSymbol? SymbolOf(
            ITypeExpression target,
            NameResolver resolver,
            IBaseScope scope)
        {
            if (target is IBase2Ast node && node.BoundSymbol is { } bound)
            {
                return bound;
            }

            if (target is PhpNamedTypeAst { Name: IBase2Ast name } && name.BoundSymbol is { } nameBound)
            {
                return nameBound;
            }

            return resolver.ResolveType(target, scope);
        }

        private static ITypeExpression? FirstTypeChild(PhpTypeExpressionAst expression) =>
            expression.Types?.GetAllNotNull().OfType<ITypeExpression>().FirstOrDefault();

        private static ICheckedType PeelNullable(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return type;
        }

        /// <summary>
        /// <c>object</c> applies to every class, interface, and enum.
        /// <c>callable</c> applies to callable-shaped targets (<c>string</c>, <c>array</c>,
        /// <c>\Closure</c>, callable shapes). Both apply to <c>\Closure</c>.
        /// </summary>
        private static bool WideTargetOverlaps(ICheckedType left, ICheckedType right)
        {
            if ((IsObjectTarget(left) && IsClassLikeTarget(right))
                || (IsObjectTarget(right) && IsClassLikeTarget(left)))
            {
                return true;
            }

            if ((IsCallableTarget(left) && IsCallableShapedTarget(right) && !IsCallableTarget(right))
                || (IsCallableTarget(right) && IsCallableShapedTarget(left) && !IsCallableTarget(left)))
            {
                return true;
            }

            return (IsObjectTarget(left) && IsCallableTarget(right))
                || (IsObjectTarget(right) && IsCallableTarget(left));
        }

        private static bool IsObjectTarget(ICheckedType type) =>
            TypeComparer.IsBuiltInName(type, "object");

        private static bool IsCallableTarget(ICheckedType type) =>
            TypeComparer.IsBuiltInName(type, "callable");

        private static bool IsClassLikeTarget(ICheckedType type)
        {
            if (TypeComparer.TryGetObjectDeclaration(type) is not
                { IsStruct: false, IsExtension: false } obj)
            {
                return false;
            }

            return obj.ObjectKind is PhpTypeDeclType.Class
                or PhpTypeDeclType.Interface
                or PhpTypeDeclType.Enum
                or PhpTypeDeclType.Unspecified;
        }

        private static bool IsCallableShapedTarget(ICheckedType type)
        {
            if (type is CallableCheckedType || IsCallableTarget(type))
            {
                return true;
            }

            if (TypeComparer.IsBuiltInName(type, "string")
                || TypeComparer.IsBuiltInName(type, "array"))
            {
                return true;
            }

            if (TypeComparer.TryGetObjectDeclaration(type) is not { } obj)
            {
                return false;
            }

            var qualified = (obj.FullyQualifiedName ?? obj.Name).TrimStart('\\');
            return string.Equals(qualified, "Closure", StringComparison.OrdinalIgnoreCase);
        }

        private static GenericTypeParameterSymbol? TypeParameter(ICheckedType type) =>
            type is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol parameter }
                ? parameter
                : null;

        private static Dictionary<string, IBase2Ast> IndexMembers(IReadOnlyList<IBase2Ast> members)
        {
            var index = new Dictionary<string, IBase2Ast>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                if (MemberKey(member) is { } key && !index.ContainsKey(key))
                {
                    index[key] = member;
                }
            }

            return index;
        }

        private static string? MemberKey(IBase2Ast member)
        {
            if (member is TyhpOperatorOverloadAst op)
            {
                if (op.BoundSymbol is ObjectOperatorOverloadMethodSymbol symbol)
                {
                    return "op:" + symbol.Operator;
                }

                return op.Op?.ValueString is { } token ? "op:" + token : null;
            }

            var name = member.Identifier;
            return string.IsNullOrEmpty(name) ? null : "method:" + name.ToLowerInvariant();
        }

        private static string MemberLabel(IBase2Ast member)
        {
            if (member is TyhpOperatorOverloadAst op)
            {
                return op.Op?.ValueString ?? "operator";
            }

            return member.Identifier ?? "";
        }

        private static bool TryDescribeIllegalShape(ITypeExpression target, out string found)
        {
            found = "";
            if (target is PhpTypeExpressionAst expression)
            {
            if (expression.TypeKind is PhpTypeKind.Intersection)
            {
                found = Spell(expression);
                return true;
            }

                var inner = expression.Types?.GetAllNotNull().OfType<ITypeExpression>().FirstOrDefault();
                if (inner is not null && TryDescribeIllegalShape(inner, out found))
                {
                    return true;
                }
            }

            if (ForbiddenBuiltin(target) is { } builtin)
            {
                found = builtin;
                return true;
            }

            if (ContainsWildcard(target))
            {
                found = "_";
                return true;
            }

            return false;
        }

        private static string? ForbiddenBuiltin(IBase2Ast node)
        {
            var name = node switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNameAst nameAst => SimpleName(nameAst),
                _ => null,
            };
            if (name is null)
            {
                return null;
            }

            if (string.Equals(name, "void", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "never", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "mixed", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            return null;
        }

        private static bool ContainsWildcard(IBase2Ast node)
        {
            if (string.Equals(SimpleName(node), "_", StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null && ContainsWildcard(child))
                {
                    return true;
                }
            }

            foreach (var addon in node.AstGrammarAddons.Values)
            {
                if (ContainsWildcard(addon))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Spell(IBase2Ast node)
        {
            switch (node)
            {
                case PhpTypeExpressionAst expression:
                    var parts = expression.Types?.GetAllNotNull().Select(Spell).ToList() ?? [];
                    var joined = expression.TypeKind switch
                    {
                        PhpTypeKind.Union => string.Join("|", parts),
                        PhpTypeKind.Intersection => string.Join("&", parts),
                        _ => parts.Count == 0 ? "type" : string.Join(", ", parts),
                    };
                    return expression.IsNullable ? "?" + joined : joined;
                case PhpBuiltinTypeAst builtin:
                    return builtin.Identifier ?? "type";
                case PhpNamedTypeAst named when named.Name is not null:
                    return Spell(named.Name);
                case TyhpGenericIdentifierAst generic:
                    var args = generic.GenericArguments is null
                        ? ""
                        : string.Join(", ", generic.GenericArguments.AstChildren.Where(child => child is not null).Select(child => Spell(child!)));
                    var name = SimpleName(generic) ?? "type";
                    return string.IsNullOrEmpty(args) ? name : name + "<" + args + ">";
                case PhpNameAst nameAst:
                    return SimpleName(nameAst) ?? "type";
                default:
                    return node.Identifier ?? "type";
            }
        }

        private static List<IBase2Ast> ContributedMembers(IReadOnlyList<IBase2Ast> members)
        {
            var implemented = OverloadSignatureHelper.CollectImplementedExtensionFunctionNames(
                members.OfType<IExtensionMemberAst>());
            return members.Where(member =>
                    member is not PhpFunctionDeclAst function
                    || !OverloadSignatureHelper.IsExtensionFunctionOverloadSignature(function, implemented))
                .ToList();
        }

        private static bool IsLooseMember(IBase2Ast member) =>
            member is PhpFunctionDeclAst
                or TyhpOperatorOverloadAst
                or TyhpdefInlineExtensionFunctionAst;

        private static ObjectMethodSymbol? MethodSymbol(IBase2Ast member, ObjectDeclarationSymbol? owner)
        {
            if (FindDeclaredMethod(owner, member) is { } declared)
            {
                return declared;
            }

            if (member.BoundSymbol is ObjectMethodSymbol bound)
            {
                return bound;
            }

            return member is TyhpdefInlineExtensionFunctionAst inline
                ? inline.Method?.BoundSymbol as ObjectMethodSymbol
                : null;
        }

        private static ObjectMethodSymbol? FindDeclaredMethod(ObjectDeclarationSymbol? owner, IBase2Ast member)
        {
            if (owner is null || string.IsNullOrEmpty(member.Identifier))
            {
                return null;
            }

            if (!owner.Members.TryGetValue(member.Identifier, out var found)
                || found is not ObjectMethodSymbol primary)
            {
                return null;
            }

            if (ReferenceEquals(primary.DeclaringAstNode, member)
                || primary.DeclaringAstNode is TyhpdefInlineExtensionFunctionAst inline
                    && ReferenceEquals(inline.Method, member))
            {
                return primary;
            }

            foreach (var overload in primary.Overloads)
            {
                if (overload is ObjectMethodSymbol method
                    && (ReferenceEquals(method.DeclaringAstNode, member)
                        || method.DeclaringAstNode is TyhpdefInlineExtensionFunctionAst overloadInline
                            && ReferenceEquals(overloadInline.Method, member)))
                {
                    return method;
                }
            }

            return null;
        }

        private static PhpStatementBlockAst? BodyOf(IBase2Ast member) =>
            member switch
            {
                PhpFunctionDeclAst function => function.Body,
                TyhpdefInlineExtensionFunctionAst inline => inline.Method?.Body,
                PhpMethodDeclAst method => method.Body,
                TyhpOperatorOverloadAst op => op.Body,
                _ => null,
            };

        private static bool HasByRefReceiver(IBase2Ast member)
        {
            if (member.AstGrammarAddons.ContainsKey(TyhpExtensionDeclAst.ByRefReceiverAddonKey))
            {
                return true;
            }

            return member is TyhpdefInlineExtensionFunctionAst { Method: { } method }
                && method.AstGrammarAddons.ContainsKey(TyhpExtensionDeclAst.ByRefReceiverAddonKey);
        }

        private static bool IsThisExpression(IExpression? expression)
        {
            expression = SpliceAst.UnwrapParens(expression);
            return expression is PhpVariableAst variable && CheckerHelpers.IsThisVariable(variable);
        }

        private static string? Qualifier(IBase2Ast? node) =>
            node is PhpNameAst name ? SimpleName(name) : null;

        private static string? SimpleName(IBase2Ast node)
        {
            if (node is not PhpNameAst name)
            {
                return null;
            }

            var text = name.ValueString ?? name.Identifier;
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var trimmed = text.TrimStart('\\');
            var separator = trimmed.LastIndexOf('\\');
            return separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
        }

        private static string NormalizeParameterName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            return name[0] == '$' ? name[1..] : name;
        }

        private sealed class TargetBlock(
            ObjectDeclarationSymbol? symbol,
            ICheckedType? target,
            IReadOnlyList<IBase2Ast> members)
        {
            public ObjectDeclarationSymbol? Symbol { get; } = symbol;

            public ICheckedType? Target { get; } = target;

            public IReadOnlyList<IBase2Ast> Members { get; } = members;
        }
    }
}
