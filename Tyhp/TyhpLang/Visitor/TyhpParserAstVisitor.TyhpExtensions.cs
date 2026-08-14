namespace Tyhp.TyhpLang.Visitor
{
    using System.Linq;
    using Antlr4.Runtime.Misc;
    using Antlr4.Runtime.Tree;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        /// <summary>
        /// Visits a Tyhp extension declaration statement.
        ///
        /// Grammar:
        ///   tyhpExtensionDeclarationStatement
        ///     : T_TYHP_EXTENSION Identifier=T_STRING Extends=extendsFrom
        ///         FindDocComment=T_OPEN_CURLY_BRACE FunctionList=tyhpExtensionFunctionList
        ///         T_CLOSE_CURLY_BRACE
        ///     ;
        ///
        /// Creates a TyhpExtensionDeclAst with:
        ///   - The extension name (Identifier token text)
        ///   - The extended class/type (from extendsFrom)
        ///   - The list of extension members (functions and operator overloads)
        ///   - An optional doc comment found before the opening brace
        /// </summary>
        public override TyhpExtensionDeclAst VisitTyhpExtensionDeclarationStatement([NotNull] TyhpParser.TyhpExtensionDeclarationStatementContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            // Truncated `extension Foo` at EOF leaves Extends / FunctionList (and sometimes
            // Identifier) null after ANTLR recovery — same pattern as object-type decls.
            string name;
            if (context.Identifier != null)
            {
                name = context.Identifier.Text;
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpExtensionDeclarationStatement.Identifier");
                name = "<error>";
            }

            IClassName? extends;
            if (context.Extends != null)
            {
                extends = this.VisitExtendsFrom(context.Extends);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpExtensionDeclarationStatement.Extends");
                extends = null;
            }

            TyhpExtensionFunctionListAst functionList;
            if (context.FunctionList != null)
            {
                functionList = this.VisitTyhpExtensionFunctionList(context.FunctionList);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpExtensionDeclarationStatement.FunctionList");
                functionList = TyhpExtensionFunctionListAst.Create(null, context);
            }

            return TyhpExtensionDeclAst.Create(
                name,
                extends,
                functionList,
                docComment,
                context
            );
        }

        /// <summary>
        /// Visits <see cref="TyhpParser.tyhpExtensionFunctionList"/>.
        /// </summary>
        public override TyhpExtensionFunctionListAst VisitTyhpExtensionFunctionList([NotNull] TyhpParser.TyhpExtensionFunctionListContext context)
            => TyhpExtensionFunctionListAst.Create(
                (context.tyhpExtensionMember() ?? Enumerable.Empty<TyhpParser.TyhpExtensionMemberContext>())
                    .Select(this.VisitTyhpExtensionMemberAsExtensionMember),
                context
            );

        public override IBase2Ast? VisitTyhpExtensionMember([NotNull] TyhpParser.TyhpExtensionMemberContext context)
            => this.VisitTyhpExtensionMemberAsExtensionMember(context);

        private IExtensionMemberAst VisitTyhpExtensionMemberAsExtensionMember(TyhpParser.TyhpExtensionMemberContext context)
        {
            var fn = context.functionDeclarationStatement();
            if (fn != null)
            {
                return this.VisitFunctionDeclarationStatement(fn);
            }

            var opOverload = context.tyhpExtensionOperatorOverload();
            if (opOverload != null)
            {
                return this.VisitTyhpExtensionOperatorOverload(opOverload);
            }

            return ErrorAst.Create(context, GetCurrentLanguageMode(context));
        }

        /// <summary>
        /// Grammar: tyhpExtensionOperatorOverload — <c>operator</c> with <c>&lt;TargetType&gt;</c> (no abstract/final).
        /// </summary>
        public override TyhpOperatorOverloadAst VisitTyhpExtensionOperatorOverload(
            [NotNull] TyhpParser.TyhpExtensionOperatorOverloadContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            var op = this.VisitTyhpClassOperatorOverloadOp(context.Op);
            var targetType = this.VisitTypeExprWithoutStatic(context.TargetType);
            var leftParam = this.VisitParameter(context.LeftParameter);
            var rightParam = context.RightParameter != null
                ? this.VisitParameter(context.RightParameter)
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
                languageMode);

            ast.ExtensionTargetType = targetType;
            return ast;
        }
    }
}
