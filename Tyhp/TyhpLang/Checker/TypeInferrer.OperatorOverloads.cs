using System;
using System.Linq;
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

            DescribeOperatorOperand(left, out var leftSymbol, out var leftName);
            DescribeOperatorOperand(right, out var rightSymbol, out var rightName);

            // Left-first, then right — mirrors AliasConverter.SelectStaticBinaryOperatorTarget.
            if (TrySelectBinaryFormOnOwningType(
                    op,
                    leftSymbol,
                    leftSymbol,
                    leftName,
                    rightSymbol,
                    rightName,
                    state,
                    out var form,
                    out var owning))
            {
                returnType = ResolveOperatorOverloadReturnType(form!, owning!, state);
                return true;
            }

            if (TrySelectBinaryFormOnOwningType(
                    op,
                    rightSymbol,
                    leftSymbol,
                    leftName,
                    rightSymbol,
                    rightName,
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

            DescribeOperatorOperand(operand, out var operandSymbol, out var operandName);
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
            IBaseSymbol? leftSymbol,
            string leftName,
            IBaseSymbol? rightSymbol,
            string rightName,
            CheckerState state,
            out ObjectOperatorOverloadMethodSymbol? form,
            out IBaseSymbol? owningType)
        {
            form = null;
            owningType = null;

            if (owningOperandSymbol is ObjectDeclarationSymbol objectDecl && !objectDecl.IsStruct)
            {
                form = FindMatchingBinaryForm(objectDecl, op, leftSymbol, leftName, rightSymbol, rightName);
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
                        objectDecl, op, leftSymbol, leftName, rightSymbol, rightName, state,
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
                    scopeBuiltin.ExtensionContributedOperators.Where(m => m.Operator == op),
                    op,
                    leftSymbol,
                    leftName,
                    rightSymbol,
                    rightName,
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
                form = FindMatchingUnaryForm(objectDecl, op, operandSymbol, operandName);
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
                    scopeBuiltin.ExtensionContributedOperators.Where(m => m.Operator == op),
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
            IBaseSymbol? leftSymbol,
            string leftName,
            IBaseSymbol? rightSymbol,
            string rightName)
        {
            // Include native-passthrough tyhpdef forms for inference (emit leaves them as PHP ops,
            // but the declared return type is still the checker truth).
            var classMatch = OperatorOverloadResolver.SelectMatchingBinaryForm(
                TypeComparer.EnumerateClassOperatorOverloads(typeSymbol, _globalScope)
                    .Where(m => m.Operator == op),
                op,
                leftSymbol,
                leftName,
                rightSymbol,
                rightName,
                typeSymbol);
            if (classMatch != null)
            {
                return classMatch;
            }

            return OperatorOverloadResolver.SelectMatchingBinaryForm(
                typeSymbol.ExtensionContributedOperators.Where(m => m.Operator == op),
                op,
                leftSymbol,
                leftName,
                rightSymbol,
                rightName,
                typeSymbol);
        }

        private ObjectOperatorOverloadMethodSymbol? FindMatchingUnaryForm(
            ObjectDeclarationSymbol typeSymbol,
            OverloadableOperator op,
            IBaseSymbol? operandSymbol,
            string operandName)
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
                typeSymbol.ExtensionContributedOperators.Where(m => m.Operator == op),
                op,
                operandSymbol,
                operandName,
                typeSymbol);
        }

        /// <summary>
        /// When a trait has no matching binary form, probe classes/enums that <c>use</c> it.
        /// Accepts a hit only when every composing user with a matching form resolves to the same
        /// return type (after <c>self</c>/<c>static</c> expansion against that user). Conflict or
        /// no matches → false (caller falls back to native inference).
        /// </summary>
        private bool TrySelectAgreedComposingBinaryForm(
            ObjectDeclarationSymbol trait,
            OverloadableOperator op,
            IBaseSymbol? leftSymbol,
            string leftName,
            IBaseSymbol? rightSymbol,
            string rightName,
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
                var left = leftSymbol;
                var leftN = leftName;
                var right = rightSymbol;
                var rightN = rightName;
                RemapTraitOperandToComposing(trait, composing, ref left, ref leftN);
                RemapTraitOperandToComposing(trait, composing, ref right, ref rightN);

                var match = FindMatchingBinaryForm(composing, op, left, leftN, right, rightN);
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

                var match = FindMatchingUnaryForm(composing, op, operand, name);
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
                resolveState.EnclosingObjectType = CheckedTypes.FromSymbol(builtinOwner);
            }

            return ResolveTypeExpression(form.ReturnType, resolveState, isReturnTypePosition: true);
        }

        private void DescribeOperatorOperand(
            ICheckedType type,
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
                symbol = TypeComparer.ResolveBuiltIn(builtinName, _globalScope)
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

        private static OverloadableOperator ToOverloadableUnaryOperator(int token) =>
            OverloadableOperatorHelper.FromToken(
                token,
                text: "",
                isAlternateKind: token is TyhpParser.T_SYM_PLUS or TyhpParser.T_SYM_MINUS);

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
