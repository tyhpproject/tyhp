// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override PhpTypeExpressionAst VisitIntersectionTypeWithoutStatic([NotNull] TyhpParser.IntersectionTypeWithoutStaticContext context)
            => PhpTypeExpressionAst.Create(
                PhpTypeExpressionListAst.Create(
                    context._Items.Select(this.VisitTypeWithoutStatic),
                    context
                ),
                PhpTypeKind.Intersection,
                false,
                false,
                context
            );
