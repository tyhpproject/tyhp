// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpEncapsListAst VisitEncapsList([NotNull] TyhpParser.EncapsListContext context)
            => PhpEncapsListAst.Create(
                context._Items.Select(this.VisitEncapsVarOrWhitespace),
                context
            );
