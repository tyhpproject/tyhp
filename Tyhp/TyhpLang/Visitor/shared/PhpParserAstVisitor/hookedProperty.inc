// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public virtual PhpPropertyAst VisitHookedProperty([NotNull] TyhpParser.HookedPropertyContext context)
            => context switch
            {
                TyhpParser.PropertyAccessorContext ctx => this.VisitPropertyAccessor(ctx),
                TyhpParser.PropertyAccessorWithDefaultValueContext ctx => this.VisitPropertyAccessorWithDefaultValue(ctx),
                TyhpParser.HookedPropertyGrammarAddonHandlerContext ctx => this.VisitHookedPropertyGrammarAddonHandler(ctx),
                _ => this.VisitHookedPropertyAlternative(context)
            };


        public virtual PhpPropertyAst VisitHookedPropertyAlternative([NotNull] TyhpParser.HookedPropertyContext context)
        {
            this.ReportUnexpectedAlternative(context, "hooked-property");
            return PhpPropertyAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override PhpPropertyAst VisitPropertyAccessor([NotNull] TyhpParser.PropertyAccessorContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpPropertyAst.Create(
                this.RequiredVariableText(context.Variable, context, "property.Variable"),
                null,
                this.VisitPropertyHookList(context.Accessors),
                docComment,
                context
            );
        }


        public override PhpPropertyAst VisitPropertyAccessorWithDefaultValue([NotNull] TyhpParser.PropertyAccessorWithDefaultValueContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpPropertyAst.Create(
                this.RequiredVariableText(context.Variable, context, "property.Variable"),
                this.VisitExpr(context.Expr),
                this.VisitPropertyHookList(context.Accessors),
                docComment,
                context
            );
        }


        public override PhpPropertyAst VisitHookedPropertyGrammarAddonHandler([NotNull] TyhpParser.HookedPropertyGrammarAddonHandlerContext context)
            => this.VisitHookedPropertyGrammarAddon(context.hookedPropertyGrammarAddon());
