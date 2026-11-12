// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Antlr4.Runtime
// #using Tyhp.TyhpLang.Parser


        public IExpression VisitNewNonDereferenceable([NotNull] TyhpParser.NewNonDereferenceableContext context)
            => context switch {
                TyhpParser.TyhpLazyNewNonDereferenceableContext tyhpLazyNewNonDereferenceableContext => this.VisitTyhpLazyNewNonDereferenceable(tyhpLazyNewNonDereferenceableContext),
                TyhpParser.NewClassInstanceNonDereferenceableContext newClassInstanceNonDereferenceableContext => this.VisitNewClassInstanceNonDereferenceable(newClassInstanceNonDereferenceableContext),
                TyhpParser.TyhpNewStructInitializerContext tyhpNewStructInitializerContext => this.VisitTyhpNewStructInitializer(tyhpNewStructInitializerContext),
                TyhpParser.TyhpNewAnonStructInitializerContext tyhpNewAnonStructInitializerContext => this.VisitTyhpNewAnonStructInitializer(tyhpNewAnonStructInitializerContext),
                TyhpParser.NewNonDereferenceableGrammarAddonHandlerContext newNonDereferenceableGrammarAddonHandlerContext => this.VisitNewNonDereferenceableGrammarAddonHandler(newNonDereferenceableGrammarAddonHandlerContext),
                _ => this.VisitNewNonDereferenceableAlt(context),
            };

        
        public virtual PhpNewAst VisitNewNonDereferenceableAlt(TyhpParser.NewNonDereferenceableContext context)
        {
            this.ReportUnexpectedAlternative(context, "new-non-dereferenceable");
            return PhpNewAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override PhpNewAst VisitNewClassInstanceNonDereferenceable([NotNull] TyhpParser.NewClassInstanceNonDereferenceableContext context)
            => PhpNewAst.Create(
                this.VisitClassNameReference(context.Identifier),
                null,
                context
            );


        public override PhpNewAst VisitTyhpLazyNewNonDereferenceable([NotNull] TyhpParser.TyhpLazyNewNonDereferenceableContext context)
        {
            var created = PhpNewAst.Create(
                this.VisitClassNameReference(context.Identifier),
                null,
                context
            );
            created.IsLazy = true;
            return created;
        }


        public override PhpNewAst VisitNewNonDereferenceableGrammarAddonHandler([NotNull] TyhpParser.NewNonDereferenceableGrammarAddonHandlerContext context)
            => this.VisitNewNonDereferenceableGrammarAddon(context.newNonDereferenceableGrammarAddon());


        public override PhpBinaryOpAst VisitTyhpNewStructInitializer([NotNull] TyhpParser.TyhpNewStructInitializerContext context)
        {
            if (context.Identifier == null)
            {
                this.ReportUnexpectedAlternative(context, "tyhpNewStructInitializer", "Identifier");
                return this.DesugarStructInitializer(
                    PhpNewAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context)),
                    context.Stop,
                    context.ArrayPairList,
                    context.OpenBracket,
                    context.CloseBracket,
                    context);
            }

            var created = PhpNewAst.Create(
                this.VisitClassName(context.Identifier),
                null,
                context);
            return this.DesugarStructInitializer(
                created,
                context.Identifier.Stop,
                context.ArrayPairList,
                context.OpenBracket,
                context.CloseBracket,
                context);
        }


        public override PhpBinaryOpAst VisitTyhpNewAnonStructInitializer([NotNull] TyhpParser.TyhpNewAnonStructInitializerContext context)
        {
            if (context.AnonStructDecl == null)
            {
                this.ReportUnexpectedAlternative(context, "tyhpNewAnonStructInitializer", "AnonStructDecl");
                return this.DesugarStructInitializer(
                    PhpNewAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context)),
                    context.Stop,
                    context.ArrayPairList,
                    context.OpenBracket,
                    context.CloseBracket,
                    context);
            }

            if (this.VisitTyhpAnonymousStruct(context.AnonStructDecl) is not TyhpStructDeclAst structDecl)
            {
                this.ReportUnexpectedAlternative(context, "tyhpNewAnonStructInitializer", "AnonStructDecl");
                return this.DesugarStructInitializer(
                    PhpNewAst.CreateError(context, PhpParserAstVisitor.GetCurrentLanguageMode(context)),
                    context.AnonStructDecl.Stop,
                    context.ArrayPairList,
                    context.OpenBracket,
                    context.CloseBracket,
                    context);
            }

            this.CurrentTopStatementList?.Add(structDecl);
            var created = PhpNewAst.Create(
                PhpNameAst.Create(structDecl.Identifier, TyhpParser.T_STRING, context),
                null,
                context);
            return this.DesugarStructInitializer(
                created,
                context.AnonStructDecl.Stop,
                context.ArrayPairList,
                context.OpenBracket,
                context.CloseBracket,
                context);
        }


        /// <summary>
        /// Lowers <c>new Name[…]</c> and <c>new struct { … }[…]</c> to
        /// <c>PhpBinaryOpAst(with, PhpNewAst, pair list)</c>. The <c>new</c> span
        /// stops at the name or the struct body's closing <c>}</c>. The pair list
        /// span is the bracket list, including <c>[]</c>.
        /// </summary>
        private PhpBinaryOpAst DesugarStructInitializer(
            PhpNewAst created,
            IToken? newEnd,
            TyhpParser.ArrayPairListContext? pairList,
            IToken? openBracket,
            IToken? closeBracket,
            ParserRuleContext context)
        {
            created.IsStructInitializer = true;
            if (newEnd != null)
            {
                created.SetSpanFromTokens(context.Start, newEnd);
            }

            var pairs = pairList != null
                ? this.VisitArrayPairList(pairList)
                : PhpArrayPairListAst.Create(Array.Empty<PhpArrayPairAst>(), context);
            if (pairs.GetAllNotNull().All(pair => pair.IsSkippedSlot))
            {
                pairs = PhpArrayPairListAst.Create(Array.Empty<PhpArrayPairAst>(), context);
            }

            if (openBracket != null && closeBracket != null)
            {
                pairs.SetSpanFromTokens(openBracket, closeBracket);
            }

            var withOp = TokenValueAst.Create("with", TyhpParser.T_TYHP_WITH, context);
            if (openBracket != null)
            {
                withOp.SetSpanFromTokens(openBracket, openBracket);
            }

            return PhpBinaryOpAst.Create(withOp, created, pairs, context);
        }
