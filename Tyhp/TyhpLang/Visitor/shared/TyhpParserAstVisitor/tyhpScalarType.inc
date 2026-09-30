// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


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
                    ScalarTypeSpelling(context),
                    context,
                    GetCurrentLanguageMode(context)),
            };
        }
