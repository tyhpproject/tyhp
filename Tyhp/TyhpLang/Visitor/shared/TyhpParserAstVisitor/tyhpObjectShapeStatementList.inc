// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visits an object-shape body. PHP <c>const NAME = expr</c> / typed
        /// <c>const T NAME = expr</c> become <see cref="PhpConstDeclListAst"/> so
        /// shape assignability can see constants; every other member is a tyhpdef
        /// class statement.
        /// </summary>
        public override PhpClassBodyAst VisitTyhpObjectShapeStatementList(
            [NotNull] TyhpParser.TyhpObjectShapeStatementListContext context)
            => PhpClassBodyAst.Create(
                (context._Items ?? []).Where(item => item != null).Select(this.VisitTyhpObjectShapeStatement!),
                context
            );
