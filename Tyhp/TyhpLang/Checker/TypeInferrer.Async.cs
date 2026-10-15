using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    public sealed partial class TypeInferrer
    {
        private ICheckedType InferAsyncBlock(TyhpAsyncBlockAst block, CheckerState state)
        {
            var blockState = state.Split(ScopeType.AnonymousFunctionDeclaration);
            blockState.IsInAsyncContext = true;
            blockState.IsInsideClosure = true;
            blockState.IsTypeGuardFunction = false;
            blockState.IsInLoopContext = false;
            blockState.LoopDepth = 0;
            blockState.IsInSwitchContext = false;
            blockState.HasReturnedOnAllPaths = false;
            blockState.ExpectedClosureType = null;
            blockState.IsAsyncReturnScope = true;
            blockState.AsyncReturnOwner = block;
            blockState.ExpectedReturnType =
                TryUnwrapPromiseLike(state.ExpectedReturnType)
                ?? TryUnwrapPromiseLike(state.ExpectedClosureType);
            if (blockState.ExpectedReturnType is not null)
            {
                blockState.ExpectedReturnType = UtilityTypeResolver.Settle(
                    blockState.ExpectedReturnType, _globalScope);
            }

            ClosureRule.PrepareAsyncBlockCaptures(block.Body, blockState, state);

            if (block.Body is not null)
            {
                _checker.CheckNode(block.Body, blockState);
            }

            _checker.RecordGenericCallTargetsIn(block, blockState);

            var inner = blockState.ExpectedReturnType
                ?? UnionReturnedTypes(block.Body)
                ?? CheckedTypes.Void;
            return WrapAsPromise(UtilityTypeResolver.Settle(inner, _globalScope));
        }

        private static ICheckedType? TryUnwrapPromiseLike(ICheckedType? type)
        {
            if (type is GenericCheckedType generic && generic.TypeArguments.Count > 0
                && IsPromiseLikeName(generic.BaseType.DisplayName))
            {
                return generic.TypeArguments[0];
            }

            return null;
        }

        private static bool IsPromiseLikeName(string name)
        {
            var simple = name.TrimStart('\\');
            var slash = simple.LastIndexOf('\\');
            if (slash >= 0)
            {
                simple = simple[(slash + 1)..];
            }

            return simple.Equals("Promise", StringComparison.OrdinalIgnoreCase)
                || simple.Equals("self", StringComparison.OrdinalIgnoreCase)
                || simple.Equals("static", StringComparison.OrdinalIgnoreCase);
        }

        private ICheckedType WrapAsPromise(ICheckedType inner)
        {
            var promise = CheckerHelpers.ResolveNamedType("Tyhp\\Promise", _symbolTree, _globalScope);
            var baseType = promise is GenericCheckedType generic ? generic.BaseType : promise;
            return new GenericCheckedType(baseType, [inner]);
        }

        /// <summary>
        /// An <c>async</c> callable's declared return type <c>R</c> is implicitly
        /// <c>__Awaited&lt;R&gt;</c>, so a call (without <c>await</c>) yields
        /// <c>Promise&lt;__Awaited&lt;R&gt;&gt;</c> — including when <c>R</c> is written as
        /// <c>Promise&lt;X&gt;</c> (the call is then <c>Promise&lt;X&gt;</c>, never a nested promise).
        /// </summary>
        private ICheckedType WrapIfAsyncCall(ICheckedType declaredReturn, bool isAsync)
        {
            if (!isAsync)
            {
                return declaredReturn;
            }

            return WrapAsPromise(UtilityTypeResolver.Settle(declaredReturn, _globalScope));
        }

        private ICheckedType? UnionReturnedTypes(IBase2Ast? body)
        {
            if (body is null)
            {
                return null;
            }

            var types = new List<ICheckedType>();
            CollectReturnedTypes(body, types, isRoot: true);
            if (types.Count == 0)
            {
                return null;
            }

            return CheckedTypes.UnionTypes(types);
        }

        private void CollectReturnedTypes(IBase2Ast node, List<ICheckedType> types, bool isRoot)
        {
            if (!isRoot
                && node is PhpInlineFunctionAst or PhpFunctionDeclAst or PhpMethodDeclAst
                    or TyhpAsyncBlockAst or PhpObjectTypeDeclAst)
            {
                return;
            }

            if (node is PhpReturnStatementAst { Expression: { } returned })
            {
                AddReturnedType(returned, types);
            }
            else if (node is PhpJumpStatementAst jump
                && jump.JumpType == PhpJumpType.Return)
            {
                if (jump.Expression is { } jumpExpr)
                {
                    AddReturnedType(jumpExpr, types);
                }
                else
                {
                    types.Add(CheckedTypes.Void);
                }
            }
            else if (node is PhpUnaryOpAst unary
                && string.Equals(unary.Operator?.ValueString, "return", StringComparison.OrdinalIgnoreCase))
            {
                if (unary.Operand is IExpression operand)
                {
                    AddReturnedType(operand, types);
                }
                else
                {
                    types.Add(CheckedTypes.Void);
                }
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    CollectReturnedTypes(child, types, isRoot: false);
                }
            }
        }

        private void AddReturnedType(IBase2Ast expression, List<ICheckedType> types)
        {
            if (_checker.TryGetExpressionType(expression, out var cached) && cached is not null)
            {
                types.Add(cached);
            }
        }
    }
}
