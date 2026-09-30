// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser

        public override PhpAttributeAst VisitAttributeDecl([NotNull] TyhpParser.AttributeDeclContext context)
        {
            var className = this.VisitClassName(context.ClassName);
            var argumentList = context.ArgumentList != null ? this.VisitArgumentList(context.ArgumentList) : null;

            return PhpAttributeAst.Create(className, argumentList, context, GetCurrentLanguageMode(context));
        }
