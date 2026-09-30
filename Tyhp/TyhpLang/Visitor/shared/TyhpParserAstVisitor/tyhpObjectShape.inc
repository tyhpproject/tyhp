// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visits <c>object { … }</c> on a type-alias RHS. Members reuse the tyhpdef
        /// class statement list (signatures only) plus PHP class-const spelling
        /// (<c>const NAME = expr</c>), which becomes <see cref="PhpConstDeclListAst"/>.
        /// </summary>
        public override TyhpObjectShapeAst VisitTyhpObjectShape(
            [NotNull] TyhpParser.TyhpObjectShapeContext context)
        {
            var members = context.StatementList != null
                ? this.VisitTyhpObjectShapeStatementList(context.StatementList)
                : PhpClassBodyAst.Create(null, context, GetCurrentLanguageMode(context));

            return TyhpObjectShapeAst.Create(members, context, GetCurrentLanguageMode(context));
        }
