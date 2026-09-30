// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpArgumentListAst VisitNonEmptyCloneArgumentList([NotNull] TyhpParser.NonEmptyCloneArgumentListContext context)
        {
            var args = new List<PhpArgumentAst>();

            if (context.FirstExpr != null && context.FirstArg != null)
            {
                args.Add(PhpArgumentAst.Create(this.VisitExpr(context.FirstExpr), context));
                args.Add(this.VisitArgument(context.FirstArg));
            }
            else if (context.FirstNoExpr != null)
            {
                args.Add(this.VisitCloneArgumentNoExpr(context.FirstNoExpr));
            }

            foreach (var rest in context._Rest)
            {
                args.Add(this.VisitArgument(rest));
            }

            return PhpArgumentListAst.Create(args, context);
        }
