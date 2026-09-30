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
        /// Visits a Tyhp extension declaration statement.
        ///
        /// Grammar:
        ///   tyhpExtensionDeclarationStatement
        ///     : T_TYHP_INTERNAL? T_TYHP_EXTENSION Identifier=T_STRING
        ///         GenericParameters=tyhpGenericParameterDeclarations?
        ///         (T_EXTENDS TargetType=typeExprWithoutStatic)?
        ///         FindDocComment=T_OPEN_CURLY_BRACE FunctionList=tyhpExtensionFunctionList
        ///         T_CLOSE_CURLY_BRACE
        ///     ;
        ///
        /// Creates a TyhpExtensionDeclAst with the extension name, optional generic
        /// parameters, optional header target, member list (functions, operators,
        /// and nested groups), and an optional doc comment.
        /// </summary>
        public override TyhpExtensionDeclAst VisitTyhpExtensionDeclarationStatement([NotNull] TyhpParser.TyhpExtensionDeclarationStatementContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            // Truncated `extension Foo` at EOF leaves FunctionList (and sometimes Identifier)
            // null after ANTLR recovery — same pattern as object-type decls.
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

            var result = TyhpExtensionDeclAst.Create(
                name,
                functionList,
                docComment,
                context
            );
            this.AttachExtensionBlockTarget(result, context.GenericParameters, context.TargetType);
            if (context.IsInternal != null)
            {
                result.AddGrammarAddon("isInternal", TokenValueAst.Create(context.IsInternal, context));
            }

            return result;
        }
