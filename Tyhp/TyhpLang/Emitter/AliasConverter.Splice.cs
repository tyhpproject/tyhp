using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Emitter.Splice;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    internal sealed partial class AliasConverter
    {
        private SpliceEngineContext CreateSpliceContext() =>
            new()
            {
                ResolveFreeFunction = name =>
                {
                    if (name.BoundSymbol is FunctionDeclarationSymbol bound)
                    {
                        return bound.Parameters;
                    }

                    var resolved = this._nameResolver.ResolveSymbol(
                        name.ValueString ?? name.Identifier ?? "",
                        this._context.GlobalScope);
                    return (resolved as FunctionDeclarationSymbol)?.Parameters;
                },
            };

        private void TransformStatementBlockWithSpliceHoists(PhpStatementBlockAst block)
        {
            var original = block.GetAllNotNull().Cast<IBase2Ast>().ToList();
            var rebuilt = new List<IBase2Ast?>();
            var changed = false;
            foreach (var statement in original)
            {
                this._pendingSpliceHoists.Clear();
                this.CollectOccupiedNames(statement);
                var transformed = AstWalker.TransformTree(
                    statement,
                    this.TransformNode,
                    this.PreTransformWith);
                if (this._pendingSpliceHoists.Count > 0)
                {
                    changed = true;
                    rebuilt.AddRange(this._pendingSpliceHoists.Cast<IBase2Ast>());
                    this._pendingSpliceHoists.Clear();
                }

                if (!ReferenceEquals(transformed, statement))
                {
                    changed = true;
                }

                rebuilt.Add(transformed);
            }

            if (!changed)
            {
                return;
            }

            block.ClearChildren();
            foreach (var child in rebuilt)
            {
                block.AddChild(child);
            }
        }

        private void CollectOccupiedNames(IBase2Ast node)
        {
            if (this._spliceContext is null)
            {
                return;
            }

            AstWalker.Walk(node, child =>
            {
                if (child is PhpVariableAst variable)
                {
                    var name = CheckerHelpers.GetVariableName(variable);
                    if (!string.IsNullOrEmpty(name))
                    {
                        this._spliceContext.OccupiedVariableNames.Add(name);
                    }
                }

                if (child is PhpParameterAst param)
                {
                    var name = SpliceAst.NormalizeName(param.Name);
                    if (name.Length > 0)
                    {
                        this._spliceContext.OccupiedVariableNames.Add(name);
                    }
                }
            });
        }

        private void CollectRefHoistForbidden(IEnumerable<IBase2Ast> roots)
        {
            foreach (var root in roots)
            {
                this.CollectRefHoistForbiddenNode(root, conditionallyEvaluated: false);
            }
        }

        private void CollectRefHoistForbiddenNode(IBase2Ast node, bool conditionallyEvaluated)
        {
            if (conditionallyEvaluated)
            {
                this._refHoistForbidden.Add(node);
            }

            switch (node)
            {
                case PhpTernaryOpAst ternary:
                    if (ternary.Condition is IBase2Ast cond)
                    {
                        this.CollectRefHoistForbiddenNode(cond, conditionallyEvaluated);
                    }

                    if (ternary.TrueExpr is IBase2Ast t)
                    {
                        this.CollectRefHoistForbiddenNode(t, conditionallyEvaluated: true);
                    }

                    if (ternary.FalseExpr is IBase2Ast f)
                    {
                        this.CollectRefHoistForbiddenNode(f, conditionallyEvaluated: true);
                    }

                    return;

                case PhpBinaryOpAst binary when IsShortCircuitBinary(binary):
                    if (binary.Left is IBase2Ast left)
                    {
                        this.CollectRefHoistForbiddenNode(left, conditionallyEvaluated);
                    }

                    if (binary.Right is IBase2Ast right)
                    {
                        this.CollectRefHoistForbiddenNode(right, conditionallyEvaluated: true);
                    }

                    return;

                case PhpLoopAst loop:
                    if (loop.Condition is IBase2Ast loopCond)
                    {
                        this.CollectRefHoistForbiddenNode(loopCond, conditionallyEvaluated: true);
                    }

                    if (loop.TestExpressions is IBase2Ast test)
                    {
                        this.CollectRefHoistForbiddenNode(test, conditionallyEvaluated: true);
                    }

                    break;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    this.CollectRefHoistForbiddenNode(child, conditionallyEvaluated);
                }
            }
        }

        private bool TrySpliceExtensionCall(
            PhpDereferenceableAst callNode,
            IExpression? receiver,
            PhpArgumentListAst? originalArgs,
            ObjectMethodSymbol methodSymbol,
            ObjectDeclarationSymbol extensionClass,
            out IBase2Ast rewritten)
        {
            rewritten = callNode;
            var spliceSymbol = SpliceAst.ImplementationForSplice(methodSymbol) as ObjectMethodSymbol
                ?? methodSymbol;
            if (this._context.RequiresGenericVariantFor(spliceSymbol)
                || this._context.RequiresGenericVariantFor(methodSymbol)
                || this._context.HasForeignGenericRuntime(spliceSymbol)
                || this._context.HasForeignGenericRuntime(methodSymbol))
            {
                // Mechanism D / foreign Generic::bind need the bound type at the call site.
                // Splicing the body would leave the callee's type parameter unbound, and a
                // stamped compiled-library member must go through Generic::bind.
                return false;
            }

            if (this._spliceEngine is null
                || !SpliceAst.TryGetSingleReturnExpression(spliceSymbol.DeclaringAstNode, out var body))
            {
                return false;
            }

            var args = new List<IExpression?>();
            if (originalArgs is not null)
            {
                foreach (var arg in originalArgs.GetAllNotNull())
                {
                    args.Add(arg.Expression);
                }
            }

            var spliceClass = extensionClass.InlineExtensionReceiverClass ?? extensionClass;
            var request = new SpliceRequest
            {
                Callee = spliceSymbol,
                CallSite = callNode,
                Body = body,
                Parameters = SpliceAst.ParametersFromSymbol(spliceSymbol.Parameters, firstIsThis: true),
                Receiver = receiver,
                Arguments = args,
                HasPhpBacker = SpliceAst.MemberHasPhpBacker(spliceSymbol.DeclaringAstNode, extensionClass),
                HasStatementSlot = !this._refHoistForbidden.Contains(callNode),
                DeclaringClassFqn = spliceClass.FullyQualifiedName,
                ParentClassFqn = CallSiteSpliceEngine.ParentFqn(spliceClass),
                CalleeVisibility = MemberModifier.Public,
                DeclaringClass = spliceClass,
            };

            var result = this._spliceEngine.TrySplice(request);
            if (!result.Success || result.Expression is null)
            {
                return false;
            }

            foreach (var hoist in result.HoistStatements)
            {
                this._pendingSpliceHoists.Add(hoist);
            }

            var transformed = AstWalker.TransformTree(
                result.Expression,
                this.TransformNode,
                this.PreTransformWith);
            rewritten = transformed ?? result.Expression;
            return true;
        }

        private bool TrySpliceBinaryOperator(
            OverloadableOperator op,
            PhpBinaryOpAst binary,
            out IBase2Ast rewritten)
        {
            rewritten = binary;
            if (this._spliceEngine is null)
            {
                return false;
            }

            ObjectOperatorOverloadMethodSymbol? form = null;
            ObjectDeclarationSymbol? owner = null;
            var resolved = this.ResolveOperatorExpressionType(binary.Left);
            if (resolved is ObjectDeclarationSymbol objectDecl && !objectDecl.IsStruct)
            {
                this.TryFindBinaryFormOnTypeOrComposingClasses(
                    objectDecl, op, binary.Left, binary.Right, out form, out var owningType, out _);
                owner = owningType as ObjectDeclarationSymbol ?? objectDecl;
            }
            else if (resolved is BuiltInTypeSymbol builtin)
            {
                form = this.FindMatchingBinaryFormForBuiltin(builtin, op, binary.Left, binary.Right);
                owner = form is null ? null : CallSiteSpliceEngine.OwnerOf(form);
            }

            if (form is null
                || !form.IsExtensionOperator
                || !SpliceAst.TryGetSingleReturnExpression(form.DeclaringAstNode, out var body))
            {
                return false;
            }

            owner ??= CallSiteSpliceEngine.OwnerOf(form);
            var spliceClass = form.ExtensionTargetSymbol as ObjectDeclarationSymbol
                ?? owner?.InlineExtensionReceiverClass
                ?? owner;
            var request = new SpliceRequest
            {
                Callee = form,
                CallSite = binary,
                Body = body,
                Parameters = SpliceAst.ParametersFromSymbol(form.Parameters, firstIsThis: false),
                Receiver = null,
                Arguments = [binary.Left, binary.Right],
                HasPhpBacker = SpliceAst.MemberHasPhpBacker(form.DeclaringAstNode, owner),
                HasStatementSlot = !this._refHoistForbidden.Contains(binary),
                DeclaringClassFqn = spliceClass?.FullyQualifiedName,
                ParentClassFqn = CallSiteSpliceEngine.ParentFqn(spliceClass),
                CalleeVisibility = MemberModifier.Public,
                DeclaringClass = spliceClass,
            };

            var result = this._spliceEngine.TrySplice(request);
            if (!result.Success || result.Expression is null)
            {
                return false;
            }

            foreach (var hoist in result.HoistStatements)
            {
                this._pendingSpliceHoists.Add(hoist);
            }

            var transformed = AstWalker.TransformTree(
                result.Expression,
                this.TransformNode,
                this.PreTransformWith);
            rewritten = transformed ?? result.Expression;
            return true;
        }

        private bool TrySpliceUnaryOperator(
            OverloadableOperator op,
            PhpUnaryOpAst unary,
            out IBase2Ast rewritten)
        {
            rewritten = unary;
            if (this._spliceEngine is null || unary.Operand is null)
            {
                return false;
            }

            if (!this.TryFindMatchingUnaryOverload(
                    unary.Operand, op, out var form, out var owningType, out _))
            {
                return false;
            }

            if (form is null
                || !form.IsExtensionOperator
                || !SpliceAst.TryGetSingleReturnExpression(form.DeclaringAstNode, out var body))
            {
                return false;
            }

            var owner = owningType as ObjectDeclarationSymbol ?? CallSiteSpliceEngine.OwnerOf(form);
            var spliceClass = form.ExtensionTargetSymbol as ObjectDeclarationSymbol
                ?? owner?.InlineExtensionReceiverClass
                ?? owner;
            var request = new SpliceRequest
            {
                Callee = form,
                CallSite = unary,
                Body = body,
                Parameters = SpliceAst.ParametersFromSymbol(form.Parameters, firstIsThis: false),
                Receiver = null,
                Arguments = [unary.Operand],
                HasPhpBacker = SpliceAst.MemberHasPhpBacker(form.DeclaringAstNode, owner),
                HasStatementSlot = !this._refHoistForbidden.Contains(unary),
                DeclaringClassFqn = spliceClass?.FullyQualifiedName,
                ParentClassFqn = CallSiteSpliceEngine.ParentFqn(spliceClass),
                CalleeVisibility = MemberModifier.Public,
                DeclaringClass = spliceClass,
            };

            var result = this._spliceEngine.TrySplice(request);
            if (!result.Success || result.Expression is null)
            {
                return false;
            }

            foreach (var hoist in result.HoistStatements)
            {
                this._pendingSpliceHoists.Add(hoist);
            }

            var transformed = AstWalker.TransformTree(
                result.Expression,
                this.TransformNode,
                this.PreTransformWith);
            rewritten = transformed ?? result.Expression;
            return true;
        }
    }
}
