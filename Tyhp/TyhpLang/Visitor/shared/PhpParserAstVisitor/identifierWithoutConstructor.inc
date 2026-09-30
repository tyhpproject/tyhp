// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpNameAst VisitIdentifierWithoutConstructor([NotNull] TyhpParser.IdentifierWithoutConstructorContext context)
        {
            if (context.TokenValue != null) return PhpNameAst.Create(context.TokenValue, context);
            if (context.SemiReserved != null) return PhpNameAst.Create(this.VisitSemiReservedWithoutConstructor(context.SemiReserved).ValueString ?? "", 0, context);

            this.ReportUnexpectedAlternative(context, "identifier-without-constructor");
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
