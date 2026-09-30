// #class PhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser
// #using Tyhp.TyhpLang.Enum
// #using static Tyhp.TyhpLang.Enum.PhpModifierExtensions


        public IClassMember VisitAttributedClassStatement([NotNull] TyhpParser.AttributedClassStatementContext context)
            => context switch
            {
                TyhpParser.ClassPropertiesContext ctx => this.VisitClassProperties(ctx),
                TyhpParser.ClassPropertyAccessorsContext ctx => this.VisitClassPropertyAccessors(ctx),
                TyhpParser.ClassConstsContext ctx => this.VisitClassConsts(ctx),
                TyhpParser.ClassTypedConstsContext ctx => this.VisitClassTypedConsts(ctx),
                TyhpParser.ClassEnumCaseContext ctx => this.VisitClassEnumCase(ctx),
                TyhpParser.PhpClassCtorContext ctx => this.VisitPhpClassCtor(ctx),
                TyhpParser.PhpClassMethodContext ctx => this.VisitPhpClassMethod(ctx),
                TyhpParser.AttributedClassStatementGrammarAddonHandlerContext ctx => this.VisitAttributedClassStatementGrammarAddonHandler(ctx),
                _ => this.VisitAttributedClassStatementAlternative(context)
            };


        public virtual IClassMember VisitAttributedClassStatementAlternative([NotNull] TyhpParser.AttributedClassStatementContext context)
        {
            this.ReportUnexpectedAlternative(context, "attributed-class-statement");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override PhpPropertyDeclAst VisitClassProperties([NotNull] TyhpParser.ClassPropertiesContext context)
            => PhpPropertyDeclAst.Create(
                this.VisitPropertyModifiers(context.Modifiers),
                this.VisitOptionalTypeWithoutStatic(context.TypeExpr),
                this.VisitPropertyList(context.PropertyList),
                context
            );


        public override PhpPropertyDeclAst VisitClassPropertyAccessors([NotNull] TyhpParser.ClassPropertyAccessorsContext context)
        {
            var hookedProperty = this.VisitHookedProperty(context.PropertyAccessors);
            return PhpPropertyDeclAst.Create(
                this.VisitPropertyModifiers(context.Modifiers),
                this.VisitOptionalTypeWithoutStatic(context.TypeExpr),
                PhpPropertyListAst.Create([hookedProperty], context),
                context
            );
        }


        public override PhpConstDeclListAst VisitClassConsts([NotNull] TyhpParser.ClassConstsContext context)
            => this.VisitClassConstList(context.ConstList, this.VisitClassConstModifiers(context.Modifiers));


        public override PhpConstDeclListAst VisitClassTypedConsts([NotNull] TyhpParser.ClassTypedConstsContext context)
            => this.VisitClassConstList(
                context.ConstList,
                this.VisitClassConstModifiers(context.Modifiers),
                this.VisitTypeExpr(context.typeExpr()));


        public override PhpEnumCaseAst VisitClassEnumCase([NotNull] TyhpParser.ClassEnumCaseContext context)
            => this.VisitEnumCase(context.EnumCase);


        public override PhpMethodDeclAst VisitPhpClassCtor([NotNull] TyhpParser.PhpClassCtorContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpMethodDeclAst.Create(
                this.GetTokenValueAst(context, context.Identifier)?.ValueString,
                false,
                this.VisitMethodModifiers(context.Modifiers),
                this.VisitCtorParameterList(context.ParameterList),
                null,
                this.VisitMethodBody(context.StatementList),
                docComment,
                context
            );
        }


        public override PhpMethodDeclAst VisitPhpClassMethod([NotNull] TyhpParser.PhpClassMethodContext context)
        {
            var docComment = this.FindPossibleDocComment(context.FindDocComment);

            return PhpMethodDeclAst.Create(
                this.VisitIdentifierWithoutConstructor(context.Identifier).ValueString,
                this.VisitReturnsRef(context.ReturnsRef) != null,
                this.VisitMethodModifiers(context.Modifiers),
                this.VisitParameterList(context.ParameterList),
                this.VisitReturnType(context.ReturnType),
                this.VisitMethodBody(context.StatementList),
                docComment,
                context
            );
        }


        public override IClassMember VisitAttributedClassStatementGrammarAddonHandler([NotNull] TyhpParser.AttributedClassStatementGrammarAddonHandlerContext context)
            => this.VisitAttributedClassStatementGrammarAddon(context.attributedClassStatementGrammarAddon());
