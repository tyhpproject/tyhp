// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public override PhpPropertyHookAst VisitPropertyHook([NotNull] TyhpParser.PropertyHookContext context)
        {
            var isExpressionBody = context.AccessorBody?.Expr != null;
            return PhpPropertyHookAst.Create(
                this.GetTokenValueAst(context, context.AccessorName)?.ValueString,
                this.VisitReturnsRef(context.ReturnsRef) != null,
                this.VisitPropertyHookModifiers(context.Modifiers),
                this.VisitOptionalParameterList(context.Parameters),
                this.VisitPropertyHookBody(context.AccessorBody),
                context,
                languageMode: null,
                isExpressionBody: isExpressionBody
            ).WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);
        }
