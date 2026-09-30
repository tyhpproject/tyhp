// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public IScalar VisitRealScalar([NotNull] TyhpParser.RealScalarContext context)
            => context switch {
                TyhpParser.ScalarLNumberContext scalarLNumberContext => this.VisitScalarLNumber(scalarLNumberContext),
                TyhpParser.ScalarDNumberContext scalarDNumberContext => this.VisitScalarDNumber(scalarDNumberContext),
                TyhpParser.ScalarONumberContext scalarONumberContext => this.VisitScalarONumber(scalarONumberContext),
                TyhpParser.ScalarHNumberContext scalarHNumberContext => this.VisitScalarHNumber(scalarHNumberContext),
                TyhpParser.ScalarBNumberContext scalarBNumberContext => this.VisitScalarBNumber(scalarBNumberContext),
                TyhpParser.ScalarHeredocContext scalarHeredocContext => this.VisitScalarHeredoc(scalarHeredocContext),
                _ => this.VisitRealScalarAlt(context),
            };


        public virtual IScalar VisitRealScalarAlt(TyhpParser.RealScalarContext context)
        {
            this.ReportUnexpectedAlternative(context, "real-scalar");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override PhpScalarAst VisitScalarLNumber([NotNull] TyhpParser.ScalarLNumberContext context)
            => PhpScalarAst.Create(
                this.GetTokenValueAst(context, context.Scalar),
                PhpScalarType.Integer,
                context
            );


        public override PhpScalarAst VisitScalarDNumber([NotNull] TyhpParser.ScalarDNumberContext context)
            => PhpScalarAst.Create(
                this.GetTokenValueAst(context, context.Scalar),
                PhpScalarType.Float,
                context
            );


        public override PhpScalarAst VisitScalarONumber([NotNull] TyhpParser.ScalarONumberContext context)
            => PhpScalarAst.Create(
                this.GetTokenValueAst(context, context.Scalar),
                PhpScalarType.OctalNumber,
                context
            );


        public override PhpScalarAst VisitScalarHNumber([NotNull] TyhpParser.ScalarHNumberContext context)
            => PhpScalarAst.Create(
                this.GetTokenValueAst(context, context.Scalar),
                PhpScalarType.HexNumber,
                context
            );


        public override PhpScalarAst VisitScalarBNumber([NotNull] TyhpParser.ScalarBNumberContext context)
            => PhpScalarAst.Create(
                this.GetTokenValueAst(context, context.Scalar),
                PhpScalarType.BinaryNumber,
                context
            );


        public override PhpEncapsListAst VisitScalarHeredoc([NotNull] TyhpParser.ScalarHeredocContext context)
            => (context.EncapsList != null ?
                this.VisitEncapsList(context.EncapsList) :
                PhpEncapsListAst.Create([], context)
            )
                .WithStringType(this.GetTokenValueAst(context, context.TokenValue));
