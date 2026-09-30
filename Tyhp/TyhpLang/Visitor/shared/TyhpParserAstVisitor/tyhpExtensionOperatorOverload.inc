// #class TyhpParserAstVisitor
// #using System.Linq
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Grammar: tyhpExtensionOperatorOverload — operands only. A
        /// <c>&lt;Type&gt;</c> after the operator token is the old declaration spelling.
        /// </summary>
        public override TyhpOperatorOverloadAst VisitTyhpExtensionOperatorOverload(
            [NotNull] TyhpParser.TyhpExtensionOperatorOverloadContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            this.ReportLegacyOperatorTarget(context.LegacyTarget, context);
            var op = this.VisitTyhpClassOperatorOverloadOp(context.Op);
            var leftParam = this.VisitAttributedParameter(context.LeftParameter);
            var rightParam = context.RightParameter != null
                ? this.VisitAttributedParameter(context.RightParameter)
                : null;
            var returnType = this.VisitReturnType(context.ConvertReturnType);

            PhpStatementBlockAst? body;
            if (context.StatementList != null)
            {
                body = this.VisitMethodBody(context.StatementList);
            }
            else if (context.ShorthandExpr != null)
            {
                var expr = this.VisitExpr(context.ShorthandExpr);
                body = PhpStatementBlockAst.Create(
                    [PhpUnaryOpAst.Create(
                        TokenValueAst.Create("return", TyhpParser.T_RETURN, context),
                        expr,
                        context,
                        languageMode
                    )],
                    context,
                    languageMode
                );
            }
            else
            {
                body = null;
            }

            var ast = TyhpOperatorOverloadAst.Create(
                op,
                leftParam,
                rightParam,
                returnType,
                body,
                null,
                context,
                languageMode,
                isShortSyntax: context.ShorthandExpr != null);

            if (context.IsInternal != null)
            {
                ast.AddGrammarAddon("isInternal", TokenValueAst.Create(context.IsInternal, context));
            }

            return ast;
        }
