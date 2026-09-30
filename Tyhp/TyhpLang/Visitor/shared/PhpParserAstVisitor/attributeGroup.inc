// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpAttributeListAst VisitAttributeGroup([NotNull] TyhpParser.AttributeGroupContext context)
            => PhpAttributeListAst.Create(
                context._Items?.Select(this.VisitAttributeDecl),
                context,
                GetCurrentLanguageMode(context)
            );
