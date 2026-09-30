// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpConstDeclAst VisitClassConstDecl([NotNull] TyhpParser.ClassConstDeclContext context)
            => this.VisitClassConstDecl(context, modifiers: null, type: null);


        private PhpConstDeclAst VisitClassConstDecl(
            [NotNull] TyhpParser.ClassConstDeclContext context,
            PhpModifierListAst? modifiers,
            ITypeExpression? type)
        {
            string name = "";
            if (context.Identifier != null)
            {
                name = this.GetTokenValueAst(context, context.Identifier)?.ValueString ?? "";
            }
            else if (context.IdentifierSR != null)
            {
                name = this.VisitSemiReserved(context.IdentifierSR).ValueString ?? "";
            }

            var docComment = this.FindPossibleDocComment(context._findDocComment);

            return PhpConstDeclAst.Create(
                name,
                this.VisitExpr(context.ValueExpr),
                docComment,
                context,
                modifiers: modifiers,
                type: type
            );
        }
