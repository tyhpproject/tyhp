// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public override PhpTypeExpressionAst VisitUnionType([NotNull] TyhpParser.UnionTypeContext context)
            => PhpTypeExpressionAst.Create(
                PhpTypeExpressionListAst.Create(
                    context._Items.Select(this.VisitUnionTypeElement),
                    context
                ),
                PhpTypeKind.Union,
                false,
                false,
                context
            );
