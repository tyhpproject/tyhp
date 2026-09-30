// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override TokenValueAst? VisitReturnsRef([NotNull] TyhpParser.ReturnsRefContext context)
            => context.ReturnsRef != null ? this.VisitAmpersand(context.ReturnsRef) : null;
