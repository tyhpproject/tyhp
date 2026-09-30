// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override Ast.Interfaces.ITypeExpression VisitUnionTypeWithoutStaticElement([NotNull] TyhpParser.UnionTypeWithoutStaticElementContext context)
        {
            if (context.BaseType != null)
            {
                return this.VisitTypeWithoutStatic(context.BaseType);
            }
            else if (context.IntersectionType != null)
            {
                return this.VisitIntersectionTypeWithoutStatic(context.IntersectionType);
            }
            else
            {
                this.ReportUnexpectedAlternative(context, "unionTypeWithoutStaticElement");
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }
        }
