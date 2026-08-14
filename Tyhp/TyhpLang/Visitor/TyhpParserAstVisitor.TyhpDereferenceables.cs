namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Antlr4.Runtime.Tree;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        public override PhpNewAst VisitNewDereferenceableGrammarAddon([NotNull] TyhpParser.NewDereferenceableGrammarAddonContext context)
            => context switch
            {
                TyhpParser.TyhpNewAnonStructInstanceContext c => this.VisitTyhpNewAnonStructInstance(c),
                _ => base.VisitNewDereferenceableGrammarAddon(context),
            };

        /// <summary>
        /// Handles `new struct { ... }` expressions.
        ///
        /// Grammar:
        ///   newDereferenceableGrammarAddon
        ///     : T_NEW AnonStructDecl=tyhpAnonymousStruct {this.isLanguageMode("tyhp")}?
        ///         #tyhpNewAnonStructInstance
        ///     ;
        ///
        /// Follows the same pattern as anonymous classes: the struct declaration
        /// is registered as a top-level statement, and a PhpNewAst wrapping the
        /// struct's generated identifier is returned for use in expression context.
        /// </summary>
        public override PhpNewAst VisitTyhpNewAnonStructInstance([NotNull] TyhpParser.TyhpNewAnonStructInstanceContext context)
        {
            if (context.AnonStructDecl == null)
            {
                this.ReportMissingRequired(context, "tyhpNewAnonStructInstance.AnonStructDecl");
                return PhpNewAst.CreateError(context, GetCurrentLanguageMode(context));
            }

            var structDecl = this.VisitTyhpAnonymousStruct(context.AnonStructDecl);

            // Register the anonymous struct declaration in the current top statement list
            this.CurrentTopStatementList?.Add(structDecl);

            // Return a new-expression referencing the anonymous struct by its generated identifier
            return PhpNewAst.Create(
                PhpNameAst.Create(structDecl.Identifier, TyhpParser.T_STRING, context),
                null,
                context
            );
        }

        // TODO: VisitNewDRefNoGrammarAddonToken - stub removed because
        // TyhpParser.NewDRefNoGrammarAddonTokenContext does not exist in
        // the current generated parser. Re-add when the grammar rule is defined.
    }
}
