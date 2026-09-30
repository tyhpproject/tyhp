using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter.Splice
{
    /// <summary>A parameter of the member being spliced, with call-site substitution info.</summary>
    public readonly record struct SpliceParameter(
        string Name,
        bool IsByReference,
        IExpression? DefaultValue,
        bool IsThis);

    /// <summary>Why a call site cannot be spliced faithfully (rule 3).</summary>
    public enum SpliceDeclineReason
    {
        None = 0,
        UnusedParameter,
        ShortCircuitLazyArgument,
        ArgumentOrderMismatch,
        RefHoistNeedsStatementSlot,
        NotSingleReturn,
        Cycle,
    }

    /// <summary>Input to <see cref="CallSiteSpliceEngine.TrySplice"/>.</summary>
    public sealed class SpliceRequest
    {
        public required IBaseSymbol Callee { get; init; }
        public required IBase2Ast CallSite { get; init; }
        public required IExpression Body { get; init; }
        public required IReadOnlyList<SpliceParameter> Parameters { get; init; }

        /// <summary>Instance / extension receiver. Substituted for <c>$this</c>.</summary>
        public IExpression? Receiver { get; init; }

        /// <summary>
        /// Call-site arguments for non-<c>$this</c> parameters, in declare order (named args
        /// already resolved by the caller). A null slot means "use the parameter default".
        /// </summary>
        public IReadOnlyList<IExpression?> Arguments { get; init; } = [];

        /// <summary>
        /// True when the member also emits a PHP method the call can fall back to. False for an
        /// erased member (tyhpdef thin mapping, or Tyhp <c>=&gt;</c> once Phase 5 omits the backer).
        /// </summary>
        public bool HasPhpBacker { get; init; }

        /// <summary>
        /// True when a ref-bound hoist can be inserted as a statement immediately before the
        /// enclosing statement of this call.
        /// </summary>
        public bool HasStatementSlot { get; init; }

        public string? DeclaringClassFqn { get; init; }
        public string? ParentClassFqn { get; init; }
        public MemberModifier CalleeVisibility { get; init; }
        public ObjectDeclarationSymbol? DeclaringClass { get; init; }
    }

    /// <summary>Result of a splice attempt.</summary>
    public sealed class SpliceResult
    {
        public bool Success { get; private init; }
        public IExpression? Expression { get; private init; }
        public IReadOnlyList<IExpression> HoistStatements { get; private init; } = [];
        public SpliceDeclineReason DeclineReason { get; private init; }
        public bool HasPhpBacker { get; private init; }

        public static SpliceResult Spliced(
            IExpression expression,
            IReadOnlyList<IExpression>? hoistStatements = null) =>
            new()
            {
                Success = true,
                Expression = expression,
                HoistStatements = hoistStatements ?? [],
            };

        public static SpliceResult Decline(SpliceDeclineReason reason, bool hasPhpBacker) =>
            new()
            {
                Success = false,
                DeclineReason = reason,
                HasPhpBacker = hasPhpBacker,
            };
    }

    /// <summary>Shared lookup / naming state for one emit or check session.</summary>
    public sealed class SpliceEngineContext
    {
        public required Func<PhpNameAst, IReadOnlyList<ParameterInfo>?> ResolveFreeFunction { get; init; }

        public Func<IBase2Ast, IReadOnlyList<ParameterInfo>?>? ResolveCallParameters { get; init; }

        /// <summary>Bare variable names (no <c>$</c>) already in the call-site function scope.</summary>
        public HashSet<string> OccupiedVariableNames { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public int NextTempIndex { get; set; } = 1;

        /// <summary>Callee symbols currently on the splice reduction stack (cycle detection).</summary>
        public HashSet<IBaseSymbol> ReductionStack { get; } =
            new(ReferenceEqualityComparer.Instance);

        public string AllocateTempName()
        {
            while (true)
            {
                var bare = GeneratedNames.InlineTempVariablePrefix + NextTempIndex.ToString();
                NextTempIndex++;
                if (OccupiedVariableNames.Add(bare))
                {
                    return bare;
                }
            }
        }
    }
}
