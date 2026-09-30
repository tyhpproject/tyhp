// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public IScalar VisitScalar([NotNull] TyhpParser.ScalarContext context)
            => context switch {
                TyhpParser.ScalarRealContext scalarRealContext => this.VisitScalarReal(scalarRealContext),
                TyhpParser.ScalarDereferenceableContext scalarDereferenceableContext => this.VisitScalarDereferenceable(scalarDereferenceableContext),
                TyhpParser.ScalarConstantContext scalarConstantContext => this.VisitScalarConstant(scalarConstantContext),
                TyhpParser.ScalarClassConstantContext scalarClassConstantContext => this.VisitScalarClassConstant(scalarClassConstantContext),
                _ => this.VisitScalarAlt(context),
            };


        public virtual IScalar VisitScalarAlt(TyhpParser.ScalarContext context)
        {
            this.ReportUnexpectedAlternative(context, "scalar");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override IScalar VisitScalarReal([NotNull] TyhpParser.ScalarRealContext context)
            => this.VisitRealScalar(context.RealScalar);


        public override IDereferenceableBase VisitScalarDereferenceable([NotNull] TyhpParser.ScalarDereferenceableContext context)
            => this.VisitDereferenceableScalar(context.Scalar);


        public override IScalar VisitScalarConstant([NotNull] TyhpParser.ScalarConstantContext context)
            => this.VisitConstant(context.Scalar);


        public override PhpDereferenceableAst VisitScalarClassConstant([NotNull] TyhpParser.ScalarClassConstantContext context)
            => this.VisitClassConstant(context.Scalar);
