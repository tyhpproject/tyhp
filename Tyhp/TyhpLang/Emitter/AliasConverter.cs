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
using Tyhp.TyhpLang.Emitter.NameGeneration;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    /// <summary>
    /// AST-level pre-pass that rewrites Tyhp-specific constructs into PHP-compatible forms before emission.
    /// </summary>
    internal sealed class AliasConverter
    {
        private readonly EmitContext _context;
        private readonly NameResolver _nameResolver;
        private PHPOutputFile? _currentFile;
        private bool _reportedStructBackingError;

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

        // Tracks the enclosing function-like node during the transform walk so struct-typed
        // variable lookups resolve against the correct scope frame.
        private readonly Stack<IBase2Ast> _functionStack = new();

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

        // Member names that follow `->` (properties/methods) must never be rewritten through the
        // tyhpdef alias map. The map is case-insensitive (PHP class names are), so a member like
        // `$this->promise` would otherwise collide with a same-named class alias (`Promise`) and
        // miscompile to `$this->Tyhp\Promise`. Collected up front by reference identity because the
        // tree walk transforms member-name children before reaching their member-access parent.
        private readonly HashSet<IBase2Ast> _protectedMemberNames =
            new(ReferenceEqualityComparer.Instance);

        public AliasConverter(EmitContext context)
        {
            this._context = context;
            var symbolTree = context.GetSymbolTree();
            this._nameResolver = new NameResolver(symbolTree, context.Diagnostics);
        }

        public void Convert(PHPOutputFile outputFile)
        {
            this._currentFile = outputFile;
            this._reportedStructBackingError = false;

            // Property-hook lowering for PHP < 8.4 lives in TyhpEmitter.PropertyAccessors.cs
            // (trait injection, registration, and $this->prop → tyhpGet/SetBacking rewrite at emit time).
            _ = this._context.Config.TargetPhpVersion;

            this._protectedMemberNames.Clear();
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

            for (var i = 0; i < outputFile.Statements.Count; i++)
            {
                if (outputFile.Statements[i] is IBase2Ast statement)
                {
                    outputFile.Statements[i] = (ITopStatement)AstWalker.TransformTree(
                        statement,
                        this.TransformNode,
                        this.PreTransformWith)!;
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

            // Expand statement-context object `with` inside blocks before children are walked.
            if (node is PhpStatementBlockAst block)
            {
                this.ExpandStatementBlockInPlace(block);
                return null;
            }

            if (node is not PhpBinaryOpAst binary
                || !StructEmissionHelper.IsWithOperator(binary.Operator))
            {
                return null;
            }

            // Struct path (array-backed only).
            if (this._context.IsStructBackedByArray() && this.IsStructWithLeft(binary.Left))
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
                    isArrayBacked: true,
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

            // Structs keep the expression rewrite (merged array / array_replace); do not expand.
            if (this.IsStructWithLeft(withBinary.Left))
            {
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
                return this.ResolveStructType(unary.Operand) is not null;
            }

            return this.ResolveStructType(left) is not null;
        }

        private void TransformWithListValues(PhpArrayPairListAst pairList)
        {
            foreach (var pair in pairList.GetAllNotNull())
            {
                if (pair.ValueExpr is not IBase2Ast value || pair is not Base2Ast pairNode)
                {
                    continue;
                }

                var transformed = AstWalker.TransformTree(value, this.TransformNode, this.PreTransformWith);
                pairNode.ReplaceChild(value, transformed);
            }
        }

        private IBase2Ast? TransformNode(IBase2Ast node)
        {
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
                case PhpMemberAccessAst memberAccess when memberAccess.Key is PhpNameAst keyName:
                    this._protectedMemberNames.Add(keyName);
                    break;
            }
        }

        private IBase2Ast TransformTyhpdefAliasName(PhpNameAst name)
        {
            var text = name.ValueString ?? "";

            // Member positions (`->name` / `$arr->key`) are protected from the free-name alias map
            // so a class alias like `Promise` cannot rewrite `$this->promise`. They still erase
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
                if (this._context.IsStructBackedByArray())
                {
                    return PhpBuiltinTypeAst.Create("array", namedType);
                }

                // Custom backing: rewrite the type name in place so the node remains an
                // ITypeExpression (PhpNamedTypeAst) that emission can spell.
                var backing = StructEmissionHelper.NormalizeBackingClassName(this._context.GetStructBacking());
                var resolved = this.ResolveBackingClass(backing);
                var fqn = resolved?.FullyQualifiedName ?? backing;
                if (!fqn.StartsWith('\\'))
                {
                    fqn = "\\" + fqn;
                }

                if (resolved is null)
                {
                    this.ReportStructBackingErrorOnce(namedType, backing);
                }

                var nameNode = PhpNameAst.CreateFromContext(fqn, namedType);
                if (namedType is Base2Ast baseNode)
                {
                    if (namedType.Name is IBase2Ast oldName)
                    {
                        baseNode.ReplaceChild(oldName, nameNode);
                    }
                    else
                    {
                        baseNode.ReplaceChildAt(0, nameNode);
                    }
                }

                // Drop the struct BoundSymbol so TypeSpellingHelper does not force-spell this as
                // `array` and instead emits the configured backing class name.
                namedType.BoundSymbol = null;
                nameNode.BoundSymbol = null;

                return namedType;
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
                this._context,
                this.ResolveBackingClass,
                ref this._reportedStructBackingError,
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
                this._context.IsStructBackedByArray(),
                out var rewritten))
            {
                return rewritten;
            }

            return null;
        }

        private void ReportStructBackingErrorOnce(Base2Ast context, string backingName)
        {
            if (this._reportedStructBackingError)
            {
                return;
            }

            this._reportedStructBackingError = true;
            var fileName = context.OwningFile?.Identifier
                ?? this._context.CurrentSourceFile?.Identifier
                ?? "";
            this._context.Diagnostics.AddErrorFromAst(
                MessageCode.EmitterStructBackingError,
                context,
                fileName,
                backingName);
        }

        private ObjectDeclarationSymbol? ResolveBackingClass(string backingName)
        {
            var normalized = backingName.TrimStart('\\');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (normalized.Contains('\\'))
            {
                var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (this._nameResolver.ResolveRelativeName(segments, this._context.GlobalScope)
                    is ObjectDeclarationSymbol qualified)
                {
                    return qualified;
                }
            }

            return this._nameResolver.ResolveSymbol(normalized.Split('\\')[^1], this._context.GlobalScope)
                as ObjectDeclarationSymbol;
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
            if (expression is PhpVariableAst variable)
            {
                var varName = NormalizeVariableName(
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

            var methodSymbol = this.ResolveExtensionMethodSymbol(callNode, memberNode, memberAccess, methodName);
            if (methodSymbol is null)
            {
                return false;
            }

            var extensionClass = this.GetOwningObjectDeclaration(methodSymbol);
            if (extensionClass == null || !extensionClass.IsExtension)
            {
                return false;
            }

            var extensionFqn = this.FormatEmittedClassFqn(
                extensionClass.FullyQualifiedName,
                extensionClass.Name);

            this.EnsureImport(extensionFqn);

            var isNullSafe = memberAccess.Accessor?.ValueInt64 == TyhpParser.T_NULLSAFE_OBJECT_OPERATOR;
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
                    methodSymbol);
                return true;
            }

            var args = this.BuildReceiverFirstArguments(memberNode.Base as IExpression, call.Arguments, callNode);
            var staticCall = this.BuildStaticCall(extensionFqn, methodName, args, callNode);
            // Stash the extension method so a later hop in `$v->a()->b()` can resolve `a`'s return
            // type after this node has been rewritten to a static call.
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
            ObjectMethodSymbol methodSymbol)
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

            var receiverType = this.ResolveReceiverType(memberNode.Base as IExpression);
            if (receiverType is null)
            {
                return null;
            }

            return this._nameResolver.ResolveExtensionMethod(methodName, receiverType) as ObjectMethodSymbol;
        }

        private bool TryRewriteStructPropertyAccess(PhpDereferenceableAst accessNode, out PhpDereferenceableAst rewritten)
        {
            rewritten = accessNode;

            // Custom backing keeps object property access (`->`).
            if (!this._context.IsStructBackedByArray())
            {
                return false;
            }

            if (accessNode.Suffix is not PhpInstanceMemberAccessAst memberAccess)
            {
                return false;
            }

            var structDecl = this.ResolveStructType(accessNode.Base as IExpression);
            if (structDecl is null)
            {
                return false;
            }

            var memberName = this.GetMemberName(memberAccess.MemberName);
            var propertyKey = StructEmissionHelper.ResolveStructPropertyKey(structDecl, memberName);
            if (propertyKey is null)
            {
                return false;
            }

            var keyExpr = propertyKey.Value.ToScalarAst(accessNode);
            rewritten = PhpDereferenceableAst.CreateFromContext(
                accessNode.Base!,
                PhpArrayAccessAst.CreateFromContext(keyExpr, accessNode),
                accessNode);

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
                op = OverloadableOperatorHelper.FromAssignmentToken(token);
                if (op == OverloadableOperator.Invalid)
                {
                    return false;
                }

                isCompoundAssign = true;
            }

            // All operator methods are static now: pick the declaring class (left operand first,
            // then right) whose operator declares a form matching the operand combination, then emit
            // `\Type::__op($left, $right)`.
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
            out string? classFqn,
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
            OverloadableOperator op)
        {
            var methodName = OperatorMethodNameGenerator.GetMethodName(op);
            if (string.IsNullOrEmpty(methodName))
            {
                return (null, null);
            }

            string binderFqn;
            if (form.IsExtensionOperator)
            {
                // Standalone `extension E { operator +<T>(…) }` methods are emitted on E.
                // Tyhpdef inline `extension operator` methods are emitted on the owner class
                // (Story 11); prefer ExtensionTargetSymbol / synthetic scope only for those.
                if (form.DeclaringExtensionSymbol is { IsCompilerGenerated: false } standaloneExt)
                {
                    binderFqn = standaloneExt.FullyQualifiedName;
                }
                else if (form.ExtensionTargetSymbol != null)
                {
                    // Inline extension operator: library consumers rewrite to `\Owner::__add`
                    // rather than a synthetic binder scope that may not ship as PHP.
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
            var isAlternate = token is TyhpParser.T_SYM_PLUS or TyhpParser.T_SYM_MINUS;
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

        private bool TryRewriteCastConversion(PhpUnaryOpAst unary, out IBase2Ast rewritten)
        {
            rewritten = unary;

            var targetKey = GetCastTargetTypeKey(unary.Operator);
            if (targetKey == null)
            {
                return false;
            }

            var operandClass = this.ResolveOperatorOperandType(unary.Operand);
            if (operandClass == null)
            {
                return false;
            }

            if (!this.ClassHasConvertToOverload(operandClass, targetKey)
                && !(operandClass.ObjectKind == PhpTypeDeclType.Trait
                    && this.TraitComposingClassHasConvertToOverload(operandClass, targetKey)))
            {
                return false;
            }

            var methodName = OperatorMethodNameGenerator.GetConvertToMethodName(targetKey);
            var emptyArgs = PhpArgumentListAst.Create([], unary);
            // Instance dispatch on `$this` late-binds to the composing class at runtime.
            rewritten = this.BuildInstanceMethodCall(unary.Operand, methodName, emptyArgs, unary);
            return true;
        }

        private bool TraitComposingClassHasConvertToOverload(
            ObjectDeclarationSymbol trait,
            string targetKey)
        {
            foreach (var composing in this.EnumerateObjectsUsingTrait(trait))
            {
                if (this.ClassHasConvertToOverload(composing, targetKey))
                {
                    return true;
                }
            }

            return false;
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

        private bool ClassHasConvertToOverload(ObjectDeclarationSymbol typeSymbol, string targetKey)
        {
            foreach (var overload in this.EnumerateClassOperatorOverloads(typeSymbol)
                .Concat(typeSymbol.ExtensionContributedOperators))
            {
                if (overload.IsNativePassthrough
                    || overload.Operator != OverloadableOperator.Convert
                    || overload.Parameters.Count != 1)
                {
                    continue;
                }

                // convert-to: sole parameter is self; return type matches the cast target.
                if (!OperatorOverloadResolver.IsConvertToForm(overload, typeSymbol))
                {
                    continue;
                }

                var returnKey = OperatorOverloadResolver.SpellTypeKey(
                    overload.ReturnType, typeSymbol.Name);
                if (string.Equals(returnKey, targetKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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

                // Story 16 Phase 1: fn => new \Tyhp\PropertyPath(...)
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

                // Story 16 Phase 2: fn => new \Tyhp\Expression(...)
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
                parameters = boundMethod.Parameters;
                selfContext = this.GetOwningObjectDeclaration(boundMethod);
                return parameters.Count > 0;
            }

            if (callNode.BoundSymbol is FunctionDeclarationSymbol boundFunction)
            {
                parameters = boundFunction.Parameters;
                return parameters.Count > 0;
            }

            // Free function: `foo(...)` — binder does not bind call-site names.
            if (callNode.Base is PhpNameAst nameAst)
            {
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
            // also accepts a composing class's convert-to.
            var operandClass = this.ResolveOperatorOperandType(expression);
            if (operandClass is not null
                && (expectedObject is null || !ReferenceEquals(operandClass, expectedObject))
                && (this.ClassHasConvertToOverload(operandClass, targetKey)
                    || (operandClass.ObjectKind == PhpTypeDeclType.Trait
                        && this.TraitComposingClassHasConvertToOverload(operandClass, targetKey))))
            {
                var methodName = OperatorMethodNameGenerator.GetConvertToMethodName(targetKey);
                var receiver = EnsureDereferenceableReceiver(expression, context);
                var emptyArgs = PhpArgumentListAst.Create([], context);
                rewritten = this.BuildInstanceMethodCall(receiver, methodName, emptyArgs, context);
                return true;
            }

            // convert-from: scalar/other source where an object type with matching __from is expected.
            if (expectedObject is not null
                && !expectedObject.IsStruct
                && (operandClass is null || !ReferenceEquals(operandClass, expectedObject))
                && this.TryFindConvertFromOverload(expectedObject, expression, out _))
            {
                var classFqn = this.FormatEmittedClassFqn(
                    expectedObject.FullyQualifiedName,
                    expectedObject.Name);
                var argList = this.BuildSingleOperandArguments(expression, context);
                rewritten = this.BuildStaticCall(
                    classFqn,
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

            if (unwrapped is PhpTypeExpressionAst composite
                && composite.TypeKind == PhpTypeKind.Union)
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
            if (type is PhpTypeExpressionAst composite && composite.IsNullable)
            {
                return composite.Types?.GetAllNotNull().FirstOrDefault() ?? type;
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

        private bool TryFindConvertFromOverload(
            ObjectDeclarationSymbol typeSymbol,
            IExpression? sourceExpression,
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
                .Concat(typeSymbol.ExtensionContributedOperators))
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
            var isAlternate = token is TyhpParser.T_SYM_PLUS or TyhpParser.T_SYM_MINUS;
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
            var isAlternate = token is TyhpParser.T_SYM_PLUS or TyhpParser.T_SYM_MINUS;
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
                    (int)(bare.Operator?.ValueInt64 ?? -1)) != OverloadableOperator.Invalid)
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
                this.GuessOperandTypeName);
            if (classMatch != null)
            {
                return classMatch;
            }

            return OperatorOverloadResolver.SelectMatchingBinaryForm(
                typeSymbol.ExtensionContributedOperators.Where(m => m.Operator == op),
                op,
                left,
                right,
                typeSymbol,
                this.ResolveOperatorExpressionType,
                this.GuessOperandTypeName);
        }

        private ObjectOperatorOverloadMethodSymbol? FindMatchingBinaryFormForBuiltin(
            BuiltInTypeSymbol typeSymbol,
            OverloadableOperator op,
            IExpression? left,
            IExpression? right)
        {
            // Builtins have no class-level operator members — only extension-contributed forms.
            return OperatorOverloadResolver.SelectMatchingBinaryForm(
                typeSymbol.ExtensionContributedOperators.Where(m => m.Operator == op),
                op,
                left,
                right,
                typeSymbol,
                this.ResolveOperatorExpressionType,
                this.GuessOperandTypeName);
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
                typeSymbol.ExtensionContributedOperators.Where(m => m.Operator == op),
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
                typeSymbol.ExtensionContributedOperators.Where(m => m.Operator == op),
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

                // Unbound call: do not fall through to the receiver type.
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
        {
            foreach (var candidate in EnumerateNonNullTypeParts(typeExpr))
            {
                if (candidate is not PhpBuiltinTypeAst { Identifier: "array" } arrayType)
                {
                    continue;
                }

                var args = GetGenericTypeArgumentsFromTypeNode(arrayType);
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
                args.AddRange(originalArgs.GetAllNotNull().Select(a => PhpArgumentAst.CreateFromContext(a.Expression, context)));
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
