// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions

        public override PhpObjectTypeDeclAst VisitClassDeclarationStatement([NotNull] TyhpParser.ClassDeclarationStatementContext context)
        {
            if (context.classDeclarationStatementGrammarAddon() != null)
            {
                return this.VisitClassDeclarationStatementGrammarAddon(context.classDeclarationStatementGrammarAddon());
            }

            // Claimed before the body is visited: any declaration inside the body that looks up its
            // own docblock advances the shared cursor past this one.
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpObjectTypeDeclAst.Create(
                this.CreateObjectTypeToken(context.ObjectType, context),
                this.GetTokenValueAst(context, context.Identifier)?.ValueString,
                context.Modifiers != null ? this.VisitClassModifiers(context.Modifiers) : null,
                context.Extends != null ? this.VisitExtendsFrom(context.Extends) : null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                null,
                this.VisitClassStatementListOrEmpty(context.StatementList, context),
                docComment,
                context
            ).WithGrammarAddon(
                "identifier",
                context.classNameGrammarAddon() != null
                    ? this.VisitClassNameGrammarAddon(context.classNameGrammarAddon())
                    : null);
        }
