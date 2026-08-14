using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>Resolves interpolation-hole expressions in template-string types to checked types.</summary>
    internal static class TemplateStringHoleResolver
    {
        public static ICheckedType Resolve(
            IExpression holeExpr,
            IBase2Ast contextNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (holeExpr is ITypeExpression typeExpr)
            {
                return resolveType(typeExpr, state, false, true);
            }

            if (holeExpr is PhpNameAst name)
            {
                return ResolveIdentifier(name.ValueString ?? string.Empty, contextNode, state, symbolTree, globalScope, resolveType);
            }

            if (holeExpr is TokenValueAst token)
            {
                if (token.ValueString is { } text &&
                    text.Length >= 2 &&
                    ((text[0] == '\'' && text[^1] == '\'') || (text[0] == '"' && text[^1] == '"')))
                {
                    if (PhpStringLiteralHelper.TryDecodeQuotedTokenText(text, out var decoded))
                    {
                        return new LiteralCheckedType(
                            decoded,
                            new SimpleCheckedType(new Binder.Symbols.BuiltInTypeSymbol("string")));
                    }
                }

                if (token.ValueInt64 is TyhpParser.T_STRING or TyhpParser.T_STRING_VARNAME)
                {
                    return ResolveIdentifier(
                        token.ValueString ?? string.Empty,
                        contextNode,
                        state,
                        symbolTree,
                        globalScope,
                        resolveType);
                }
            }

            if (holeExpr is PhpBinaryOpAst binary && binary.Operator?.ValueInt64 == TyhpParser.T_SYM_PIPE)
            {
                var left = Resolve(binary.Left!, contextNode, state, symbolTree, globalScope, resolveType);
                var right = Resolve(binary.Right!, contextNode, state, symbolTree, globalScope, resolveType);
                return CheckedTypes.UnionTypes([left, right]);
            }

            if (holeExpr is PhpEncapsListAst encaps &&
                PhpStringLiteralHelper.TryGetStaticLiteral(encaps, out var literal))
            {
                return new LiteralCheckedType(
                    literal,
                    new SimpleCheckedType(new Binder.Symbols.BuiltInTypeSymbol("string")));
            }

            return CheckedTypes.Unresolved;
        }

        private static ICheckedType ResolveIdentifier(
            string identifier,
            IBase2Ast contextNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return CheckedTypes.Unresolved;
            }

            var builtinAst = PhpBuiltinTypeAst.Create(identifier, contextNode);
            return resolveType(builtinAst, state, false, true);
        }
    }
}
