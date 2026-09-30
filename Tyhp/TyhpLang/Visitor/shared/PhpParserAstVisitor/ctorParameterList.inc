// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpParameterListAst? VisitCtorParameterList([NotNull] TyhpParser.CtorParameterListContext context)
            => context.ParameterList != null ? this.VisitNonEmptyCtorParameterList(context.ParameterList) : null;
