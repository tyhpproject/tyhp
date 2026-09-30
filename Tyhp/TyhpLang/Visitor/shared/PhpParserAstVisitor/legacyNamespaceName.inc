// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpNameAst VisitLegacyNamespaceName([NotNull] TyhpParser.LegacyNamespaceNameContext context)
        {
            if (context.Name != null) return this.VisitNamespaceName(context.Name);
            if (context.FullyQualifiedName != null) return PhpNameAst.Create(context.FullyQualifiedName, context);
            if (context.legacyNamespaceNameGrammarAddon() != null) return this.VisitLegacyNamespaceNameGrammarAddon(context.legacyNamespaceNameGrammarAddon());

            this.ReportUnexpectedAlternative(context, "legacy-namespace-name");
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
