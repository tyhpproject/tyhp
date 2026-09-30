// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpObjectTypeDeclAst VisitEnumDeclarationStatement([NotNull] TyhpParser.EnumDeclarationStatementContext context)
        {
            if (context.enumDeclarationStatementGrammarAddon() != null)
            {
                return this.VisitEnumDeclarationStatementGrammarAddon(context.enumDeclarationStatementGrammarAddon());
            }

            var docComment = this.FindPossibleDocComment(context.FindDocComment);
            var modifiersAddon = context.enumModifiersGrammarAddon();
            var modifiers = modifiersAddon != null ? this.VisitEnumModifiersGrammarAddon(modifiersAddon) : null;
            var nameAddon = context.enumNameGrammarAddon();
            var backingType = context.enumBackingType();

            return PhpObjectTypeDeclAst.Create(
                this.CreateObjectTypeToken(context.ObjectType, context),
                this.GetTokenValueAst(context, context.Identifier)?.ValueString,
                modifiers,
                null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                backingType != null ? this.VisitEnumBackingType(backingType) as ITypeExpression : null,
                this.VisitClassStatementListOrEmpty(context.StatementList, context),
                docComment,
                context
            ).WithGrammarAddon("modifiers", modifiers)
            .WithGrammarAddon(
                "identifier",
                nameAddon != null ? this.VisitEnumNameGrammarAddon(nameAddon) : null);
        }
