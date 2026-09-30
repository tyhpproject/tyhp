// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpObjectTypeDeclAst VisitTraitDeclarationStatement([NotNull] TyhpParser.TraitDeclarationStatementContext context)
        {
            if (context.traitDeclarationStatementGrammarAddon() != null)
            {
                return this.VisitTraitDeclarationStatementGrammarAddon(context.traitDeclarationStatementGrammarAddon());
            }

            var docComment = this.FindPossibleDocComment(context.FindDocComment);
            var modifiersAddon = context.traitModifiersGrammarAddon();
            var modifiers = modifiersAddon != null ? this.VisitTraitModifiersGrammarAddon(modifiersAddon) : null;
            var nameAddon = context.traitNameGrammarAddon();

            return PhpObjectTypeDeclAst.Create(
                this.CreateObjectTypeToken(context.ObjectType, context),
                this.GetTokenValueAst(context, context.Identifier)?.ValueString,
                modifiers,
                context.Extends != null ? this.VisitExtendsFrom(context.Extends) : null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                null,
                this.VisitClassStatementListOrEmpty(context.StatementList, context),
                docComment,
                context
            ).WithGrammarAddon("modifiers", modifiers)
            .WithGrammarAddon(
                "identifier",
                nameAddon != null ? this.VisitTraitNameGrammarAddon(nameAddon) : null);
        }
