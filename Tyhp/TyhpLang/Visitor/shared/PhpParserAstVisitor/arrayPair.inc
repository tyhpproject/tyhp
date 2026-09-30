// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public PhpArrayPairAst VisitArrayPair([NotNull] TyhpParser.ArrayPairContext context)
            => context switch {
                TyhpParser.ArrayPairItemContext arrayPairItemContext => this.VisitArrayPairItem(arrayPairItemContext),
                TyhpParser.ArrayPairExpansionContext arrayPairExpansionContext => this.VisitArrayPairExpansion(arrayPairExpansionContext),
                _ => this.VisitArrayPairAlt(context),
            };


        public virtual PhpArrayPairAst VisitArrayPairAlt(TyhpParser.ArrayPairContext context)
        {
            this.ReportUnexpectedAlternative(context, "array-pair");
            return PhpArrayPairAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override PhpArrayPairAst VisitArrayPairItem([NotNull] TyhpParser.ArrayPairItemContext context)
            => context.isKey != null
                ? PhpArrayPairAst.Create(
                    this.VisitExpr(context.KeyOrValueExpr),
                    this.VisitExpr(context.Value),
                    false,
                    context
                )
                : PhpArrayPairAst.Create(
                    null,
                    this.VisitExpr(context.KeyOrValueExpr),
                    false,
                    context
                );


        public override PhpArrayPairAst VisitArrayPairExpansion([NotNull] TyhpParser.ArrayPairExpansionContext context)
            => PhpArrayPairAst.Create(
                null,
                this.VisitExpr(context.Expr),
                true,
                context
            );
