// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public IDereferenceableBase VisitFullyDereferenceable([NotNull] TyhpParser.FullyDereferenceableContext context)
            => context switch {
                TyhpParser.FullyDereferenceableDRefSuffixContext fullyDereferenceableDRefSuffixContext => this.VisitFullyDereferenceableDRefSuffix(fullyDereferenceableDRefSuffixContext),
                TyhpParser.DereferenceableSimpleContext dereferenceableSimpleContext => this.VisitDereferenceableSimple(dereferenceableSimpleContext),
                TyhpParser.DereferenceableConstantContext dereferenceableConstantContext => this.VisitDereferenceableConstant(dereferenceableConstantContext),
                TyhpParser.DereferenceableClassNamePrefixContext dereferenceableClassNamePrefixContext => this.VisitDereferenceableClassNamePrefix(dereferenceableClassNamePrefixContext),
                TyhpParser.DereferenceableReadOnlyContext dereferenceableReadOnlyContext => this.VisitDereferenceableReadOnly(dereferenceableReadOnlyContext),
                TyhpParser.DereferenceableScalarRefContext dereferenceableScalarRefContext => this.VisitDereferenceableScalarRef(dereferenceableScalarRefContext),
                TyhpParser.DereferenceableNewDRefContext dereferenceableNewDRefContext => this.VisitDereferenceableNewDRef(dereferenceableNewDRefContext),
                TyhpParser.DereferenceableExprContext dereferenceableExprContext => this.VisitDereferenceableExpr(dereferenceableExprContext),
                TyhpParser.DereferenceableBaseGrammarAddonHandlerContext dereferenceableBaseGrammarAddonContext => this.VisitDereferenceableBaseGrammarAddonHandler(dereferenceableBaseGrammarAddonContext),
                _ => this.VisitFullyDereferenceableAlt(context)
            };


        public virtual IDereferenceableBase VisitFullyDereferenceableAlt(TyhpParser.FullyDereferenceableContext context)
        {
            this.ReportUnexpectedAlternative(context, "fully-dereferenceable");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override PhpDereferenceableAst VisitFullyDereferenceableDRefSuffix([NotNull] TyhpParser.FullyDereferenceableDRefSuffixContext context)
            => PhpDereferenceableAst.Create(
                this.VisitFullyDereferenceable(context.DRef),
                this.VisitFullyDereferenceableSuffix(context.Suffix),
                context
            );


        public override IDereferenceableBase VisitDereferenceableSimple([NotNull] TyhpParser.DereferenceableSimpleContext context)
            => this.VisitSimpleVariable(context.Variable);


        public override IDereferenceableBase VisitDereferenceableConstant([NotNull] TyhpParser.DereferenceableConstantContext context)
            => this.VisitConstantTokenValue(context.Constant);


        public override IDereferenceableBase VisitDereferenceableClassNamePrefix([NotNull] TyhpParser.DereferenceableClassNamePrefixContext context)
            => this.VisitClassName(context.ClassName);


        public override IDereferenceableBase VisitDereferenceableReadOnly([NotNull] TyhpParser.DereferenceableReadOnlyContext context)
           => PhpNameAst.Create(context.IsReadOnlyPrefix, context);


        public override IDereferenceableBase VisitDereferenceableScalarRef([NotNull] TyhpParser.DereferenceableScalarRefContext context)
            => this.VisitDereferenceableScalar(context.Scalar);


        public override IDereferenceableBase VisitDereferenceableNewDRef([NotNull] TyhpParser.DereferenceableNewDRefContext context)
            => this.VisitNewDereferenceable(context.NewDRef);


        public override PhpDereferenceableExpressionAst VisitDereferenceableExpr([NotNull] TyhpParser.DereferenceableExprContext context)
            => PhpDereferenceableExpressionAst.Create(this.VisitExpr(context.Expr), context);


        public override IDereferenceableBase VisitDereferenceableBaseGrammarAddonHandler([NotNull] TyhpParser.DereferenceableBaseGrammarAddonHandlerContext context)
            => this.VisitDereferenceableBaseGrammarAddon(context.dereferenceableBaseGrammarAddon());
