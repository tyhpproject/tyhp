using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
        /// Infers a concrete list/map type for unannotated / open <c>array</c> locals after
        /// <c>$arr[] = $v</c> or <c>$arr[$k] = $v</c> with a known <c>int</c>/<c>string</c> key.
        /// Bare <c>array $digits = []</c> otherwise stays <c>array&lt;TKey, TValue&gt;</c>
        /// (unresolved) and is not assignable to <c>implode</c>'s
        /// <c>array&lt;int|string, mixed&gt;</c> or to generic <c>array_reverse</c>.
        /// </summary>
    internal static class ArrayAppendInference
    {
        /// <summary>
        /// When <paramref name="left"/> is <c>$name[]</c> (append) or <c>$name[$k]</c> (keyed
        /// write) on an open array local, narrows (and, for inferred locals, locks) the local to
        /// <c>array&lt;K, …&gt;</c> from the write's key/value. Append always contributes an
        /// <c>int</c> key (PHP's implicit next index). A keyed write only contributes when
        /// <paramref name="resolveIndexType"/> resolves the index to a plain <c>int</c> or
        /// <c>string</c> — <c>$arr[$k] = $v</c> can legitimately introduce either key kind, but an
        /// unresolved/mixed index is not guessed at. Returns <see langword="true"/> when the write
        /// grew an open array so the caller should skip checking the value against the old element
        /// type (<c>never</c> / unresolved <c>TValue</c>).
        /// </summary>
        public static bool TryRefineLocal(
            IExpression? left,
            ICheckedType appendedValue,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IExpression, ICheckedType>? resolveIndexType = null)
        {
            if (left is not PhpDereferenceableAst
                {
                    Base: PhpVariableAst variable,
                    Suffix: PhpArrayAccessAst arrayAccess,
                })
            {
                return false;
            }

            if (!TryGetWriteKeyType(arrayAccess.IndexExpression, resolveIndexType, out var keyType))
            {
                return false;
            }

            var name = CheckerHelpers.GetVariableName(variable);
            if (name is null || state.LookupVariable(name) is not { } local)
            {
                return false;
            }

            if (!IsOpenArrayLocal(local))
            {
                return false;
            }

            var next = NextListType(
                local.EffectiveType,
                keyType,
                WidenLiteral(appendedValue),
                symbolTree,
                globalScope);
            if (next is null)
            {
                return false;
            }

            state.RefineArrayLocal(name, next);
            return true;
        }

        /// <summary>
        /// <c>$arr[] = …</c> (<paramref name="indexExpression"/> is <see langword="null"/>) always
        /// contributes PHP's implicit <c>int</c> key. <c>$arr[$k] = …</c> only contributes when the
        /// key resolves to a plain <c>int</c> or <c>string</c> — that is the same "grow the open
        /// array" operation as append for type purposes, just with the write's own key instead of
        /// an implicit one. A <c>mixed</c>/unresolved/other-typed index is not coerced into a guess.
        /// </summary>
        private static bool TryGetWriteKeyType(
            IExpression? indexExpression,
            Func<IExpression, ICheckedType>? resolveIndexType,
            out ICheckedType keyType)
        {
            if (indexExpression is null)
            {
                keyType = CheckedTypes.Int;
                return true;
            }

            if (resolveIndexType is null)
            {
                keyType = CheckedTypes.Unresolved;
                return false;
            }

            var resolved = WidenLiteral(resolveIndexType(indexExpression));
            if (TypeComparer.IsBuiltInName(resolved, "int") || TypeComparer.IsBuiltInName(resolved, "string"))
            {
                keyType = resolved;
                return true;
            }

            keyType = CheckedTypes.Unresolved;
            return false;
        }

        private static bool IsOpenArrayLocal(VariableState local)
        {
            var declared = local.DeclaredType;
            if (declared is null)
            {
                return local.IsInferred;
            }

            return IsUntypedArray(declared) || IsPlaceholderArray(declared);
        }

        private static ICheckedType? NextListType(
            ICheckedType current,
            ICheckedType writeKeyType,
            ICheckedType appendedValue,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (IsUntypedArray(current) || !TryGetArrayKeyValue(current, out var keyType, out var valueType))
            {
                return MakeArrayType(writeKeyType, appendedValue);
            }

            var nextKey = IsPlaceholderType(keyType)
                ? writeKeyType
                : TypeComparer.UnionTypes(keyType, writeKeyType, symbolTree, globalScope);
            var nextValue = IsPlaceholderType(valueType)
                ? appendedValue
                : TypeComparer.UnionTypes(valueType, appendedValue, symbolTree, globalScope);
            return MakeArrayType(nextKey, nextValue);
        }

        private static bool TryGetArrayKeyValue(
            ICheckedType type,
            out ICheckedType keyType,
            out ICheckedType valueType)
        {
            if (type is GenericCheckedType generic
                && generic.TypeArguments.Count >= 1
                && IsArrayBase(generic.BaseType))
            {
                if (generic.TypeArguments.Count >= 2)
                {
                    keyType = generic.TypeArguments[0];
                    valueType = generic.TypeArguments[^1];
                }
                else
                {
                    keyType = CheckedTypes.UnionTypes(CheckedTypes.Int, CheckedTypes.String);
                    valueType = generic.TypeArguments[0];
                }

                return true;
            }

            keyType = CheckedTypes.Unresolved;
            valueType = CheckedTypes.Unresolved;
            return false;
        }

        private static bool IsPlaceholderArray(ICheckedType type) =>
            TryGetArrayKeyValue(type, out var keyType, out var valueType)
            && (IsPlaceholderType(keyType) || IsPlaceholderType(valueType));

        private static bool IsPlaceholderType(ICheckedType type) =>
            TypeComparer.IsNeverType(type)
            || TypeComparer.IsUnresolvedType(type)
            || type is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol };

        private static bool IsUntypedArray(ICheckedType type) =>
            IsArrayBase(type is GenericCheckedType generic ? generic.BaseType : type)
            && type is not GenericCheckedType { TypeArguments.Count: > 0 };

        private static bool IsArrayBase(ICheckedType type)
        {
            var name = type.DisplayName;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var lastSegment = name.TrimStart('?').TrimStart('\\');
            return string.Equals(lastSegment, "array", StringComparison.OrdinalIgnoreCase);
        }

        private static ICheckedType WidenLiteral(ICheckedType type) =>
            type is LiteralCheckedType literal ? literal.UnderlyingType : type;

        private static ICheckedType MakeArrayType(ICheckedType keyType, ICheckedType valueType) =>
            new GenericCheckedType(
                CheckedTypes.FromSymbol(new BuiltInTypeSymbol("array")),
                [keyType, valueType]);
    }
}
