// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public PhpTraitAliasAst VisitTraitAlias([NotNull] TyhpParser.TraitAliasContext context)
            => context switch
            {
                TyhpParser.TraitAliasRenameContext ctx => this.VisitTraitAliasRename(ctx),
                TyhpParser.TraitAliasVisibilityContext ctx => this.VisitTraitAliasVisibility(ctx),
                TyhpParser.TraitAliasGrammarAddonHandlerContext ctx => this.VisitTraitAliasGrammarAddonHandler(ctx),
                _ => this.VisitTraitAliasAlternative(context)
            };


        public virtual PhpTraitAliasAst VisitTraitAliasAlternative([NotNull] TyhpParser.TraitAliasContext context)
        {
            this.ReportUnexpectedAlternative(context, "trait-alias");
            return PhpTraitAliasAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override PhpTraitAliasAst VisitTraitAliasRename([NotNull] TyhpParser.TraitAliasRenameContext context)
        {
            string newName = "";
            if (context.AliasString != null)
            {
                newName = this.GetTokenValueAst(context, context.AliasString)?.ValueString ?? "";
            }
            else if (context.AliasRNM != null)
            {
                newName = this.VisitReservedNonModifiers(context.AliasRNM).ValueString ?? "";
            }

            return PhpTraitAliasAst.Create(
                this.VisitTraitMethodReference(context.AliasOf),
                newName,
                context
            ).WithGrammarAddon("alias", this.VisitOptionalTraitAliasNameGrammarAddon(context.traitAliasNameGrammarAddon()));
        }


        public override PhpTraitAliasAst VisitTraitAliasVisibility([NotNull] TyhpParser.TraitAliasVisibilityContext context)
        {
            var newName = context.Identifier != null ? this.VisitIdentifier(context.Identifier).ValueString : null;
            var modifier = FromToken((int)(this.VisitMemberModifier(context.Modifier).ValueInt64 ?? 0));
            
            return PhpTraitAliasAst.Create(
                this.VisitTraitMethodReference(context.AliasOf),
                newName ?? "",
                modifier,
                context
            ).WithGrammarAddon("alias", this.VisitOptionalTraitAliasNameGrammarAddon(context.traitAliasNameGrammarAddon()));
        }


        public override PhpTraitAliasAst VisitTraitAliasGrammarAddonHandler([NotNull] TyhpParser.TraitAliasGrammarAddonHandlerContext context)
            => this.VisitTraitAliasGrammarAddon(context.traitAliasGrammarAddon());
