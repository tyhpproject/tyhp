// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override PhpTypeExpressionAst VisitUnionTypeWithoutStatic([NotNull] TyhpParser.UnionTypeWithoutStaticContext context)
            => PhpTypeExpressionAst.Create(
                PhpTypeExpressionListAst.Create(
                    context._Items.Select(this.VisitUnionTypeWithoutStaticElement),
                    context
                ),
                PhpTypeKind.Union,
                false,
                false,
                context
            );
