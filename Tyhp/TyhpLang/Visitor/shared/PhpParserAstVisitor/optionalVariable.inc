// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpVariableAst? VisitOptionalVariable([NotNull] TyhpParser.OptionalVariableContext context)
            => context.TokenValue != null ? PhpVariableAst.Create(this.GetTokenValueAst(context, context.TokenValue), false, context) : null;
