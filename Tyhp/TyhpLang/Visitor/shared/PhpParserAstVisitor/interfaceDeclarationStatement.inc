// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpObjectTypeDeclAst VisitInterfaceDeclarationStatement([NotNull] TyhpParser.InterfaceDeclarationStatementContext context)
        {
            if (context.interfaceDeclarationStatementGrammarAddon() != null)
            {
                return this.VisitInterfaceDeclarationStatementGrammarAddon(context.interfaceDeclarationStatementGrammarAddon());
            }

            var docComment = this.FindPossibleDocComment(context.FindDocComment);
            var modifiersAddon = context.interfaceModifiersGrammarAddon();
            var modifiers = modifiersAddon != null ? this.VisitInterfaceModifiersGrammarAddon(modifiersAddon) : null;
            var nameAddon = context.interfaceNameGrammarAddon();

            return PhpObjectTypeDeclAst.Create(
                this.CreateObjectTypeToken(context.ObjectType, context),
                this.GetTokenValueAst(context, context.Identifier)?.ValueString,
                modifiers,
                null,
                context.Extends != null ? this.VisitInterfaceExtendsList(context.Extends) : null,
                null,
                this.VisitClassStatementListOrEmpty(context.StatementList, context),
                docComment,
                context
            ).WithGrammarAddon("modifiers", modifiers)
            .WithGrammarAddon(
                "identifier",
                nameAddon != null ? this.VisitInterfaceNameGrammarAddon(nameAddon) : null);
        }
