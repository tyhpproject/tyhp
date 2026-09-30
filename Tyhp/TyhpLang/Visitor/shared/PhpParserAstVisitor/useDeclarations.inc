// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpImportDeclListAst VisitUseDeclarations([NotNull] TyhpParser.UseDeclarationsContext context)
        {
            var items = context._Items?.Select(this.VisitUseDeclaration) ?? null;
            return PhpImportDeclListAst.Create(items, context, GetCurrentLanguageMode(context));
        }
