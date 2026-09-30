// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpFunctionDeclAst VisitFunctionDeclarationStatement([NotNull] TyhpParser.FunctionDeclarationStatementContext context)
        {
            if (context.functionDeclarationStatementGrammarAddon() != null)
            {
                return this.VisitFunctionDeclarationStatementGrammarAddon(context.functionDeclarationStatementGrammarAddon());
            }

            if (IsErrorRecoveryContext(context))
            {
                // ANTLR recovery for this single-alt sequence rule can produce a wholly empty stub
                // (no Identifier/ParameterList/ReturnType/StatementList) when prediction fails
                // partway through a malformed declaration — e.g. a truncated return-type union
                // (`function demo(): Foo| {}`) throws mid-rule and unwinds before any field is
                // assigned. The parser already reported the real syntax diagnostic; walking the
                // null mandatory fields below would NRE (TYHP1003) instead of a clean recovery.
                return this.CreateErrorFunctionDecl(context);
            }

            var docComment = this.FindPossibleDocComment(context.FindDocComment);
            return PhpFunctionDeclAst.Create(
                this.VisitFunctionName(context.Identifier).ValueString,
                this.VisitReturnsRef(context.ReturnsRef) != null,
                this.VisitParameterList(context.ParameterList),
                this.VisitReturnType(context.ReturnType),
                this.VisitInnerStatementList(context.StatementList),
                context,
                GetCurrentLanguageMode(context),
                docComment
            ).WithGrammarAddon("modifiers", this.VisitFunctionModifiersGrammarAddon(context.functionModifiersGrammarAddon()))
            .WithGrammarAddon("identifier", this.VisitFunctionNameGrammarAddon(context.functionNameGrammarAddon()))
            .WithGrammarAddon("parameters", this.VisitFunctionParametersGrammarAddon(context.functionParametersGrammarAddon()));
        }
