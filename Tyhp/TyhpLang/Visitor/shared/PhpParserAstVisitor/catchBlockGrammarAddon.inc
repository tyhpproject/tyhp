// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpCatchClauseAst VisitCatchBlockGrammarAddon([NotNull] TyhpParser.CatchBlockGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "catchBlockGrammarAddon", context.GetType().Name
            );
            var languageMode = GetCurrentLanguageMode(context);
            return PhpCatchClauseAst.Create(
                PhpClassNameListAst.Create(System.Array.Empty<IClassName>(), context, languageMode),
                null,
                PhpStatementBlockAst.CreateError(context, languageMode),
                context,
                languageMode
            );
        }
