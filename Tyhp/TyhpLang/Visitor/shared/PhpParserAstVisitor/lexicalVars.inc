// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpVariableListAst? VisitLexicalVars([NotNull] TyhpParser.LexicalVarsContext context)
            => context.LexicalVarsList != null ? this.VisitLexicalVarList(context.LexicalVarsList) : null;
