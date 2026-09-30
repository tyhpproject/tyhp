// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpImportDeclListAst VisitInlineUseDeclarations([NotNull] TyhpParser.InlineUseDeclarationsContext context)
        {
            var items = context._Items?.Select(this.VisitInlineUseDeclaration) ?? null;
            return PhpImportDeclListAst.Create(items, context, GetCurrentLanguageMode(context));
        }
