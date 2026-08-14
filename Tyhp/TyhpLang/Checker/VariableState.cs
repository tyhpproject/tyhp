using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Per-variable tracking for definite assignment, nullability, and type narrowing.
    /// </summary>
    public class VariableState
    {
        public VariableSymbol? Symbol { get; init; }

        public ICheckedType? DeclaredType { get; set; }

        /// <summary>Current narrowed type; null means same as <see cref="DeclaredType"/>.</summary>
        public ICheckedType? NarrowedType { get; set; }

        public ICheckedType EffectiveType => NarrowedType ?? DeclaredType ?? CheckedTypes.Unresolved;

        public bool IsDefinitelyAssigned { get; set; }

        public bool IsPossiblyNull { get; set; }

        public bool IsPossiblyUndefined { get; set; }

        public bool IsDisposable { get; set; }

        public bool IsParameter { get; set; }

        public bool IsReference { get; set; }

        /// <summary>True when the type was inferred from context rather than explicitly annotated.</summary>
        public bool IsInferred { get; set; }

        /// <summary>True after the variable is read in an expression context.</summary>
        public bool IsRead { get; set; }

        public ReferenceGroup? ReferenceGroup { get; set; }

        /// <summary>
        /// The block-level <see cref="CheckerState"/> a typed-local declaration was written from.
        /// Used to tell a genuine duplicate/shadow (declared again in the same or an enclosing
        /// block) apart from a harmless re-declaration in a sibling block that has already been
        /// exited — e.g. <c>int $id</c> inside two consecutive <c>foreach</c> bodies. Variables
        /// are function-scoped in PHP, so the latter is allowed.
        /// </summary>
        public CheckerState? DeclaringBlockScope { get; set; }

        public static VariableState ForParameter(VariableSymbol param, ICheckedType type, bool isReference)
        {
            var state = new VariableState
            {
                Symbol = param,
                DeclaredType = type,
                IsDefinitelyAssigned = true,
                IsParameter = true,
                IsReference = isReference,
                IsPossiblyNull = type.IsNullable,
            };

            if (isReference)
            {
                state.ReferenceGroup = new ReferenceGroup();
                state.ReferenceGroup.AddMember(param.Name);
            }

            return state;
        }

        public static VariableState ForDeclaration(VariableSymbol symbol, ICheckedType? type, bool isAssigned) =>
            new()
            {
                Symbol = symbol,
                DeclaredType = type,
                IsDefinitelyAssigned = isAssigned,
                IsPossiblyNull = type?.IsNullable ?? false,
                IsPossiblyUndefined = !isAssigned,
                IsDisposable = symbol.IsDisposable,
            };

        public VariableState Clone() =>
            new()
            {
                Symbol = Symbol,
                DeclaredType = DeclaredType,
                NarrowedType = NarrowedType,
                IsDefinitelyAssigned = IsDefinitelyAssigned,
                IsPossiblyNull = IsPossiblyNull,
                IsPossiblyUndefined = IsPossiblyUndefined,
                IsDisposable = IsDisposable,
                IsParameter = IsParameter,
                IsReference = IsReference,
                IsInferred = IsInferred,
                IsRead = IsRead,
                ReferenceGroup = ReferenceGroup,
                DeclaringBlockScope = DeclaringBlockScope,
            };

        public void JoinReferenceGroup(VariableState other, string thisName, string otherName)
        {
            IsReference = true;
            other.IsReference = true;

            if (ReferenceGroup == null && other.ReferenceGroup == null)
            {
                ReferenceGroup = new ReferenceGroup();
                ReferenceGroup.AddMember(thisName);
                ReferenceGroup.AddMember(otherName);
                other.ReferenceGroup = ReferenceGroup;
                return;
            }

            var group = ReferenceGroup ?? other.ReferenceGroup ?? new ReferenceGroup();
            group.AddMember(thisName);
            group.AddMember(otherName);
            ReferenceGroup = group;
            other.ReferenceGroup = group;
        }
    }
}
