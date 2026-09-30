// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpVariableListAst VisitStaticVarList([NotNull] TyhpParser.StaticVarListContext context)
            => PhpVariableListAst.Create(
                context._Items.Select(this.VisitStaticVar),
                context
            );
