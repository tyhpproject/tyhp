using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Scope-local state tracked while walking the AST during type checking.
    /// </summary>
    public class CheckerState
    {
        private bool _locked;

        public CheckerState()
        {
            Modifiers = MemberModifier.None;
            ObjectGenerics = [];
            FunctionGenerics = [];
            Variables = new Dictionary<string, VariableState>(StringComparer.Ordinal);
            PropertyInit = new Dictionary<string, PropertyInitializationState>(StringComparer.Ordinal);
            IndexAccessNarrowing = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
            MemberAccessNarrowing = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
            ScopeType = ScopeType.Root;
        }

        private CheckerState(CheckerState source, bool asSnapshot)
        {
            Parent = asSnapshot ? source.Parent : source;
            Modifiers = source.Modifiers;
            ObjectGenerics = source.ObjectGenerics;
            FunctionGenerics = source.FunctionGenerics;
            EnclosingObject = source.EnclosingObject;
            EnclosingFunction = source.EnclosingFunction;
            EnclosingCallable = source.EnclosingCallable;
            ExpectedReturnType = source.ExpectedReturnType;
            IsTypeGuardFunction = source.IsTypeGuardFunction;
            ScopeType = source.ScopeType;
            IsInAsyncContext = source.IsInAsyncContext;
            IsInGeneratorContext = source.IsInGeneratorContext;
            IsInLoopContext = source.IsInLoopContext;
            LoopDepth = source.LoopDepth;
            IsInSwitchContext = source.IsInSwitchContext;
            HasReturnedOnAllPaths = source.HasReturnedOnAllPaths;
            EnclosingObjectType = source.EnclosingObjectType;
            IsInsideFinally = source.IsInsideFinally;
            IsInsideClosure = source.IsInsideClosure;
            CurrentFileName = source.CurrentFileName;
            CurrentNamespaceName = source.CurrentNamespaceName;
            NameResolutionScope = source.NameResolutionScope;
            IsParameterTypePosition = source.IsParameterTypePosition;
            IsPropertyTypePosition = source.IsPropertyTypePosition;
            IsGenericConstraintPosition = source.IsGenericConstraintPosition;
            IsGenericTypeArgumentPosition = source.IsGenericTypeArgumentPosition;
            ExpectedClosureType = source.ExpectedClosureType;
            IsExistenceProbeContext = source.IsExistenceProbeContext;
            Variables = source.Variables.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Clone(),
                StringComparer.Ordinal);
            PropertyInit = source.PropertyInit.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Clone(),
                StringComparer.Ordinal);
            IndexAccessNarrowing = new Dictionary<string, ICheckedType>(
                source.IndexAccessNarrowing, StringComparer.Ordinal);
            MemberAccessNarrowing = new Dictionary<string, ICheckedType>(
                source.MemberAccessNarrowing, StringComparer.Ordinal);
        }

        public CheckerState? Parent { get; private set; }

        public MemberModifier Modifiers { get; set; } = MemberModifier.None;

        public IReadOnlyList<GenericTypeParameterSymbol> ObjectGenerics { get; set; } = [];

        public IReadOnlyList<GenericTypeParameterSymbol> FunctionGenerics { get; set; } = [];

        public ObjectDeclarationSymbol? EnclosingObject { get; set; }

        public FunctionDeclarationSymbol? EnclosingFunction { get; set; }

        /// <summary>
        /// Innermost function or method declaration whose generic parameters are in
        /// <see cref="FunctionGenerics"/> — a <see cref="FunctionDeclarationSymbol"/> for a free
        /// function, an <see cref="ObjectMethodSymbol"/> for a method. Unlike
        /// <see cref="EnclosingFunction"/> this covers methods too, so a rule that finds a
        /// construct needing runtime generic information can attribute it to the callable that
        /// must carry the type arguments (Mechanism D binder, FOUND_BUGS Mechanism D lineage).
        /// </summary>
        public IBaseSymbol? EnclosingCallable { get; set; }

        public ICheckedType? ExpectedReturnType { get; set; }

        /// <summary>True while checking a callable whose return type is a <c>$param is Type</c> guard.</summary>
        public bool IsTypeGuardFunction { get; set; }

        public ScopeType ScopeType { get; set; } = ScopeType.Root;

        public Dictionary<string, VariableState> Variables { get; set; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Per-property initialization state for the enclosing object's instance properties
        /// (Prop-init #7). Keys use the binder property member name including the leading <c>$</c>.
        /// </summary>
        public Dictionary<string, PropertyInitializationState> PropertyInit { get; set; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Control-flow narrowing for constant-index array access expressions
        /// (e.g. <c>$callable[1]</c> after <c>\is_string($callable[1])</c>).
        /// Keys are structural: <c>$name[literal]</c> (int literals unquoted; string literals
        /// single-quoted). Only constant indices are tracked — dynamic indices are ignored.
        /// </summary>
        public Dictionary<string, ICheckedType> IndexAccessNarrowing { get; set; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Control-flow narrowing for instance member access on an arbitrary variable
        /// (e.g. <c>$node->ifTrue</c> after <c>$node->ifTrue !== null</c>).
        /// Keys are structural: <c>$name->prop</c>. <c>$this->prop</c> stays on
        /// <see cref="PropertyInit"/>; dynamic member names are ignored.
        /// </summary>
        public Dictionary<string, ICheckedType> MemberAccessNarrowing { get; set; } =
            new(StringComparer.Ordinal);

        /// <summary>
        /// True while walking the left operand of <c>??</c>/<c>??=</c> or an <c>isset</c>/<c>empty</c>
        /// operand — those probes do not throw on uninitialized typed properties (or undefined
        /// variables), so definite-assignment / property-init diagnostics are suppressed.
        /// </summary>
        public bool IsExistenceProbeContext { get; set; }

        public bool IsInAsyncContext { get; set; }

        /// <summary>
        /// True when <see cref="await"/> is allowed at file/namespace scope so the emitter can wrap
        /// the entry point in <c>Promise::run()</c> (Story 11).
        /// </summary>
        public bool IsTopLevelAwaitableScope() =>
            ScopeType is ScopeType.File
                or ScopeType.Namespace
                or ScopeType.NamespaceBlock
                or ScopeType.Root
                or ScopeType.DeclareBlock;

        public bool IsInGeneratorContext { get; set; }

        public bool IsInLoopContext { get; set; }

        public int LoopDepth { get; set; }

        public bool IsInSwitchContext { get; set; }

        /// <summary>
        /// True when every path through the current block exits abruptly — via <c>return</c>,
        /// <c>throw</c>, <c>break</c>, or <c>continue</c>. Used by <c>CheckIf</c> for early-exit
        /// guard narrowing (absorb only the negative arm) and by callable checks for missing returns.
        /// </summary>
        public bool HasReturnedOnAllPaths { get; set; }

        public ICheckedType? EnclosingObjectType { get; set; }

        public bool IsInsideFinally { get; set; }

        /// <summary>
        /// True once traversal has entered a closure/arrow function body. <see cref="EnclosingCallable"/>
        /// deliberately still leaks through closures (needed so a named function declared inside one is
        /// still caught as a nested-function violation — see <c>CheckerNestedNamedFunctionNotAllowed</c>),
        /// but a closure's own <c>return</c> belongs to the closure, not to whatever named
        /// function/method it is lexically inside. Checks that attribute a return to the *specific*
        /// enclosing callable (e.g. the <c>__construct</c>/<c>__destruct</c> void-return rule) must
        /// consult this flag rather than <see cref="EnclosingCallable"/> alone.
        /// </summary>
        public bool IsInsideClosure { get; set; }

            public string? CurrentFileName { get; set; }

            /// <summary>
            /// Active namespace name for statement-style <c>namespace Foo;</c> and block-namespace
            /// scopes (no leading <c>\</c>). Empty/null means the global namespace.
            /// </summary>
            public string? CurrentNamespaceName { get; set; }

            /// <summary>
            /// When set, <see cref="TypeInferrer.GetResolutionScope"/> returns this scope instead of
            /// deriving one from the access-site enclosing function/object. Used so type annotations
            /// written in another file resolve against that file's namespace and <c>use</c> imports.
            /// </summary>
            public Binder.Scopes.Interfaces.IBaseScope? NameResolutionScope { get; set; }

        /// <summary>True while checking a type annotation on a parameter declaration.</summary>
        public bool IsParameterTypePosition { get; set; }

        /// <summary>True while checking a type annotation on a property declaration.</summary>
        public bool IsPropertyTypePosition { get; set; }

        /// <summary>
        /// True while checking a generic type-parameter <c>extends</c> constraint (e.g.
        /// <c>TReturn extends void|mixed</c>). Constraint unions may intentionally include
        /// <c>mixed</c>/<c>never</c>; <c>CheckerMixedInComposite</c> is suppressed here.
        /// </summary>
        public bool IsGenericConstraintPosition { get; set; }

        /// <summary>
        /// True while resolving a type argument inside a generic instantiation (e.g.
        /// <c>TResult</c> in <c>callable&lt;?TResult, int&gt;</c> or <c>array&lt;Unknown&gt;</c>).
        /// Unresolved named types in this position are diagnosed; top-level parameter/return
        /// names stay binder-owned (TYHP3019/3020) to avoid duplicate diagnostics.
        /// </summary>
        public bool IsGenericTypeArgumentPosition { get; set; }

        /// <summary>Expected callable type for contextual closure parameter inference.</summary>
        public ICheckedType? ExpectedClosureType { get; set; }

        public bool IsLocked => _locked;

        /// <summary>
        /// Creates an immutable deep copy of the current state for branching control flow.
        /// </summary>
        public CheckerState SnapShot()
        {
            ThrowIfLocked();
            var snapshot = Fork();
            snapshot._locked = true;
            return snapshot;
        }

        /// <summary>
        /// Deep copy that stays mutable, for resolving a declaration written in a different generic
        /// or name-resolution scope than the current one. <see cref="SnapShot"/> cannot be used for
        /// that: its result is locked, and resolution may itself snapshot the state it is handed
        /// (e.g. to rebind a cross-file annotation), which throws on a locked state.
        /// </summary>
        public CheckerState Fork()
        {
            var fork = new CheckerState(this, asSnapshot: true);
            // Include hoisted function-scope locals visible via parent walk, not only this
            // dictionary — otherwise if/loop merges treat pre-declared locals as branch-only.
            fork.Variables = CloneVisibleVariables();
            fork.PropertyInit = ClonePropertyInit();
            fork.IndexAccessNarrowing = CloneIndexAccessNarrowing();
            fork.MemberAccessNarrowing = CloneMemberAccessNarrowing();
            return fork;
        }

        /// <summary>
        /// Creates a child state for entering a new scope.
        /// </summary>
        public CheckerState Split(ScopeType scopeType) => CreateChildState(scopeType);

        private CheckerState CreateChildState(ScopeType scopeType)
        {
            var child = new CheckerState(this, asSnapshot: false)
            {
                ScopeType = scopeType,
            };

            switch (scopeType)
            {
                case ScopeType.Root:
                    child.Modifiers = MemberModifier.None;
                    child.ObjectGenerics = [];
                    child.FunctionGenerics = [];
                    child.EnclosingObject = null;
                    child.EnclosingFunction = null;
                    child.EnclosingCallable = null;
                    child.ExpectedReturnType = null;
                    child.EnclosingObjectType = null;
                    child.Variables = new Dictionary<string, VariableState>(StringComparer.Ordinal);
                    child.PropertyInit = new Dictionary<string, PropertyInitializationState>(StringComparer.Ordinal);
                    child.IndexAccessNarrowing = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                    child.MemberAccessNarrowing = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                    break;

                case ScopeType.File:
                case ScopeType.Namespace:
                case ScopeType.NamespaceBlock:
                    child.FunctionGenerics = [];
                    child.EnclosingFunction = null;
                    child.EnclosingCallable = null;
                    child.ExpectedReturnType = null;
                    child.Variables = new Dictionary<string, VariableState>(StringComparer.Ordinal);
                    child.PropertyInit = new Dictionary<string, PropertyInitializationState>(StringComparer.Ordinal);
                    child.IndexAccessNarrowing = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                    child.MemberAccessNarrowing = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                    break;

                case ScopeType.ObjectTypeDeclaration:
                case ScopeType.AnonymousObjectDeclaration:
                    child.FunctionGenerics = [];
                    child.EnclosingFunction = null;
                    child.EnclosingCallable = null;
                    child.ExpectedReturnType = null;
                    child.Variables = new Dictionary<string, VariableState>(StringComparer.Ordinal);
                    child.PropertyInit = new Dictionary<string, PropertyInitializationState>(StringComparer.Ordinal);
                    child.IndexAccessNarrowing = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                    child.MemberAccessNarrowing = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                    break;

                case ScopeType.FunctionDeclaration:
                case ScopeType.InstanceMethodDeclaration:
                case ScopeType.StaticMethodDeclaration:
                case ScopeType.AnonymousFunctionDeclaration:
                    child.Variables = new Dictionary<string, VariableState>(StringComparer.Ordinal);
                    // Fresh map — CheckMethod seeds from declaration / post-construction guarantees.
                    child.PropertyInit = new Dictionary<string, PropertyInitializationState>(StringComparer.Ordinal);
                    child.IndexAccessNarrowing = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                    child.MemberAccessNarrowing = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                    break;

                case ScopeType.CodeBlock:
                case ScopeType.Statement:
                case ScopeType.DeclareBlock:
                case ScopeType.Label:
                    child.Variables = CloneVisibleVariables();
                    child.PropertyInit = ClonePropertyInit();
                    child.IndexAccessNarrowing = CloneIndexAccessNarrowing();
                    child.MemberAccessNarrowing = CloneMemberAccessNarrowing();
                    break;

                default:
                    child.Variables = CloneVisibleVariables();
                    child.PropertyInit = ClonePropertyInit();
                    child.IndexAccessNarrowing = CloneIndexAccessNarrowing();
                    child.MemberAccessNarrowing = CloneMemberAccessNarrowing();
                    break;
            }

            return child;
        }

        /// <summary>
        /// Deep-copies every variable visible from this scope through enclosing non-function
        /// parents (and the function scope itself). Typed locals are hoisted to the function
        /// dictionary, so a bare copy of <see cref="Variables"/> alone misses them.
        /// </summary>
        private Dictionary<string, VariableState> CloneVisibleVariables()
        {
            var result = new Dictionary<string, VariableState>(StringComparer.Ordinal);
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                foreach (var (name, variable) in scope.Variables)
                {
                    result.TryAdd(name, variable.Clone());
                }

                if (IsFunctionBoundary(scope.ScopeType))
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Merges a divergent branch state back into this state after control-flow reunification.
        /// Treats <paramref name="branchState"/> as one path and <see cref="this"/> as the other
        /// (e.g. if-without-else: then vs fall-through). For if/else and ternary, join the two
        /// branch exits with <see cref="Merge"/> then <see cref="AbsorbJoinedVariables"/> so the
        /// pre-branch state is not counted as a third path.
        /// </summary>
        public void Merge(CheckerState branchState)
        {
            ThrowIfLocked();

            var allNames = new HashSet<string>(Variables.Keys, StringComparer.Ordinal);
            allNames.UnionWith(branchState.Variables.Keys);

            foreach (var name in allNames)
            {
                var inThis = Variables.TryGetValue(name, out var thisVar);
                var inBranch = branchState.Variables.TryGetValue(name, out var branchVar);

                if (inThis && inBranch)
                {
                    Variables[name] = MergeVariable(thisVar!, branchVar!);
                }
                else if (inThis)
                {
                    thisVar!.IsPossiblyUndefined = true;
                }
                else if (inBranch)
                {
                    // Typed locals are hoisted to the function scope, so a branch-only dictionary
                    // entry may still correspond to a pre-branch binding on an ancestor. Merge with
                    // that binding instead of treating the name as newly introduced (which wrongly
                    // flagged loop reassignments like `T $body = …; for (…) { $body = …; }` as 4014).
                    if (LookupVariable(name) is { } ancestorVar)
                    {
                        Variables[name] = MergeVariable(ancestorVar, branchVar!);
                    }
                    else
                    {
                        var merged = branchVar!.Clone();
                        merged.IsPossiblyUndefined = true;
                        Variables[name] = merged;
                    }
                }
            }

            HasReturnedOnAllPaths = HasReturnedOnAllPaths && branchState.HasReturnedOnAllPaths;
            MergeReferenceGroups(branchState);
            MergePropertyInitMaps(branchState);
            MergeIndexAccessNarrowingMaps(branchState);
            MergeMemberAccessNarrowingMaps(branchState);
        }

        /// <summary>
        /// Copies variable states from a already-joined branch result (then⋈else, ternary arms)
        /// over this state. Unlike <see cref="Merge"/>, does not treat the pre-join map as a path.
        /// </summary>
        public void AbsorbJoinedVariables(CheckerState joined)
        {
            ThrowIfLocked();

            foreach (var (name, variable) in joined.Variables)
            {
                Variables[name] = variable;
            }

            AbsorbJoinedPropertyInit(joined);
            AbsorbJoinedIndexAccessNarrowing(joined);
            AbsorbJoinedMemberAccessNarrowing(joined);
        }

        /// <summary>
        /// Copies property-initialization states from a joined branch result over this state.
        /// </summary>
        public void AbsorbJoinedPropertyInit(CheckerState joined)
        {
            ThrowIfLocked();

            foreach (var (name, propState) in joined.PropertyInit)
            {
                PropertyInit[name] = propState;
            }
        }

        /// <summary>
        /// Copies index-access narrowing from a joined branch result over this state.
        /// </summary>
        public void AbsorbJoinedIndexAccessNarrowing(CheckerState joined)
        {
            ThrowIfLocked();

            IndexAccessNarrowing = new Dictionary<string, ICheckedType>(
                joined.IndexAccessNarrowing, StringComparer.Ordinal);
        }

        /// <summary>
        /// Copies member-access narrowing from a joined branch result over this state.
        /// </summary>
        public void AbsorbJoinedMemberAccessNarrowing(CheckerState joined)
        {
            ThrowIfLocked();

            MemberAccessNarrowing = new Dictionary<string, ICheckedType>(
                joined.MemberAccessNarrowing, StringComparer.Ordinal);
        }

        /// <summary>
        /// Control-flow type narrowing for a constant-index array access
        /// (<c>$arr[0]</c>, <c>$arr['k']</c>).
        /// </summary>
        public void NarrowIndexAccess(string indexKey, ICheckedType narrowedType)
        {
            ThrowIfLocked();
            IndexAccessNarrowing[indexKey] = narrowedType;
        }

        /// <summary>
        /// Control-flow type narrowing for an instance member access on a variable
        /// (<c>$obj->prop</c>).
        /// </summary>
        public void NarrowMemberAccess(string memberKey, ICheckedType narrowedType)
        {
            ThrowIfLocked();
            MemberAccessNarrowing[memberKey] = narrowedType;
        }

        /// <summary>
        /// Looks up control-flow narrowing for a constant-index array access key.
        /// </summary>
        public ICheckedType? LookupIndexAccess(string indexKey)
        {
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                if (scope.IndexAccessNarrowing.TryGetValue(indexKey, out var type))
                {
                    return type;
                }

                if (IsFunctionBoundary(scope.ScopeType))
                {
                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Looks up control-flow narrowing for a <c>$var->prop</c> member-access key.
        /// </summary>
        public ICheckedType? LookupMemberAccess(string memberKey)
        {
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                if (scope.MemberAccessNarrowing.TryGetValue(memberKey, out var type))
                {
                    return type;
                }

                if (IsFunctionBoundary(scope.ScopeType))
                {
                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Clears all index-access narrowing entries for <paramref name="variableName"/>
        /// (with or without a leading <c>$</c>) after the array variable is reassigned.
        /// </summary>
        public void ResetIndexAccessNarrowingForVariable(string variableName)
        {
            ThrowIfLocked();
            var bare = variableName.TrimStart('$');
            var prefix = "$" + bare + "[";
            var toRemove = IndexAccessNarrowing.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            foreach (var key in toRemove)
            {
                IndexAccessNarrowing.Remove(key);
            }
        }

        /// <summary>
        /// Clears all member-access narrowing entries for <paramref name="variableName"/>
        /// (with or without a leading <c>$</c>) after the receiver variable is reassigned.
        /// </summary>
        public void ResetMemberAccessNarrowingForVariable(string variableName)
        {
            ThrowIfLocked();
            var bare = variableName.TrimStart('$');
            var prefix = "$" + bare + "->";
            var toRemove = MemberAccessNarrowing.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();
            foreach (var key in toRemove)
            {
                MemberAccessNarrowing.Remove(key);
            }
        }

        /// <summary>
        /// Clears the index-access narrowing entry for exactly <paramref name="indexKey"/> (e.g.
        /// <c>$arr[1]</c>) after that specific slot is reassigned — narrowing describes what a
        /// prior *read* observed, not a constraint on a later *write*, and must not be treated as
        /// still holding once the slot's value has changed.
        /// </summary>
        public bool RemoveIndexAccessNarrowing(string indexKey)
        {
            ThrowIfLocked();
            return IndexAccessNarrowing.Remove(indexKey);
        }

        /// <summary>
        /// Clears the member-access narrowing entry for exactly <paramref name="memberKey"/> (e.g.
        /// <c>$obj->prop</c>) after that property is written — same rationale as
        /// <see cref="RemoveIndexAccessNarrowing"/>.
        /// </summary>
        public bool RemoveMemberAccessNarrowing(string memberKey)
        {
            ThrowIfLocked();
            return MemberAccessNarrowing.Remove(memberKey);
        }

        /// <summary>
        /// Seeds or replaces the property-init map (used when entering a constructor / instance method).
        /// </summary>
        public void ReplacePropertyInit(Dictionary<string, PropertyInitializationState> seeded)
        {
            ThrowIfLocked();
            PropertyInit = seeded.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Clone(),
                StringComparer.Ordinal);
        }

        public void AssignProperty(string propertyKey)
        {
            ThrowIfLocked();
            PropertyInitializationState? source = null;
            if (PropertyInit.TryGetValue(propertyKey, out var existing))
            {
                source = existing;
            }
            else if (LookupPropertyInit(propertyKey) is { } lookedUp)
            {
                source = lookedUp;
            }
            else
            {
                // Not a tracked property (untyped, static, hooked, inherited-only, etc.).
                return;
            }

            // Clone-on-write into *this* scope so branch assigns do not mutate a parent map.
            var local = source.Clone();
            local.IsDefinitelyInitialized = true;
            // A write invalidates prior null/instanceof narrowing until
            // <see cref="AssignPropertyType"/> / <see cref="NarrowProperty"/> re-sets it.
            local.NarrowedType = null;
            PropertyInit[propertyKey] = local;
        }

        /// <summary>
        /// Records the post-assignment type for a tracked <c>$this->prop</c> (mirrors
        /// <see cref="AssignVariable"/> setting <see cref="VariableState.NarrowedType"/>).
        /// Also marks the property definitely initialized.
        /// </summary>
        public void AssignPropertyType(string propertyKey, ICheckedType type)
        {
            ThrowIfLocked();
            PropertyInitializationState? source = null;
            if (PropertyInit.TryGetValue(propertyKey, out var existing))
            {
                source = existing;
            }
            else if (LookupPropertyInit(propertyKey) is { } lookedUp)
            {
                source = lookedUp;
            }
            else
            {
                return;
            }

            var local = source.Clone();
            local.IsDefinitelyInitialized = true;
            local.NarrowedType = type;
            PropertyInit[propertyKey] = local;
        }

        /// <summary>
        /// Control-flow type narrowing for <c>$this->prop</c> (null-check, instanceof, type guards).
        /// Clone-on-write into this scope, mirroring <see cref="NarrowVariable"/>.
        /// </summary>
        public void NarrowProperty(string propertyKey, ICheckedType narrowedType)
        {
            ThrowIfLocked();
            PropertyInitializationState? source = null;
            if (PropertyInit.TryGetValue(propertyKey, out var existing))
            {
                source = existing;
            }
            else if (LookupPropertyInit(propertyKey) is { } lookedUp)
            {
                source = lookedUp;
            }
            else
            {
                return;
            }

            var local = source.Clone();
            local.NarrowedType = narrowedType;
            PropertyInit[propertyKey] = local;
        }

        /// <summary>
        /// Clears control-flow narrowing for <c>$this->prop</c> after a write that does not
        /// itself supply a new tracked type.
        /// </summary>
        public void ResetPropertyNarrowing(string propertyKey)
        {
            ThrowIfLocked();
            PropertyInitializationState? source = null;
            if (PropertyInit.TryGetValue(propertyKey, out var existing))
            {
                source = existing;
            }
            else if (LookupPropertyInit(propertyKey) is { } lookedUp)
            {
                source = lookedUp;
            }
            else
            {
                return;
            }

            if (source.NarrowedType is null && PropertyInit.ContainsKey(propertyKey))
            {
                return;
            }

            var local = source.Clone();
            local.NarrowedType = null;
            PropertyInit[propertyKey] = local;
        }

        /// <summary>
        /// Marks a tracked <c>$this->prop</c> uninitialized after <c>unset($this->prop)</c>
        /// (Prop-init #8). Clone-on-write into this scope, mirroring <see cref="AssignProperty"/>.
        /// </summary>
        public void UnsetProperty(string propertyKey)
        {
            ThrowIfLocked();
            PropertyInitializationState? source = null;
            if (PropertyInit.TryGetValue(propertyKey, out var existing))
            {
                source = existing;
            }
            else if (LookupPropertyInit(propertyKey) is { } lookedUp)
            {
                source = lookedUp;
            }
            else
            {
                return;
            }

            var local = source.Clone();
            local.IsDefinitelyInitialized = false;
            local.NarrowedType = null;
            PropertyInit[propertyKey] = local;
        }

        /// <summary>
        /// Clears definite-assignment after <c>unset($x)</c> (Prop-init #8). Subsequent reads
        /// report TYHP4014 until reassigned. Clone-on-write like <see cref="AssignVariable"/>.
        /// </summary>
        public void UnsetVariable(string name)
        {
            ThrowIfLocked();
            var location = FindVariableLocation(name);
            if (location is null)
            {
                return;
            }

            var variable = BindLocally(location.Value.scope, location.Value.variable, name);
            variable.IsDefinitelyAssigned = false;
            variable.IsPossiblyUndefined = true;
            variable.NarrowedType = null;
        }

        public PropertyInitializationState? LookupPropertyInit(string propertyKey)
        {
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                if (scope.PropertyInit.TryGetValue(propertyKey, out var propState))
                {
                    return propState;
                }

                if (IsFunctionBoundary(scope.ScopeType))
                {
                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Deep-copies <c>$this-&gt;prop</c> initialization / narrowing visible in this scope
        /// (stops at function boundaries). Used when a non-static closure captures <c>$this</c>.
        /// </summary>
        public Dictionary<string, PropertyInitializationState> CloneVisiblePropertyInit() =>
            ClonePropertyInit();

        private Dictionary<string, PropertyInitializationState> ClonePropertyInit()
        {
            var result = new Dictionary<string, PropertyInitializationState>(StringComparer.Ordinal);
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                foreach (var (name, propState) in scope.PropertyInit)
                {
                    result.TryAdd(name, propState.Clone());
                }

                if (IsFunctionBoundary(scope.ScopeType))
                {
                    break;
                }
            }

            return result;
        }

        private Dictionary<string, ICheckedType> CloneIndexAccessNarrowing()
        {
            var result = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                foreach (var (key, type) in scope.IndexAccessNarrowing)
                {
                    result.TryAdd(key, type);
                }

                if (IsFunctionBoundary(scope.ScopeType))
                {
                    break;
                }
            }

            return result;
        }

        private Dictionary<string, ICheckedType> CloneMemberAccessNarrowing()
        {
            var result = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                foreach (var (key, type) in scope.MemberAccessNarrowing)
                {
                    result.TryAdd(key, type);
                }

                if (IsFunctionBoundary(scope.ScopeType))
                {
                    break;
                }
            }

            return result;
        }

        private void MergePropertyInitMaps(CheckerState branchState)
        {
            var allKeys = new HashSet<string>(PropertyInit.Keys, StringComparer.Ordinal);
            allKeys.UnionWith(branchState.PropertyInit.Keys);

            foreach (var key in allKeys)
            {
                var inThis = PropertyInit.TryGetValue(key, out var thisProp);
                var inBranch = branchState.PropertyInit.TryGetValue(key, out var branchProp);

                if (inThis && inBranch)
                {
                    PropertyInit[key] = PropertyInitializationState.Merge(thisProp!, branchProp!);
                }
                else if (inThis)
                {
                    // Present only on the pre-branch / fall-through path → not definite after join.
                    var merged = thisProp!.Clone();
                    merged.IsDefinitelyInitialized = false;
                    PropertyInit[key] = merged;
                }
                else if (inBranch)
                {
                    if (LookupPropertyInit(key) is { } ancestorProp)
                    {
                        PropertyInit[key] = PropertyInitializationState.Merge(ancestorProp, branchProp!);
                    }
                    else
                    {
                        var merged = branchProp!.Clone();
                        merged.IsDefinitelyInitialized = false;
                        PropertyInit[key] = merged;
                    }
                }
            }
        }

        /// <summary>
        /// Joins index-access narrowing across branches. Keeps a key only when both sides carry
        /// the same narrowed type (mirrors <see cref="MergeVariable"/> clearing <c>NarrowedType</c>
        /// when paths disagree).
        /// </summary>
        private void MergeIndexAccessNarrowingMaps(CheckerState branchState)
        {
            var allKeys = new HashSet<string>(IndexAccessNarrowing.Keys, StringComparer.Ordinal);
            allKeys.UnionWith(branchState.IndexAccessNarrowing.Keys);
            var kept = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);

            foreach (var key in allKeys)
            {
                var inThis = IndexAccessNarrowing.TryGetValue(key, out var thisType);
                var inBranch = branchState.IndexAccessNarrowing.TryGetValue(key, out var branchType);
                if (inThis && inBranch && TypeComparer.AreTypesEqual(thisType!, branchType!))
                {
                    kept[key] = thisType!;
                }
            }

            IndexAccessNarrowing = kept;
        }

        /// <summary>
        /// Joins member-access narrowing across branches. Same agreement rule as
        /// <see cref="MergeIndexAccessNarrowingMaps"/>.
        /// </summary>
        private void MergeMemberAccessNarrowingMaps(CheckerState branchState)
        {
            var allKeys = new HashSet<string>(MemberAccessNarrowing.Keys, StringComparer.Ordinal);
            allKeys.UnionWith(branchState.MemberAccessNarrowing.Keys);
            var kept = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);

            foreach (var key in allKeys)
            {
                var inThis = MemberAccessNarrowing.TryGetValue(key, out var thisType);
                var inBranch = branchState.MemberAccessNarrowing.TryGetValue(key, out var branchType);
                if (inThis && inBranch && TypeComparer.AreTypesEqual(thisType!, branchType!))
                {
                    kept[key] = thisType!;
                }
            }

            MemberAccessNarrowing = kept;
        }

        public void DeclareVariable(
            string name,
            VariableSymbol symbol,
            ICheckedType? type,
            bool isAssigned,
            DiagnosticBag diagnostics)
        {
            ThrowIfLocked();
            var scope = FindDeclarationScope();

            if (scope.Variables.TryGetValue(name, out var existing))
            {
                // Typed-local declarations are hoisted to the enclosing function scope (PHP
                // variables are function-scoped). Re-declaring the same name is only a genuine
                // duplicate when the previous declaration is still visible from here: declared in
                // this exact block, or in an enclosing block (a shadow). A previous declaration
                // that lives in a sibling block we have already left — e.g. `int $id` inside two
                // consecutive `foreach` bodies — is harmless, so update the existing binding
                // instead of reporting a false duplicate.
                var previousScope = existing.DeclaringBlockScope;
                var isStillVisibleDeclaration = previousScope is null
                    || ReferenceEquals(previousScope, this)
                    || IsAncestorScope(previousScope, this);

                if (isStillVisibleDeclaration)
                {
                    diagnostics.AddError(
                        MessageCode.BinderDuplicateSymbolDeclaration,
                        CurrentFileName ?? symbol.SourceFile,
                        symbol.Line,
                        symbol.Column,
                        name);
                    return;
                }
            }

            var declared = VariableState.ForDeclaration(symbol, type, isAssigned);
            declared.DeclaringBlockScope = this;
            scope.Variables[name] = declared;
        }

        private static bool IsAncestorScope(CheckerState candidate, CheckerState node)
        {
            for (var scope = node.Parent; scope != null; scope = scope.Parent)
            {
                if (ReferenceEquals(scope, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        public void AssignVariable(string name, ICheckedType type, DiagnosticBag diagnostics)
        {
            ThrowIfLocked();
            var location = FindVariableLocation(name);
            if (location is null)
            {
                // First assignment without a prior typed declaration (Story 08): infer and lock the
                // type from the RHS so later uses (e.g. `clone $cfg with [...]`) see a real type
                // instead of `unknown`.
                var scope = FindDeclarationScope();
                scope.Variables[name] = new VariableState
                {
                    DeclaredType = type,
                    NarrowedType = type,
                    IsDefinitelyAssigned = true,
                    IsInferred = true,
                    IsPossiblyNull = type.IsNullable || type is LiteralCheckedType { Value: null },
                    IsPossiblyUndefined = false,
                };
                return;
            }

            // Typed locals are hoisted to the function scope, while `Split(CodeBlock)` only copies
            // the current dictionary. Branch assigns must clone-on-write into *this* scope so they
            // do not mutate the pre-branch binding (which would make if-without-else look assigned).
            var variable = BindLocally(location.Value.scope, location.Value.variable, name);
            variable.IsDefinitelyAssigned = true;
            variable.IsPossiblyUndefined = false;
            variable.IsPossiblyNull = type.IsNullable || type is LiteralCheckedType { Value: null };
            variable.NarrowedType = type;
            variable.DeclaredType ??= type;

            if (variable.IsReference && variable.ReferenceGroup is not null)
            {
                variable.ReferenceGroup.PropagateTypeChange(name, type, GetOwningScopeVariables(this));
            }
        }

        public VariableState? LookupVariable(string name)
        {
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                if (scope.Variables.TryGetValue(name, out var variable))
                {
                    return variable;
                }

                if (IsFunctionBoundary(scope.ScopeType))
                {
                    break;
                }
            }

            return null;
        }

        public void NarrowVariable(string name, ICheckedType narrowedType)
        {
            ThrowIfLocked();
            var location = FindVariableLocation(name);
            if (location is null)
            {
                return;
            }

            BindLocally(location.Value.scope, location.Value.variable, name).NarrowedType = narrowedType;
        }

        public void ResetNarrowing(string name)
        {
            ThrowIfLocked();
            var location = FindVariableLocation(name);
            if (location is null)
            {
                return;
            }

            BindLocally(location.Value.scope, location.Value.variable, name).NarrowedType = null;
        }

        /// <summary>
        /// Ensures mutations apply to a <see cref="VariableState"/> owned by this scope. When the
        /// binding lives only on an ancestor (hoisted typed local), clones it into
        /// <see cref="Variables"/> first.
        /// </summary>
        private VariableState BindLocally(CheckerState foundScope, VariableState variable, string name)
        {
            if (ReferenceEquals(foundScope, this))
            {
                return variable;
            }

            var clone = variable.Clone();
            Variables[name] = clone;
            return clone;
        }

        public Dictionary<string, VariableState> GetAllVariablesInScope()
        {
            var result = new Dictionary<string, VariableState>(StringComparer.Ordinal);

            for (var scope = this; scope != null; scope = scope.Parent)
            {
                foreach (var (name, variable) in scope.Variables)
                {
                    result.TryAdd(name, variable);
                }

                if (IsFunctionBoundary(scope.ScopeType))
                {
                    break;
                }
            }

            return result;
        }

        private CheckerState FindDeclarationScope()
        {
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                if (IsFunctionBoundary(scope.ScopeType))
                {
                    return scope;
                }
            }

            return this;
        }

        private (CheckerState scope, VariableState variable)? FindVariableLocation(string name)
        {
            for (var scope = this; scope != null; scope = scope.Parent)
            {
                if (scope.Variables.TryGetValue(name, out var variable))
                {
                    return (scope, variable);
                }

                if (IsFunctionBoundary(scope.ScopeType))
                {
                    break;
                }
            }

            return null;
        }

        private static bool IsFunctionBoundary(ScopeType scopeType) =>
            scopeType is ScopeType.FunctionDeclaration
                or ScopeType.InstanceMethodDeclaration
                or ScopeType.StaticMethodDeclaration
                or ScopeType.AnonymousFunctionDeclaration;

        private static Dictionary<string, VariableState> GetOwningScopeVariables(CheckerState scope) =>
            scope.GetAllVariablesInScope();

        private void MergeReferenceGroups(CheckerState branchState)
        {
            foreach (var name in Variables.Keys.Intersect(branchState.Variables.Keys, StringComparer.Ordinal))
            {
                var left = Variables[name];
                var right = branchState.Variables[name];
                if (left.ReferenceGroup is null || right.ReferenceGroup is null)
                {
                    continue;
                }

                if (!ReferenceEquals(left.ReferenceGroup, right.ReferenceGroup))
                {
                    continue;
                }

                foreach (var member in left.ReferenceGroup.MemberVariables)
                {
                    if (!Variables.TryGetValue(member, out var memberState))
                    {
                        continue;
                    }

                    memberState.DeclaredType = CheckedTypes.UnionTypes(left.EffectiveType, right.EffectiveType);
                    memberState.NarrowedType = null;
                }
            }
        }

        private static VariableState MergeVariable(VariableState left, VariableState right)
        {
            var merged = left.Clone();
            merged.DeclaredType = CheckedTypes.UnionTypes(left.EffectiveType, right.EffectiveType);
            merged.NarrowedType = null;
            merged.IsDefinitelyAssigned = left.IsDefinitelyAssigned && right.IsDefinitelyAssigned;
            merged.IsPossiblyNull = left.IsPossiblyNull || right.IsPossiblyNull;
            // Assigned on every joined path ⇒ defined; otherwise keep the OR of undefined flags.
            merged.IsPossiblyUndefined = merged.IsDefinitelyAssigned
                ? false
                : left.IsPossiblyUndefined || right.IsPossiblyUndefined;
            merged.IsDisposable = left.IsDisposable || right.IsDisposable;
            merged.IsReference = left.IsReference || right.IsReference;
            return merged;
        }

        private void ThrowIfLocked()
        {
            if (_locked)
            {
                throw new InvalidOperationException("CheckerState snapshot is immutable.");
            }
        }
    }
}
