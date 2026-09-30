// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override IDereferenceableBase VisitDereferenceableScalar([NotNull] TyhpParser.DereferenceableScalarContext context)
        {
            if (context.ArrayPairList != null)
            {
                return this.VisitArrayPairList(context.ArrayPairList);
            }
            if (context.TokenValue.Type == TyhpParser.T_CONSTANT_ENCAPSED_STRING)
            {
                return PhpEncapsListAst.Create([
                    PhpEncapsStringAst.Create(
                        this.GetTokenValueAst(context, context.TokenValue),
                        context
                    )
                ], context).WithStringType(this.GetTokenValueAst(context, context.TokenValue));
            }
            if (((int[])[
                TyhpParser.T_BINARY_DOUBLE_QUOTE,
                TyhpParser.T_DOUBLE_QUOTE,
                TyhpParser.T_BACKQUOTE,
                TyhpParser.T_BINARY_BACKQUOTE
            ]).Contains(context.TokenValue.Type))
            {
                return (context.EncapsList != null ?
                    this.VisitEncapsList(context.EncapsList) :
                    PhpEncapsListAst.Create([], context)
                )
                    .WithStringType(this.GetTokenValueAst(context, context.TokenValue));
            }

            this.ReportUnexpectedAlternative(context, "dereferenceable-scalar");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
