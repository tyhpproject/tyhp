// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        //#endregion Class Statement Grammar Addon

        //#region Trait Alias Grammar Addon

        /// <summary>
        /// Dispatches trait alias grammar addon alternatives.
        /// Overrides the PhpParserAstVisitor's throwing implementation.
        ///
        /// Grammar (TyhpParser):
        ///   traitAliasGrammarAddon
        ///     : AliasOf=traitPropertyReference T_AS AliasString=T_VARIABLE
        ///         {this.isLanguageMode("tyhp")}?          #tyhpTraitAliasPropertyRename
        ///     ;
        /// </summary>
        public override PhpTraitAliasAst VisitTraitAliasGrammarAddon([NotNull] TyhpParser.TraitAliasGrammarAddonContext context)
        {
            return context switch
            {
                TyhpParser.TyhpTraitAliasPropertyRenameContext ctx => this.VisitTyhpTraitAliasPropertyRename(ctx),
                TyhpParser.TyhpTraitAliasHideContext ctx => this.VisitTyhpTraitAliasHide(ctx),
                _ => HandleUnexpectedAlternativeSpecial(context, "traitAliasGrammarAddon",
                    () => PhpTraitAliasAst.CreateError(context, GetCurrentLanguageMode(context)))
            };
        }


        /// <summary>
        /// Visits a postfix <c>hide</c> adaptation inside <c>use extension { }</c>
        /// (<c>Foo::bar hide;</c> / <c>operator *&lt;string&gt; hide;</c>).
        /// </summary>
        public override PhpTraitAliasAst VisitTyhpTraitAliasHide([NotNull] TyhpParser.TyhpTraitAliasHideContext context)
        {
            PhpTraitMemberRefAst aliasOf;
            if (context.AliasOf != null)
            {
                aliasOf = this.VisitTraitMethodReference(context.AliasOf);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpTraitAliasHide.AliasOf");
                aliasOf = PhpTraitMemberRefAst.Create(
                    PhpNameAst.CreateError(context, GetCurrentLanguageMode(context)),
                    context,
                    GetCurrentLanguageMode(context));
            }

            return PhpTraitAliasAst.CreateHide(aliasOf, context, GetCurrentLanguageMode(context));
        }


        /// <summary>
        /// Visits a trait alias property rename declaration.
        /// This is a Tyhp extension that allows renaming trait properties
        /// (not just methods) when using traits.
        ///
        /// Grammar:
        ///   traitAliasGrammarAddon
        ///     : AliasOf=traitPropertyReference T_AS AliasString=T_VARIABLE
        ///         {this.isLanguageMode("tyhp")}?          #tyhpTraitAliasPropertyRename
        ///     ;
        ///
        /// Creates a PhpTraitAliasAst with the property reference and the
        /// new variable name (AliasString is a T_VARIABLE token).
        /// </summary>
        public override PhpTraitAliasAst VisitTyhpTraitAliasPropertyRename([NotNull] TyhpParser.TyhpTraitAliasPropertyRenameContext context)
        {
            var aliasOf = this.VisitTraitPropertyReference(context.AliasOf);
            var newName = context.AliasString.Text;

            return PhpTraitAliasAst.Create(
                aliasOf,
                newName,
                context,
                GetCurrentLanguageMode(context)
            );
        }
