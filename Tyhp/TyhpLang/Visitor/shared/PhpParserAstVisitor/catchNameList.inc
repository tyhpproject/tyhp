// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpClassNameListAst VisitCatchNameList([NotNull] TyhpParser.CatchNameListContext context)
            => PhpClassNameListAst.Create(
                context._Items.Select(this.VisitClassName),
                context
            );
