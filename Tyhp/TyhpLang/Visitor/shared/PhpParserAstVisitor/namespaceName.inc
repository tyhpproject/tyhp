// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpNameAst VisitNamespaceName([NotNull] TyhpParser.NamespaceNameContext context)
        {
            if (context.Name != null) return PhpNameAst.Create(context.Name, context);
            if (context.QualifiedName != null) return PhpNameAst.Create(context.QualifiedName, context);
            if (context.namespaceNameGrammarAddon() != null) return this.VisitNamespaceNameGrammarAddon(context.namespaceNameGrammarAddon());

            this.ReportUnexpectedAlternative(context, "namespace-name");
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
