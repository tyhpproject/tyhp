using System.Diagnostics.CodeAnalysis;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Emitter.NameGeneration;
using Tyhp.TyhpLang.Emitter.Splice;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// AST-level pre-pass that rewrites Tyhp-specific constructs into PHP-compatible forms before emission.
    /// </summary>
    internal sealed partial class AliasConverter
    {
        private readonly EmitContext _context;
        private readonly NameResolver _nameResolver;
        private PHPOutputFile? _currentFile;
        // Maps bare variable names (no leading '$') whose declared type is a struct onto that
        // struct symbol. PhpVariableAst usages are often unbound, so property/`with`/`clone`
        // rewrites consult this map collected before type erasure. PHP variables are
        // function-scoped, so frames are keyed per function-like node (and a separate global
        // frame for top-level code) to avoid a same-named variable in one function bleeding a
        // struct rewrite into an unrelated variable in another function.
        private readonly Dictionary<IBase2Ast, Dictionary<string, ObjectDeclarationSymbol>> _structVarsByFunction =
            new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<string, ObjectDeclarationSymbol> _globalStructVars =
            new(StringComparer.OrdinalIgnoreCase);

        // Same shape as struct vars, but for non-struct class/interface types so object `with`
        // can resolve `$obj with [...]` / `clone $obj with [...]` when PhpVariableAst is unbound.
        private readonly Dictionary<IBase2Ast, Dictionary<string, ObjectDeclarationSymbol>> _objectVarsByFunction =
            new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<string, ObjectDeclarationSymbol> _globalObjectVars =
            new(StringComparer.OrdinalIgnoreCase);

        // Receiver types for extension-method rewriting when PhpVariableAst is unbound: classes,
        // structs, and scalar builtins (int/string/float/bool/array). Nullable/`T|null` forms are
        // stored as the non-null component so `$s->ext()` still resolves for `?string $s`.
        private readonly Dictionary<IBase2Ast, Dictionary<string, IBaseSymbol>> _typedVarsByFunction =
            new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<string, IBaseSymbol> _globalTypedVars =
            new(StringComparer.OrdinalIgnoreCase);

        // Full declared type expressions for typed variables (parameters / typed vars). Needed so
        // `$arr[$i]` can recover the element type from `array<T>` / `array<K,V>` — the symbol map
        // above only stores the erased `array` builtin.
        private readonly Dictionary<IBase2Ast, Dictionary<string, ITypeExpression>> _typedTypeExprsByFunction =
            new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<string, ITypeExpression> _globalTypedTypeExprs =
            new(StringComparer.OrdinalIgnoreCase);

        // Member-access chains in lvalue / existence-tolerant positions (see MarkStructReadExemptions);
        // their struct reads stay a bare `$s['key']`.
        private readonly HashSet<IBase2Ast> _structReadExempt = new(ReferenceEqualityComparer.Instance);

        // Tracks the enclosing function-like node during the transform walk so struct-typed
        // variable lookups resolve against the correct scope frame.
        private readonly Stack<IBase2Ast> _functionStack = new();

        // Checker types carried from an authored expression to the node that replaced it while
        // rewriting (see CarryCheckedTypeToReplacement). EmitContext.ExpressionTypes is read-only
        // and keyed by authored nodes, so rewritten nodes are recorded here.
        private readonly Dictionary<IBase2Ast, ICheckedType> _rewrittenExpressionTypes = new();

        // Tracks the enclosing object declaration during the same transform walk so `$this`
        // resolves to the current class/interface/trait/enum for operator-overload matching.
        // `$this` is never registered into typed-var maps and the binder does not bind a
        // VariableSymbol on its PhpVariableAst — without this stack, `$this->prop OP …` never
        // rewrites. Null entries keep push/pop balanced when a decl cannot be resolved.
        // For a trait declaration this is the trait symbol (methods are not inlined per user);
        // direct-operand `$this OP …` whose overload lives only on a composing class is recovered
        // via composing-class search + `static::` late-static-binding emit (see
        // TryFindBinaryFormOnTypeOrComposingClasses).
        private readonly Stack<ObjectDeclarationSymbol?> _classStack = new();

        // `typeof` takes a typeExpr, and a source type-alias declaration's own body (plus its
        // generic-parameter default types) feeds `TyhpEmitter.BuildRuntimeTypeExpression` for the
        // `\Tyhp\Type` factory (Story 21.9). Struct named types in either position must stay
        // structs so emit can materialize `\Tyhp\Type::struct(...)` instead of the erased `array`
        // hint spelling. Hint positions (parameters, properties, returns) still erase to `array`.
        private int _typeofTypeExprDepth;

        // Member names that follow `->` (properties/methods) must never be rewritten through the
        // tyhpdef alias map. The map is case-insensitive (PHP class names are), so a member like
        // `$this->promise` would otherwise collide with a same-named class alias (`Promise`) and
        // miscompile to `$this->Tyhp\Promise`. Collected up front by reference identity because the
        // tree walk transforms member-name children before reaching their member-access parent.
        private readonly HashSet<IBase2Ast> _protectedMemberNames =
            new(ReferenceEqualityComparer.Instance);

        private CallSiteSpliceEngine? _spliceEngine;
        private SpliceEngineContext? _spliceContext;
        private readonly List<IExpression> _pendingSpliceHoists = [];
        private readonly HashSet<IBase2Ast> _refHoistForbidden = new(ReferenceEqualityComparer.Instance);

        public AliasConverter(EmitContext context)
        {
            this._context = context;
            var symbolTree = context.GetSymbolTree();
            this._nameResolver = new NameResolver(symbolTree, context.Diagnostics);
        }

        public void Convert(PHPOutputFile outputFile)
        {
            this._currentFile = outputFile;
            // Property-hook lowering for PHP < 8.4 lives in TyhpEmitter.PropertyAccessors.cs
            // (trait injection, registration, and $this->prop → tyhpGet/SetBacking rewrite at emit time).
            _ = this._context.Config.TargetPhpVersion;

            this._protectedMemberNames.Clear();
            this._pendingSpliceHoists.Clear();
            this._refHoistForbidden.Clear();
            this._spliceContext = this.CreateSpliceContext();
            this._spliceEngine = new CallSiteSpliceEngine(this._spliceContext);
            this._structVarsByFunction.Clear();
            this._globalStructVars.Clear();
            this._objectVarsByFunction.Clear();
            this._globalObjectVars.Clear();
            this._typedVarsByFunction.Clear();
            this._globalTypedVars.Clear();
            this._typedTypeExprsByFunction.Clear();
            this._globalTypedTypeExprs.Clear();
            this._functionStack.Clear();
            this._classStack.Clear();
            this._typeofTypeExprDepth = 0;
            AstWalker.WalkStatements(
                outputFile.Statements.OfType<ITopStatement>(),
                this.CollectProtectedMemberName);
            foreach (var statement in outputFile.Statements.OfType<IBase2Ast>())
            {
                this.CollectStructTypedVariables(statement, enclosingFunction: null);
            }

            // Expand top-level statement-context object `with` before the tree walk so assignment
            // forms become property-assignment sequences instead of ObjectHelper expressions.
            this.ExpandStatementListInPlace(outputFile.Statements);
            this.CollectRefHoistForbidden(outputFile.Statements.OfType<IBase2Ast>());

            for (var i = 0; i < outputFile.Statements.Count; i++)
            {
                if (outputFile.Statements[i] is IBase2Ast statement)
                {
                    this._pendingSpliceHoists.Clear();
                    this.CollectOccupiedNames(statement);
                    outputFile.Statements[i] = (ITopStatement)AstWalker.TransformTree(
                        statement,
                        this.TransformNode,
                        this.PreTransformWith)!;
                    if (this._pendingSpliceHoists.Count > 0
                        && outputFile.Statements[i] is IExpression)
                    {
                        foreach (var hoist in this._pendingSpliceHoists)
                        {
                            if (hoist is ITopStatement hoistStmt)
                            {
                                outputFile.Statements.Insert(i, hoistStmt);
                                i++;
                            }
                        }

                        this._pendingSpliceHoists.Clear();
                    }
                }
            }

            if (outputFile.IsEntryPoint && !string.IsNullOrWhiteSpace(this._context.Config.EntryPointAutoloader))
            {
                // Autoloader inclusion is handled in PHPOutputFile.Generate() for entry point files.
            }

            this._currentFile = null;
        }

        private void CollectStructTypedVariables(IBase2Ast node, IBase2Ast? enclosingFunction)
        {
            var currentFunction = IsFunctionLike(node) ? node : enclosingFunction;

            switch (node)
            {
                case PhpParameterAst param:
                {
                    var varName = GetParameterVariableName(param);
                    if (varName is not null)
                    {
                        this.CollectTypedVariable(currentFunction, varName, param.Type);
                    }

                    break;
                }
                case TyhpTypedVarExprAst typedVar:
                {
                    var varName = NormalizeVariableName(
                        typedVar.Variable?.VariableToken?.ValueString
                        ?? typedVar.Variable?.Identifier);
                    if (varName is not null)
                    {
                        this.CollectTypedVariable(currentFunction, varName, typedVar.TypeExpression);
                    }

                    break;
                }
                // `$p = new Point()` / `$a = new Money()` (and clone/with / copy-from-typed-var)
                // has no declared type, but later rewrites still need the LHS type:
                // `$p->x` → `$p['x']`, `$a + $b` → `\Money::__add($a, $b)`. Register the LHS
                // when the RHS clearly produces a struct or class. Process the assignment before
                // walking children so subsequent statements in the same block can copy from this
                // variable.
                case PhpBinaryOpAst assign
                    when WithKeywordHelper.IsSimpleAssignmentOperator(assign.Operator)
                    && WithKeywordHelper.IsSimpleVariable(assign.Left):
                {
                    var varName = NormalizeVariableName(
                        (assign.Left as PhpVariableAst)?.VariableToken?.ValueString
                        ?? (assign.Left as PhpVariableAst)?.Identifier);
                    if (varName is not null
                        && this.ResolveStructFromAssignmentRhs(assign.Right, currentFunction)
                            is { } structDecl)
                    {
                        this.AddStructTypedVariable(currentFunction, varName, structDecl);
                        this.AddTypedVariable(currentFunction, varName, structDecl);
                    }
                    else if (varName is not null
                        && this.ResolveObjectFromAssignmentRhs(assign.Right, currentFunction)
                            is { } objectDecl)
                    {
                        this.AddObjectTypedVariable(currentFunction, varName, objectDecl);
                        this.AddTypedVariable(currentFunction, varName, objectDecl);
                    }

                    break;
                }
                case PhpLoopAst { LoopType: PhpLoopType.Foreach } loop:
                {
                    this.CollectForeachStructValue(loop, currentFunction);
                    break;
                }
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    this.CollectStructTypedVariables(child, currentFunction);
                }
            }
        }

        /// <summary>
        /// Resolves a struct type from an assignment RHS during the pre-pass collection walk.
        /// Intentionally narrow: does not recurse through property access (so
        /// <c>$val = $p->y</c> does not treat <c>$val</c> as a struct).
        /// </summary>
        private ObjectDeclarationSymbol? ResolveStructFromAssignmentRhs(
            IExpression? rhs,
            IBase2Ast? function)
        {
            if (rhs is null)
            {
                return null;
            }

            if (rhs is PhpBinaryOpAst binary && StructEmissionHelper.IsWithOperator(binary.Operator))
            {
                return this.ResolveStructFromAssignmentRhs(binary.Left, function);
            }

            if (rhs is PhpUnaryOpAst unary && StructEmissionHelper.IsCloneOperator(unary.Operator))
            {
                return this.ResolveStructFromAssignmentRhs(unary.Operand, function);
            }

            if (rhs is PhpNewAst newExpr)
            {
                return this.ResolveStructFromNew(newExpr);
            }

            if (rhs is PhpVariableAst variable)
            {
                var varName = NormalizeVariableName(
                    variable.VariableToken?.ValueString
                    ?? variable.Identifier);
                if (varName is null)
                {
                    return null;
                }

                // Function stack is empty during collection; look up the frame directly.
                if (function is null)
                {
                    return this._globalStructVars.TryGetValue(varName, out var global) ? global : null;
                }

                return this._structVarsByFunction.TryGetValue(function, out var frame)
                    && frame.TryGetValue(varName, out var scoped)
                    ? scoped
                    : null;
            }

            return null;
        }

        /// <summary>
        /// Resolves a non-struct class type from an assignment RHS during the pre-pass collection
        /// walk (narrow: <c>new Class</c>, clone/with of that, or copy from an already-tracked
        /// object-typed variable — not property/method results).
        /// </summary>
        private ObjectDeclarationSymbol? ResolveObjectFromAssignmentRhs(
            IExpression? rhs,
            IBase2Ast? function)
        {
            if (rhs is null)
            {
                return null;
            }

            if (rhs is PhpBinaryOpAst binary && StructEmissionHelper.IsWithOperator(binary.Operator))
            {
                return this.ResolveObjectFromAssignmentRhs(binary.Left, function);
            }

            if (rhs is PhpUnaryOpAst unary && StructEmissionHelper.IsCloneOperator(unary.Operator))
            {
                return this.ResolveObjectFromAssignmentRhs(unary.Operand, function);
            }

            if (rhs is PhpNewAst newExpr)
            {
                return this.ResolveObjectFromNew(newExpr);
            }

            if (rhs is PhpVariableAst variable)
            {
                var varName = NormalizeVariableName(
                    variable.VariableToken?.ValueString
                    ?? variable.Identifier);
                if (varName is null)
                {
                    return null;
                }

                if (function is null)
                {
                    return this._globalObjectVars.TryGetValue(varName, out var global) ? global : null;
                }

                return this._objectVarsByFunction.TryGetValue(function, out var frame)
                    && frame.TryGetValue(varName, out var scoped)
                    ? scoped
                    : null;
            }

            return null;
        }

        private void AddStructTypedVariable(
            IBase2Ast? function,
            string varName,
            ObjectDeclarationSymbol structDecl)
        {
            if (function is null)
            {
                this._globalStructVars[varName] = structDecl;
                return;
            }

            if (!this._structVarsByFunction.TryGetValue(function, out var frame))
            {
                frame = new Dictionary<string, ObjectDeclarationSymbol>(StringComparer.OrdinalIgnoreCase);
                this._structVarsByFunction[function] = frame;
            }

            frame[varName] = structDecl;
        }

        private void AddObjectTypedVariable(
            IBase2Ast? function,
            string varName,
            ObjectDeclarationSymbol objectDecl)
        {
            if (function is null)
            {
                this._globalObjectVars[varName] = objectDecl;
                return;
            }

            if (!this._objectVarsByFunction.TryGetValue(function, out var frame))
            {
                frame = new Dictionary<string, ObjectDeclarationSymbol>(StringComparer.OrdinalIgnoreCase);
                this._objectVarsByFunction[function] = frame;
            }

            frame[varName] = objectDecl;
        }

        private void CollectTypedVariable(IBase2Ast? function, string varName, ITypeExpression? typeExpr)
        {
            if (typeExpr is not null)
            {
                this.AddTypedTypeExpression(function, varName, typeExpr);
            }

            var structDecl = this.ResolveStructTypeFromTypeExpression(typeExpr);
            if (structDecl is not null)
            {
                this.AddStructTypedVariable(function, varName, structDecl);
                this.AddTypedVariable(function, varName, structDecl);
                return;
            }

            if (this.ResolveObjectTypeFromTypeExpression(typeExpr) is { } objectDecl)
            {
                this.AddObjectTypedVariable(function, varName, objectDecl);
                this.AddTypedVariable(function, varName, objectDecl);
                return;
            }

            if (this.ResolveTypeSymbolFromTypeExpression(typeExpr) is { } anyType)
            {
                this.AddTypedVariable(function, varName, anyType);
            }
        }

        /// <summary>
        /// <c>foreach ($amounts as $m)</c> over <c>array&lt;Struct&gt;</c> / <c>iterable&lt;Struct&gt;</c>
        /// must register <c>$m</c> as that struct so later <c>$m-&gt;prop</c> rewrites to array keys.
        /// Collection runs before the transform walk, so the function stack is empty — look up
        /// declared iterable types on the enclosing function frame directly.
        /// </summary>
        private void CollectForeachStructValue(PhpLoopAst loop, IBase2Ast? function)
        {
            if (loop.ValueVariable is not PhpVariableAst valueVar)
            {
                return;
            }

            var varName = CheckerHelpers.GetVariableName(valueVar);
            if (varName is null)
            {
                return;
            }

            // `TryGetStructFromCheckedExpression` below already returns null for a checker
            // recovery (`Unresolved`) binding — `UnresolvedCheckedType` never satisfies
            // `TryGetObjectDeclaration`'s `IsStruct` check, so it naturally falls through to the
            // iterable-type-expression peel a few lines down. That peel is the load-bearing path
            // for a well-typed `array<Struct>` parameter whose *binding-node* checker type still
            // came back `Unresolved` for unrelated inference reasons (e.g. an extension-spliced
            // receiver reusing this same foreach value) — do not gate it on the checker type too,
            // or a legitimate `foreach ($amounts as $m)` over `array<Money>` stops registering
            // `$m` as `Money` and `$m->prop` never rewrites to `['prop']` (Story 21.12
            // Workstream C review).
            if (this.TryGetStructFromCheckedExpression(valueVar) is { } fromBinding)
            {
                this.AddStructTypedVariable(function, varName, fromBinding);
                this.AddTypedVariable(function, varName, fromBinding);
                return;
            }

            if (valueVar.VariableExpression is PhpVariableAst inner
                && this.TryGetStructFromCheckedExpression(inner) is { } fromInner)
            {
                this.AddStructTypedVariable(function, varName, fromInner);
                this.AddTypedVariable(function, varName, fromInner);
                return;
            }

            if (loop.Condition is not PhpVariableAst iterableVar
                || CheckerHelpers.GetVariableName(iterableVar) is not { } iterableName)
            {
                return;
            }

            var iterableTypeExpr = this.LookupTypedTypeExpressionInFunction(function, iterableName);
            var valueTypeExpr = TryGetArrayOrIterableValueTypeExpression(iterableTypeExpr);
            if (this.ResolveStructTypeFromTypeExpression(valueTypeExpr) is not { } fromIterable)
            {
                return;
            }

            this.AddStructTypedVariable(function, varName, fromIterable);
            this.AddTypedVariable(function, varName, fromIterable);
        }

        private ITypeExpression? LookupTypedTypeExpressionInFunction(IBase2Ast? function, string varName)
        {
            if (function is null)
            {
                return this._globalTypedTypeExprs.TryGetValue(varName, out var global) ? global : null;
            }

            return this._typedTypeExprsByFunction.TryGetValue(function, out var frame)
                && frame.TryGetValue(varName, out var scoped)
                ? scoped
                : null;
        }

        private ObjectDeclarationSymbol? TryGetStructFromCheckedExpression(IExpression? expression)
        {
            if (expression is not IBase2Ast node
                || !this._context.ExpressionTypes.TryGetValue(node, out var checkedType))
            {
                return null;
            }

            return TryGetStructDeclaration(checkedType);
        }

        /// <summary>
        /// True when the checker memoized this expression as recovery <c>Unresolved</c>.
        /// Struct <c>-&gt;</c> rewrite and extension splice must not treat that as a typed receiver.
        /// </summary>
        private bool IsUnresolvedCheckedReceiver(IExpression? expression)
        {
            if (expression is not IBase2Ast node
                || !this.TryGetCheckedExpressionType(node, out var checkedType))
            {
                return false;
            }

            return TypeComparer.IsUnresolvedType(checkedType);
        }

        /// <summary>
        /// The checker's type for <paramref name="node"/>: the recorded type of an authored node, or
        /// the type carried over to the node that replaced it during this walk
        /// (<see cref="_rewrittenExpressionTypes"/>).
        /// </summary>
        private bool TryGetCheckedExpressionType(IBase2Ast node, [NotNullWhen(true)] out ICheckedType? checkedType)
        {
            if (this._context.ExpressionTypes.TryGetValue(node, out checkedType))
            {
                return true;
            }

            return this._rewrittenExpressionTypes.TryGetValue(node, out checkedType);
        }

        /// <summary>
        /// Operators, extension calls, and similar rewrites replace an authored expression with a
        /// new node the checker never saw. The replacement computes the same value, so it keeps
        /// the original's checked type; an enclosing operator (<c>$a + $b + $c</c>) then still
        /// resolves its operand's type after the inner operation was rewritten or spliced.
        /// </summary>
        private void CarryCheckedTypeToReplacement(IBase2Ast original, IBase2Ast? replacement)
        {
            if (replacement is null
                || ReferenceEquals(replacement, original)
                || this._context.ExpressionTypes.ContainsKey(replacement)
                || this._rewrittenExpressionTypes.ContainsKey(replacement)
                || !this.TryGetCheckedExpressionType(original, out var checkedType))
            {
                return;
            }

            this._rewrittenExpressionTypes[replacement] = checkedType;
        }

        private static ObjectDeclarationSymbol? TryGetStructDeclaration(ICheckedType? type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is null)
            {
                return null;
            }

            return TypeComparer.TryGetObjectDeclaration(type) is { IsStruct: true } obj
                ? obj
                : null;
        }

        private IBaseSymbol? TryGetSymbolFromCheckedExpression(IExpression? expression)
        {
            if (expression is not IBase2Ast node
                || !this.TryGetCheckedExpressionType(node, out var checkedType))
            {
                return null;
            }

            while (checkedType is NullableCheckedType nullable)
            {
                checkedType = nullable.InnerType;
            }

            return TypeComparer.TryGetNominalSymbol(checkedType);
        }

        private void AddTypedVariable(IBase2Ast? function, string varName, IBaseSymbol typeSymbol)
        {
            if (function is null)
            {
                this._globalTypedVars[varName] = typeSymbol;
                return;
            }

            if (!this._typedVarsByFunction.TryGetValue(function, out var frame))
            {
                frame = new Dictionary<string, IBaseSymbol>(StringComparer.OrdinalIgnoreCase);
                this._typedVarsByFunction[function] = frame;
            }

            frame[varName] = typeSymbol;
        }

        private void AddTypedTypeExpression(
            IBase2Ast? function,
            string varName,
            ITypeExpression typeExpr)
        {
            if (function is null)
            {
                this._globalTypedTypeExprs[varName] = typeExpr;
                return;
            }

            if (!this._typedTypeExprsByFunction.TryGetValue(function, out var frame))
            {
                frame = new Dictionary<string, ITypeExpression>(StringComparer.OrdinalIgnoreCase);
                this._typedTypeExprsByFunction[function] = frame;
            }

            frame[varName] = typeExpr;
        }

        private ObjectDeclarationSymbol? LookupStructTypedVariable(string varName)
        {
            // Inside a function, only that function's frame is consulted; PHP has no implicit
            // access to top-level variables, so we never fall back to the global frame there.
            if (this._functionStack.Count > 0)
            {
                return this._structVarsByFunction.TryGetValue(this._functionStack.Peek(), out var frame)
                    && frame.TryGetValue(varName, out var scoped)
                    ? scoped
                    : null;
            }

            return this._globalStructVars.TryGetValue(varName, out var global) ? global : null;
        }

        private ObjectDeclarationSymbol? LookupObjectTypedVariable(string varName)
        {
            if (this._functionStack.Count > 0)
            {
                return this._objectVarsByFunction.TryGetValue(this._functionStack.Peek(), out var frame)
                    && frame.TryGetValue(varName, out var scoped)
                    ? scoped
                    : null;
            }

            return this._globalObjectVars.TryGetValue(varName, out var global) ? global : null;
        }

        private IBaseSymbol? LookupTypedVariable(string varName)
        {
            if (this._functionStack.Count > 0)
            {
                return this._typedVarsByFunction.TryGetValue(this._functionStack.Peek(), out var frame)
                    && frame.TryGetValue(varName, out var scoped)
                    ? scoped
                    : null;
            }

            return this._globalTypedVars.TryGetValue(varName, out var global) ? global : null;
        }

        private ITypeExpression? LookupTypedTypeExpression(string varName)
        {
            if (this._functionStack.Count > 0)
            {
                return this._typedTypeExprsByFunction.TryGetValue(this._functionStack.Peek(), out var frame)
                    && frame.TryGetValue(varName, out var scoped)
                    ? scoped
                    : null;
            }

            return this._globalTypedTypeExprs.TryGetValue(varName, out var global) ? global : null;
        }

        private static bool IsFunctionLike(IBase2Ast node) =>
            node is PhpFunctionDeclAst or PhpMethodDeclAst or PhpInlineFunctionAst
                or TyhpOperatorOverloadAst or TyhpAsyncBlockAst;

        private ObjectDeclarationSymbol? ResolveStructTypeFromTypeExpression(ITypeExpression? typeExpr)
        {
            switch (typeExpr)
            {
                case PhpNamedTypeAst named:
                    return StructEmissionHelper.ResolveStructFromNamedType(
                        named,
                        this._context.GlobalScope);
                case PhpTypeExpressionAst composite:
                {
                    var parts = composite.Types?.GetAllNotNull().ToList() ?? [];
                    // `Point` and `?Point` (nullable wrapper / single-element union) both carry one
                    // named type we can resolve as a struct.
                    foreach (var part in parts)
                    {
                        if (part is PhpNamedTypeAst namedPart
                            && StructEmissionHelper.ResolveStructFromNamedType(
                                namedPart,
                                this._context.GlobalScope) is { } found)
                        {
                            return found;
                        }

                        if (part is ITypeExpression nested
                            && this.ResolveStructTypeFromTypeExpression(nested) is { } nestedFound)
                        {
                            return nestedFound;
                        }
                    }

                    return null;
                }
                default:
                    return null;
            }
        }

        private static string? GetParameterVariableName(PhpParameterAst param)
        {
            if (param.BoundSymbol is VariableSymbol vs && !string.IsNullOrWhiteSpace(vs.Name))
            {
                return NormalizeVariableName(vs.Name);
            }

            return NormalizeVariableName(param.Name);
        }

        private static string? NormalizeVariableName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return name.StartsWith('$') ? name[1..] : name;
        }

        private IBase2Ast? PreTransformWith(IBase2Ast node)
        {
            // Runs as the pre-transform for every node: enter a scope frame for function-like
            // nodes so struct-typed variable lookups resolve correctly. The matching pop happens
            // in the post-transform (TransformNode). Function-like nodes never short-circuit here
            // (they are not `with` binaries), guaranteeing balanced push/pop.
            if (IsFunctionLike(node))
            {
                this._functionStack.Push(node);
            }

            // Same push/pop contract for object declarations so `$this` resolves while walking
            // methods. Object decls are never `with` binaries, so they never short-circuit.
            // Always push (possibly null) so TransformNode can always pop one frame per decl.
            if (node is PhpObjectTypeDeclAst typeDecl)
            {
                this._classStack.Push(this.TryResolveObjectDeclarationFromDecl(typeDecl));
            }

            if (node is TyhpTypeofAst or TyhpTypeAliasAst)
            {
                this._typeofTypeExprDepth++;
            }

            this.MarkStructReadExemptions(node);

            // Expand statement-context object `with` inside blocks before children are walked.
            if (node is PhpStatementBlockAst block)
            {
                this.ExpandStatementBlockInPlace(block);
                this.TransformStatementBlockWithSpliceHoists(block);
                return block;
            }

            if (node is not PhpBinaryOpAst binary
                || !StructEmissionHelper.IsWithOperator(binary.Operator))
            {
                return null;
            }

            // Struct path.
            if (this.IsStructWithLeft(binary.Left))
            {
                if (binary.Right is PhpArrayPairListAst pairList)
                {
                    this.TransformWithListValues(pairList);
                }

                // For `new Struct() with [...]`, keep the PhpNewAst intact so TryRewriteStructWith can
                // merge declaration defaults. For clone / in-place, rewrite the left subtree first
                // (e.g. erase `clone` on the operand).
                if (binary.Left is not PhpNewAst
                    && binary.Left is IBase2Ast leftNode
                    && binary is Base2Ast binaryNode)
                {
                    var transformedLeft = AstWalker.TransformTree(
                        leftNode,
                        this.TransformNode,
                        this.PreTransformWith);
                    if (!ReferenceEquals(transformedLeft, leftNode))
                    {
                        binaryNode.ReplaceChild(leftNode, transformedLeft);
                    }
                }

                return StructEmissionHelper.TryRewriteStructWith(
                    binary,
                    this.ResolveStructFromNew,
                    this.ResolveStructType,
                    this.ResolveAnonymousStructShape,
                    this.TransformStructDefaultValue,
                    out var rewritten)
                    ? rewritten
                    : null;
            }

            // Object-form expression rewrite (statement forms were expanded earlier).
            if (binary.Right is PhpArrayPairListAst objectPairs)
            {
                this.TransformWithListValues(objectPairs);
            }

            if (binary.Left is IBase2Ast objectLeft && binary is Base2Ast objectBinary)
            {
                var transformedLeft = AstWalker.TransformTree(
                    objectLeft,
                    this.TransformNode,
                    this.PreTransformWith);
                if (!ReferenceEquals(transformedLeft, objectLeft))
                {
                    objectBinary.ReplaceChild(objectLeft, transformedLeft);
                }
            }

            var objectDecl = this.ResolveObjectDeclarationFromWithLeft(binary.Left);
            return WithKeywordHelper.RewriteAsExpression(binary, this._context, objectDecl);
        }

        private void ExpandStatementListInPlace(IList<ITopStatement> statements)
        {
            var expanded = new List<ITopStatement>();
            var anyExpanded = false;
            foreach (var statement in statements)
            {
                if (statement is IBase2Ast ast
                    && (this.TryExpandObjectWithStatement(ast, out var parts)
                        || this.TryExpandCompoundAssignWithTemps(ast, out parts)
                        || this.TryExpandIncrementDecrementWithTemps(ast, out parts)
                        || this.TryExpandPostfixIncrementDecrementAsValue(ast, out parts)))
                {
                    anyExpanded = true;
                    foreach (var part in parts)
                    {
                        if (part is ITopStatement top)
                        {
                            expanded.Add(top);
                        }
                    }
                }
                else
                {
                    expanded.Add(statement);
                }
            }

            // Only rewrite the list when a statement actually expanded; otherwise leave
            // the original nodes (and their whitespace/trivia) untouched.
            if (!anyExpanded)
            {
                return;
            }

            statements.Clear();
            foreach (var statement in expanded)
            {
                statements.Add(statement);
            }
        }

        private void ExpandStatementBlockInPlace(PhpStatementBlockAst block)
        {
            var original = block.GetAllNotNull().Cast<IBase2Ast>().ToList();
            var expanded = new List<IBase2Ast?>();
            var anyExpanded = false;
            foreach (var statement in original)
            {
                if (this.TryExpandObjectWithStatement(statement, out var parts)
                    || this.TryExpandCompoundAssignWithTemps(statement, out parts)
                    || this.TryExpandIncrementDecrementWithTemps(statement, out parts)
                    || this.TryExpandPostfixIncrementDecrementAsValue(statement, out parts))
                {
                    anyExpanded = true;
                    expanded.AddRange(parts);
                }
                else
                {
                    expanded.Add(statement);
                }
            }

            // Rebuilding a block via ClearChildren/AddChild resets statement trivia, so only do it
            // when a statement actually expanded. Untouched blocks keep their formatting.
            if (!anyExpanded)
            {
                return;
            }

            block.ClearChildren();
            foreach (var child in expanded)
            {
                block.AddChild(child);
            }
        }

        private bool TryExpandObjectWithStatement(IBase2Ast statement, out IReadOnlyList<IBase2Ast> expanded)
        {
            expanded = [statement];

            if (!this.TryGetStatementWithBinary(statement, out var withBinary))
            {
                return false;
            }

            // Structs keep the expression rewrite (merged array / array_replace); do not expand
            // into property assignments. A bare in-place `$p with [...];` statement is value
            // semantics on a PHP array, so it must reassign its target
            // (`$p = \array_replace($p, [...]);`) or the updated copy is discarded.
            if (this.IsStructWithLeft(withBinary.Left))
            {
                if (ReferenceEquals(statement, withBinary)
                    && IsInPlaceWithTarget(withBinary.Left)
                    && withBinary.Left is IExpression target)
                {
                    expanded = [WithKeywordHelper.CreateAssignment(target, withBinary, withBinary)];
                    return true;
                }

                return false;
            }

            var objectDecl = this.ResolveObjectDeclarationFromWithLeft(withBinary.Left);
            if (!WithKeywordHelper.CanExpandToStatements(withBinary, this._context, objectDecl))
            {
                return false;
            }

            // Rewrite nested `with` inside override values first (expression context).
            if (withBinary.Right is PhpArrayPairListAst pairList)
            {
                this.TransformWithListValues(pairList);
            }

            expanded = WithKeywordHelper.ExpandToStatements(
                statement,
                withBinary,
                this._context,
                objectDecl);
            return expanded.Count > 0
                && !(expanded.Count == 1 && ReferenceEquals(expanded[0], statement));
        }

        /// <summary>
        /// True for the in-place <c>with</c> form (<c>$x with [...]</c>): the left side is an
        /// existing value, not a <c>new</c> or <c>clone</c> expression.
        /// </summary>
        private static bool IsInPlaceWithTarget(IExpression? left) =>
            left is not null
            && left is not PhpNewAst
            && !(left is PhpUnaryOpAst unary && StructEmissionHelper.IsCloneOperator(unary.Operator));

        private bool TryGetStatementWithBinary(IBase2Ast statement, out PhpBinaryOpAst withBinary)
        {
            withBinary = null!;

            if (statement is PhpBinaryOpAst bare
                && StructEmissionHelper.IsWithOperator(bare.Operator))
            {
                withBinary = bare;
                return true;
            }

            if (statement is PhpBinaryOpAst assign
                && WithKeywordHelper.IsSimpleAssignmentOperator(assign.Operator)
                && assign.Right is PhpBinaryOpAst assignWith
                && StructEmissionHelper.IsWithOperator(assignWith.Operator))
            {
                withBinary = assignWith;
                return true;
            }

            if (statement is TyhpTypedVarExprAst typedVar
                && typedVar.AssignedExpression is PhpBinaryOpAst typedWith
                && StructEmissionHelper.IsWithOperator(typedWith.Operator))
            {
                withBinary = typedWith;
                return true;
            }

            return false;
        }

        private ObjectDeclarationSymbol? ResolveObjectDeclarationFromWithLeft(IExpression? left)
        {
            if (left is PhpNewAst newExpr)
            {
                return this.ResolveObjectFromNew(newExpr);
            }

            if (left is PhpUnaryOpAst unary && StructEmissionHelper.IsCloneOperator(unary.Operator))
            {
                return this.ResolveObjectDeclarationFromExpression(unary.Operand);
            }

            return this.ResolveObjectDeclarationFromExpression(left);
        }

        private ObjectDeclarationSymbol? ResolveObjectFromNew(PhpNewAst newExpr)
        {
            if (newExpr.ClassName is PhpNameAst name
                && name.BoundSymbol is ObjectDeclarationSymbol bound
                && !bound.IsStruct)
            {
                return bound;
            }

            if (newExpr.ClassName is PhpNamedTypeAst namedType)
            {
                if (namedType.BoundSymbol is ObjectDeclarationSymbol namedBound && !namedBound.IsStruct)
                {
                    return namedBound;
                }

                if (namedType.Name is PhpNameAst nestedName
                    && nestedName.BoundSymbol is ObjectDeclarationSymbol nestedBound
                    && !nestedBound.IsStruct)
                {
                    return nestedBound;
                }
            }

            var className = GetClassNameReferenceText(newExpr.ClassName);
            if (string.IsNullOrWhiteSpace(className))
            {
                return null;
            }

            return this.FindObjectTypeSymbol(className);
        }

        private ObjectDeclarationSymbol? ResolveObjectDeclarationFromExpression(IExpression? expression)
        {
            if (expression?.BoundSymbol is ObjectDeclarationSymbol objectDecl && !objectDecl.IsStruct)
            {
                return objectDecl;
            }

            if (expression is PhpNewAst newExpr)
            {
                return this.ResolveObjectFromNew(newExpr);
            }

            if (expression is PhpVariableAst variable)
            {
                // Prefer DeclaredType on a bound variable symbol (parameters / typed locals).
                if (variable.BoundSymbol is VariableSymbol { DeclaredType: not null } vs)
                {
                    var fromDeclared = this.ResolveObjectTypeFromTypeExpression(vs.DeclaredType);
                    if (fromDeclared is not null)
                    {
                        return fromDeclared;
                    }
                }

                var varName = NormalizeVariableName(
                    variable.VariableToken?.ValueString ?? variable.Identifier);
                if (varName is not null
                    && this.LookupObjectTypedVariable(varName) is { IsStruct: false } typed)
                {
                    return typed;
                }
            }

            if (this.ResolveExpressionType(expression) is ObjectDeclarationSymbol resolved && !resolved.IsStruct)
            {
                return resolved;
            }

            return null;
        }

        private ObjectDeclarationSymbol? ResolveObjectTypeFromTypeExpression(ITypeExpression? typeExpr)
        {
            switch (typeExpr)
            {
                case PhpNamedTypeAst named:
                {
                    if (named.BoundSymbol is ObjectDeclarationSymbol bound && !bound.IsStruct)
                    {
                        return bound;
                    }

                    if (named.Name is PhpNameAst typeName)
                    {
                        if (typeName.BoundSymbol is ObjectDeclarationSymbol nameBound && !nameBound.IsStruct)
                        {
                            return nameBound;
                        }

                        var text = typeName.ValueString ?? typeName.Identifier;
                        return string.IsNullOrWhiteSpace(text) ? null : this.FindObjectTypeSymbol(text);
                    }

                    var fallback = named.Name?.Identifier;
                    return string.IsNullOrWhiteSpace(fallback) ? null : this.FindObjectTypeSymbol(fallback);
                }
                case PhpTypeExpressionAst composite:
                {
                    foreach (var part in composite.Types?.GetAllNotNull() ?? [])
                    {
                        if (part is ITypeExpression nested
                            && this.ResolveObjectTypeFromTypeExpression(nested) is { } found)
                        {
                            return found;
                        }
                    }

                    return null;
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Finds a non-struct class/interface/enum by simple or qualified name, walking file
        /// scopes under <see cref="EmitContext.GlobalScope"/> (same strategy as struct lookup).
        /// </summary>
        private ObjectDeclarationSymbol? FindObjectTypeSymbol(string name)
        {
            var simple = name.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries)[^1];
            if (this.FindTypeSymbol(simple) is { IsStruct: false } viaResolver)
            {
                return viaResolver;
            }

            return FindObjectSymbol(this._context.GlobalScope, simple);
        }

        private static ObjectDeclarationSymbol? FindObjectSymbol(IBaseScope? scope, string name)
        {
            if (scope is null)
            {
                return null;
            }

            if (scope.FindChildSymbolByName(name) is ObjectDeclarationSymbol direct && !direct.IsStruct)
            {
                return direct;
            }

            foreach (var childScope in scope.GetAllChildScopes())
            {
                if (FindObjectSymbol(childScope, name) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        private bool IsStructWithLeft(IExpression? left)
        {
            if (left is PhpNewAst newExpr)
            {
                return this.ResolveStructFromNew(newExpr) is not null;
            }

            if (left is PhpUnaryOpAst unary
                && StructEmissionHelper.IsCloneOperator(unary.Operator))
            {
                return this.ResolveStructType(unary.Operand) is not null
                    || this.ResolveAnonymousStructShape(unary.Operand) is not null;
            }

            return this.ResolveStructType(left) is not null
                || this.ResolveAnonymousStructShape(left) is not null;
        }

        private void TransformWithListValues(PhpArrayPairListAst pairList)
        {
            foreach (var pair in pairList.GetAllTrimmingTrailingSkippedSlots())
            {
                if (pair.IsSkippedSlot
                    || pair.ValueExpr is not IBase2Ast value
                    || pair is not Base2Ast pairNode)
                {
                    continue;
                }

                var transformed = AstWalker.TransformTree(value, this.TransformNode, this.PreTransformWith);
                pairNode.ReplaceChild(value, transformed);
            }
        }

        private IBase2Ast? TransformNode(IBase2Ast node)
        {
            var replacement = this.TransformNodeCore(node);
            this.CarryCheckedTypeToReplacement(node, replacement);
            return replacement;
        }

        private IBase2Ast? TransformNodeCore(IBase2Ast node)
        {
            // `): parent(args)` lives in a grammar addon, which TransformTree does not walk.
            // Transform it while the constructor is still the current function frame.
            if (node is PhpMethodDeclAst ctor
                && ctor.AstGrammarAddons.TryGetValue("ctorReturnType", out var ctorAddon)
                && ctorAddon is TyhpCtorReturnTypeAst { Arguments: { } ctorArgs } ctorReturn)
            {
                ctorReturn.ReplaceChild(ctorArgs, AstWalker.TransformTree(ctorArgs, this.TransformNode, this.PreTransformWith));
            }

            if (IsFunctionLike(node)
                && this._functionStack.Count > 0
                && ReferenceEquals(this._functionStack.Peek(), node))
            {
                this._functionStack.Pop();
            }

            if (node is PhpObjectTypeDeclAst && this._classStack.Count > 0)
            {
                this._classStack.Pop();
            }

            if (node is TyhpTypeofAst or TyhpTypeAliasAst && this._typeofTypeExprDepth > 0)
            {
                this._typeofTypeExprDepth--;
            }

            switch (node)
            {
                case PhpMagicConstantAst magic:
                    return this.TransformMagicConstant(magic);
                case TokenValueAst token when this.IsTyhpMagicConstant(token):
                    return this.TransformMagicConstantFromToken(token);
                case PhpNameAst name:
                    return this.TransformTyhpdefAliasName(name);
                case PhpBuiltinTypeAst builtin:
                    return this.TransformBuiltinType(builtin);
                case PhpNamedTypeAst namedType:
                    return this.TransformNamedType(namedType);
                case PhpDereferenceableAst dereferenceable:
                    return this.TransformDereferenceable(dereferenceable);
                case PhpBinaryOpAst binary:
                    return this.TransformBinaryOperator(binary);
                case PhpNewAst newExpr:
                    return this.TransformStructNew(newExpr);
                case TyhpTypedVarExprAst typedVar:
                    return this.TransformTypedVarExpressionCapture(typedVar);
                case PhpJumpStatementAst jump when jump.JumpType == PhpJumpType.Return:
                    return this.TransformReturnJump(jump);
                case PhpReturnStatementAst returnStmt:
                    return this.TransformReturnStatement(returnStmt);
                case PhpUnaryOpAst unary:
                {
                    var clone = this.TransformStructClone(unary);
                    if (clone != null && !ReferenceEquals(clone, unary))
                    {
                        return clone;
                    }

                    if (this.TryRewriteUnaryOperatorOverload(unary, out var rewritten))
                    {
                        return rewritten;
                    }

                    if (this.TryRewriteCastConversion(unary, out var castRewritten))
                    {
                        return castRewritten;
                    }

                    return unary;
                }
                case PhpEmptyStatementAst empty:
                {
                    if (this.TryRewriteEmptyOperatorOverload(empty, out var emptyRewritten))
                    {
                        return emptyRewritten;
                    }

                    return empty;
                }
                default:
                    return null;
            }
        }

        private IBase2Ast TransformMagicConstant(PhpMagicConstantAst magic)
        {
            var name = magic.ValueString ?? "";
            if (string.Equals(name, "__TYHP_LINE__", StringComparison.Ordinal))
            {
                return PhpScalarAst.CreateIntegerFromContext(magic, magic.Line > 0 ? magic.Line : 0);
            }

            if (string.Equals(name, "__TYHP_FILE__", StringComparison.Ordinal))
            {
                var filePath = magic.OwningFile?.Identifier
                    ?? this._context.CurrentSourceFile?.Identifier
                    ?? "";
                return PhpScalarAst.CreateStringFromContext(magic, filePath);
            }

            return magic;
        }

        private bool IsTyhpMagicConstant(TokenValueAst token)
        {
            var name = token.ValueString ?? "";
            return string.Equals(name, "__TYHP_LINE__", StringComparison.Ordinal)
                || string.Equals(name, "__TYHP_FILE__", StringComparison.Ordinal);
        }

        private IBase2Ast TransformMagicConstantFromToken(TokenValueAst token)
        {
            if (string.Equals(token.ValueString, "__TYHP_LINE__", StringComparison.Ordinal))
            {
                return PhpScalarAst.CreateIntegerFromContext(token, token.Line > 0 ? token.Line : 0);
            }

            var filePath = token.OwningFile?.Identifier
                ?? this._context.CurrentSourceFile?.Identifier
                ?? "";
            return PhpScalarAst.CreateStringFromContext(token, filePath);
        }

        private void CollectProtectedMemberName(IBase2Ast node)
        {
            switch (node)
            {
                case PhpInstanceMemberAccessAst instance when instance.MemberName is PhpNameAst instanceName:
                    this._protectedMemberNames.Add(instanceName);
                    break;
                case PhpStaticMemberAccessAst staticAccess when staticAccess.Member is PhpNameAst staticName:
                    this._protectedMemberNames.Add(staticName);
                    break;
                case PhpClassConstantAccessAst constant when constant.Member is PhpNameAst constantName:
                    // `Type::name` (methods and class constants) is this node, not
                    // PhpStaticMemberAccessAst. Protect it so `Route::view` is not
                    // rewritten to the imported class `View`.
                    this._protectedMemberNames.Add(constantName);
                    break;
                case PhpMemberAccessAst memberAccess when memberAccess.Key is PhpNameAst keyName:
                    this._protectedMemberNames.Add(keyName);
                    break;
            }
        }

        private IBase2Ast TransformTyhpdefAliasName(PhpNameAst name)
        {
            if (ShouldKeepSourceTypeAliasFactorySpelling(name))
            {
                return name;
            }

            var text = name.ValueString ?? "";

            // Member positions (`->name`, `::name`, `$arr->key`) are protected from the free-name alias map
            // so a class alias like `Promise` cannot rewrite `$this->promise` or `Route::view`. They still erase
            // through the dedicated member-alias map (tyhpdef `function php_name as tyhpName`).
            if (this._protectedMemberNames.Contains(name))
            {
                if (this._context.TyhpdefMemberAliasMap.TryGetValue(text, out var memberResolved)
                    || (text.StartsWith('$')
                        && this._context.TyhpdefMemberAliasMap.TryGetValue(text.TrimStart('$'), out memberResolved)))
                {
                    var memberReplacement = PhpNameAst.CreateFromContext(memberResolved, name);
                    memberReplacement.BoundSymbol = name.BoundSymbol;
                    CopyGrammarAddons(name, memberReplacement);
                    return memberReplacement;
                }

                return name;
            }

            if (!TryResolveTyhpdefAlias(text, out var resolved))
            {
                return name;
            }

            // A `use function Ns\fn;` import maps `fn` to `Ns\fn`, which is already
            // namespace-qualified. Left relative, emit would resolve it against the current
            // namespace (`App\E\Ns\fn`), so anchor it at the root.
            if (name.BoundSymbol is FunctionDeclarationSymbol or ConstantSymbol
                && resolved.Contains('\\')
                && !resolved.StartsWith('\\'))
            {
                resolved = "\\" + resolved;
            }

            // Preserve TyhpGenericIdentifierAst (and its type-argument children) so later
            // GenericObject property registration still sees `\Closure<bool>` etc.
            if (name is TyhpGenericIdentifierAst generic)
            {
                var preserved = TyhpGenericIdentifierAst.CreateFromContext(
                    resolved,
                    generic.GenericArguments,
                    generic);
                preserved.BoundSymbol = generic.BoundSymbol;
                CopyGrammarAddons(name, preserved);
                return preserved;
            }

            // PhpNameAst type args ride on AstGrammarAddons ("identifier" / "typeName"), e.g.
            // `new PropertyAccessor<T>()` after a tyhpdef `use Tyhp\PropertyAccessor`. Dropping
            // them makes Mechanism C factories emit null type arguments.
            var replacement = PhpNameAst.CreateFromContext(resolved, name);
            replacement.BoundSymbol = name.BoundSymbol;
            CopyGrammarAddons(name, replacement);
            return replacement;
        }

        /// <summary>
        /// Copies grammar addons (generic type-argument lists, etc.) onto a replacement name node
        /// produced by tyhpdef alias rewriting.
        /// </summary>
        private static void CopyGrammarAddons(IBase2Ast source, IBase2Ast target)
        {
            foreach (var (key, addon) in source.AstGrammarAddons)
            {
                target.AddGrammarAddon(key, addon);
            }
        }

        /// <summary>
        /// Looks up <paramref name="text"/> in <see cref="EmitContext.TyhpdefAliasMap"/>, trying
        /// both the raw spelling and a leading-<c>\</c>-stripped form. When the source name was
        /// root-anchored, the resolved PHP name is re-anchored so <c>\alias</c> emits as
        /// <c>\original</c>.
        /// </summary>
        private bool TryResolveTyhpdefAlias(string text, out string resolved)
        {
            resolved = "";
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (this._context.TyhpdefAliasMap.TryGetValue(text, out var mapped))
            {
                resolved = mapped;
                return true;
            }

            var rooted = text.StartsWith('\\');
            var bare = rooted ? text.TrimStart('\\') : text;
            if (rooted
                && !string.IsNullOrEmpty(bare)
                && this._context.TyhpdefAliasMap.TryGetValue(bare, out mapped))
            {
                resolved = mapped.StartsWith('\\') ? mapped : "\\" + mapped;
                return true;
            }

            return false;
        }

        /// <summary>
        /// A class-kind <c>use App\Types\UserId</c> must not rewrite <c>UserId</c> to a relative
        /// class FQN. Value-position alias calls lower to <c>\Tyhp\Type::ref</c> in the
        /// expression emitter; this guard only preserves a leftover short name.
        /// </summary>
        private bool ShouldKeepSourceTypeAliasFactorySpelling(PhpNameAst name)
        {
            if (name.BoundSymbol is TypeAliasSymbol alias
                && CheckerHelpers.IsSourceTypeAliasFactory(alias))
            {
                return true;
            }

            if (name.BoundSymbol is UseIncludeSymbol use)
            {
                var segments = use.ImportedNameSegments;
                if (segments is { Length: > 0 }
                    && this._nameResolver.ResolveQualifiedName(segments) is TypeAliasSymbol imported
                    && CheckerHelpers.IsSourceTypeAliasFactory(imported))
                {
                    return true;
                }
            }

            return false;
        }

        private IBase2Ast TransformBuiltinType(PhpBuiltinTypeAst builtin)
        {
            var typeName = builtin.Identifier ?? "";
            if (this._context.TypeAliasMap.TryGetValue(typeName, out var aliased))
            {
                return PhpNameAst.CreateFromContext(aliased, builtin);
            }

            return builtin;
        }

        private IBase2Ast TransformNamedType(PhpNamedTypeAst namedType)
        {
            if (StructEmissionHelper.ResolveStructFromNamedType(namedType, this._context.GlobalScope) is not null)
            {
                if (this._typeofTypeExprDepth > 0)
                {
                    return namedType;
                }

                return PhpBuiltinTypeAst.Create("array", namedType);
            }

            if (namedType.Name is PhpNameAst name)
            {
                var transformed = this.TransformTyhpdefAliasName(name);
                if (!ReferenceEquals(transformed, name) && namedType is Base2Ast baseNode)
                {
                    baseNode.ReplaceChildAt(0, transformed);
                }
            }

            return namedType;
        }

        private IBase2Ast TransformDereferenceable(PhpDereferenceableAst dereferenceable)
        {
            // A first-class callable the checker planned a wrapper for: its parameters accept
            // implicitly convertible types, and an extension-method callable has no native form.
            if (this.TryRewriteCallableWrapper(dereferenceable, out var callableWrapper))
            {
                return callableWrapper;
            }

            if (this.TryRewriteExtensionMethodCall(dereferenceable, out var extensionCall))
            {
                // Extension rewrite prepends the receiver and stashes the method BoundSymbol —
                // apply implicit convert against the final static call's parameter list.
                if (extensionCall is PhpDereferenceableAst rewrittenCall)
                {
                    this.TryRewriteCallArgumentConverts(rewrittenCall);
                }
                else if (extensionCall is PhpDereferenceableExpressionAst
                    {
                        Expression: PhpTernaryOpAst { FalseExpr: PhpDereferenceableAst nullSafeCall }
                    })
                {
                    this.TryRewriteCallArgumentConverts(nullSafeCall);
                }

                return extensionCall;
            }

            if (this.TryRewriteStructPropertyAccess(dereferenceable, out var arrayAccess))
            {
                return arrayAccess;
            }

            // PHP's `?->` short-circuits the *entire remainder* of the chain, including plain
            // `->` hops that follow it (e.g. `$s?->asMoney()->display()` never calls `display()`
            // when `$s` is null). Our null-safe extension rewrite replaces `$s?->asMoney()` with a
            // `(...) === null ? null : ...` ternary that is no longer part of a native PHP chain,
            // so a subsequent plain `->member` on that result would otherwise call the member on a
            // possibly-null value (fatal error) instead of short-circuiting. Upgrade that hop's
            // accessor to `?->` so PHP's own chain semantics take over from here.
            this.UpgradeAccessorForNullSafeTaintedReceiver(dereferenceable);

            this.TryRewriteCallArgumentConverts(dereferenceable);
            return dereferenceable;
        }

        private IBase2Ast TransformReturnJump(PhpJumpStatementAst returnJump)
        {
            if (returnJump.Expression is not IExpression expr
                || returnJump is not Base2Ast returnNode)
            {
                return returnJump;
            }

            var expected = this.GetEnclosingFunctionReturnType();
            var selfContext = this._classStack.Count > 0 ? this._classStack.Peek() : null;
            if (this.TryRewriteImplicitConvert(expr, expected, selfContext, returnJump, out var rewritten)
                && rewritten is IBase2Ast rewrittenAst)
            {
                returnNode.ReplaceChild(expr, rewrittenAst);
            }

            return returnJump;
        }

        private IBase2Ast TransformReturnStatement(PhpReturnStatementAst returnStmt)
        {
            if (returnStmt.Expression is not IExpression expr
                || returnStmt is not Base2Ast returnNode)
            {
                return returnStmt;
            }

            var expected = this.GetEnclosingFunctionReturnType();
            var selfContext = this._classStack.Count > 0 ? this._classStack.Peek() : null;
            if (this.TryRewriteImplicitConvert(expr, expected, selfContext, returnStmt, out var rewritten)
                && rewritten is IBase2Ast rewrittenAst)
            {
                returnNode.ReplaceChild(expr, rewrittenAst);
            }

            return returnStmt;
        }

        /// <summary>
        /// If <paramref name="dereferenceable"/> is a plain (non-null-safe) member access whose
        /// receiver is the result of a null-safe extension rewrite (see
        /// <see cref="BuildNullSafeExtensionCall"/>), mutates its accessor token in place to
        /// <c>?-&gt;</c> so the hop keeps participating in PHP's null-safe short-circuit chain.
        /// </summary>
        private void UpgradeAccessorForNullSafeTaintedReceiver(PhpDereferenceableAst dereferenceable)
        {
            if (dereferenceable.Suffix is not PhpInstanceMemberAccessAst memberAccess
                || memberAccess is not Base2Ast memberAccessNode)
            {
                return;
            }

            if (memberAccess.Accessor?.ValueInt64 == TyhpParser.T_NULLSAFE_OBJECT_OPERATOR)
            {
                return;
            }

            if (!IsNullSafeRewriteResult(dereferenceable.Base as IExpression))
            {
                return;
            }

            var nullSafeAccessor = TokenValueAst.CreateFromContext(
                "?->", TyhpParser.T_NULLSAFE_OBJECT_OPERATOR, memberAccessNode);
            memberAccessNode.ReplaceChildAt(0, nullSafeAccessor);
        }

        /// <summary>
        /// True when <paramref name="expression"/> is the synthetic
        /// <c>($__recv = …) === null ? null : …</c> wrap produced by
        /// <see cref="BuildNullSafeExtensionCall"/> for an earlier null-safe (<c>?-&gt;</c>)
        /// extension hop in this same chain — i.e. this expression may evaluate to <c>null</c>.
        /// <see cref="ObjectMethodSymbol"/> is stashed as <c>BoundSymbol</c> only by this
        /// converter's own null-safe extension rewrite (never by the binder/checker), and only on
        /// a single <see cref="PhpDereferenceableExpressionAst"/> layer, so this check cannot
        /// misfire on a genuine source-level parenthesized ternary and deliberately does not match
        /// through an explicit *user* grouping, e.g. <c>($v?-&gt;a())-&gt;b()</c> — parenthesizing
        /// a nullsafe result is the user opting out of PHP's automatic chain short-circuit, so
        /// <c>-&gt;b()</c> should fail on null exactly like native PHP would.
        /// </summary>
        private static bool IsNullSafeRewriteResult(IExpression? expression)
            => expression is PhpDereferenceableExpressionAst { Expression: PhpTernaryOpAst { BoundSymbol: ObjectMethodSymbol } };

        private IBase2Ast TransformBinaryOperator(PhpBinaryOpAst binary)
        {
            if (this.TryRewriteOperatorOverload(binary, out var rewritten))
            {
                return rewritten;
            }

            return binary;
        }

        private IBase2Ast? TransformStructNew(PhpNewAst newExpr)
        {
            if (StructEmissionHelper.TryRewriteStructNew(
                newExpr,
                this.ResolveStructFromNew,
                this.TransformStructDefaultValue,
                out var rewritten))
            {
                return rewritten;
            }

            // Not a struct (or the struct rewrite did not apply) — `new Type($arg)` is otherwise
            // left untouched by the walk, so implicit-convert matching against the constructor's
            // declared parameters needs its own hook here.
            this.TryRewriteConstructorArgumentConverts(newExpr);
            return null;
        }

        private IBase2Ast? TransformStructClone(PhpUnaryOpAst unary)
        {
            if (StructEmissionHelper.TryRewriteStructClone(
                unary,
                this.ResolveStructType,
                this.ResolveAnonymousStructShape,
                out var rewritten))
            {
                return rewritten;
            }

            return null;
        }

        private int _structDefaultDepth;

        /// <summary>
        /// Struct declaration defaults live on the symbol and are never walked in place, so a
        /// default such as <c>Inner $inner = new Inner();</c> (or <c>new Inner() with [...]</c>)
        /// must be rewritten here to fold into Inner's own defaults array. Other default values
        /// are copied through unchanged.
        /// </summary>
        private IExpression TransformStructDefaultValue(IExpression value)
        {
            var isStructConstruction = value switch
            {
                PhpNewAst newExpr => this.ResolveStructFromNew(newExpr) is not null,
                PhpBinaryOpAst binary when StructEmissionHelper.IsWithOperator(binary.Operator)
                    => this.IsStructWithLeft(binary.Left),
                _ => false,
            };

            // A struct whose default constructs itself would recurse forever; leave it unfolded.
            if (!isStructConstruction || value is not IBase2Ast node || this._structDefaultDepth >= 32)
            {
                return value;
            }

            this._structDefaultDepth++;
            try
            {
                return AstWalker.TransformTree(node, this.TransformNode, this.PreTransformWith) as IExpression
                    ?? value;
            }
            finally
            {
                this._structDefaultDepth--;
            }
        }

        /// <summary>
        /// The checker's nameless struct shape for <paramref name="expression"/> when its type is
        /// an anonymous <c>struct { … }</c> (type position), unwrapping nullable wrappers.
        /// Null for named structs, classes, and untyped / unresolved expressions.
        /// </summary>
        private StructCheckedType? ResolveAnonymousStructShape(IExpression? expression)
        {
            if (expression is not IBase2Ast node
                || !this.TryGetCheckedExpressionType(node, out var checkedType))
            {
                return null;
            }

            while (checkedType is NullableCheckedType nullable)
            {
                checkedType = nullable.InnerType;
            }

            return checkedType as StructCheckedType;
        }

        private ObjectDeclarationSymbol? ResolveStructFromNew(PhpNewAst newExpr)
        {
            if (newExpr.ClassName is PhpNameAst name
                && name.BoundSymbol is ObjectDeclarationSymbol bound
                && bound.IsStruct)
            {
                return bound;
            }

            var className = GetClassNameReferenceText(newExpr.ClassName);
            if (string.IsNullOrWhiteSpace(className))
            {
                return null;
            }

            if (className.Contains('\\'))
            {
                var segments = className.TrimStart('\\').Split('\\');
                var nested = this._nameResolver.TryResolveObjectTypeAlias(
                    segments,
                    this._context.GlobalScope,
                    isFullyQualified: className.StartsWith('\\'));
                if (nested is ObjectDeclarationSymbol { IsStruct: true } nestedStruct)
                {
                    return nestedStruct;
                }

                if (this._nameResolver.ResolveRelativeName(segments, this._context.GlobalScope)
                    is ObjectDeclarationSymbol qualified
                    && qualified.IsStruct)
                {
                    return qualified;
                }
            }

            var simpleName = className.TrimStart('\\').Split('\\')[^1];
            return StructEmissionHelper.FindStructSymbol(this._context.GlobalScope, simpleName);
        }

        private ObjectDeclarationSymbol? ResolveStructType(IExpression? expression)
        {
            // A checker recovery (`Unresolved`) binding never satisfies `TryGetObjectDeclaration`'s
            // `IsStruct` check, so `TryGetStructFromCheckedExpression` already returns null for it
            // without a separate guard here — one is not added, because gating this whole method on
            // the checker type would also skip the `LookupStructTypedVariable` map lookup below,
            // which is the load-bearing source of truth for a variable whose checker binding-node
            // type came back `Unresolved` for reasons unrelated to its real (parameter-declared /
            // foreach-peeled) struct type (Story 21.12 Workstream C review).
            if (this.TryGetStructFromCheckedExpression(expression) is { } fromChecked)
            {
                return fromChecked;
            }

            if (expression is PhpVariableAst variable)
            {
                var varName = CheckerHelpers.GetVariableName(variable)
                    ?? NormalizeVariableName(
                        variable.VariableToken?.ValueString
                        ?? variable.Identifier);
                if (varName is not null
                    && this.LookupStructTypedVariable(varName) is { } fromMap)
                {
                    return fromMap;
                }
            }

            return StructEmissionHelper.ResolveStructTypeFromExpression(
                expression,
                this._context.GlobalScope);
        }

        private static string? GetClassNameReferenceText(IClassNameReference? className) =>
            className switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                IClassName reference => reference.Identifier,
                _ => null,
            };

        private bool TryRewriteExtensionMethodCall(PhpDereferenceableAst callNode, out IBase2Ast rewritten)
        {
            rewritten = callNode;

            if (callNode.Suffix is not PhpCallAst call)
            {
                return false;
            }

            if (callNode.Base is not PhpDereferenceableAst memberNode
                || memberNode.Suffix is not PhpInstanceMemberAccessAst memberAccess)
            {
                return false;
            }

            var methodName = this.GetMemberName(memberAccess.MemberName);
            if (string.IsNullOrWhiteSpace(methodName))
            {
                return false;
            }

            if (this.IsUnresolvedCheckedReceiver(memberNode.Base as IExpression))
            {
                return false;
            }

            var methodSymbol = this.ResolveExtensionMethodSymbol(callNode, memberNode, memberAccess, methodName);
            if (methodSymbol is null)
            {
                return false;
            }

            var extensionClass = this.GetOwningExtensionClass(methodSymbol);
            if (extensionClass == null || !extensionClass.IsExtension)
            {
                return false;
            }

            // `$v?->ext($args)` must short-circuit when `$v` is null: splicing the body in place
            // would evaluate it unconditionally and drop the null check entirely. Only attempt the
            // splice for a plain `->` receiver; a null-safe call always falls through to
            // BuildNullSafeExtensionCall below.
            var isNullSafe = memberAccess.Accessor?.ValueInt64 == TyhpParser.T_NULLSAFE_OBJECT_OPERATOR;
            if (!isNullSafe
                && this.TrySpliceExtensionCall(
                    callNode,
                    memberNode.Base as IExpression,
                    call.Arguments,
                    methodSymbol,
                    extensionClass,
                    out rewritten))
            {
                return true;
            }

            // Tyhpdef thin mappings have no PHP backer. Do not emit `__TyhpInlineExt_*::method`.
            if (extensionClass.IsCompilerGenerated)
            {
                return false;
            }

            var extensionFqn = this.FormatEmittedClassFqn(
                extensionClass.FullyQualifiedName,
                extensionClass.Name);

            this.EnsureImport(extensionFqn);

            if (isNullSafe)
            {
                // `$v?->ext($args)` must short-circuit when `$v` is null. Extract the receiver into
                // a temp so side-effecting receivers (e.g. `$a->b()?->ext()`) evaluate once:
                // `($__recv = <receiver>) === null ? null : \Ext::ext($__recv, $args)`.
                rewritten = this.BuildNullSafeExtensionCall(
                    memberNode.Base as IExpression,
                    extensionFqn,
                    methodName,
                    call.Arguments,
                    callNode,
                    methodSymbol,
                    memberAccess.MemberName);
                return true;
            }

            var args = this.BuildReceiverFirstArguments(memberNode.Base as IExpression, call.Arguments, callNode);
            var staticCall = this.BuildStaticCall(extensionFqn, methodName, args, callNode);
            CopyCallSiteTypeArgumentAddons(memberAccess.MemberName, FindStaticCalleeName(staticCall));
            // Stash the extension method so a later hop in `$v->a()->b()` can resolve `a`'s return
            // type after this node has been rewritten to a static call. Also used by Mechanism D
            // emit when the rewritten PhpCallAst is not in GenericCallTargets.
            staticCall.BoundSymbol = methodSymbol;
            rewritten = staticCall;
            return true;
        }

        /// <summary>
        /// Builds <c>($__recv = &lt;receiver&gt;) === null ? null : \Ext::method($__recv, …)</c>
        /// for null-safe extension calls, preserving PHP <c>?-&gt;</c> short-circuit semantics.
        /// Wrapped in <see cref="PhpDereferenceableExpressionAst"/> so a later chain hop can still
        /// use the result as a dereferenceable base (ternaries alone are not
        /// <see cref="IDereferenceableBase"/>).
        /// </summary>
        private PhpDereferenceableExpressionAst BuildNullSafeExtensionCall(
            IExpression? receiver,
            string extensionFqn,
            string methodName,
            PhpArgumentListAst? originalArgs,
            Base2Ast context,
            ObjectMethodSymbol methodSymbol,
            IBase2Ast? typeArgSource = null)
        {
            var tempName = this._context.GenerateUniqueVarName("__recv");
            var tempVar = PhpVariableAst.CreateFromContext(tempName, context);
            var bindTemp = WithKeywordHelper.CreateAssignment(
                tempVar,
                receiver ?? PhpNameAst.CreateFromContext("null", context),
                context);

            var identicalOp = TokenValueAst.CreateFromContext(
                "===", TyhpParser.T_IS_IDENTICAL, context);
            var nullForCompare = PhpNameAst.CreateFromContext("null", context);
            var isNull = PhpBinaryOpAst.CreateFromContext(identicalOp, bindTemp, nullForCompare, context);

            var args = this.BuildReceiverFirstArguments(tempVar, originalArgs, context);
            var staticCall = this.BuildStaticCall(extensionFqn, methodName, args, context);
            CopyCallSiteTypeArgumentAddons(typeArgSource, FindStaticCalleeName(staticCall));
            staticCall.BoundSymbol = methodSymbol;

            var question = TokenValueAst.CreateFromContext("?", TyhpParser.T_SYM_QUESTION, context);
            var colon = TokenValueAst.CreateFromContext(":", TyhpParser.T_SYM_COLON, context);
            var nullResult = PhpNameAst.CreateFromContext("null", context);
            var ternary = PhpTernaryOpAst.CreateFromContext(
                question,
                colon,
                isNull,
                nullResult,
                staticCall,
                context);
            ternary.BoundSymbol = methodSymbol;

            // Parenthesize so the rewrite remains a valid dereferenceable base for chain hops
            // like `$v?->a()?->b()` (PhpTernaryOpAst alone is not IDereferenceableBase).
            var wrapped = PhpDereferenceableExpressionAst.CreateFromContext(ternary, context);
            wrapped.BoundSymbol = methodSymbol;
            return wrapped;
        }

        /// <summary>
        /// Resolves the extension <see cref="ObjectMethodSymbol"/> for a <c>$recv->method(...)</c>
        /// call. Prefers an already-bound extension symbol when present; otherwise resolves from
        /// the receiver's type (class, scalar builtin, or nullable unwrap).
        /// </summary>
        private ObjectMethodSymbol? ResolveExtensionMethodSymbol(
            PhpDereferenceableAst callNode,
            PhpDereferenceableAst memberNode,
            PhpInstanceMemberAccessAst memberAccess,
            string methodName)
        {
            var receiverType = this.ResolveReceiverType(memberNode.Base as IExpression);
            var callSite = this._nameResolver.FindCallSiteScope(callNode, lexicalScope: null);
            // A file-local or global `hide` keeps the call as an instance call. Visible siblings
            // still rewrite.
            if (this.IsExtensionMemberHidden(receiverType, callSite, methodName))
            {
                return null;
            }

            if (EmitHelpers.IsExtensionMethodCall(callNode, this._context)
                && callNode.BoundSymbol is ObjectMethodSymbol callBound)
            {
                return callBound;
            }

            if (EmitHelpers.IsExtensionMethodCall(memberNode, this._context)
                && memberNode.BoundSymbol is ObjectMethodSymbol memberBound)
            {
                return memberBound;
            }

            if (EmitHelpers.IsExtensionMethodCall(memberAccess, this._context)
                && memberAccess.BoundSymbol is ObjectMethodSymbol accessBound)
            {
                return accessBound;
            }

            if (receiverType is null)
            {
                return null;
            }

            var resolved = this._nameResolver.ResolveExtensionMethod(
                    methodName,
                    receiverType,
                    callSite)
                as ObjectMethodSymbol;
            if (resolved != null)
            {
                return resolved;
            }

            // Block-target methods carry the receiver on the extension/group symbol. The
            // synthesized `$this` parameter's type AST does not always resolve from the
            // global scope used by the method index, so match the bound target symbol.
            return this.ResolveBlockTargetExtensionMethod(methodName, receiverType);
        }

        /// <summary>
        /// File-local and global <c>use extension</c> <c>hide</c>, plus hides recorded on the
        /// receiver type. Matches binder lookup: the key is the method name, and a file hide
        /// lives on <see cref="FileScope"/> even when the call site scope is a namespace block.
        /// </summary>
        private bool IsExtensionMemberHidden(IBaseSymbol? onType, IBaseScope fromScope, string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            if (onType is ObjectDeclarationSymbol receiver
                && receiver.ExtensionUseHiddenMembers != null
                && receiver.ExtensionUseHiddenMembers.Contains(methodName))
            {
                return true;
            }

            for (var scope = fromScope; scope != null; scope = scope.ParentScope)
            {
                if (scope is FileScope file
                    && file.ExtensionUseHiddenMembers != null
                    && file.ExtensionUseHiddenMembers.Contains(methodName))
                {
                    return true;
                }

                if (scope.DeclarationSymbol is NamespaceBlockSymbol ns
                    && ns.OwningFileScope?.ExtensionUseHiddenMembers != null
                    && ns.OwningFileScope.ExtensionUseHiddenMembers.Contains(methodName))
                {
                    return true;
                }
            }

            return this._context.GlobalScope.ExtensionUseHiddenMembers != null
                && this._context.GlobalScope.ExtensionUseHiddenMembers.Contains(methodName);
        }

        private ObjectMethodSymbol? ResolveBlockTargetExtensionMethod(string methodName, IBaseSymbol receiverType)
        {
            if (!this._context.GetSymbolTree().ExtensionMethodIndex.TryGetValue(methodName, out var candidates))
            {
                return null;
            }

            ObjectMethodSymbol? match = null;
            foreach (var candidate in candidates)
            {
                if (!ExtensionBlockTargetMatches(candidate, receiverType))
                {
                    continue;
                }

                match = candidate;
                break;
            }

            return match;
        }

        private static bool ExtensionBlockTargetMatches(ObjectMethodSymbol method, IBaseSymbol receiverType)
        {
            for (var scope = method.ContainingScope; scope != null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is not ObjectDeclarationSymbol block
                    || block.ExtensionBlockTargetSymbol is not { } target)
                {
                    continue;
                }

                if (ReferenceEquals(target, receiverType))
                {
                    return true;
                }

                if (target is BuiltInTypeSymbol builtin
                    && receiverType is BuiltInTypeSymbol receiverBuiltin
                    && string.Equals(builtin.Name, receiverBuiltin.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(target.FullyQualifiedName)
                    && string.Equals(
                        target.FullyQualifiedName.TrimStart('\\'),
                        receiverType.FullyQualifiedName?.TrimStart('\\'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }

            return false;
        }

        /// <summary>
        /// Records the member-access chains that appear where PHP needs an lvalue or an
        /// existence-tolerant operand (assignment targets, <c>isset</c>/<c>unset</c>/<c>empty</c>
        /// operands, the left of <c>??</c>, <c>++</c>/<c>--</c>, <c>&amp;</c>). A struct read in
        /// those positions erases to a bare <c>$s['key']</c>; every other read of an optional
        /// property gets a <c>?? null</c> guard (see <see cref="TryRewriteStructPropertyAccess"/>).
        /// Runs from the pre-transform so the original nodes are marked before children are rewritten.
        /// </summary>
        private void MarkStructReadExemptions(IBase2Ast node)
        {
            switch (node)
            {
                case PhpBinaryOpAst binary
                    when SpliceAst.IsAssignmentOperator(binary.Operator) || IsCoalesceOperator(binary.Operator):
                    this.MarkStructReadExempt(binary.Left);
                    break;
                case PhpIssetStatementAst isset:
                    foreach (var variable in isset.Variables?.GetAllNotNull() ?? [])
                    {
                        this.MarkStructReadExempt(variable);
                    }

                    break;
                case PhpEmptyStatementAst empty:
                    this.MarkStructReadExempt(empty.Expression);
                    break;
                case PhpUnsetStatementAst unset:
                    foreach (var variable in unset.Variables?.GetAllNotNull() ?? [])
                    {
                        this.MarkStructReadExempt(variable);
                    }

                    break;
                case PhpUnaryOpAst unary when IsLvalueUnaryOperator(unary.Operator):
                    this.MarkStructReadExempt(unary.Operand);
                    break;
            }
        }

        private void MarkStructReadExempt(object? expression)
        {
            while (expression is PhpDereferenceableAst dereferenceable)
            {
                this._structReadExempt.Add(dereferenceable);
                expression = dereferenceable.Base;
            }
        }

        private static bool IsCoalesceOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(op.ValueString)
                ? op.ValueString == "??"
                : (int)(op.ValueInt64 ?? -1) == TyhpParser.T_COALESCE;
        }

        private static bool IsLvalueUnaryOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(op.ValueString))
            {
                return op.ValueString is "++" or "--" or "&";
            }

            return (int)(op.ValueInt64 ?? -1) is TyhpParser.T_INC
                or TyhpParser.T_DEC
                or TyhpParser.T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG;
        }

        /// <summary>
        /// <c>$s['key'] ?? null</c> (parenthesized) for a read of an optional struct property. A
        /// nullable property without a default is left out of <c>new Struct()</c>, so the bare
        /// array read would raise "Undefined array key".
        /// </summary>
        private IBase2Ast GuardOptionalStructRead(PhpDereferenceableAst original, IBase2Ast read)
        {
            var coalesce = PhpBinaryOpAst.CreateFromContext(
                TokenValueAst.CreateFromContext("??", TyhpParser.T_COALESCE, original),
                (IExpression)read,
                PhpNameAst.CreateFromContext("null", original),
                original);
            return PhpDereferenceableExpressionAst.CreateFromContext(coalesce, original);
        }

        private bool IsOptionalStructRead(PhpDereferenceableAst accessNode, ObjectPropertySymbol property)
        {
            if (property.DefaultValue is not null || this._structReadExempt.Contains(accessNode))
            {
                return false;
            }

            return this.TryGetCheckedExpressionType(accessNode, out var checkedType) && checkedType.IsNullable;
        }

        private bool TryRewriteStructPropertyAccess(PhpDereferenceableAst accessNode, out IBase2Ast rewritten)
        {
            rewritten = accessNode;

            if (accessNode.Suffix is not PhpInstanceMemberAccessAst memberAccess)
            {
                return false;
            }

            // A memoized Unresolved receiver must not rewrite `->` to `['key']` even if
            // `LookupStructTypedVariable` still has a foreach-peeled struct for the name.
            // Missing ExpressionTypes still falls through to that map (well-typed foreach).
            if (this.IsUnresolvedCheckedReceiver(accessNode.Base as IExpression))
            {
                return false;
            }

            var structDecl = this.ResolveStructType(accessNode.Base as IExpression);
            if (structDecl is null)
            {
                return this.TryRewriteAnonymousStructPropertyAccess(accessNode, memberAccess, out rewritten);
            }

            var memberName = this.GetMemberName(memberAccess.MemberName);
            // Only declared properties erase to `['key']`. Methods / extensions stay as
            // `->name` so TryRewriteExtensionMethodCall can rewrite the enclosing
            // `$recv->name()` call (children are transformed first).
            if (!StructEmissionHelper.TryResolveStructProperty(structDecl, memberName, out var property))
            {
                return false;
            }

            var keyExpr = StructEmissionHelper.GetStructArrayKey(property!).ToScalarAst(accessNode);
            var rewrittenNode = PhpDereferenceableAst.CreateFromContext(
                accessNode.Base!,
                PhpArrayAccessAst.CreateFromContext(keyExpr, accessNode),
                accessNode);

            // Children transform bottom-up, so `accessNode.Base` here is already the erased
            // form (e.g. `$o['inner']`), which has no checked type recorded and no syntactic
            // shape `ResolveStructType` can walk back to the struct. Stash the property's own
            // struct type (when it is one) on the rewritten node's BoundSymbol so a further
            // chain hop (`$o->inner->x` → `$o['inner']->x`) still resolves the correct struct
            // (`Inner`, not `Outer`) instead of silently staying `->x` on an array.
            rewrittenNode.BoundSymbol = this.ResolveStructTypeFromTypeExpression(property!.DeclaredType);

            rewritten = this.IsOptionalStructRead(accessNode, property)
                ? this.GuardOptionalStructRead(accessNode, rewrittenNode)
                : rewrittenNode;
            return true;
        }

        /// <summary>
        /// <c>$c-&gt;lat</c> on a receiver whose checker type is an anonymous
        /// <c>struct { … }</c> erases to <c>$c['lat']</c> exactly like a named struct. Writes,
        /// <c>isset</c>, and <c>??</c> reuse the rewritten read node.
        /// </summary>
        private bool TryRewriteAnonymousStructPropertyAccess(
            PhpDereferenceableAst accessNode,
            PhpInstanceMemberAccessAst memberAccess,
            out IBase2Ast rewritten)
        {
            rewritten = accessNode;

            if (this.ResolveAnonymousStructShape(accessNode.Base as IExpression) is not { } shape)
            {
                return false;
            }

            var memberName = this.GetMemberName(memberAccess.MemberName);
            if (memberName is null
                || StructEmissionHelper.ResolveAnonymousStructKey(shape, memberName) is not { } key)
            {
                return false;
            }

            var anonymousRead = PhpDereferenceableAst.CreateFromContext(
                accessNode.Base!,
                PhpArrayAccessAst.CreateFromContext(key.ToScalarAst(accessNode), accessNode),
                accessNode);
            var lookupKey = memberName.StartsWith('$') ? memberName : "$" + memberName;
            var isOptionalProperty = shape.Properties.TryGetValue(lookupKey, out var info)
                    || shape.Properties.TryGetValue(memberName, out info)
                ? info!.IsOptional
                : false;
            rewritten = isOptionalProperty && !this._structReadExempt.Contains(accessNode)
                ? this.GuardOptionalStructRead(accessNode, anonymousRead)
                : anonymousRead;
            return true;
        }

        private bool TryRewriteOperatorOverload(PhpBinaryOpAst binary, out IBase2Ast rewritten)
        {
            rewritten = binary;

            var token = (int)(binary.Operator?.ValueInt64 ?? -1);
            var op = OverloadableOperatorHelper.FromToken(
                token,
                binary.Operator?.ValueString ?? "");
            var isCompoundAssign = false;
            if (op == OverloadableOperator.Invalid)
            {
                op = OverloadableOperatorHelper.FromAssignmentToken(
                    token, binary.Operator?.ValueString ?? "");
                if (op == OverloadableOperator.Invalid)
                {
                    return false;
                }

                isCompoundAssign = true;
            }

            // All operator methods are static now: pick the declaring class (left operand first,
            // then right) whose operator declares a form matching the operand combination, then emit
            // `\Type::__op($left, $right)`.
            if (this.TrySpliceBinaryOperator(op, binary, out var spliced))
            {
                // `$a += $b` → `$a = <spliced body of operator +($a, $b)>`
                rewritten = isCompoundAssign && binary.Left != null && spliced is IExpression splicedExpression
                    ? WithKeywordHelper.CreateAssignment(binary.Left, splicedExpression, binary)
                    : spliced;
                return true;
            }

            if (!this.SelectStaticBinaryOperatorTarget(
                    op, binary.Left, binary.Right, out var classFqn, out var methodName))
            {
                return false;
            }

            var argList = this.BuildBinaryOperatorArguments(binary.Left, binary.Right, binary);
            var call = this.BuildStaticCall(classFqn!, methodName!, argList, binary);

            if (isCompoundAssign && binary.Left != null)
            {
                // `$a += $b` → `$a = \Type::__add($a, $b)`
                rewritten = WithKeywordHelper.CreateAssignment(binary.Left, call, binary);
                return true;
            }

            rewritten = call;
            return true;
        }

        /// <summary>
        /// Resolves the static operator call target for a binary operator: the fully-qualified class
        /// name that owns the matching operator form (left operand first, then right) and the exact
        /// generated method name. Extension operators resolve to their extension class.
        /// </summary>
        private bool SelectStaticBinaryOperatorTarget(
            OverloadableOperator op,
            IExpression? left,
            IExpression? right,
            out string? classFqn,
            out string? methodName)
        {
            classFqn = null;
            methodName = null;

            if (this.TrySelectStaticBinaryFromOperand(op, left, left, right, out classFqn, out methodName))
            {
                return true;
            }

            if (this.TrySelectStaticBinaryFromOperand(op, right, left, right, out classFqn, out methodName))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Looks up a matching binary form on the type of <paramref name="owningOperand"/> (class or
        /// builtin with extension-contributed operators) and resolves the static emit target.
        /// When the operand type is a trait with no own overload, composing classes that <c>use</c>
        /// the trait are searched (with <c>$this</c> temporarily resolved to each user) and a hit
        /// emits <c>static::__op(...)</c> so the shared trait method body late-binds per user.
        /// </summary>
        private bool TrySelectStaticBinaryFromOperand(
            OverloadableOperator op,
            IExpression? owningOperand,
            IExpression? left,
            IExpression? right,
            out string? classFqn,
            out string? methodName)
        {
            classFqn = null;
            methodName = null;

            var resolved = this.ResolveOperatorExpressionType(owningOperand);
            if (resolved is ObjectDeclarationSymbol objectDecl && !objectDecl.IsStruct)
            {
                if (!this.TryFindBinaryFormOnTypeOrComposingClasses(
                        objectDecl, op, left, right, out var form, out var owningType, out var useLateStatic))
                {
                    return false;
                }

                return this.TryResolveStaticOperatorCallTarget(
                    form!, owningType!, op, useLateStatic, out classFqn, out methodName);
            }

            if (resolved is BuiltInTypeSymbol builtin)
            {
                var form = this.FindMatchingBinaryFormForBuiltin(builtin, op, left, right);
                if (form == null)
                {
                    return false;
                }

                (classFqn, methodName) = this.ResolveStaticOperatorTarget(form, builtin, op);
                return classFqn != null;
            }

            return false;
        }

        /// <summary>
        /// Resolves the static call target for an operator form. When
        /// <paramref name="useLateStaticBinding"/> is set (trait method body matching a composing
        /// class's overload), emits <c>static</c> so PHP late-static-binds to the runtime class —
        /// except extension operators, which always target the extension/owner class FQN.
        /// </summary>
        private bool TryResolveStaticOperatorCallTarget(
            ObjectOperatorOverloadMethodSymbol form,
            IBaseSymbol owningType,
            OverloadableOperator op,
            bool useLateStaticBinding,
            [NotNullWhen(true)] out string? classFqn,
            out string? methodName)
        {
            classFqn = null;
            methodName = null;

            if (useLateStaticBinding && !form.IsExtensionOperator)
            {
                methodName = OperatorMethodNameGenerator.GetMethodName(op);
                if (string.IsNullOrEmpty(methodName))
                {
                    return false;
                }

                classFqn = "static";
                return true;
            }

            (classFqn, methodName) = this.ResolveStaticOperatorTarget(form, owningType, op);
            return classFqn != null;
        }

        /// <summary>
        /// Finds a matching binary overload on <paramref name="typeSymbol"/>, or — when that symbol
        /// is a trait with no match — on a composing class that uses the trait. While probing each
        /// composing class, pushes it onto <see cref="_classStack"/> so <c>$this</c> matches that
        /// class's <c>self</c> parameters.
        /// </summary>
        private bool TryFindBinaryFormOnTypeOrComposingClasses(
            ObjectDeclarationSymbol typeSymbol,
            OverloadableOperator op,
            IExpression? left,
            IExpression? right,
            out ObjectOperatorOverloadMethodSymbol? form,
            out IBaseSymbol? owningType,
            out bool useLateStaticBinding)
        {
            form = this.FindMatchingBinaryForm(typeSymbol, op, left, right);
            if (form != null)
            {
                owningType = typeSymbol;
                useLateStaticBinding = false;
                return true;
            }

            if (typeSymbol.ObjectKind != PhpTypeDeclType.Trait)
            {
                owningType = null;
                useLateStaticBinding = false;
                return false;
            }

            foreach (var composing in this.EnumerateObjectsUsingTrait(typeSymbol))
            {
                this._classStack.Push(composing);
                try
                {
                    form = this.FindMatchingBinaryForm(composing, op, left, right);
                    if (form != null)
                    {
                        owningType = composing;
                        useLateStaticBinding = true;
                        return true;
                    }
                }
                finally
                {
                    this._classStack.Pop();
                }
            }

            owningType = null;
            useLateStaticBinding = false;
            return false;
        }

        /// <summary>
        /// Finds a matching unary overload on <paramref name="typeSymbol"/>, or on a composing class
        /// when the type is a trait (same late-static-binding contract as binary).
        /// </summary>
        private bool TryFindUnaryFormOnTypeOrComposingClasses(
            ObjectDeclarationSymbol typeSymbol,
            OverloadableOperator op,
            IExpression? operand,
            out ObjectOperatorOverloadMethodSymbol? form,
            out IBaseSymbol? owningType,
            out bool useLateStaticBinding)
        {
            form = this.FindMatchingUnaryForm(typeSymbol, op, operand);
            if (form != null)
            {
                owningType = typeSymbol;
                useLateStaticBinding = false;
                return true;
            }

            if (typeSymbol.ObjectKind != PhpTypeDeclType.Trait)
            {
                owningType = null;
                useLateStaticBinding = false;
                return false;
            }

            foreach (var composing in this.EnumerateObjectsUsingTrait(typeSymbol))
            {
                this._classStack.Push(composing);
                try
                {
                    form = this.FindMatchingUnaryForm(composing, op, operand);
                    if (form != null)
                    {
                        owningType = composing;
                        useLateStaticBinding = true;
                        return true;
                    }
                }
                finally
                {
                    this._classStack.Pop();
                }
            }

            owningType = null;
            useLateStaticBinding = false;
            return false;
        }

        /// <summary>
        /// Classes/enums that <c>use</c> <paramref name="trait"/> (transitively via nested trait
        /// uses). Used when a trait method's <c>$this</c> needs an overload declared on a user.
        /// </summary>
        private IEnumerable<ObjectDeclarationSymbol> EnumerateObjectsUsingTrait(
            ObjectDeclarationSymbol trait)
        {
            var symbolTree = this._context.GetSymbolTree();
            foreach (var candidate in EnumerateObjectDeclarations(this._context.GlobalScope))
            {
                if (candidate.ObjectKind is not (PhpTypeDeclType.Class or PhpTypeDeclType.Enum))
                {
                    continue;
                }

                var used = TypeComparer.ResolveUsedTraits(
                    candidate, symbolTree, this._context.GlobalScope, out _);
                if (used.Contains(trait))
                {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<ObjectDeclarationSymbol> EnumerateObjectDeclarations(IBaseScope scope)
        {
            foreach (var childScope in scope.GetAllChildScopes())
            {
                if (childScope is ObjectDeclarationScope { DeclarationSymbol: ObjectDeclarationSymbol decl })
                {
                    yield return decl;
                }

                foreach (var nested in EnumerateObjectDeclarations(childScope))
                {
                    yield return nested;
                }
            }
        }

        private (string? ClassFqn, string? MethodName) ResolveStaticOperatorTarget(
            ObjectOperatorOverloadMethodSymbol form,
            IBaseSymbol owningType,
            OverloadableOperator op,
            string? methodNameOverride = null)
        {
            var methodName = methodNameOverride ?? OperatorMethodNameGenerator.GetMethodName(op);
            if (string.IsNullOrEmpty(methodName))
            {
                return (null, null);
            }

            string binderFqn;
            if (form.IsExtensionOperator)
            {
                // Standalone `extension E { operator +<T>(…) }` methods are emitted on E.
                // Tyhpdef inline `extension operator` members are erased thin mappings: splice
                // already ran; never emit `__TyhpInlineExt_*` or a fake owner `__add`.
                if (form.DeclaringExtensionSymbol is { IsCompilerGenerated: true })
                {
                    return (null, null);
                }

                if (form.DeclaringExtensionSymbol is { IsCompilerGenerated: false } standaloneExt)
                {
                    binderFqn = standaloneExt.FullyQualifiedName;
                }
                else if (form.ExtensionTargetSymbol != null)
                {
                    binderFqn = form.ExtensionTargetSymbol.FullyQualifiedName;
                }
                else
                {
                    var extensionClass = form.DeclaringExtensionSymbol
                        ?? (owningType as ObjectDeclarationSymbol)?.SyntheticInlineExtension;
                    binderFqn = extensionClass?.FullyQualifiedName ?? owningType.FullyQualifiedName;
                }
            }
            else
            {
                binderFqn = owningType.FullyQualifiedName;
            }

            var classFqn = this.FormatEmittedClassFqn(binderFqn, owningType.Name);
            if (form.IsExtensionOperator)
            {
                this.EnsureImport(classFqn);
            }

            return (classFqn, methodName);
        }

        private bool TryRewriteUnaryOperatorOverload(PhpUnaryOpAst unary, out IBase2Ast rewritten)
        {
            rewritten = unary;

            var token = (int)(unary.Operator?.ValueInt64 ?? -1);
            var text = unary.Operator?.ValueString ?? "";
            // Unary +/- use the Plus/Minus variants (isAlternateKind).
            var isAlternate = text is "+" or "-"
                || token is TyhpParser.T_SYM_PLUS or TyhpParser.T_SYM_MINUS;
            var op = OverloadableOperatorHelper.FromToken(token, text, isAlternateKind: isAlternate);
            if (op == OverloadableOperator.Invalid || op == OverloadableOperator.Convert)
            {
                return false;
            }

            if (!this.TryFindMatchingUnaryOverload(
                    unary.Operand, op, out var overload, out var owningType, out var useLateStatic))
            {
                return false;
            }

            if (this.TrySpliceUnaryOperator(op, unary, out rewritten))
            {
                return true;
            }

            if (overload is { IsExtensionOperator: true, DeclaringExtensionSymbol.IsCompilerGenerated: true })
            {
                return false;
            }

            if (!this.TryResolveStaticOperatorCallTarget(
                    overload!, owningType!, op, useLateStatic, out var classFqn, out var methodName))
            {
                return false;
            }

            // Unary operator methods are static single-operand calls: `\Type::__not($a)`.
            var argList = this.BuildSingleOperandArguments(unary.Operand, unary);
            var call = this.BuildStaticCall(classFqn!, methodName!, argList, unary);

            // ++/-- return-and-reassign model (`: self`): `$a = \Type::__increment($a)` (and the
            // same for simple `$obj->prop`) writes the updated value back. Non-simple LHS forms are
            // expanded to by-ref temps in statement position (TryExpandIncrementDecrementWithTemps).
            // Postfix-as-value (`$b = $a++`) is statement-split earlier
            // (TryExpandPostfixIncrementDecrementAsValue) so Transform only sees bare / prefix forms
            // here — both correctly yield the new value.
            if (op is OverloadableOperator.Increment or OverloadableOperator.Decrement
                && IsSimpleOperatorLhs(unary.Operand))
            {
                rewritten = WithKeywordHelper.CreateAssignment(unary.Operand!, call, unary);
                return true;
            }

            rewritten = call;
            return true;
        }

        // `empty($o)` on a type declaring `operator empty` rewrites to
        // `(empty($o) || \Type::__isEmpty($o))`: PHP's native emptiness check still short-circuits
        // (unset/null/falsey), and the user-defined static `__isEmpty` decides the object case.
        private bool TryRewriteEmptyOperatorOverload(PhpEmptyStatementAst empty, out IBase2Ast rewritten)
        {
            rewritten = empty;

            var operand = empty.Expression;
            if (operand == null)
            {
                return false;
            }

            if (!this.TryFindMatchingUnaryOverload(
                    operand,
                    OverloadableOperator.IsEmpty,
                    out var overload,
                    out var owningType,
                    out var useLateStatic))
            {
                return false;
            }

            if (!this.TryResolveStaticOperatorCallTarget(
                    overload!,
                    owningType!,
                    OverloadableOperator.IsEmpty,
                    useLateStatic,
                    out var classFqn,
                    out var methodName))
            {
                return false;
            }

            var argList = this.BuildSingleOperandArguments(operand, empty);
            var isEmptyCall = this.BuildStaticCall(classFqn!, methodName!, argList, empty);
            var orToken = TokenValueAst.CreateFromContext("||", TyhpParser.T_BOOLEAN_OR, empty);
            var orExpr = PhpBinaryOpAst.CreateFromContext(orToken, empty, isEmptyCall, empty);
            rewritten = PhpDereferenceableExpressionAst.CreateFromContext(orExpr, empty);
            return true;
        }

        /// <summary>
        /// <c>(decimal) $x</c> is the convert-from / convert-to contract of <c>\Tyhp\Decimal</c>:
        /// a value that already is a <c>Decimal</c> stays as is, an operand with
        /// <c>operator convert(self): decimal</c> becomes its instance <c>__toDecimal()</c>, and
        /// anything else becomes the static <c>\Tyhp\Decimal::__from($x)</c>, which dispatches on
        /// the runtime type of <c>$x</c> over every <c>operator convert(T)</c> declared on Decimal.
        /// The cast is left untouched when <c>\Tyhp\Decimal</c> is not loaded.
        /// </summary>
        private bool TryRewriteDecimalCast(PhpUnaryOpAst unary, out IBase2Ast rewritten)
        {
            rewritten = unary;
            if ((int)(unary.Operator?.ValueInt64 ?? -1) != TyhpParser.T_DECIMAL_CAST
                || unary.Operand is null
                || this._nameResolver.ResolveQualifiedName(new[] { "Tyhp", "Decimal" })
                    is not ObjectDeclarationSymbol { ObjectKind: PhpTypeDeclType.Class } decimalClass)
            {
                return false;
            }

            var operandClass = this.ResolveOperatorOperandType(unary.Operand);
            if (operandClass is not null)
            {
                if (ReferenceEquals(operandClass, decimalClass))
                {
                    rewritten = EnsureDereferenceableReceiver(unary.Operand, unary);
                    return true;
                }

                if (this.TryRewriteConvertToCall(unary.Operand, operandClass, "Decimal", unary, out var converted)
                    && converted is not null)
                {
                    rewritten = converted;
                    return true;
                }
            }

            var classFqn = this.FormatEmittedClassFqn(decimalClass.FullyQualifiedName, decimalClass.Name);
            if (string.IsNullOrEmpty(classFqn))
            {
                return false;
            }

            rewritten = this.BuildStaticCall(
                classFqn,
                OperatorMethodNameGenerator.ConvertFromMethodName,
                this.BuildSingleOperandArguments(unary.Operand, unary),
                unary);
            return true;
        }

        private bool TryRewriteCastConversion(PhpUnaryOpAst unary, out IBase2Ast rewritten)
        {
            rewritten = unary;

            if (this.TryRewriteDecimalCast(unary, out var decimalRewritten))
            {
                rewritten = decimalRewritten;
                return true;
            }

            var targetKey = GetCastTargetTypeKey(unary.Operator);
            if (targetKey == null || unary.Operand is null)
            {
                return false;
            }

            var operandClass = this.ResolveOperatorOperandType(unary.Operand);
            if (operandClass == null)
            {
                return false;
            }

            if (!this.TryRewriteConvertToCall(unary.Operand, operandClass, targetKey, unary, out var converted)
                || converted is null)
            {
                return false;
            }

            rewritten = converted;
            return true;
        }

        private static string? GetCastTargetTypeKey(TokenValueAst? op)
        {
            if (op == null)
            {
                return null;
            }

            return (int)(op.ValueInt64 ?? -1) switch
            {
                TyhpParser.T_INT_CAST => "Int",
                TyhpParser.T_DOUBLE_CAST => "Float",
                TyhpParser.T_STRING_CAST => "String",
                TyhpParser.T_BOOL_CAST => "Bool",
                TyhpParser.T_ARRAY_CAST => "Array",
                _ => null,
            };
        }

        /// <summary>
        /// True for a standalone <c>extension E { operator … }</c> form whose PHP backer lives on
        /// E. Compiler-generated tyhpdef inline <c>extension operator</c> members are not
        /// standalone — their PHP target is the owning class (or already spliced).
        /// </summary>
        private static bool IsStandaloneExtensionOperator(ObjectOperatorOverloadMethodSymbol form)
            => form.IsExtensionOperator
                && form.DeclaringExtensionSymbol is { IsCompilerGenerated: false };

        /// <summary>
        /// Rewrites convert-to to an instance <c>$expr->__to{T}()</c> on the owning class, or to a
        /// static <c>E::__to{T}($expr)</c> when the matching form is a standalone extension.
        /// Trait-<c>$this</c> also accepts a composing class's convert-to.
        /// </summary>
        private bool TryRewriteConvertToCall(
            IExpression operand,
            ObjectDeclarationSymbol operandClass,
            string targetKey,
            Base2Ast context,
            out IExpression? rewritten)
        {
            rewritten = operand;
            if (!this.TryFindConvertToOverload(operandClass, targetKey, this.ConvertSiteFile(context), out var overload)
                || overload is null)
            {
                return false;
            }

            var methodName = OperatorMethodNameGenerator.GetConvertToMethodName(targetKey);
            if (IsStandaloneExtensionOperator(overload))
            {
                var (classFqn, resolvedName) = this.ResolveStaticOperatorTarget(
                    overload, operandClass, OverloadableOperator.Convert, methodName);
                if (classFqn is null || resolvedName is null)
                {
                    return false;
                }

                rewritten = this.BuildStaticCall(
                    classFqn,
                    resolvedName,
                    this.BuildSingleOperandArguments(operand, context),
                    context);
                return true;
            }

            // Instance dispatch on `$this` late-binds to the composing class at runtime.
            var receiver = EnsureDereferenceableReceiver(operand, context);
            rewritten = this.BuildInstanceMethodCall(
                receiver,
                methodName,
                PhpArgumentListAst.Create([], context),
                context);
            return true;
        }

        /// <summary>
        /// The file whose extension scope applies to a convert at <paramref name="node"/>: the node's
        /// own file (a spliced extension body keeps its declaring file), else the file being emitted.
        /// </summary>
        private SrcFileAst? ConvertSiteFile(Base2Ast? node) =>
            node?.OwningFile ?? this._currentFile?.SourceFileAst;

        private bool IsActiveExtensionOperator(ObjectOperatorOverloadMethodSymbol method, IExpression? site) =>
            ExtensionActivation.IsOperatorVisible(
                method,
                ConvertSiteFile(site as Base2Ast),
                this._context.GlobalScope);

        /// <summary>
        /// Extension-contributed converts of <paramref name="typeSymbol"/> that apply in
        /// <paramref name="site"/> — the same predicate the checker uses.
        /// </summary>
        private IEnumerable<ObjectOperatorOverloadMethodSymbol> VisibleExtensionConverts(
            ObjectDeclarationSymbol typeSymbol,
            SrcFileAst? site) =>
            typeSymbol.ExtensionContributedOperators.Where(
                m => ExtensionActivation.IsConvertVisible(m, site, this._context.GlobalScope));

        private bool TryFindConvertToOverload(
            ObjectDeclarationSymbol typeSymbol,
            string targetKey,
            SrcFileAst? site,
            out ObjectOperatorOverloadMethodSymbol? overload)
        {
            overload = this.FindConvertToOverloadOnType(typeSymbol, targetKey, site);
            if (overload is not null)
            {
                return true;
            }

            if (typeSymbol.ObjectKind != PhpTypeDeclType.Trait)
            {
                return false;
            }

            foreach (var composing in this.EnumerateObjectsUsingTrait(typeSymbol))
            {
                overload = this.FindConvertToOverloadOnType(composing, targetKey, site);
                if (overload is not null)
                {
                    return true;
                }
            }

            return false;
        }

        private ObjectOperatorOverloadMethodSymbol? FindConvertToOverloadOnType(
            ObjectDeclarationSymbol typeSymbol,
            string targetKey,
            SrcFileAst? site)
        {
            foreach (var candidate in this.EnumerateClassOperatorOverloads(typeSymbol)
                .Concat(this.VisibleExtensionConverts(typeSymbol, site)))
            {
                if (candidate.IsNativePassthrough
                    || candidate.Operator != OverloadableOperator.Convert
                    || candidate.Parameters.Count != 1
                    || !OperatorOverloadResolver.IsConvertToForm(candidate, typeSymbol))
                {
                    continue;
                }

                var returnKey = OperatorOverloadResolver.SpellTypeKey(
                    candidate.ReturnType, typeSymbol.Name);
                if (string.Equals(returnKey, targetKey, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Rewrites a typed-local <c>Expression&lt;…&gt;</c> / <c>PropertyPath&lt;…&gt;</c>
        /// assignment of an inline <c>fn</c> the same way call arguments are rewritten.
        /// </summary>
        private IBase2Ast TransformTypedVarExpressionCapture(TyhpTypedVarExprAst typedVar)
        {
            if (typedVar.AssignedExpression is not IExpression assigned
                || typedVar.TypeExpression is null
                || typedVar is not Base2Ast typedNode)
            {
                return typedVar;
            }

            if (PropertyPathEmissionHelper.TryRewriteInlineFn(
                    assigned,
                    typedVar.TypeExpression,
                    this.ResolveTypeSymbolFromTypeExpression,
                    this.FormatEmittedClassFqn,
                    closure => this._context.TryGetInferredClosureSignature(closure, out var pathSig)
                        ? pathSig
                        : null,
                    typedVar,
                    out var propertyPathRewritten)
                && propertyPathRewritten is IBase2Ast propertyPathAst)
            {
                this._context.RequirePackage("tyhp/lambda");
                typedNode.ReplaceChild(assigned, propertyPathAst);
                return typedVar;
            }

            if (ExpressionTreeEmissionHelper.TryRewriteInlineFn(
                    assigned,
                    typedVar.TypeExpression,
                    this.ResolveTypeSymbolFromTypeExpression,
                    this.FormatEmittedClassFqn,
                    closure => this._context.TryGetInferredClosureSignature(closure, out var exprSig)
                        ? exprSig
                        : null,
                    this._context.ExpressionTypes,
                    typedVar,
                    out var expressionRewritten)
                && expressionRewritten is IBase2Ast expressionAst)
            {
                this._context.RequirePackage("tyhp/lambda");
                typedNode.ReplaceChild(assigned, expressionAst);
            }

            return typedVar;
        }

        /// <summary>
        /// Rewrites call arguments that need an implicit <c>convert</c> overload to match the
        /// callee's declared parameter types (convert-to instance <c>__to{T}()</c> or convert-from
        /// static <c>__from</c>).
        /// </summary>
        private void TryRewriteCallArgumentConverts(PhpDereferenceableAst callNode)
        {
            if (callNode.Suffix is not PhpCallAst call || call.Arguments is null)
            {
                return;
            }

            if (!this.TryResolveCalleeParameters(callNode, out var parameters, out var selfContext))
            {
                return;
            }

            this.RewriteArgumentListConverts(call.Arguments, parameters, selfContext);
        }

        /// <summary>
        /// Rewrites <c>new Type(...)</c> constructor arguments the same way as call arguments —
        /// <c>new</c> is a distinct AST node (<see cref="PhpNewAst"/>, not a
        /// <see cref="PhpDereferenceableAst"/>/<see cref="PhpCallAst"/> pair), so it needs its own
        /// entry point into the shared argument-rewrite loop. Struct <c>new</c> is rewritten
        /// separately by <see cref="TransformStructNew"/> before this runs.
        /// </summary>
        private void TryRewriteConstructorArgumentConverts(PhpNewAst newExpr)
        {
            if (newExpr.Arguments is null
                || this.ResolveObjectDeclarationFromClassNameReference(newExpr.ClassName)
                    is not { } objectDecl
                || TryGetInstanceMethod(objectDecl, "__construct") is not { } ctor)
            {
                return;
            }

            this.RewriteArgumentListConverts(newExpr.Arguments, ctor.Parameters, objectDecl);
        }

        private void RewriteArgumentListConverts(
            PhpArgumentListAst arguments,
            IReadOnlyList<ParameterInfo> parameters,
            ObjectDeclarationSymbol? selfContext)
        {
            if (parameters.Count == 0)
            {
                return;
            }

            var positionalIndex = 0;
            foreach (var arg in arguments.GetAllNotNull())
            {
                if (arg.IsVariadic)
                {
                    continue;
                }

                ParameterInfo? param = null;
                if (arg.Name?.ValueString is { } named)
                {
                    param = parameters.FirstOrDefault(p =>
                        string.Equals(
                            p.Name.TrimStart('$'),
                            named.TrimStart('$'),
                            StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    if (positionalIndex >= parameters.Count)
                    {
                        continue;
                    }

                    param = parameters[positionalIndex++];
                }

                if (param is null
                    || param.DeclaredType is null
                    || arg.Expression is null
                    || arg is not Base2Ast argNode)
                {
                    continue;
                }

                // fn => \Tyhp\PropertyPath::from(...)
                if (PropertyPathEmissionHelper.TryRewriteInlineFn(
                        arg.Expression,
                        param.DeclaredType,
                        this.ResolveTypeSymbolFromTypeExpression,
                        this.FormatEmittedClassFqn,
                        closure => this._context.TryGetInferredClosureSignature(closure, out var sig)
                            ? sig
                            : null,
                        arg,
                        out var propertyPathRewritten)
                    && propertyPathRewritten is IBase2Ast propertyPathAst)
                {
                    this._context.RequirePackage("tyhp/lambda");
                    argNode.ReplaceChild(arg.Expression, propertyPathAst);
                    continue;
                }

                // fn => \Tyhp\Expression::lambda(...)
                if (ExpressionTreeEmissionHelper.TryRewriteInlineFn(
                        arg.Expression,
                        param.DeclaredType,
                        this.ResolveTypeSymbolFromTypeExpression,
                        this.FormatEmittedClassFqn,
                        closure => this._context.TryGetInferredClosureSignature(closure, out var exprSig)
                            ? exprSig
                            : null,
                        this._context.ExpressionTypes,
                        arg,
                        out var expressionRewritten)
                    && expressionRewritten is IBase2Ast expressionAst)
                {
                    this._context.RequirePackage("tyhp/lambda");
                    argNode.ReplaceChild(arg.Expression, expressionAst);
                    continue;
                }

                // Story 16 Phase 1: PropertyPath/Expression → \Closure extracts ->callable
                if (this.ShouldExtractCallableForClosureParam(arg.Expression, param.DeclaredType)
                    && PropertyPathEmissionHelper.TryBuildCallableExtraction(arg.Expression, arg, out var callableExtracted)
                    && callableExtracted is IBase2Ast callableAst)
                {
                    argNode.ReplaceChild(arg.Expression, callableAst);
                    continue;
                }

                if (this.TryRewriteImplicitConvert(
                        arg.Expression,
                        param.DeclaredType,
                        selfContext,
                        arg,
                        out var rewritten)
                    && rewritten is IBase2Ast rewrittenAst)
                {
                    argNode.ReplaceChild(arg.Expression, rewrittenAst);
                }
            }
        }

        /// <summary>
        /// True when a call argument is a PropertyPath/Expression value being passed where
        /// <c>\Closure</c> is expected — the emitter extracts <c>-&gt;callable</c>.
        /// </summary>
        private bool ShouldExtractCallableForClosureParam(
            IExpression expression,
            ITypeExpression expectedType)
        {
            if (!PropertyPathEmissionHelper.IsClosureTypeExpression(expectedType))
            {
                return false;
            }

            if (expression is PhpNewAst newExpr
                && PropertyPathEmissionHelper.IsPropertyPathOrExpressionNew(newExpr))
            {
                return true;
            }

            if (expression is PhpVariableAst variable)
            {
                var varName = NormalizeVariableName(
                    variable.VariableToken?.ValueString ?? variable.Identifier);
                if (varName is not null
                    && this.LookupTypedTypeExpression(varName) is { } typeExpr
                    && PropertyPathEmissionHelper.IsPropertyPathOrExpressionTypeExpression(
                        typeExpr,
                        this.ResolveTypeSymbolFromTypeExpression))
                {
                    return true;
                }
            }

            // Any other argument whose checked type is `Expression` (an inferred variable, a call
            // result, a property read) is extracted the same way; the annotation is not required.
            if (expression is IBase2Ast node
                && this.TryGetCheckedExpressionType(node, out var checkedType)
                && ExpressionTreeSupport.IsExpressionType(checkedType))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves the <see cref="ObjectDeclarationSymbol"/> a <c>new</c> expression's class-name
        /// reference points at (bound symbol first, then qualified/simple name lookup) — used for
        /// constructor-argument implicit-convert matching. Unlike <see cref="ResolveStructFromNew"/>,
        /// this is not restricted to struct types.
        /// </summary>
        private ObjectDeclarationSymbol? ResolveObjectDeclarationFromClassNameReference(
            IClassNameReference? className)
        {
            if (className is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol bound })
            {
                return bound;
            }

            var text = GetClassNameReferenceText(className);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (text.Contains('\\'))
            {
                var segments = text.TrimStart('\\').Split('\\');
                if (this._nameResolver.ResolveRelativeName(segments, this._context.GlobalScope)
                    is ObjectDeclarationSymbol qualified)
                {
                    return qualified;
                }
            }

            var simpleName = text.TrimStart('\\').Split('\\')[^1];
            return this.FindObjectTypeSymbol(simpleName) ?? this.FindTypeSymbol(simpleName);
        }

        private bool TryResolveCalleeParameters(
            PhpDereferenceableAst callNode,
            out IReadOnlyList<ParameterInfo> parameters,
            out ObjectDeclarationSymbol? selfContext)
        {
            parameters = [];
            selfContext = null;

            if (callNode.BoundSymbol is ObjectMethodSymbol boundMethod)
            {
                // A call of a source overload set is bound to the emitted implementation; its
                // arguments follow the declared signature the checker selected for the call.
                parameters = callNode.SelectedOverload is ObjectMethodSymbol selectedMethod
                    ? selectedMethod.Parameters
                    : boundMethod.Parameters;
                selfContext = this.GetOwningObjectDeclaration(boundMethod);
                return parameters.Count > 0;
            }

            if (callNode.BoundSymbol is FunctionDeclarationSymbol boundFunction)
            {
                parameters = callNode.SelectedOverload is FunctionDeclarationSymbol selectedBoundFunction
                    ? selectedBoundFunction.Parameters
                    : boundFunction.Parameters;
                return parameters.Count > 0;
            }

            // `$obj(...)` on an object with `__invoke`: the checker records the selected signature.
            if (callNode.SelectedOverload is ObjectMethodSymbol { Name: "__invoke" } invokeSignature)
            {
                parameters = invokeSignature.Parameters;
                selfContext = this.GetOwningObjectDeclaration(invokeSignature);
                return parameters.Count > 0;
            }

            // Free function: `foo(...)` — binder does not bind call-site names.
            if (callNode.Base is PhpNameAst nameAst)
            {
                if (nameAst.SelectedOverload is FunctionDeclarationSymbol selectedFunction)
                {
                    parameters = selectedFunction.Parameters;
                    return parameters.Count > 0;
                }

                // The checker binds the callee name to the function it resolved, which honours
                // `use function` imports and qualified names. Resolving the spelled name again
                // only finds the current namespace and the global scope.
                if (nameAst.BoundSymbol is FunctionDeclarationSymbol checkerResolved)
                {
                    parameters = SelectFunctionSignatureForArity(
                        checkerResolved,
                        callNode.Suffix as PhpCallAst).Parameters;
                    return parameters.Count > 0;
                }

                var raw = nameAst.ValueString ?? nameAst.Identifier;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var simple = raw.TrimStart('\\');
                    var resolvedFunction =
                        this._nameResolver.ResolveSymbol(simple, this._context.GlobalScope)
                            as FunctionDeclarationSymbol
                        ?? this._nameResolver.ResolveRelativeName([simple], this._context.GlobalScope)
                            as FunctionDeclarationSymbol
                        ?? this.ResolveNamespacedFreeFunction(simple);
                    if (resolvedFunction is not null)
                    {
                        parameters = SelectFunctionSignatureForArity(
                            resolvedFunction,
                            callNode.Suffix as PhpCallAst).Parameters;
                        return parameters.Count > 0;
                    }
                }

                return false;
            }

            // Instance / static method: `$o->m(...)` / `Type::m(...)`.
            if (callNode.Base is PhpDereferenceableAst
                {
                    Base: IExpression receiver,
                    Suffix: PhpInstanceMemberAccessAst instanceAccess
                }
                && this.GetMemberName(instanceAccess.MemberName) is { } instanceMethodName
                && this.ResolveOperatorExpressionType(receiver) is ObjectDeclarationSymbol instanceOwner
                && TryGetInstanceMethod(instanceOwner, instanceMethodName) is { } instanceMethod)
            {
                parameters = instanceMethod.Parameters;
                selfContext = instanceOwner;
                return parameters.Count > 0;
            }

            if (callNode.Base is PhpDereferenceableAst
                {
                    Base: IExpression staticReceiver,
                    Suffix: PhpStaticMemberAccessAst staticAccess
                }
                && this.GetMemberName(staticAccess.Member) is { } staticMethodName)
            {
                ObjectDeclarationSymbol? staticOwner = null;
                if (staticReceiver is PhpNameAst typeName)
                {
                    var text = typeName.ValueString ?? typeName.Identifier;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        staticOwner = this.FindObjectTypeSymbol(text) ?? this.FindTypeSymbol(text);
                    }
                }
                else
                {
                    staticOwner = this.ResolveOperatorExpressionType(staticReceiver) as ObjectDeclarationSymbol;
                }

                if (staticOwner is not null
                    && TryGetInstanceMethod(staticOwner, staticMethodName) is { } staticMethod)
                {
                    parameters = staticMethod.Parameters;
                    selfContext = staticOwner;
                    return parameters.Count > 0;
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves a bare free-function call against the current output file's namespace
        /// (<c>namespace App; sortBy(...)</c> → <c>App\sortBy</c>). Global-scope lookup alone
        /// misses namespaced callees, so PropertyPath / Expression argument rewriting never ran.
        /// </summary>
        private FunctionDeclarationSymbol? ResolveNamespacedFreeFunction(string simpleName)
        {
            if (simpleName.Contains('\\'))
            {
                return null;
            }

            var ns = this._currentFile?.FileNameSpace switch
            {
                PhpNamespaceDeclAst statementNs => statementNs.Identifier,
                PhpBlockNamespaceDeclAst blockNs => blockNs.Identifier,
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(ns))
            {
                return null;
            }

            var segments = ns.Trim('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Concat([simpleName])
                .ToArray();
            return this._nameResolver.ResolveRelativeName(segments, this._context.GlobalScope)
                as FunctionDeclarationSymbol;
        }

        private FunctionDeclarationSymbol? FindFileScopedFreeFunction(string simpleName)
        {
            if (string.IsNullOrEmpty(simpleName) || simpleName.Contains('\\'))
            {
                return null;
            }

            foreach (var scope in ((IBaseScope)this._context.GlobalScope).GetAllChildScopes())
            {
                if (scope is not FileScope fileScope)
                {
                    continue;
                }

                if (((IBaseScope)fileScope).FindChildSymbolByName(simpleName) is FunctionDeclarationSymbol fn)
                {
                    return fn;
                }
            }

            return null;
        }

        private static FunctionDeclarationSymbol SelectFunctionSignatureForArity(
            FunctionDeclarationSymbol primary,
            PhpCallAst? call)
        {
            if (primary.Overloads.Count == 0 || call?.Arguments is null)
            {
                return primary;
            }

            var argCount = call.Arguments.GetAllNotNull().Count(a => !a.IsVariadic);
            foreach (var candidate in EnumerateFunctionSignatures(primary))
            {
                if (candidate.Parameters.Count == argCount)
                {
                    return candidate;
                }
            }

            return primary;
        }

        private static IEnumerable<FunctionDeclarationSymbol> EnumerateFunctionSignatures(
            FunctionDeclarationSymbol primary)
        {
            yield return primary;
            foreach (var overload in primary.Overloads)
            {
                yield return overload;
            }
        }

        private ITypeExpression? GetEnclosingFunctionReturnType()
        {
            if (this._functionStack.Count == 0)
            {
                return null;
            }

            return this._functionStack.Peek() switch
            {
                PhpFunctionDeclAst function => function.ReturnType
                    ?? (function.BoundSymbol as FunctionDeclarationSymbol)?.ReturnType,
                PhpMethodDeclAst method => method.ReturnType
                    ?? (method.BoundSymbol as ObjectMethodSymbol)?.ReturnType,
                PhpInlineFunctionAst inline => inline.ReturnType,
                TyhpOperatorOverloadAst overload => overload.ReturnType
                    ?? (overload.BoundSymbol as ObjectOperatorOverloadMethodSymbol)?.ReturnType,
                _ => null,
            };
        }

        /// <summary>
        /// When <paramref name="expression"/>'s type has a matching <c>convert</c> overload for
        /// <paramref name="expectedType"/>, rewrite to instance <c>__to{T}()</c> (convert-to) or
        /// static <c>\Type::__from(...)</c> (convert-from).
        /// </summary>
        private bool TryRewriteImplicitConvert(
            IExpression? expression,
            ITypeExpression? expectedType,
            ObjectDeclarationSymbol? selfContext,
            Base2Ast context,
            out IExpression? rewritten)
        {
            rewritten = expression;
            if (expression is null || expectedType is null)
            {
                return false;
            }

            if (!this.TryGetSingleExpectedType(expectedType, selfContext, out var targetKey, out var expectedObject))
            {
                return false;
            }

            // convert-to: object where a scalar/named target is expected. Trait-`$this` (operand
            // resolves to the trait, not the composing class — see `TryRewriteCastConversion`)
            // also accepts a composing class's convert-to. Standalone extension convert-to
            // rewrites to `E::__to{T}($expr)` rather than an instance call on the target.
            var operandClass = this.ResolveOperatorOperandType(expression);
            if (operandClass is not null
                && (expectedObject is null || !ReferenceEquals(operandClass, expectedObject))
                && this.TryRewriteConvertToCall(
                    expression, operandClass, targetKey, context, out var convertTo)
                && convertTo is not null)
            {
                rewritten = convertTo;
                return true;
            }

            // convert-from: scalar/other source where an object type with matching __from is expected.
            // Standalone extension convert-from lives on E (`E::__from($expr)`), not the target.
            if (expectedObject is not null
                && !expectedObject.IsStruct
                && (operandClass is null || !ReferenceEquals(operandClass, expectedObject))
                && this.TryFindConvertFromOverload(expectedObject, expression, this.ConvertSiteFile(context), out var fromForm)
                && fromForm is not null
                && this.TryResolveConvertFromClassFqn(fromForm, expectedObject, out var classFqn))
            {
                var argList = this.BuildSingleOperandArguments(expression, context);
                rewritten = this.BuildStaticCall(
                    classFqn!,
                    OperatorMethodNameGenerator.ConvertFromMethodName,
                    argList,
                    context);
                return true;
            }

            return false;
        }

        private bool TryGetSingleExpectedType(
            ITypeExpression expectedType,
            ObjectDeclarationSymbol? selfContext,
            out string targetKey,
            out ObjectDeclarationSymbol? expectedObject)
        {
            targetKey = "";
            expectedObject = null;

            var unwrapped = UnwrapNullableTypeExpression(expectedType);
            if (unwrapped is null)
            {
                return false;
            }

            // Multi-arm union or intersection is not one convert target. Nullable
            // `?(string|int)` / `?(Foo&Bar)` stay intact above so this sees every arm;
            // a first-arm peel would spell the composite as `string` or `Foo`.
            if (unwrapped is PhpTypeExpressionAst composite
                && composite.TypeKind is PhpTypeKind.Union or PhpTypeKind.Intersection)
            {
                var members = composite.Types?.GetAllNotNull()
                    .Where(m => m is not PhpBuiltinTypeAst { Identifier: "null" or "void" or "never" })
                    .ToList() ?? [];
                if (members.Count != 1)
                {
                    return false;
                }

                unwrapped = members[0];
            }

            var selfKey = selfContext?.Name ?? "";
            targetKey = OperatorOverloadResolver.SpellTypeKey(unwrapped, selfKey);
            if (string.IsNullOrEmpty(targetKey)
                || string.Equals(targetKey, "Mixed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (selfContext is not null && IsSelfOrStaticTypeExpression(unwrapped))
            {
                expectedObject = selfContext;
                return true;
            }

            if (this.ResolveTypeSymbolFromTypeExpression(unwrapped) is ObjectDeclarationSymbol objectDecl
                && !objectDecl.IsStruct)
            {
                expectedObject = objectDecl;
            }

            return true;
        }

        private static ITypeExpression? UnwrapNullableTypeExpression(ITypeExpression type)
        {
            if (type is not PhpTypeExpressionAst composite || !composite.IsNullable)
            {
                return type;
            }

            // `?T` has one member. A nullable union or intersection keeps every arm;
            // taking the first would treat `?(string|int)` as `string`.
            var members = composite.Types?.GetAllNotNull().ToList();
            if (members is { Count: 1 })
            {
                return members[0];
            }

            return type;
        }

        private static bool IsSelfOrStaticTypeExpression(ITypeExpression? type) =>
            type switch
            {
                PhpBuiltinTypeAst builtin =>
                    string.Equals(builtin.Identifier, "self", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(builtin.Identifier, "static", StringComparison.OrdinalIgnoreCase),
                PhpNamedTypeAst named =>
                    string.Equals(GetNamedTypeText(named), "self", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(GetNamedTypeText(named), "static", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };

        private static string? GetNamedTypeText(PhpNamedTypeAst named) =>
            named.Name switch
            {
                PhpNameAst name => name.ValueString ?? name.Identifier,
                TokenValueAst token => token.ValueString,
                _ => named.Name?.Identifier,
            };

        private bool TryResolveConvertFromClassFqn(
            ObjectOperatorOverloadMethodSymbol fromForm,
            ObjectDeclarationSymbol expectedObject,
            out string? classFqn)
        {
            classFqn = null;
            if (IsStandaloneExtensionOperator(fromForm))
            {
                (classFqn, _) = this.ResolveStaticOperatorTarget(
                    fromForm,
                    expectedObject,
                    OverloadableOperator.Convert,
                    OperatorMethodNameGenerator.ConvertFromMethodName);
                return classFqn is not null;
            }

            classFqn = this.FormatEmittedClassFqn(
                expectedObject.FullyQualifiedName,
                expectedObject.Name);
            return !string.IsNullOrEmpty(classFqn);
        }

        private bool TryFindConvertFromOverload(
            ObjectDeclarationSymbol typeSymbol,
            IExpression? sourceExpression,
            SrcFileAst? site,
            out ObjectOperatorOverloadMethodSymbol? overload)
        {
            overload = null;
            var sourceKey = this.GetConvertSourceTypeKey(sourceExpression, typeSymbol.Name);
            if (string.IsNullOrEmpty(sourceKey)
                || string.Equals(sourceKey, "Mixed", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var candidate in this.EnumerateClassOperatorOverloads(typeSymbol)
                .Concat(this.VisibleExtensionConverts(typeSymbol, site)))
            {
                if (candidate.IsNativePassthrough
                    || candidate.Operator != OverloadableOperator.Convert
                    || candidate.Parameters.Count != 1
                    || OperatorOverloadResolver.IsConvertToForm(candidate, typeSymbol))
                {
                    continue;
                }

                var paramKey = OperatorOverloadResolver.SpellTypeKey(
                    candidate.Parameters[0].DeclaredType, typeSymbol.Name);
                if (string.Equals(paramKey, sourceKey, StringComparison.OrdinalIgnoreCase))
                {
                    overload = candidate;
                    return true;
                }
            }

            return false;
        }

        private string GetConvertSourceTypeKey(IExpression? expression, string selfTypeKey)
        {
            var resolved = this.ResolveOperatorExpressionType(expression);
            if (resolved is ObjectDeclarationSymbol objectDecl)
            {
                var formatted = TypeNameFormatter.FormatTypeNameSegment(objectDecl.Name);
                return string.IsNullOrEmpty(formatted) ? objectDecl.Name : formatted;
            }

            if (resolved is BuiltInTypeSymbol builtin)
            {
                var formatted = TypeNameFormatter.FormatTypeNameSegment(builtin.Name);
                return string.IsNullOrEmpty(formatted) ? builtin.Name : formatted;
            }

            // Prefer collected declared type expressions (parameters / typed vars).
            if (expression is PhpVariableAst variable)
            {
                var varName = NormalizeVariableName(
                    variable.VariableToken?.ValueString ?? variable.Identifier);
                if (varName is not null
                    && this.LookupTypedTypeExpression(varName) is { } declared)
                {
                    return OperatorOverloadResolver.SpellTypeKey(declared, selfTypeKey);
                }
            }

            return this.GuessOperandTypeName(expression);
        }

        private static IExpression EnsureDereferenceableReceiver(IExpression expression, Base2Ast context)
        {
            if (expression is IDereferenceableBase)
            {
                return expression;
            }

            return PhpDereferenceableExpressionAst.CreateFromContext(expression, context);
        }

        private bool TryExpandCompoundAssignWithTemps(
            IBase2Ast statement,
            out IReadOnlyList<IBase2Ast> expanded)
        {
            expanded = [statement];

            if (!this.TryGetCompoundAssignBinary(statement, out var compound))
            {
                return false;
            }

            if (IsSimpleOperatorLhs(compound.Left))
            {
                return false;
            }

            var token = (int)(compound.Operator?.ValueInt64 ?? -1);
            var op = OverloadableOperatorHelper.FromAssignmentToken(token);
            if (op == OverloadableOperator.Invalid)
            {
                return false;
            }

            // Resolve the static call target from the original operands' types, then bind the
            // non-simple LHS to a by-ref temp so it is evaluated once.
            if (!this.SelectStaticBinaryOperatorTarget(
                    op, compound.Left, compound.Right, out var classFqn, out var methodName))
            {
                return false;
            }

            // $__tmp = &$lhs; $__tmp = \Type::__add($__tmp, $rhs);
            var tempName = this._context.GenerateUniqueVarName("__tmp");
            var tempVar = PhpVariableAst.CreateFromContext(tempName, compound);

            var amp = TokenValueAst.CreateFromContext(
                "&", TyhpParser.T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG, compound);
            var refExpr = PhpUnaryOpAst.CreateFromContext(amp, compound.Left!, compound);
            var bindRef = WithKeywordHelper.CreateAssignment(tempVar, refExpr, compound);

            var argList = this.BuildBinaryOperatorArguments(tempVar, compound.Right, compound);
            var call = this.BuildStaticCall(classFqn!, methodName!, argList, compound);
            var assignResult = WithKeywordHelper.CreateAssignment(tempVar, call, compound);

            expanded = [bindRef, assignResult];
            return true;
        }

        /// <summary>
        /// Statement-position <c>$o-&gt;box-&gt;money++</c> (non-simple LHS) must not emit a bare
        /// <c>\Money::__increment(...)</c> call that discards the result. Bind a by-ref temp and
        /// assign the incremented value back — same pattern as compound-assign temp extraction.
        /// </summary>
        private bool TryExpandIncrementDecrementWithTemps(
            IBase2Ast statement,
            out IReadOnlyList<IBase2Ast> expanded)
        {
            expanded = [statement];

            if (statement is not PhpUnaryOpAst unary)
            {
                return false;
            }

            var token = (int)(unary.Operator?.ValueInt64 ?? -1);
            var text = unary.Operator?.ValueString ?? "";
            var isAlternate = text is "+" or "-"
                || token is TyhpParser.T_SYM_PLUS or TyhpParser.T_SYM_MINUS;
            var op = OverloadableOperatorHelper.FromToken(token, text, isAlternateKind: isAlternate);
            if (op is not (OverloadableOperator.Increment or OverloadableOperator.Decrement))
            {
                return false;
            }

            if (IsSimpleOperatorLhs(unary.Operand))
            {
                return false;
            }

            if (!this.TryFindMatchingUnaryOverload(
                    unary.Operand, op, out var overload, out var owningType, out var useLateStatic))
            {
                return false;
            }

            if (!this.TryResolveStaticOperatorCallTarget(
                    overload!, owningType!, op, useLateStatic, out var classFqn, out var methodName))
            {
                return false;
            }

            // $__tmp = &$lhs; $__tmp = \Type::__increment($__tmp);
            var tempName = this._context.GenerateUniqueVarName("__tmp");
            var tempVar = PhpVariableAst.CreateFromContext(tempName, unary);

            var amp = TokenValueAst.CreateFromContext(
                "&", TyhpParser.T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG, unary);
            var refExpr = PhpUnaryOpAst.CreateFromContext(amp, unary.Operand!, unary);
            var bindRef = WithKeywordHelper.CreateAssignment(tempVar, refExpr, unary);

            var argList = this.BuildSingleOperandArguments(tempVar, unary);
            var call = this.BuildStaticCall(classFqn, methodName!, argList, unary);
            var assignResult = WithKeywordHelper.CreateAssignment(tempVar, call, unary);

            expanded = [bindRef, assignResult];
            return true;
        }

        /// <summary>
        /// Statement-splits overloaded postfix <c>++</c>/<c>--</c> used as a value so the
        /// expression yields the prior value (PHP semantics). Example:
        /// <c>$b = $a++</c> → <c>$__old = $a; $a = \Type::__increment($a); $b = $__old;</c>.
        /// Positions that cannot be split safely (short-circuit / loop conditions) get a diagnostic.
        /// </summary>
        private bool TryExpandPostfixIncrementDecrementAsValue(
            IBase2Ast statement,
            out IReadOnlyList<IBase2Ast> expanded)
        {
            expanded = [statement];

            // Bare `$a++` / `++$a` discard the value — Transform's assignment rewrite is correct.
            if (statement is PhpUnaryOpAst)
            {
                return false;
            }

            var sites = new List<PostfixIncrementSite>();
            this.CollectPostfixIncrementSites(
                statement,
                parent: null,
                conditionallyEvaluated: false,
                valueDiscarded: false,
                sites);

            if (sites.Count == 0)
            {
                return false;
            }

            var prelude = new List<IBase2Ast>();
            var anyHoisted = false;

            foreach (var site in sites)
            {
                if (site.ValueDiscarded)
                {
                    continue;
                }

                if (site.ConditionallyEvaluated || site.Parent is null)
                {
                    this.ReportPostfixOperatorOverloadRequiresStatementSplit(site.Unary);
                    continue;
                }

                if (!this.TryBuildPostfixIncrementPrelude(site.Unary, out var sitePrelude, out var oldVar))
                {
                    this.ReportPostfixOperatorOverloadRequiresStatementSplit(site.Unary);
                    continue;
                }

                site.Parent.ReplaceChild(site.Unary, oldVar);
                prelude.AddRange(sitePrelude);
                anyHoisted = true;
            }

            if (!anyHoisted)
            {
                return false;
            }

            prelude.Add(statement);
            expanded = prelude;
            return true;
        }

        private readonly record struct PostfixIncrementSite(
            PhpUnaryOpAst Unary,
            Base2Ast? Parent,
            bool ConditionallyEvaluated,
            bool ValueDiscarded);

        private void CollectPostfixIncrementSites(
            IBase2Ast node,
            Base2Ast? parent,
            bool conditionallyEvaluated,
            bool valueDiscarded,
            List<PostfixIncrementSite> sites)
        {
            // Nested statement structures are expanded when their own block/list is processed.
            if (parent is not null
                && node is PhpStatementBlockAst or PhpIfAst or PhpLoopAst or PhpTryCatchAst
                    or PhpReturnStatementAst)
            {
                return;
            }

            if (node is PhpUnaryOpAst unary
                && parent is not null
                && !unary.IsPrefix
                && this.TryGetIncrementDecrementOperator(unary, out var op)
                && this.TryFindMatchingUnaryOverload(unary.Operand, op, out _, out _, out _))
            {
                sites.Add(new PostfixIncrementSite(
                    unary,
                    parent,
                    conditionallyEvaluated,
                    valueDiscarded));
            }

            switch (node)
            {
                case PhpTernaryOpAst ternary:
                    if (ternary.Condition is IBase2Ast cond)
                    {
                        this.CollectPostfixIncrementSites(
                            cond, ternary, conditionallyEvaluated, valueDiscarded, sites);
                    }

                    if (ternary.TrueExpr is IBase2Ast trueExpr)
                    {
                        this.CollectPostfixIncrementSites(
                            trueExpr, ternary, conditionallyEvaluated: true, valueDiscarded, sites);
                    }

                    if (ternary.FalseExpr is IBase2Ast falseExpr)
                    {
                        this.CollectPostfixIncrementSites(
                            falseExpr, ternary, conditionallyEvaluated: true, valueDiscarded, sites);
                    }

                    return;

                case PhpBinaryOpAst binary when IsShortCircuitBinary(binary):
                    if (binary.Left is IBase2Ast left)
                    {
                        this.CollectPostfixIncrementSites(
                            left, binary, conditionallyEvaluated, valueDiscarded, sites);
                    }

                    if (binary.Right is IBase2Ast right)
                    {
                        this.CollectPostfixIncrementSites(
                            right, binary, conditionallyEvaluated: true, valueDiscarded, sites);
                    }

                    return;

                case PhpIfAst ifAst:
                    this.CollectPostfixSitesInIfChain(ifAst, conditionallyEvaluated, sites);
                    return;

                case PhpLoopAst loop:
                    this.CollectPostfixSitesInLoop(loop, conditionallyEvaluated, sites);
                    return;
            }

            if (node is not Base2Ast baseNode)
            {
                return;
            }

            foreach (var child in baseNode.AstChildren)
            {
                if (child is null)
                {
                    continue;
                }

                this.CollectPostfixIncrementSites(
                    child, baseNode, conditionallyEvaluated, valueDiscarded, sites);
            }
        }

        /// <summary>
        /// Walks an `if` / `else if` chain for postfix sites. `else if` attaches the nested
        /// <see cref="PhpIfAst"/> directly as <see cref="PhpIfAst.ElseStatement"/> (no wrapping
        /// block), so it never becomes its own top-level statement or block that gets expanded
        /// independently — this method is the only place its condition is ever visited. Each
        /// `else if` condition only runs when every preceding condition in the chain was false,
        /// so it is always conditionally evaluated: a postfix increment/decrement used as a
        /// value there must be diagnosed (TYHP5019), not hoisted before the whole chain.
        /// </summary>
        private void CollectPostfixSitesInIfChain(
            PhpIfAst ifAst,
            bool conditionallyEvaluated,
            List<PostfixIncrementSite> sites)
        {
            if (ifAst.Condition is IBase2Ast ifCond)
            {
                this.CollectPostfixIncrementSites(
                    ifCond, ifAst, conditionallyEvaluated, valueDiscarded: false, sites);
            }

            if (ifAst.ElseStatement is PhpIfAst chainedElseIf)
            {
                this.CollectPostfixSitesInIfChain(chainedElseIf, conditionallyEvaluated: true, sites);
            }
        }

        private void CollectPostfixSitesInLoop(
            PhpLoopAst loop,
            bool conditionallyEvaluated,
            List<PostfixIncrementSite> sites)
        {
            switch (loop.LoopType)
            {
                case PhpLoopType.While:
                case PhpLoopType.DoWhile:
                    if (loop.Condition is IBase2Ast condition)
                    {
                        // Re-evaluated each iteration — cannot hoist before the loop.
                        this.CollectPostfixIncrementSites(
                            condition,
                            loop,
                            conditionallyEvaluated: true,
                            valueDiscarded: false,
                            sites);
                    }

                    break;

                case PhpLoopType.For:
                    if (loop.InitExpressions is IBase2Ast init)
                    {
                        this.CollectPostfixIncrementSites(
                            init,
                            loop,
                            conditionallyEvaluated,
                            valueDiscarded: false,
                            sites);
                    }

                    if (loop.TestExpressions is IBase2Ast test)
                    {
                        this.CollectPostfixIncrementSites(
                            test,
                            loop,
                            conditionallyEvaluated: true,
                            valueDiscarded: false,
                            sites);
                    }

                    // Update expressions discard the value (like a bare `$a++` statement).
                    if (loop.UpdateExpressions is IBase2Ast update)
                    {
                        this.CollectPostfixIncrementSites(
                            update,
                            loop,
                            conditionallyEvaluated: false,
                            valueDiscarded: true,
                            sites);
                    }

                    break;

                case PhpLoopType.Foreach:
                    if (loop.Condition is IBase2Ast iterable)
                    {
                        this.CollectPostfixIncrementSites(
                            iterable,
                            loop,
                            conditionallyEvaluated,
                            valueDiscarded: false,
                            sites);
                    }

                    break;
            }
        }

        private static bool IsShortCircuitBinary(PhpBinaryOpAst binary)
        {
            var text = binary.Operator?.ValueString;
            if (!string.IsNullOrEmpty(text))
            {
                return text is "&&" or "||" or "??"
                    || text.Equals("and", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("or", StringComparison.OrdinalIgnoreCase);
            }

            var token = (int)(binary.Operator?.ValueInt64 ?? -1);
            return token is TyhpParser.T_BOOLEAN_AND
                or TyhpParser.T_BOOLEAN_OR
                or TyhpParser.T_LOGICAL_AND
                or TyhpParser.T_LOGICAL_OR
                or TyhpParser.T_COALESCE;
        }

        private bool TryGetIncrementDecrementOperator(PhpUnaryOpAst unary, out OverloadableOperator op)
        {
            op = this.GetIncrementDecrementOperator(unary);
            return op is OverloadableOperator.Increment or OverloadableOperator.Decrement;
        }

        private OverloadableOperator GetIncrementDecrementOperator(PhpUnaryOpAst unary)
        {
            var token = (int)(unary.Operator?.ValueInt64 ?? -1);
            var text = unary.Operator?.ValueString ?? "";
            var isAlternate = text is "+" or "-"
                || token is TyhpParser.T_SYM_PLUS or TyhpParser.T_SYM_MINUS;
            return OverloadableOperatorHelper.FromToken(token, text, isAlternateKind: isAlternate);
        }

        private bool TryBuildPostfixIncrementPrelude(
            PhpUnaryOpAst unary,
            out List<IBase2Ast> prelude,
            out PhpVariableAst oldVar)
        {
            prelude = [];
            oldVar = null!;

            var op = this.GetIncrementDecrementOperator(unary);
            if (op is not (OverloadableOperator.Increment or OverloadableOperator.Decrement))
            {
                return false;
            }

            if (!this.TryFindMatchingUnaryOverload(
                    unary.Operand, op, out var overload, out var owningType, out var useLateStatic))
            {
                return false;
            }

            if (!this.TryResolveStaticOperatorCallTarget(
                    overload!, owningType!, op, useLateStatic, out var classFqn, out var methodName))
            {
                return false;
            }

            var oldName = this._context.GenerateUniqueVarName("__old");
            oldVar = PhpVariableAst.CreateFromContext(oldName, unary);

            if (IsSimpleOperatorLhs(unary.Operand))
            {
                // $__old = $lhs; $lhs = \Type::__increment($lhs);
                prelude.Add(WithKeywordHelper.CreateAssignment(oldVar, unary.Operand!, unary));
                var argList = this.BuildSingleOperandArguments(unary.Operand, unary);
                var call = this.BuildStaticCall(classFqn, methodName!, argList, unary);
                prelude.Add(WithKeywordHelper.CreateAssignment(unary.Operand!, call, unary));
                return true;
            }

            // $__tmp = &$lhs; $__old = $__tmp; $__tmp = \Type::__increment($__tmp);
            var tempName = this._context.GenerateUniqueVarName("__tmp");
            var tempVar = PhpVariableAst.CreateFromContext(tempName, unary);
            var amp = TokenValueAst.CreateFromContext(
                "&", TyhpParser.T_AMPERSAND_FOLLOWED_BY_VAR_OR_VARARG, unary);
            var refExpr = PhpUnaryOpAst.CreateFromContext(amp, unary.Operand!, unary);
            prelude.Add(WithKeywordHelper.CreateAssignment(tempVar, refExpr, unary));
            prelude.Add(WithKeywordHelper.CreateAssignment(oldVar, tempVar, unary));
            var tmpArgs = this.BuildSingleOperandArguments(tempVar, unary);
            var tmpCall = this.BuildStaticCall(classFqn, methodName!, tmpArgs, unary);
            prelude.Add(WithKeywordHelper.CreateAssignment(tempVar, tmpCall, unary));
            return true;
        }

        private void ReportPostfixOperatorOverloadRequiresStatementSplit(PhpUnaryOpAst unary)
        {
            var fileName = unary.OwningFile?.Identifier
                ?? this._context.CurrentSourceFile?.Identifier
                ?? "";
            var opText = unary.Operator?.ValueString ?? "++";
            this._context.Diagnostics.AddErrorFromAst(
                MessageCode.EmitterPostfixOperatorOverloadRequiresStatementSplit,
                unary,
                fileName,
                opText);
        }

        private bool TryGetCompoundAssignBinary(IBase2Ast statement, out PhpBinaryOpAst compound)
        {
            compound = null!;

            if (statement is PhpBinaryOpAst bare
                && OverloadableOperatorHelper.FromAssignmentToken(
                    (int)(bare.Operator?.ValueInt64 ?? -1),
                    bare.Operator?.ValueString ?? "") != OverloadableOperator.Invalid)
            {
                compound = bare;
                return true;
            }

            return false;
        }

        private static bool IsSimpleOperatorLhs(IExpression? expression)
        {
            if (WithKeywordHelper.IsSimpleVariable(expression))
            {
                return true;
            }

            // $obj->prop where $obj is a simple variable and prop is a fixed name.
            if (expression is PhpDereferenceableAst
                {
                    Base: PhpVariableAst receiver,
                    Suffix: PhpInstanceMemberAccessAst { MemberName: PhpNameAst }
                }
                && WithKeywordHelper.IsSimpleVariable(receiver))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves an operator operand to an <see cref="ObjectDeclarationSymbol"/>.
        /// Unlike <see cref="ResolveReceiverType"/>, this does <b>not</b> fall through member-access
        /// bases (so <c>$obj-&gt;intProp + 1</c> is not treated as an overload on <c>$obj</c>).
        /// </summary>
        private ObjectDeclarationSymbol? ResolveOperatorOperandType(IExpression? expression)
        {
            var resolved = this.ResolveOperatorExpressionType(expression);
            if (resolved is ObjectDeclarationSymbol objectDecl && !objectDecl.IsStruct)
            {
                return objectDecl;
            }

            return null;
        }

        /// <summary>
        /// Finds a matching unary overload on the operand's class or builtin (extension-contributed).
        /// Trait operands also search composing classes (see
        /// <see cref="TryFindUnaryFormOnTypeOrComposingClasses"/>).
        /// </summary>
        private bool TryFindMatchingUnaryOverload(
            IExpression? operand,
            OverloadableOperator op,
            out ObjectOperatorOverloadMethodSymbol? overload,
            out IBaseSymbol? owningType,
            out bool useLateStaticBinding)
        {
            overload = null;
            owningType = null;
            useLateStaticBinding = false;

            var resolved = this.ResolveOperatorExpressionType(operand);
            if (resolved is ObjectDeclarationSymbol objectDecl && !objectDecl.IsStruct)
            {
                return this.TryFindUnaryFormOnTypeOrComposingClasses(
                    objectDecl, op, operand, out overload, out owningType, out useLateStaticBinding);
            }

            if (resolved is BuiltInTypeSymbol builtin)
            {
                overload = this.FindMatchingUnaryFormForBuiltin(builtin, op, operand);
                if (overload == null)
                {
                    return false;
                }

                owningType = builtin;
                return true;
            }

            return false;
        }

        private ObjectOperatorOverloadMethodSymbol? FindMatchingBinaryForm(
            ObjectDeclarationSymbol typeSymbol,
            OverloadableOperator op,
            IExpression? left,
            IExpression? right)
        {
            // Class-level bodyless tyhpdef `operator …;` is native PHP passthrough — do not rewrite.
            var classMatch = OperatorOverloadResolver.SelectMatchingBinaryForm(
                EnumerateClassOperatorOverloads(typeSymbol)
                    .Where(m => m.Operator == op && !m.IsNativePassthrough),
                op,
                left,
                right,
                typeSymbol,
                this.ResolveOperatorExpressionType,
                this.GuessOperandTypeName,
                this.ResolveOperatorUnionMembers,
                this.IsOperatorClassSubtypeOf);
            if (classMatch != null)
            {
                return classMatch;
            }

            return OperatorOverloadResolver.SelectMatchingBinaryForm(
                typeSymbol.ExtensionContributedOperators.Where(
                    m => m.Operator == op && IsActiveExtensionOperator(m, left ?? right)),
                op,
                left,
                right,
                typeSymbol,
                this.ResolveOperatorExpressionType,
                this.GuessOperandTypeName,
                this.ResolveOperatorUnionMembers,
                this.IsOperatorClassSubtypeOf);
        }

        /// <summary>
        /// The arms of a union-typed operator operand (from the checker's recorded type), or
        /// <c>null</c> when the operand is not a union. Lets an overload parameter that accepts every
        /// arm match an operand such as <c>float|int|string</c> or a <c>DecimalCoercible</c> alias.
        /// </summary>
        private IReadOnlyList<OperatorOperandMember>? ResolveOperatorUnionMembers(IExpression? expression)
        {
            if (expression is not IBase2Ast node
                || !this.TryGetCheckedExpressionType(node, out var checkedType))
            {
                return null;
            }

            while (checkedType is NullableCheckedType nullable)
            {
                checkedType = nullable.InnerType;
            }

            return checkedType is UnionCheckedType { Members.Count: > 0 }
                ? TypeInferrer.DescribeOperatorOperandMembers(checkedType, this._context.GlobalScope)
                : null;
        }

        private bool IsOperatorClassSubtypeOf(ObjectDeclarationSymbol child, ObjectDeclarationSymbol parent)
            => TypeComparer.ImplementsOrExtends(
                child,
                parent,
                this._context.GetSymbolTree(),
                this._context.GlobalScope,
                new HashSet<ObjectDeclarationSymbol>());

        private ObjectOperatorOverloadMethodSymbol? FindMatchingBinaryFormForBuiltin(
            BuiltInTypeSymbol typeSymbol,
            OverloadableOperator op,
            IExpression? left,
            IExpression? right)
        {
            // Builtins have no class-level operator members — only extension-contributed forms.
            return OperatorOverloadResolver.SelectMatchingBinaryForm(
                typeSymbol.ExtensionContributedOperators.Where(
                    m => m.Operator == op && IsActiveExtensionOperator(m, left ?? right)),
                op,
                left,
                right,
                typeSymbol,
                this.ResolveOperatorExpressionType,
                this.GuessOperandTypeName,
                this.ResolveOperatorUnionMembers,
                this.IsOperatorClassSubtypeOf);
        }

        private ObjectOperatorOverloadMethodSymbol? FindMatchingUnaryForm(
            ObjectDeclarationSymbol typeSymbol,
            OverloadableOperator op,
            IExpression? operand)
        {
            // Class-level bodyless tyhpdef `operator …;` is native PHP passthrough — do not rewrite.
            var classMatch = OperatorOverloadResolver.SelectMatchingUnaryForm(
                EnumerateClassOperatorOverloads(typeSymbol)
                    .Where(m => m.Operator == op && !m.IsNativePassthrough),
                op,
                operand,
                typeSymbol,
                this.ResolveOperatorExpressionType,
                this.GuessOperandTypeName);
            if (classMatch != null)
            {
                return classMatch;
            }

            return OperatorOverloadResolver.SelectMatchingUnaryForm(
                typeSymbol.ExtensionContributedOperators.Where(
                    m => m.Operator == op && IsActiveExtensionOperator(m, operand)),
                op,
                operand,
                typeSymbol,
                this.ResolveOperatorExpressionType,
                this.GuessOperandTypeName);
        }

        private ObjectOperatorOverloadMethodSymbol? FindMatchingUnaryFormForBuiltin(
            BuiltInTypeSymbol typeSymbol,
            OverloadableOperator op,
            IExpression? operand)
        {
            return OperatorOverloadResolver.SelectMatchingUnaryForm(
                typeSymbol.ExtensionContributedOperators.Where(
                    m => m.Operator == op && IsActiveExtensionOperator(m, operand)),
                op,
                operand,
                typeSymbol,
                this.ResolveOperatorExpressionType,
                this.GuessOperandTypeName);
        }

        /// <summary>
        /// Type resolution for operator-overload matching. Resolves plain <c>$var</c> via typed maps,
        /// property/method types from their declared types, array-element types from
        /// <c>array&lt;T&gt;</c>/<c>array&lt;K,V&gt;</c>, and scalars — without walking through a
        /// member-access base (which would mis-attribute <c>$obj-&gt;scalarProp</c> as <c>$obj</c>).
        /// <c>$this</c> resolves to the enclosing object declaration from <see cref="_classStack"/>.
        /// </summary>
        private IBaseSymbol? ResolveOperatorExpressionType(IExpression? expression)
        {
            if (expression is null)
            {
                return null;
            }

            if (expression is PhpVariableAst thisVar
                && string.Equals(
                    NormalizeVariableName(thisVar.VariableToken?.ValueString ?? thisVar.Identifier),
                    "this",
                    StringComparison.OrdinalIgnoreCase))
            {
                return this._classStack.Count > 0 ? this._classStack.Peek() : null;
            }

            if (expression is PhpDereferenceableAst { Suffix: PhpCallAst } callExpr)
            {
                if (callExpr.BoundSymbol is ObjectMethodSymbol callMethod)
                {
                    return this.ResolveTypeSymbolFromTypeExpression(callMethod.ReturnType);
                }

                // Call that is not a bound method (free function, callable value, …): the checker's
                // result type is the only truth; never fall through to the callee/receiver type.
                return this.TryGetSymbolFromCheckedExpression(expression);
            }

            if (expression is PhpDereferenceableAst
                {
                    Base: IExpression receiver,
                    Suffix: PhpInstanceMemberAccessAst { MemberName: PhpNameAst propName }
                })
            {
                var receiverType = this.ResolveOperatorExpressionType(receiver);
                if (receiverType is ObjectDeclarationSymbol objectDecl)
                {
                    var property = WithKeywordHelper.TryGetProperty(objectDecl, propName.ValueString ?? "");
                    if (property?.DeclaredType != null)
                    {
                        return this.ResolveTypeSymbolFromTypeExpression(property.DeclaredType);
                    }
                }

                return null;
            }

            if (expression is PhpDereferenceableAst { Base: IExpression arrayBase, Suffix: PhpArrayAccessAst })
            {
                // Element type of `array<T>` / `array<K,V>` — never fall through to the array
                // receiver (that would rewrite `$arr[$i] + 1` as an overload on `$arr`).
                var elementTypeExpr = TryGetArrayElementTypeExpression(
                    this.ResolveOperatorExpressionTypeExpression(arrayBase));
                return elementTypeExpr is null
                    ? null
                    : this.ResolveTypeSymbolFromTypeExpression(elementTypeExpr);
            }

            if (expression is PhpDereferenceableAst deref)
            {
                // Other suffixes (static access, etc.): resolve the base only when it is the value.
                return this.ResolveOperatorExpressionType(deref.Base as IExpression);
            }

            // Variables, scalars, new, BoundSymbol — reuse receiver resolver (no member fallthrough).
            return this.ResolveReceiverType(expression) ?? this.ResolveExpressionType(expression);
        }

        /// <summary>
        /// Resolves the declared/inferred <see cref="ITypeExpression"/> for an operator operand so
        /// generic args (especially <c>array&lt;T&gt;</c> / <c>array&lt;K,V&gt;</c>) survive until
        /// array-element typing. Prefers bound declared types, then collected typed-var maps, then
        /// property/method declared return types — without falling through member-access bases.
        /// </summary>
        private ITypeExpression? ResolveOperatorExpressionTypeExpression(IExpression? expression)
        {
            if (expression is null)
            {
                return null;
            }

            if (expression is PhpDereferenceableAst { Suffix: PhpCallAst } callExpr)
            {
                if (callExpr.BoundSymbol is ObjectMethodSymbol callMethod)
                {
                    return callMethod.ReturnType;
                }

                if (callExpr.Base is PhpDereferenceableAst
                    {
                        Base: IExpression callReceiver,
                        Suffix: PhpInstanceMemberAccessAst callMember
                    }
                    && this.GetMemberName(callMember.MemberName) is { } callMethodName
                    && this.ResolveOperatorExpressionType(callReceiver) is ObjectDeclarationSymbol callOwner
                    && TryGetInstanceMethod(callOwner, callMethodName) is { } unboundMethod)
                {
                    return unboundMethod.ReturnType;
                }

                return null;
            }

            if (expression is PhpDereferenceableAst
                {
                    Base: IExpression receiver,
                    Suffix: PhpInstanceMemberAccessAst { MemberName: PhpNameAst propName }
                })
            {
                var receiverType = this.ResolveOperatorExpressionType(receiver);
                if (receiverType is ObjectDeclarationSymbol objectDecl)
                {
                    var property = WithKeywordHelper.TryGetProperty(objectDecl, propName.ValueString ?? "");
                    if (property?.DeclaredType != null)
                    {
                        return property.DeclaredType;
                    }
                }

                return null;
            }

            if (expression is PhpDereferenceableAst { Base: IExpression arrayBase, Suffix: PhpArrayAccessAst })
            {
                return TryGetArrayElementTypeExpression(
                    this.ResolveOperatorExpressionTypeExpression(arrayBase));
            }

            if (expression.BoundSymbol is VariableSymbol { DeclaredType: not null } typedVar)
            {
                return typedVar.DeclaredType;
            }

            if (expression is PhpVariableAst variable)
            {
                var varName = NormalizeVariableName(
                    variable.VariableToken?.ValueString ?? variable.Identifier);
                if (varName is not null)
                {
                    return this.LookupTypedTypeExpression(varName);
                }
            }

            return null;
        }

        /// <summary>
        /// Last type argument of <c>array&lt;T&gt;</c> or <c>array&lt;K,V&gt;</c> (nullable /
        /// <c>T|null</c> wrappers unwrapped). Returns null when the type is not a generic array.
        /// </summary>
        private static ITypeExpression? TryGetArrayElementTypeExpression(ITypeExpression? typeExpr)
            => TryGetGenericBuiltinValueTypeExpression(typeExpr, "array");

        /// <summary>
        /// Last type argument of <c>array&lt;…&gt;</c> or <c>iterable&lt;…&gt;</c> (value last).
        /// </summary>
        private static ITypeExpression? TryGetArrayOrIterableValueTypeExpression(ITypeExpression? typeExpr)
            => TryGetGenericBuiltinValueTypeExpression(typeExpr, "array")
                ?? TryGetGenericBuiltinValueTypeExpression(typeExpr, "iterable");

        private static ITypeExpression? TryGetGenericBuiltinValueTypeExpression(
            ITypeExpression? typeExpr,
            string builtinName)
        {
            foreach (var candidate in EnumerateNonNullTypeParts(typeExpr))
            {
                if (candidate is not PhpBuiltinTypeAst builtin
                    || !string.Equals(builtin.Identifier, builtinName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var args = GetGenericTypeArgumentsFromTypeNode(builtin);
                if (args.Count >= 1)
                {
                    return args[^1];
                }
            }

            return null;
        }

        private static IEnumerable<ITypeExpression> EnumerateNonNullTypeParts(ITypeExpression? typeExpr)
        {
            switch (typeExpr)
            {
                case null:
                    yield break;

                case PhpTypeExpressionAst composite:
                    foreach (var part in composite.Types?.GetAllNotNull() ?? [])
                    {
                        if (part is PhpBuiltinTypeAst { Identifier: "null" or "void" or "never" })
                        {
                            continue;
                        }

                        if (part is ITypeExpression nested)
                        {
                            foreach (var inner in EnumerateNonNullTypeParts(nested))
                            {
                                yield return inner;
                            }
                        }
                    }

                    yield break;

                default:
                    yield return typeExpr;
                    yield break;
            }
        }

        private static IReadOnlyList<ITypeExpression> GetGenericTypeArgumentsFromTypeNode(IBase2Ast? node)
        {
            if (node?.AstGrammarAddons.TryGetValue("typeName", out var addon) == true
                && addon is PhpTypeExpressionListAst list)
            {
                return FlattenTypeArgumentList(list);
            }

            if (node is PhpNamedTypeAst { Name: TyhpGenericIdentifierAst genericOnNamed }
                && genericOnNamed.GenericArguments is PhpTypeExpressionListAst namedArgs)
            {
                return FlattenTypeArgumentList(namedArgs);
            }

            if (node is TyhpGenericIdentifierAst generic
                && generic.GenericArguments is PhpTypeExpressionListAst identifierArgs)
            {
                return FlattenTypeArgumentList(identifierArgs);
            }

            return Array.Empty<ITypeExpression>();
        }

        private static List<ITypeExpression> FlattenTypeArgumentList(PhpTypeExpressionListAst list)
        {
            var raw = list.GetAllNotNull().ToList();
            if (raw.Count == 1
                && raw[0] is PhpTypeExpressionAst { Types: PhpTypeExpressionListAst inner })
            {
                var innerArgs = inner.GetAllNotNull().ToList();
                if (innerArgs.Count > 0)
                {
                    return innerArgs;
                }
            }

            return raw;
        }

        private IEnumerable<ObjectOperatorOverloadMethodSymbol> EnumerateClassOperatorOverloads(
            ObjectDeclarationSymbol typeSymbol)
        {
            var objectScope = FindObjectDeclarationScope(this._context.GlobalScope, typeSymbol);
            if (objectScope != null)
            {
                foreach (var symbol in ((IBaseScope)objectScope).GetAllChildSymbols())
                {
                    if (symbol is ObjectOperatorOverloadMethodSymbol classOverload
                        && !classOverload.IsExtensionOperator)
                    {
                        yield return classOverload;
                    }
                }

                yield break;
            }

            foreach (var member in typeSymbol.Members.Values)
            {
                if (member is ObjectOperatorOverloadMethodSymbol classOverload
                    && !classOverload.IsExtensionOperator)
                {
                    yield return classOverload;
                }
            }
        }

        private static ObjectDeclarationScope? FindObjectDeclarationScope(
            IBaseScope scope,
            ObjectDeclarationSymbol typeSymbol)
        {
            if (scope is ObjectDeclarationScope objectScope
                && ReferenceEquals(objectScope.DeclarationSymbol, typeSymbol))
            {
                return objectScope;
            }

            foreach (var childScope in scope.GetAllChildScopes())
            {
                var found = FindObjectDeclarationScope(childScope, typeSymbol);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private PhpArgumentListAst BuildBinaryOperatorArguments(
            IExpression? left,
            IExpression? right,
            Base2Ast context)
        {
            var args = new List<PhpArgumentAst>();
            if (left != null)
            {
                args.Add(PhpArgumentAst.CreateFromContext(left, context));
            }

            if (right != null)
            {
                args.Add(PhpArgumentAst.CreateFromContext(right, context));
            }

            return PhpArgumentListAst.Create(args, context);
        }

        private PhpArgumentListAst BuildSingleOperandArguments(
            IExpression? operand,
            Base2Ast context)
        {
            var args = new List<PhpArgumentAst>();
            if (operand != null)
            {
                args.Add(PhpArgumentAst.CreateFromContext(operand, context));
            }

            return PhpArgumentListAst.Create(args, context);
        }

        /// <summary>
        /// True when a call-site argument name is the extension receiver. That name is not a
        /// PHP parameter the caller can pass; the receiver is always the first positional argument.
        /// </summary>
        private static bool IsExtensionReceiverArgumentName(string name)
        {
            var bare = name.TrimStart('$');
            return string.Equals(bare, "this", StringComparison.OrdinalIgnoreCase);
        }

        private PhpArgumentListAst BuildReceiverFirstArguments(
            IExpression? receiver,
            PhpArgumentListAst? originalArgs,
            Base2Ast context)
        {
            var args = new List<PhpArgumentAst>();
            if (receiver != null)
            {
                args.Add(PhpArgumentAst.CreateFromContext(receiver, context));
            }

            if (originalArgs != null)
            {
                foreach (var arg in originalArgs.GetAllNotNull())
                {
                    var argName = arg.Name?.ValueString;
                    if (!string.IsNullOrEmpty(argName) && !IsExtensionReceiverArgumentName(argName))
                    {
                        args.Add(PhpArgumentAst.CreateNamedFromContext(
                            arg.Expression,
                            argName.TrimStart('$'),
                            context));
                        continue;
                    }

                    args.Add(PhpArgumentAst.CreateFromContext(arg.Expression, context));
                }
            }

            return PhpArgumentListAst.Create(args, context);
        }

        private string FormatEmittedClassFqn(string? binderFqn, string? fallbackName)
            => EmittedFqnHelper.Format(binderFqn, this._context.Config.NamespacePrefix, fallbackName: fallbackName);

        private PhpDereferenceableAst BuildStaticCall(
            string classFqn,
            string methodName,
            PhpArgumentListAst args,
            Base2Ast context)
        {
            var staticMember = PhpStaticMemberAccessAst.CreateFromContext(
                PhpNameAst.CreateFromContext(methodName, context),
                context);
            var classBase = PhpDereferenceableAst.CreateFromContext(
                PhpNameAst.CreateFromContext(classFqn, context),
                staticMember,
                context);
            return PhpDereferenceableAst.CreateFromContext(
                classBase,
                PhpCallAst.CreateFromContext(args, context),
                context);
        }

        private static IBase2Ast? FindStaticCalleeName(PhpDereferenceableAst staticCall) =>
            staticCall.Base is PhpDereferenceableAst { Suffix: PhpStaticMemberAccessAst staticAccess }
                ? staticAccess.Member
                : null;

        private static void CopyCallSiteTypeArgumentAddons(IBase2Ast? from, IBase2Ast? to)
        {
            if (from is null || to is null)
            {
                return;
            }

            foreach (var key in (string[])["memberName", "identifier"])
            {
                if (from.AstGrammarAddons.TryGetValue(key, out var addon))
                {
                    to.AddGrammarAddon(key, addon);
                }
            }
        }

        private PhpDereferenceableAst BuildInstanceMethodCall(
            IExpression? receiver,
            string methodName,
            PhpArgumentListAst args,
            Base2Ast context)
        {
            if (receiver is not IDereferenceableBase receiverBase)
            {
                throw new InvalidOperationException("Operator overload receiver must be dereferenceable.");
            }

            var accessor = TokenValueAst.CreateFromContext("->", TyhpParser.T_OBJECT_OPERATOR, context);

            var memberAccess = PhpInstanceMemberAccessAst.CreateFromContext(
                accessor,
                PhpNameAst.CreateFromContext(methodName, context),
                context);
            var memberNode = PhpDereferenceableAst.CreateFromContext(receiverBase, memberAccess, context);
            return PhpDereferenceableAst.CreateFromContext(
                memberNode,
                PhpCallAst.CreateFromContext(args, context),
                context);
        }

        private void EnsureImport(string fqn)
        {
            if (string.IsNullOrWhiteSpace(fqn) || this._currentFile == null)
            {
                return;
            }

            this._context.TrackUsedImport(fqn);

            var normalized = fqn.TrimStart('\\');
            foreach (var importList in this._currentFile.FileImports)
            {
                if (importList.GetAllNotNull().Any(i => string.Equals(i.NamespaceName, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    // Already present (e.g. a source `use` for the same extension class). It is still
                    // referenced only via the fully-qualified static call the rewrite emits below, so
                    // mark it for the late pass to drop.
                    this._context.TrackFullyQualifiedStaticCallImport(normalized);
                    return;
                }
            }

            // The rewritten call site uses the fully-qualified name directly
            // (e.g. `\Tyhp\Extensions\StringExtensions::method()`), so the `use` statement added here
            // is redundant. Track it as a fully-qualified static call import so the late import pass
            // drops it from the file header.
            this._context.TrackFullyQualifiedStaticCallImport(normalized);

            var import = PhpImportDeclAst.CreateFromContext(normalized, alias: null, useType: null, this._currentFile.SourceFileAst ?? new TyhpSrcFileAst());
            if (this._currentFile.FileImports.Count == 0)
            {
                this._currentFile.FileImports.Add(new PhpImportDeclListAst());
            }

            var lastList = this._currentFile.FileImports[^1];
            if (lastList is Base2Ast listNode)
            {
                listNode.AddChild(import);
            }
        }

        /// <summary>
        /// The <c>extension Name</c> that emits the PHP backer. Skips a nested
        /// <c>extends Type { }</c> group, which is an object scope but not the class.
        /// </summary>
        private ObjectDeclarationSymbol? GetOwningExtensionClass(ObjectMethodSymbol method)
        {
            for (var scope = method.ContainingScope; scope != null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj
                    && obj.IsExtension
                    && !obj.IsExtensionTargetGroup)
                {
                    return obj;
                }
            }

            return this.GetOwningObjectDeclaration(method);
        }

        private ObjectDeclarationSymbol? GetOwningObjectDeclaration(ObjectMethodSymbol method)
        {
            var scope = method.ContainingScope;
            while (scope != null)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol objectDecl)
                {
                    return objectDecl;
                }

                scope = scope.ParentScope;
            }

            return null;
        }

        private IBaseSymbol? ResolveExpressionType(IExpression? expression)
        {
            if (expression?.BoundSymbol is ObjectDeclarationSymbol objectDecl)
            {
                return objectDecl;
            }

            if (StructEmissionHelper.ResolveStructTypeFromExpression(expression, this._context.GlobalScope)
                is { } structDecl)
            {
                return structDecl;
            }

            if (expression?.BoundSymbol is IBaseSymbol bound)
            {
                return bound;
            }

            if (expression is PhpNameAst name)
            {
                var text = name.ValueString ?? "";
                if (string.Equals(text, "decimal", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, @"\Tyhp\Decimal", StringComparison.OrdinalIgnoreCase))
                {
                    return this.FindTypeSymbol("Decimal");
                }

                return this.FindTypeSymbol(text);
            }

            if (expression is PhpVariableAst)
            {
                return null;
            }

            if (expression is PhpDereferenceableAst deref)
            {
                return this.ResolveExpressionType(deref.Base as IExpression);
            }

            return null;
        }

        /// <summary>
        /// Resolves the type of an extension-method receiver expression. Unlike the shared
        /// <see cref="ResolveExpressionType"/> (kept deliberately conservative so it does not
        /// alter the operator-overload rewrite path), this handles plain <c>$var</c> receivers,
        /// scalar builtins, nullable unwrapping, and already-rewritten chained calls so
        /// <c>$v-&gt;ext1()-&gt;ext2()</c> resolves each hop.
        /// </summary>
        private IBaseSymbol? ResolveReceiverType(IExpression? expression)
        {
            if (expression is null)
            {
                return null;
            }

            if (this.IsUnresolvedCheckedReceiver(expression))
            {
                return null;
            }

            if (this.TryGetSymbolFromCheckedExpression(expression) is { } fromChecked)
            {
                return fromChecked;
            }

            // `$this` is never registered into typed-var maps and has no BoundSymbol — without this,
            // `$this->extensionMethod(...)` (calling an extension method on the enclosing class from
            // one of its own methods) never resolves a receiver and is left uncalled/unrewritten.
            if (expression is PhpVariableAst thisVar
                && string.Equals(
                    NormalizeVariableName(thisVar.VariableToken?.ValueString ?? thisVar.Identifier),
                    "this",
                    StringComparison.OrdinalIgnoreCase))
            {
                return this._classStack.Count > 0 ? this._classStack.Peek() : null;
            }

            // Rewritten (or already-bound) method calls: use the method's return type so chained
            // extension calls like `$v->ext1()->ext2()` (and null-safe ternaries) can resolve the
            // outer receiver. BoundSymbol is stashed on the rewritten static call or ternary.
            if (expression.BoundSymbol is ObjectMethodSymbol boundMethod)
            {
                return this.ResolveTypeSymbolFromTypeExpression(boundMethod.ReturnType);
            }

            if (expression.BoundSymbol is ObjectDeclarationSymbol objectDecl)
            {
                return objectDecl;
            }

            if (expression.BoundSymbol is BuiltInTypeSymbol builtinBound)
            {
                return builtinBound;
            }

            if (StructEmissionHelper.ResolveStructTypeFromExpression(expression, this._context.GlobalScope)
                is { } structDecl)
            {
                return structDecl;
            }

            // VariableSymbol is not itself a type — resolve its DeclaredType (with nullable unwrap).
            if (expression.BoundSymbol is VariableSymbol { DeclaredType: not null } typedVar)
            {
                return this.ResolveTypeSymbolFromTypeExpression(typedVar.DeclaredType);
            }

            if (expression is PhpNameAst name)
            {
                var text = name.ValueString ?? "";
                if (string.Equals(text, "decimal", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(text, @"\Tyhp\Decimal", StringComparison.OrdinalIgnoreCase))
                {
                    return (IBaseSymbol?)this.FindTypeSymbol("Decimal")
                        ?? this.FindBuiltInTypeSymbol("Decimal");
                }

                return (IBaseSymbol?)this.FindTypeSymbol(text)
                    ?? this.FindBuiltInTypeSymbol(text.TrimStart('\\'));
            }

            if (expression is PhpVariableAst variable)
            {
                var varName = NormalizeVariableName(
                    variable.VariableToken?.ValueString ?? variable.Identifier);
                if (varName is not null)
                {
                    if (this.LookupTypedVariable(varName) is { } typed)
                    {
                        return typed;
                    }

                    if (this.LookupObjectTypedVariable(varName) is { } objectTyped)
                    {
                        return objectTyped;
                    }

                    if (this.LookupStructTypedVariable(varName) is { } structTyped)
                    {
                        return structTyped;
                    }
                }

                return null;
            }

            if (expression is PhpScalarAst scalar)
            {
                return this.ResolveScalarLiteralType(scalar);
            }

            // Double/single-quoted string literals (and interpolations) are PhpEncapsListAst,
            // not PhpScalarAst — treat them as `string` for extension-method matching.
            if (expression is PhpEncapsListAst or PhpStringAst)
            {
                return this.FindBuiltInTypeSymbol("string");
            }

            if (expression is PhpArrayAst)
            {
                return this.FindBuiltInTypeSymbol("array");
            }

            if (expression is PhpBinaryOpAst binary)
            {
                return this.ResolveReceiverType(binary.Left)
                    ?? this.ResolveReceiverType(binary.Right);
            }

            if (expression is PhpNewAst newExpr)
            {
                return this.ResolveObjectFromNew(newExpr)
                    ?? this.ResolveStructFromNew(newExpr);
            }

            // Parenthesized expressions from null-safe extension rewrites (and source `(expr)`).
            if (expression is PhpDereferenceableExpressionAst paren)
            {
                return this.ResolveReceiverType(paren.Expression);
            }

            // Null-safe rewrite ternary: prefer BoundSymbol (already checked); fall through to
            // the non-null branch (the static extension call) for return-type resolution.
            if (expression is PhpTernaryOpAst ternary)
            {
                return this.ResolveReceiverType(ternary.FalseExpr);
            }

            // Method call: use return type (bound or looked up on the receiver), never the
            // receiver's own type — `$w->current()` is Money, not Wallet.
            if (expression is PhpDereferenceableAst { Suffix: PhpCallAst } callExpr)
            {
                if (callExpr.Base is PhpDereferenceableAst
                    {
                        Base: IExpression callReceiver,
                        Suffix: PhpInstanceMemberAccessAst callMember
                    }
                    && this.GetMemberName(callMember.MemberName) is { } callMethodName
                    && this.ResolveReceiverType(callReceiver) is ObjectDeclarationSymbol callOwner
                    && TryGetInstanceMethod(callOwner, callMethodName) is { } unboundMethod)
                {
                    return this.ResolveTypeSymbolFromTypeExpression(unboundMethod.ReturnType);
                }

                if (callExpr.Base is PhpNameAst fnName)
                {
                    var fnText = fnName.ValueString ?? fnName.Identifier ?? "";
                    var resolved = this._nameResolver.ResolveSymbol(fnText, this._context.GlobalScope)
                        ?? this.ResolveNamespacedFreeFunction(fnText)
                        ?? this.FindFileScopedFreeFunction(fnText);
                    if (resolved is FunctionDeclarationSymbol fn)
                    {
                        return this.ResolveTypeSymbolFromTypeExpression(fn.ReturnType);
                    }
                }

                return null;
            }

            // Property access: resolve the property's declared type.
            if (expression is PhpDereferenceableAst
                {
                    Base: IExpression propReceiver,
                    Suffix: PhpInstanceMemberAccessAst { MemberName: PhpNameAst propName }
                })
            {
                if (this.ResolveReceiverType(propReceiver) is ObjectDeclarationSymbol propOwner
                    && WithKeywordHelper.TryGetProperty(propOwner, propName.ValueString ?? "")
                        is { DeclaredType: not null } property)
                {
                    return this.ResolveTypeSymbolFromTypeExpression(property.DeclaredType);
                }

                return null;
            }

            return null;
        }

        private static ObjectMethodSymbol? TryGetInstanceMethod(
            ObjectDeclarationSymbol objectDecl,
            string methodName)
        {
            if (objectDecl.Members.TryGetValue(methodName, out var member)
                && member is ObjectMethodSymbol byName)
            {
                return byName;
            }

            foreach (var candidate in objectDecl.Members.Values.OfType<ObjectMethodSymbol>())
            {
                if (string.Equals(candidate.Name, methodName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves a type expression to a symbol suitable for extension-method matching.
        /// Nullable wrappers and <c>T|null</c> unions yield the non-null component so a receiver
        /// typed <c>?string</c> still matches <c>extends string</c>.
        /// </summary>
        private IBaseSymbol? ResolveTypeSymbolFromTypeExpression(ITypeExpression? typeExpr)
        {
            switch (typeExpr)
            {
                case PhpBuiltinTypeAst builtin:
                    return this.FindBuiltInTypeSymbol(builtin.Identifier);

                case PhpNamedTypeAst named:
                {
                    if (named.BoundSymbol is ObjectDeclarationSymbol boundNamed)
                    {
                        return boundNamed;
                    }

                    if (named.BoundSymbol is BuiltInTypeSymbol boundBuiltin)
                    {
                        return boundBuiltin;
                    }

                    if (named.Name is PhpNameAst typeName)
                    {
                        if (typeName.BoundSymbol is ObjectDeclarationSymbol nameBound)
                        {
                            return nameBound;
                        }

                        if (typeName.BoundSymbol is BuiltInTypeSymbol nameBuiltin)
                        {
                            return nameBuiltin;
                        }

                        var text = typeName.ValueString ?? typeName.Identifier;
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            return null;
                        }

                        return (IBaseSymbol?)this.FindTypeSymbol(text)
                            ?? (IBaseSymbol?)this.FindObjectTypeSymbol(text)
                            ?? (IBaseSymbol?)this.FindBuiltInTypeSymbol(text.TrimStart('\\'))
                            ?? StructEmissionHelper.ResolveStructFromNamedType(named, this._context.GlobalScope);
                    }

                    var fallback = named.Name?.Identifier;
                    return string.IsNullOrWhiteSpace(fallback)
                        ? null
                        : (IBaseSymbol?)this.FindTypeSymbol(fallback)
                            ?? (IBaseSymbol?)this.FindBuiltInTypeSymbol(fallback);
                }

                case PhpTypeExpressionAst composite:
                {
                    foreach (var part in composite.Types?.GetAllNotNull() ?? [])
                    {
                        if (part is PhpBuiltinTypeAst { Identifier: "null" or "void" or "never" })
                        {
                            continue;
                        }

                        if (part is ITypeExpression nested
                            && this.ResolveTypeSymbolFromTypeExpression(nested) is { } found
                            && !IsNullTypeSymbol(found))
                        {
                            return found;
                        }
                    }

                    return null;
                }

                default:
                    return this._nameResolver.ResolveType(typeExpr, this._context.GlobalScope) is { } resolved
                        && !IsNullTypeSymbol(resolved)
                        ? resolved
                        : null;
            }
        }

        private static bool IsNullTypeSymbol(IBaseSymbol? symbol) =>
            symbol is BuiltInTypeSymbol { Name: "null" or "void" or "never" };

        private BuiltInTypeSymbol? FindBuiltInTypeSymbol(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var simple = name.TrimStart('\\');
            if (((IBaseScope)this._context.GlobalScope).FindChildSymbolByName(simple) is BuiltInTypeSymbol direct)
            {
                return direct;
            }

            return this._nameResolver.ResolveSymbol(simple, this._context.GlobalScope) as BuiltInTypeSymbol;
        }

        private BuiltInTypeSymbol? ResolveScalarLiteralType(PhpScalarAst scalar) =>
            scalar.ScalarType switch
            {
                PhpScalarType.Integer
                    or PhpScalarType.OctalNumber
                    or PhpScalarType.HexNumber
                    or PhpScalarType.BinaryNumber => this.FindBuiltInTypeSymbol("int"),
                PhpScalarType.Float => this.FindBuiltInTypeSymbol("float"),
                PhpScalarType.String => this.FindBuiltInTypeSymbol("string"),
                _ => null,
            };

        private ObjectDeclarationSymbol? FindTypeSymbol(string name)
        {
            var symbol = this._nameResolver.ResolveSymbol(name, this._context.GlobalScope);
            return symbol as ObjectDeclarationSymbol;
        }

        /// <summary>
        /// Resolves the <see cref="ObjectDeclarationSymbol"/> for an object-type AST node so the
        /// transform walk can push it onto <see cref="_classStack"/>. Prefers a bound symbol or
        /// DeclaringAstNode match (anonymous classes are not file-scoped symbols), then name lookup.
        /// </summary>
        private ObjectDeclarationSymbol? TryResolveObjectDeclarationFromDecl(PhpObjectTypeDeclAst objectDecl)
        {
            if (objectDecl.BoundSymbol is ObjectDeclarationSymbol bound)
            {
                return bound;
            }

            if (FindObjectSymbolByDeclaringAst(this._context.GlobalScope, objectDecl) is { } byDeclaring)
            {
                return byDeclaring;
            }

            var name = objectDecl.Identifier;
            return string.IsNullOrEmpty(name) ? null : this.FindTypeSymbol(name);
        }

        private static ObjectDeclarationSymbol? FindObjectSymbolByDeclaringAst(
            IBaseScope? scope,
            IBase2Ast declaringAst)
        {
            if (scope is null)
            {
                return null;
            }

            if (scope.DeclarationSymbol is ObjectDeclarationSymbol scoped
                && ReferenceEquals(scoped.DeclaringAstNode, declaringAst))
            {
                return scoped;
            }

            foreach (var childSymbol in scope.GetAllChildSymbols())
            {
                if (childSymbol is ObjectDeclarationSymbol child
                    && ReferenceEquals(child.DeclaringAstNode, declaringAst))
                {
                    return child;
                }
            }

            foreach (var childScope in scope.GetAllChildScopes())
            {
                if (FindObjectSymbolByDeclaringAst(childScope, declaringAst) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        private string? GetMemberName(IExpression? memberName)
        {
            return memberName switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                _ => null,
            };
        }

        private string GuessOperandTypeName(IExpression? expression)
        {
            if (expression is PhpNameAst name)
            {
                var text = name.ValueString ?? "";
                if (string.Equals(text, "decimal", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Decimal", StringComparison.Ordinal))
                {
                    return "Decimal";
                }

                return text.TrimStart('\\').Split('\\')[^1];
            }

            if (expression is PhpScalarAst scalar)
            {
                if (scalar.ScalarType == PhpScalarType.Float)
                {
                    return "Float";
                }

                if (scalar.ScalarType is PhpScalarType.Integer or PhpScalarType.OctalNumber
                    or PhpScalarType.HexNumber or PhpScalarType.BinaryNumber
                    || scalar.ValueInt64.HasValue)
                {
                    return "Int";
                }

                if (scalar.ScalarType == PhpScalarType.String)
                {
                    return "String";
                }
            }

            if (expression is PhpBuiltinTypeAst builtin)
            {
                var builtinName = builtin.Identifier ?? "Mixed";
                if (builtinName.Length == 0)
                {
                    return "Mixed";
                }

                return char.ToUpperInvariant(builtinName[0]) + builtinName[1..];
            }

            // Prefer operator-aware resolution so `$obj->intProp` spells as Int/int, not the
            // receiver class (ResolveReceiverType falls through member-access bases).
            var resolved = this.ResolveOperatorExpressionType(expression);
            if (resolved is BuiltInTypeSymbol builtinSym)
            {
                var n = builtinSym.Name ?? "Mixed";
                return n.Length == 0 ? "Mixed" : char.ToUpperInvariant(n[0]) + n[1..];
            }

            if (resolved is ObjectDeclarationSymbol obj)
            {
                return obj.Name;
            }

            var receiver = this.ResolveReceiverType(expression);
            if (receiver is BuiltInTypeSymbol receiverBuiltin)
            {
                var n = receiverBuiltin.Name ?? "Mixed";
                return n.Length == 0 ? "Mixed" : char.ToUpperInvariant(n[0]) + n[1..];
            }

            if (receiver is ObjectDeclarationSymbol receiverObj)
            {
                return receiverObj.Name;
            }

            var type = this.ResolveExpressionType(expression);
            return type?.Name ?? "Mixed";
        }
    }
}
