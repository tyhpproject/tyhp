// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpPropertyAst VisitProperty([NotNull] TyhpParser.PropertyContext context)
        {
            // The grammar anchors this after the declaration, and the scan runs backwards, so the
            // modifiers and type in front of the variable are simply passed over.
            var docComment = this.FindPossibleDocComment(context._findDocComment);

            return PhpPropertyAst.Create(
                this.RequiredVariableText(context.Variable, context, "property.Variable"),
                context.ValueExpr != null ? this.VisitExpr(context.ValueExpr) : null,
                null,
                docComment,
                context
            );
        }
