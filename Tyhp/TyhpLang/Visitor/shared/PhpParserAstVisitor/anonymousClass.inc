// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpObjectTypeDeclAst VisitAnonymousClass([NotNull] TyhpParser.AnonymousClassContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpObjectTypeDeclAst.Create(
                this.CreateObjectTypeToken(context.ObjectType, context),
                null, // null here means we are creating an anonymous class
                context.Modifiers != null ? this.VisitClassModifiersOptional(context.Modifiers) : null,
                context.Extends != null ? this.VisitExtendsFrom(context.Extends) : null,
                context.Implements != null ? this.VisitImplementsList(context.Implements) : null,
                null,
                this.VisitClassStatementListOrEmpty(context.StatementList, context),
                docComment,
                context
            );
        }
