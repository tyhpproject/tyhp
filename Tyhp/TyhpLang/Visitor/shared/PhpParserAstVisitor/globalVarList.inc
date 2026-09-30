// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpVariableListAst VisitGlobalVarList([NotNull] TyhpParser.GlobalVarListContext context)
            => PhpVariableListAst.Create(
                context._Items.Select(this.VisitGlobalVar),
                context
            );
