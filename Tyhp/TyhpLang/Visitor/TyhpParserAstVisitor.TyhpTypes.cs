namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;

    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        public override ITypeExpression VisitTypeWithoutStaticGrammarAddon(
            TyhpParser.TypeWithoutStaticGrammarAddonContext? context)
        {
            // Callers should null-check the GrammarAddon child; keep a defensive guard so a stray
            // null context cannot NRE on ScalarType (same truncated-`?` recovery class).
            if (context == null)
            {
                return ErrorAst.Create(
                    "Missing type after error recovery",
                    Domain.Exceptions.MessageCode.VisitorMissingRequiredNode,
                    0,
                    0);
            }

            if (context.ScalarType != null)
            {
                return VisitTyhpScalarType(context.ScalarType);
            }

            return base.VisitTypeWithoutStaticGrammarAddon(context);
        }

        public ITypeExpression VisitTyhpScalarType([NotNull] TyhpParser.TyhpScalarTypeContext context)
        {
            return context switch
            {
                TyhpParser.ScalarTypeDoubleQuoteStringContext dq => TyhpTemplateStringTypeAst.Create(
                    dq.EncapsList != null
                        ? VisitEncapsList(dq.EncapsList)
                        : PhpEncapsListAst.Create([], dq),
                    dq,
                    GetCurrentLanguageMode(dq)),
                TyhpParser.ScalarTypeSingleQuoteStringContext sq => PhpBuiltinTypeAst.Create(
                    sq.Scalar?.Text ?? string.Empty,
                    sq,
                    GetCurrentLanguageMode(sq)),
                _ => PhpBuiltinTypeAst.Create(
                    context.Start?.Text ?? "<unknown>",
                    context,
                    GetCurrentLanguageMode(context)),
            };
        }
    }
}
