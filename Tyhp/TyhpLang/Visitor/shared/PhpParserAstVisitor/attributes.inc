// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

            
        public override PhpAttributeListAst VisitAttributes([NotNull] TyhpParser.AttributesContext context)
            => PhpAttributeListAst.Create(
                context._Items?.SelectMany(nodeList => this.VisitAttribute(nodeList).GetAll()),
                context,
                GetCurrentLanguageMode(context)
            );
