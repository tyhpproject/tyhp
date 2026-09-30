// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using System.Linq
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visit a Tyhp using block statement.
        ///
        /// Grammar (TyhpParser.g4):
        ///   tyhpUsingBlock
        ///       : T_TYHP_USING IsAsync=T_TYHP_AWAIT?
        ///         T_OPEN_ROUND_BRACE Resources=tyhpUsingResourceList T_CLOSE_ROUND_BRACE
        ///         T_OPEN_CURLY_BRACE StatementList=innerStatementList T_CLOSE_CURLY_BRACE
        ///       ;
        /// </summary>
        public override TyhpUsingBlockAst VisitTyhpUsingBlock(
            [NotNull] TyhpParser.TyhpUsingBlockContext context)
        {
            var isAsync = context.IsAsync != null;
            var resources = context.Resources._Items
                .Select(r => VisitTyhpUsingResource(r))
                .ToList();
            var body = context.StatementList != null
                ? this.VisitInnerStatementList(context.StatementList)
                : null;

            return TyhpUsingBlockAst.Create(isAsync, resources, body, context, GetCurrentLanguageMode(context));
        }
