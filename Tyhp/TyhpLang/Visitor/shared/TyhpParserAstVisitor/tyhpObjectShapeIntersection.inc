// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visits an alias-RHS intersection that mixes nominal types with an object
        /// shape, e.g. <c>LoggerInterface &amp; object { … }</c>. Produces the same
        /// <see cref="PhpTypeKind.Intersection"/> wrapper as an ordinary
        /// <c>intersectionType</c>; each shape item is a <see cref="TyhpObjectShapeAst"/>
        /// (members retained), not builtin <c>object</c>.
        /// </summary>
        public override PhpTypeExpressionAst VisitTyhpObjectShapeIntersection(
            [NotNull] TyhpParser.TyhpObjectShapeIntersectionContext context)
            => PhpTypeExpressionAst.Create(
                PhpTypeExpressionListAst.Create(
                    context._Items.Select(this.VisitTyhpObjectShapeIntersectionItem),
                    context
                ),
                PhpTypeKind.Intersection,
                false,
                false,
                context
            );
