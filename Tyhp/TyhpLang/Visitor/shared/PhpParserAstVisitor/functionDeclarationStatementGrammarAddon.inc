// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public virtual PhpFunctionDeclAst VisitFunctionDeclarationStatementGrammarAddon([NotNull] TyhpParser.FunctionDeclarationStatementGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "functionDeclarationStatementGrammarAddon", context.GetType().Name
            );
            return this.CreateErrorFunctionDecl(context);
        }
