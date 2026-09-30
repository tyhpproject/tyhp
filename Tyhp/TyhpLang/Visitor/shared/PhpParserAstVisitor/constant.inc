// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override IScalar VisitConstant([NotNull] TyhpParser.ConstantContext context)
        {
            if (context.Identifier != null) return this.VisitName(context.Identifier);
            if (context.TokenValue != null) return this.VisitConstantTokenValue(context.TokenValue);

            this.ReportUnexpectedAlternative(context, "constant");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
