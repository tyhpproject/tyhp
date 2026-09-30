// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpAttributeListAst VisitAttribute([NotNull] TyhpParser.AttributeContext context)
            => this.VisitAttributeGroup(context.AttributesList);
