// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpNameAst VisitNamespaceDeclarationName([NotNull] TyhpParser.NamespaceDeclarationNameContext context)
        {
            if (context.Name != null) return this.VisitIdentifier(context.Name);
            if (context.QualifiedName != null) return PhpNameAst.Create(context.QualifiedName, context);

            this.ReportUnexpectedAlternative(context, "namespace-declaration-name");
            return PhpNameAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
