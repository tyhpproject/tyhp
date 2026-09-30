// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpVariableListAst VisitLexicalVarList([NotNull] TyhpParser.LexicalVarListContext context)
            => PhpVariableListAst.Create(
                context._Items?.Select(this.VisitLexicalVar),
                context
            );
