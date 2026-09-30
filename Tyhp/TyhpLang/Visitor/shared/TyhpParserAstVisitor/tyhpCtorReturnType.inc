// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        //#endregion Name Grammar Addons

        //#region Tyhp Constructor Return Type

        /// <summary>
        /// Visits a Tyhp constructor return type annotation.
        ///
        /// Grammar:
        ///   tyhpCtorReturnType
        ///     : T_SYM_COLON TokenValue=T_TYHP_VOID
        ///     | T_SYM_COLON TokenValue=T_TYHP_PARENT ArgumentsList=argumentList
        ///     ;
        ///
        /// This rule is referenced optionally from `tyhpClassCtorWithReturnType`
        /// (`ReturnType=tyhpCtorReturnType?`). Omitting the annotation is equivalent
        /// to `: void` (no `parent::__construct` insertion); this visitor is only
        /// reached when the author wrote an explicit `: void` or `: parent(...)`.
        ///
        /// Returns a TyhpCtorReturnTypeAst with the type token and optional
        /// argument list for parent constructor delegation, or null when ANTLR error
        /// recovery entered this rule (e.g. `: int`) without matching `T_TYHP_VOID` /
        /// `T_TYHP_PARENT` — the parser already reported the real syntax diagnostic, so
        /// the caller treats a null result the same as an omitted annotation instead of
        /// throwing on the missing token.
        /// </summary>
        public override TyhpCtorReturnTypeAst? VisitTyhpCtorReturnType([NotNull] TyhpParser.TyhpCtorReturnTypeContext context)
        {
            var typeToken = this.GetTokenValueAst(context, context.TokenValue);
            if (typeToken is null)
            {
                return null;
            }

            var arguments = context.ArgumentsList != null
                ? this.VisitArgumentList(context.ArgumentsList)
                : null;

            return TyhpCtorReturnTypeAst.Create(typeToken, arguments, context);
        }
