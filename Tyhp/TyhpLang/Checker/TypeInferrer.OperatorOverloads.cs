using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker
{
    public sealed partial class TypeInferrer
    {
        /// <summary>
        /// True when a binary operator form matches <paramref name="left"/> / <paramref name="right"/>
        /// (same selection as inference / emit). Used by <c>TypeCompatibilityRule</c> to reject
        /// object operands with no applicable overload (TYHP4029).
        /// </summary>
        internal bool HasMatchingBinaryOperatorOverload(
            OverloadableOperator op,
            ICheckedType left,
            ICheckedType right,
            CheckerState state)
            => TryInferBinaryOperatorOverloadReturn(op, left, right, state, out _);

        /// <summary>
        /// True when a unary operator form matches <paramref name="operand"/> (same selection as
        /// inference / emit).
        /// </summary>
        internal bool HasMatchingUnaryOperatorOverload(
            OverloadableOperator op,
            ICheckedType operand,
            CheckerState state)
            => TryInferUnaryOperatorOverloadReturn(op, operand, state, out _);

        /// <summary>
        /// When a binary operator's operand types match a declared overload (left-first, then right —
        /// same selection as <c>AliasConverter</c>), returns that form's declared return type.
        /// Native PHP promotion is used only when no matching overload exists.
        /// </summary>
        private bool TryInferBinaryOperatorOverloadReturn(
            OverloadableOperator op,
            ICheckedType left,
            ICheckedType right,
            CheckerState state,
            out ICheckedType returnType)
        {
            returnType = CheckedTypes.Unresolved;
            if (op == OverloadableOperator.Invalid || op == OverloadableOperator.Convert)
            {
                return false;
            }

            if (TypeComparer.IsUnresolvedType(left) || TypeComparer.IsUnresolvedType(right))
            {
                return false;
            }

            var leftMembers = DescribeOperatorOperandMembers(left);
            var rightMembers = DescribeOperatorOperandMembers(right);

            // Left-first, then right — mirrors AliasConverter.SelectStaticBinaryOperatorTarget.
            if (TrySelectBinaryFormOnOwningType(
                    op,
                    SingleMemberSymbol(leftMembers),
                    leftMembers,
                    rightMembers,
                    state,
                    out var form,
                    out var owning))
            {
                returnType = ResolveOperatorOverloadReturnType(form!, owning!, state);
                return true;
            }

            if (TrySelectBinaryFormOnOwningType(
                    op,
                    SingleMemberSymbol(rightMembers),
                    leftMembers,
                    rightMembers,
                    state,
                    out form,
                    out owning))
            {
                returnType = ResolveOperatorOverloadReturnType(form!, owning!, state);
                return true;
            }

            return false;
        }

        /// <summary>
        /// When a unary operator's operand type matches a declared overload, returns that form's
        /// declared return type.
        /// </summary>
        private bool TryInferUnaryOperatorOverloadReturn(
            OverloadableOperator op,
            ICheckedType operand,
            CheckerState state,
            out ICheckedType returnType)
        {
            returnType = CheckedTypes.Unresolved;
            if (op == OverloadableOperator.Invalid || op == OverloadableOperator.Convert)
            {
                return false;
            }

            if (TypeComparer.IsUnresolvedType(operand))
            {
                return false;
            }

            DescribeOperatorOperand(operand, _globalScope, out var operandSymbol, out var operandName);
            if (!TrySelectUnaryFormOnOwningType(
                    op, operandSymbol, operandName, state, out var form, out var owning))
            {
                return false;
            }

            returnType = ResolveOperatorOverloadReturnType(form!, owning!, state);
            return true;
        }

        private bool TrySelectBinaryFormOnOwningType(
            OverloadableOperator op,
            IBaseSymbol? owningOperandSymbol,
            IReadOnlyList<OperatorOperandMember> leftMembers,
            IReadOnlyList<OperatorOperandMember> rightMembers,
            CheckerState state,
            out ObjectOperatorOverloadMethodSymbol? form,
            out IBaseSymbol? owningType)
        {
            form = null;
            owningType = null;

            if (owningOperandSymbol is ObjectDeclarationSymbol objectDecl && !objectDecl.IsStruct)
            {
                form = FindMatchingBinaryForm(objectDecl, op, leftMembers, rightMembers, state);
                if (form is not null)
                {
                    owningType = objectDecl;
                    return true;
                }

                // Trait-$this: no form on the trait itself — search composing classes (mirrors
                // AliasConverter), but only accept when every matching user's resolved return type
                // agrees. Disagreement falls through to native inference (Unresolved for two
                // objects) rather than picking a lying first-match type.
                if (objectDecl.ObjectKind == PhpTypeDeclType.Trait
                    && TrySelectAgreedComposingBinaryForm(
                        objectDecl, op, leftMembers, rightMembers, state,
                        out form, out owningType))
                {
                    return true;
                }

                return false;
            }

            if (owningOperandSymbol is BuiltInTypeSymbol builtin)
            {
                // Prefer the GlobalScope singleton so extension-contributed operators are visible.
                var scopeBuiltin = TypeComparer.ResolveBuiltIn(builtin.Name, _globalScope) ?? builtin;
                form = OperatorOverloadResolver.SelectMatchingBinaryForm(
                    scopeBuiltin.ExtensionContributedOperators.Where(
                        m => m.Operator == op && IsActiveExtensionOperator(m, state)),
                    op,
                    leftMembers,
                    rightMembers,
                    scopeBuiltin,
                    IsClassSubtypeOf);
                if (form is null)
                {
                    return false;
                }

                owningType = scopeBuiltin;
                return true;
            }

            return false;
        }

        private bool TrySelectUnaryFormOnOwningType(
            OverloadableOperator op,
            IBaseSymbol? operandSymbol,
            string operandName,
            CheckerState state,
            out ObjectOperatorOverloadMethodSymbol? form,
            out IBaseSymbol? owningType)
        {
            form = null;
            owningType = null;

            if (operandSymbol is ObjectDeclarationSymbol objectDecl && !objectDecl.IsStruct)
            {
                form = FindMatchingUnaryForm(objectDecl, op, operandSymbol, operandName, state);
                if (form is not null)
                {
                    owningType = objectDecl;
                    return true;
                }

                if (objectDecl.ObjectKind == PhpTypeDeclType.Trait
                    && TrySelectAgreedComposingUnaryForm(
                        objectDecl, op, operandSymbol, operandName, state,
                        out form, out owningType))
                {
                    return true;
                }

                return false;
            }

            if (operandSymbol is BuiltInTypeSymbol builtin)
            {
                var scopeBuiltin = TypeComparer.ResolveBuiltIn(builtin.Name, _globalScope) ?? builtin;
                form = OperatorOverloadResolver.SelectMatchingUnaryForm(
                    scopeBuiltin.ExtensionContributedOperators.Where(
                        m => m.Operator == op && IsActiveExtensionOperator(m, state)),
                    op,
                    operandSymbol,
                    operandName,
                    scopeBuiltin);
                if (form is null)
                {
                    return false;
                }

                owningType = scopeBuiltin;
                return true;
            }

            return false;
        }

        private ObjectOperatorOverloadMethodSymbol? FindMatchingBinaryForm(
            ObjectDeclarationSymbol typeSymbol,
            OverloadableOperator op,
            IReadOnlyList<OperatorOperandMember> leftMembers,
            IReadOnlyList<OperatorOperandMember> rightMembers,
            CheckerState state)
        {
            // Include native-passthrough tyhpdef forms for inference (emit leaves them as PHP ops,
            // but the declared return type is still the checker truth).
            var classMatch = OperatorOverloadResolver.SelectMatchingBinaryForm(
                TypeComparer.EnumerateClassOperatorOverloads(typeSymbol, _globalScope)
                    .Where(m => m.Operator == op),
                op,
                leftMembers,
                rightMembers,
                typeSymbol,
                IsClassSubtypeOf);
            if (classMatch != null)
            {
                return classMatch;
            }

            return OperatorOverloadResolver.SelectMatchingBinaryForm(
                typeSymbol.ExtensionContributedOperators.Where(
                    m => m.Operator == op && IsActiveExtensionOperator(m, state)),
                op,
                leftMembers,
                rightMembers,
                typeSymbol,
                IsClassSubtypeOf);
        }

        /// <summary>
        /// True when <paramref name="child"/> is, extends, or implements <paramref name="parent"/>
        /// (the operand-to-parameter relation used when matching operator overload forms).
        /// </summary>
        private bool IsClassSubtypeOf(ObjectDeclarationSymbol child, ObjectDeclarationSymbol parent)
            => TypeComparer.ImplementsOrExtends(
                child, parent, _symbolTree, _globalScope, new HashSet<ObjectDeclarationSymbol>());

        private ObjectOperatorOverloadMethodSymbol? FindMatchingUnaryForm(
            ObjectDeclarationSymbol typeSymbol,
            OverloadableOperator op,
            IBaseSymbol? operandSymbol,
            string operandName,
            CheckerState state)
        {
            var classMatch = OperatorOverloadResolver.SelectMatchingUnaryForm(
                TypeComparer.EnumerateClassOperatorOverloads(typeSymbol, _globalScope)
                    .Where(m => m.Operator == op),
                op,
                operandSymbol,
                operandName,
                typeSymbol);
            if (classMatch != null)
            {
                return classMatch;
            }

            return OperatorOverloadResolver.SelectMatchingUnaryForm(
                typeSymbol.ExtensionContributedOperators.Where(
                    m => m.Operator == op && IsActiveExtensionOperator(m, state)),
                op,
                operandSymbol,
                operandName,
                typeSymbol);
        }

        private bool IsActiveExtensionOperator(
            ObjectOperatorOverloadMethodSymbol method,
            CheckerState state) =>
            ExtensionActivation.IsOperatorVisible(method, state.CurrentFileName, _globalScope);

        /// <summary>
        /// When a trait has no matching binary form, probe classes/enums that <c>use</c> it.
        /// Accepts a hit only when every composing user with a matching form resolves to the same
        /// return type (after <c>self</c>/<c>static</c> expansion against that user). Conflict or
        /// no matches → false (caller falls back to native inference).
        /// </summary>
        private bool TrySelectAgreedComposingBinaryForm(
            ObjectDeclarationSymbol trait,
            OverloadableOperator op,
            IReadOnlyList<OperatorOperandMember> leftMembers,
            IReadOnlyList<OperatorOperandMember> rightMembers,
            CheckerState state,
            out ObjectOperatorOverloadMethodSymbol? form,
            out IBaseSymbol? owningType)
        {
            form = null;
            owningType = null;
            ICheckedType? agreedReturn = null;
            ObjectOperatorOverloadMethodSymbol? agreedForm = null;
            ObjectDeclarationSymbol? agreedOwner = null;
            var sawMatch = false;

            foreach (var composing in TypeComparer.EnumerateObjectsUsingTrait(
                         trait, _symbolTree, _globalScope))
            {
                var left = RemapTraitMembersToComposing(trait, composing, leftMembers);
                var right = RemapTraitMembersToComposing(trait, composing, rightMembers);

                var match = FindMatchingBinaryForm(composing, op, left, right, state);
                if (match is null)
                {
                    continue;
                }

                var ret = ResolveOperatorOverloadReturnType(match, composing, state);
                if (!sawMatch)
                {
                    sawMatch = true;
                    agreedReturn = ret;
                    agreedForm = match;
                    agreedOwner = composing;
                    continue;
                }

                if (!TypeComparer.AreTypesEqual(agreedReturn!, ret))
                {
                    return false;
                }
            }

            if (!sawMatch)
            {
                return false;
            }

            form = agreedForm;
            owningType = agreedOwner;
            return true;
        }

        private static IReadOnlyList<OperatorOperandMember> RemapTraitMembersToComposing(
            ObjectDeclarationSymbol trait,
            ObjectDeclarationSymbol composing,
            IReadOnlyList<OperatorOperandMember> members)
        {
            var remapped = new List<OperatorOperandMember>(members.Count);
            foreach (var member in members)
            {
                var symbol = member.Symbol;
                var name = member.Name;
                RemapTraitOperandToComposing(trait, composing, ref symbol, ref name);
                remapped.Add(new OperatorOperandMember(symbol, name));
            }

            return remapped;
        }

        /// <summary>
        /// Unary counterpart of <see cref="TrySelectAgreedComposingBinaryForm"/>.
        /// </summary>
        private bool TrySelectAgreedComposingUnaryForm(
            ObjectDeclarationSymbol trait,
            OverloadableOperator op,
            IBaseSymbol? operandSymbol,
            string operandName,
            CheckerState state,
            out ObjectOperatorOverloadMethodSymbol? form,
            out IBaseSymbol? owningType)
        {
            form = null;
            owningType = null;
            ICheckedType? agreedReturn = null;
            ObjectOperatorOverloadMethodSymbol? agreedForm = null;
            ObjectDeclarationSymbol? agreedOwner = null;
            var sawMatch = false;

            foreach (var composing in TypeComparer.EnumerateObjectsUsingTrait(
                         trait, _symbolTree, _globalScope))
            {
                var operand = operandSymbol;
                var name = operandName;
                RemapTraitOperandToComposing(trait, composing, ref operand, ref name);

                var match = FindMatchingUnaryForm(composing, op, operand, name, state);
                if (match is null)
                {
                    continue;
                }

                var ret = ResolveOperatorOverloadReturnType(match, composing, state);
                if (!sawMatch)
                {
                    sawMatch = true;
                    agreedReturn = ret;
                    agreedForm = match;
                    agreedOwner = composing;
                    continue;
                }

                if (!TypeComparer.AreTypesEqual(agreedReturn!, ret))
                {
                    return false;
                }
            }

            if (!sawMatch)
            {
                return false;
            }

            form = agreedForm;
            owningType = agreedOwner;
            return true;
        }

        /// <summary>
        /// Treats a trait-typed operand (typically <c>$this</c> / <c>self</c> inside a trait method)
        /// as the composing class while matching that class's <c>self</c> operator parameters —
        /// the checker-side analogue of temporarily pushing the user onto AliasConverter's
        /// <c>_classStack</c>.
        /// </summary>
        private static void RemapTraitOperandToComposing(
            ObjectDeclarationSymbol trait,
            ObjectDeclarationSymbol composing,
            ref IBaseSymbol? symbol,
            ref string name)
        {
            if (symbol is not ObjectDeclarationSymbol operandObject)
            {
                return;
            }

            if (!ReferenceEquals(operandObject, trait)
                && !string.Equals(
                    operandObject.FullyQualifiedName,
                    trait.FullyQualifiedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            symbol = composing;
            name = composing.Name;
        }

        private ICheckedType ResolveOperatorOverloadReturnType(
            ObjectOperatorOverloadMethodSymbol form,
            IBaseSymbol owningType,
            CheckerState state)
        {
            if (form.ReturnType is null)
            {
                // Untyped / omitted return — fall back to the owning object when it is a class.
                return owningType is ObjectDeclarationSymbol objectDecl
                    ? CheckedTypes.FromSymbol(objectDecl)
                    : CheckedTypes.Mixed;
            }

            var resolveState = state;
            if (owningType is ObjectDeclarationSymbol owningObject)
            {
                resolveState = state.Fork();
                resolveState.EnclosingObject = owningObject;
                resolveState.EnclosingObjectType = CheckedTypes.FromSymbol(owningObject);
                if (owningObject.GenericParameters.Count > 0)
                {
                    resolveState.ObjectGenerics = owningObject.GenericParameters;
                }
            }
            else if (owningType is BuiltInTypeSymbol builtinOwner && form.DeclaringExtensionSymbol is { } declaringExtension)
            {
                // Extension-contributed operator on a builtin (e.g. `extension StringOperators {
                // operator *<string>(self $left, int $right): self }`): `self`/`static` in the
                // return type must mean that builtin. ResolveRelativeType still requires a
                // non-null EnclosingObject to avoid CheckerRelativeTypeOutsideClass even though the
                // actual value it resolves comes from EnclosingObjectType — mirrors
                // ExtensionRule.CheckExtensionOperatorOverload, which seeds EnclosingObject with the
                // extension declaration symbol while EnclosingObjectType carries the real self-type.
                resolveState = state.Fork();
                resolveState.EnclosingObject = declaringExtension;
                if (declaringExtension.ContainingScope is { } declaringScope)
                {
                    resolveState.NameResolutionScope = declaringScope;
                }

                // Resolve the block's own declared target expression (alias-aware) instead of
                // wrapping the unwrapped lookup symbol, so a nullable target (`extends ?string`,
                // or an alias of it) keeps `self`/`static` nullable here too (same gap as
                // ResolveMethodReturnType's TryGetBlockTargetDeclaration path).
                var blockTargetAst = TryGetBlockTargetDeclaration(form)?.PendingExtensionBlockTarget;
                var blockTargetType = blockTargetAst is not null
                    ? ResolveTypeExpression(blockTargetAst, resolveState)
                    : null;
                resolveState.EnclosingObjectType =
                    blockTargetType is not null && !TypeComparer.IsUnresolvedType(blockTargetType)
                        ? blockTargetType
                        : CheckedTypes.FromSymbol(builtinOwner);
            }

            return ResolveTypeExpression(form.ReturnType, resolveState, isReturnTypePosition: true);
        }

        private static IBaseSymbol? SingleMemberSymbol(IReadOnlyList<OperatorOperandMember> members)
            => members.Count == 1 ? members[0].Symbol : null;

        /// <summary>
        /// Describes an operator operand as one member per union arm (a single member for any other
        /// type), so an overload parameter must accept every arm of a union operand.
        /// </summary>
        private IReadOnlyList<OperatorOperandMember> DescribeOperatorOperandMembers(ICheckedType type)
            => DescribeOperatorOperandMembers(type, _globalScope);

        /// <summary>
        /// Static form of <see cref="DescribeOperatorOperandMembers(ICheckedType)"/> shared with the
        /// emitter's operator selection.
        /// </summary>
        internal static IReadOnlyList<OperatorOperandMember> DescribeOperatorOperandMembers(
            ICheckedType type,
            GlobalScope globalScope)
        {
            var unwrapped = type is NullableCheckedType nullable ? nullable.InnerType : type;
            if (unwrapped is UnionCheckedType union && union.Members.Count > 0)
            {
                var members = new List<OperatorOperandMember>(union.Members.Count);
                foreach (var arm in union.Members)
                {
                    DescribeOperatorOperand(arm, globalScope, out var armSymbol, out var armName);
                    members.Add(new OperatorOperandMember(armSymbol, armName));
                }

                return members;
            }

            DescribeOperatorOperand(type, globalScope, out var symbol, out var name);
            return [new OperatorOperandMember(symbol, name)];
        }

        private static void DescribeOperatorOperand(
            ICheckedType type,
            GlobalScope globalScope,
            out IBaseSymbol? symbol,
            out string name)
        {
            symbol = null;
            name = "";

            var unwrapped = type is NullableCheckedType nullable
                ? nullable.InnerType
                : type;

            if (unwrapped is LiteralCheckedType literal)
            {
                unwrapped = literal.UnderlyingType;
            }

            if (TypeComparer.TryGetObjectDeclaration(unwrapped) is { } objectDecl)
            {
                symbol = objectDecl;
                name = objectDecl.Name;
                return;
            }

            if (TypeComparer.TryGetBuiltInName(unwrapped, out var builtinName))
            {
                symbol = TypeComparer.ResolveBuiltIn(builtinName, globalScope)
                    ?? new BuiltInTypeSymbol(builtinName);
                name = builtinName;
                return;
            }

            symbol = TypeComparer.TryGetNominalSymbol(unwrapped);
            name = unwrapped.DisplayName?.TrimStart('\\') ?? "";
            if (symbol is ObjectDeclarationSymbol namedObj)
            {
                name = namedObj.Name;
            }
            else if (symbol is BuiltInTypeSymbol namedBuiltin)
            {
                name = namedBuiltin.Name;
            }
        }

        /// <summary>
        /// Maps a binary PHP operator to the Story 11 overloadable operator used for form selection.
        /// Unary +/- use <see cref="OverloadableOperator.Plus"/>/<see cref="OverloadableOperator.Minus"/>;
        /// binary +/- use Add/Subtract.
        /// </summary>
        private static OverloadableOperator ToOverloadableBinaryOperator(PhpBinaryOperator op) =>
            op switch
            {
                PhpBinaryOperator.Plus => OverloadableOperator.Add,
                PhpBinaryOperator.Minus => OverloadableOperator.Subtract,
                PhpBinaryOperator.Multiply => OverloadableOperator.Multiply,
                PhpBinaryOperator.Divide => OverloadableOperator.Divide,
                PhpBinaryOperator.Modulo => OverloadableOperator.Mod,
                PhpBinaryOperator.Power => OverloadableOperator.Pow,
                PhpBinaryOperator.Concat => OverloadableOperator.Concat,
                PhpBinaryOperator.BitwiseAnd => OverloadableOperator.BitwiseAnd,
                PhpBinaryOperator.BitwiseOr => OverloadableOperator.BitwiseOr,
                PhpBinaryOperator.BitwiseXor => OverloadableOperator.BitwiseXor,
                PhpBinaryOperator.ShiftLeft => OverloadableOperator.BitwiseShiftLeft,
                PhpBinaryOperator.ShiftRight => OverloadableOperator.BitwiseShiftRight,
                PhpBinaryOperator.Equal => OverloadableOperator.CompareEqual,
                PhpBinaryOperator.NotEqual => OverloadableOperator.CompareNotEqual,
                PhpBinaryOperator.Identical => OverloadableOperator.CompareIdentical,
                PhpBinaryOperator.NotIdentical => OverloadableOperator.CompareNotIdentical,
                PhpBinaryOperator.LessThan => OverloadableOperator.CompareLessThan,
                PhpBinaryOperator.LessThanOrEqual => OverloadableOperator.CompareLessThanOrEqualTo,
                PhpBinaryOperator.GreaterThan => OverloadableOperator.CompareGreaterThan,
                PhpBinaryOperator.GreaterThanOrEqual => OverloadableOperator.CompareGreaterThanOrEqualTo,
                PhpBinaryOperator.Spaceship => OverloadableOperator.CompareSpaceship,
                _ => OverloadableOperator.Invalid,
            };

        private static OverloadableOperator ToOverloadableUnaryOperator(int token, string text) =>
            OverloadableOperatorHelper.FromToken(
                token,
                text,
                isAlternateKind: text is "+" or "-"
                    || token is TyhpParser.T_SYM_PLUS or TyhpParser.T_SYM_MINUS);

        private static OverloadableOperator ToOverloadableAssignmentOperator(PhpAssignmentOperator op) =>
            op switch
            {
                PhpAssignmentOperator.PlusAssign => OverloadableOperator.Add,
                PhpAssignmentOperator.MinusAssign => OverloadableOperator.Subtract,
                PhpAssignmentOperator.MultiplyAssign => OverloadableOperator.Multiply,
                PhpAssignmentOperator.DivideAssign => OverloadableOperator.Divide,
                PhpAssignmentOperator.ModuloAssign => OverloadableOperator.Mod,
                PhpAssignmentOperator.PowerAssign => OverloadableOperator.Pow,
                PhpAssignmentOperator.ConcatAssign => OverloadableOperator.Concat,
                PhpAssignmentOperator.BitwiseAndAssign => OverloadableOperator.BitwiseAnd,
                PhpAssignmentOperator.BitwiseOrAssign => OverloadableOperator.BitwiseOr,
                PhpAssignmentOperator.BitwiseXorAssign => OverloadableOperator.BitwiseXor,
                PhpAssignmentOperator.ShiftLeftAssign => OverloadableOperator.BitwiseShiftLeft,
                PhpAssignmentOperator.ShiftRightAssign => OverloadableOperator.BitwiseShiftRight,
                _ => OverloadableOperator.Invalid,
            };
    }
}
