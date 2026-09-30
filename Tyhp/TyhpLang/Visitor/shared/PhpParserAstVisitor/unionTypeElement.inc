// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override ITypeExpression VisitUnionTypeElement([NotNull] TyhpParser.UnionTypeElementContext context)
        {
            if (context.BaseType != null)
            {
                return this.VisitType(context.BaseType);
            }
            else if (context.IntersectionType != null)
            {
                return this.VisitIntersectionType(context.IntersectionType);
            }
            else
            {
                this.ReportUnexpectedAlternative(context, "unionTypeElement");
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
        }
