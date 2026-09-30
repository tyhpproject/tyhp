// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        public override PhpCatchListAst VisitCatchList([NotNull] TyhpParser.CatchListContext context)
            => PhpCatchListAst.Create(
                context._Items.Select(this.VisitCatchBlock),
                context
            );
