// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpVariableAst VisitLexicalVar([NotNull] TyhpParser.LexicalVarContext context)
            => PhpVariableAst.Create(
                this.GetTokenValueAst(context, context.Variable),
                context.IsRef != null,
                context
            );
