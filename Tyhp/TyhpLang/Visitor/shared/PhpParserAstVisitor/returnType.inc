// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum


        public ITypeExpression? VisitReturnType([NotNull] TyhpParser.ReturnTypeContext context)
        {
            // Error recovery can leave returnType null on the parent; callers should guard, but
            // also tolerate a null context here so malformed tyhpdef cannot NRE mid-visit.
            if (context == null)
            {
                return null;
            }

            return context switch
            {
                TyhpParser.ReturnTypeTypeContext returnTypeTypeContext => this.VisitReturnTypeType(returnTypeTypeContext),
                TyhpParser.ReturnTypeGrammarAddonHandlerContext returnTypeGrammarAddonHandlerContext => this.VisitReturnTypeGrammarAddonHandler(returnTypeGrammarAddonHandlerContext),
                _ => this.VisitReturnTypeAlternate(context),
            };
        }


        public virtual ITypeExpression? VisitReturnTypeAlternate([NotNull] TyhpParser.ReturnTypeContext context)
        {
            this.ReportUnexpectedAlternative(context, "returnTypeAlternate");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override ITypeExpression? VisitReturnTypeType([NotNull] TyhpParser.ReturnTypeTypeContext context)
            => context.TypeExpr != null ? this.VisitTypeExpr(context.TypeExpr) : null;


        public override ITypeExpression VisitReturnTypeGrammarAddonHandler([NotNull] TyhpParser.ReturnTypeGrammarAddonHandlerContext context)
        {
            var addon = context.returnTypeGrammarAddon();
            if (addon == null)
            {
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            return this.VisitReturnTypeGrammarAddon(addon);
        }
