// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpArgumentListAst VisitNonEmptyArgumentList([NotNull] TyhpParser.NonEmptyArgumentListContext context)
            => PhpArgumentListAst.Create(
                context._Items.Select(this.VisitArgument),
                context
            );
