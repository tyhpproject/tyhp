// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override PhpVariableAst VisitSimpleVariable([NotNull] TyhpParser.SimpleVariableContext context)
        {
            if (context.Variable != null) return PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context);
            if (context.DoubleDollarVariable != null) return PhpVariableAst.Create(this.VisitSimpleVariable(context.DoubleDollarVariable), false, context);
            if (context.BracedExpr != null) return PhpVariableAst.Create(this.VisitExpr(context.BracedExpr), false, context);
            if (context.simpleVariableGrammarAddon() != null) return this.VisitSimpleVariableGrammarAddon(context.simpleVariableGrammarAddon());

            this.ReportUnexpectedAlternative(context, "simple-variable");
            return PhpVariableAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
