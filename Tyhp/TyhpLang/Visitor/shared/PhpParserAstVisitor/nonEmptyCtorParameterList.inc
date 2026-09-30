// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpParameterListAst VisitNonEmptyCtorParameterList([NotNull] TyhpParser.NonEmptyCtorParameterListContext context)
            => PhpParameterListAst.Create(
                context._Items.Select(this.VisitAttributedCtorParameter),
                context
            );
