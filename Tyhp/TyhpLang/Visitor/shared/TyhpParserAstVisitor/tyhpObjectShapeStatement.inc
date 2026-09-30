// #class TyhpParserAstVisitor
// #using Antlr4.Runtime
// #using Antlr4.Runtime.Misc
// #using Antlr4.Runtime.Tree
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public IClassMember? VisitTyhpObjectShapeStatement(
            TyhpParser.TyhpObjectShapeStatementContext? context)
        {
            if (context == null)
            {
                return null;
            }

            return context switch
            {
                TyhpParser.TyhpObjectShapeClassConstsContext ctx =>
                    this.VisitTyhpObjectShapeClassConsts(ctx),
                TyhpParser.TyhpObjectShapeClassTypedConstsContext ctx =>
                    this.VisitTyhpObjectShapeClassTypedConsts(ctx),
                TyhpParser.TyhpObjectShapeTyhpdefMemberContext ctx =>
                    this.VisitTyhpObjectShapeTyhpdefMember(ctx),
                _ => HandleUnexpectedAlternative<IClassMember>(context, "tyhpObjectShapeStatement")
            };
        }


        /// <summary>
        /// Visits untyped <c>const NAME = expr</c> inside an object shape.
        /// </summary>
        public override PhpConstDeclListAst VisitTyhpObjectShapeClassConsts(
            [NotNull] TyhpParser.TyhpObjectShapeClassConstsContext context)
            => this.VisitObjectShapeClassConstList(
                context,
                context.Attributes,
                context.tyhpdefDeprecatedOrObsolete(),
                context.Modifiers,
                context.ConstList,
                type: null);


        /// <summary>
        /// Visits typed <c>const T NAME = expr</c> inside an object shape.
        /// </summary>
        public override PhpConstDeclListAst VisitTyhpObjectShapeClassTypedConsts(
            [NotNull] TyhpParser.TyhpObjectShapeClassTypedConstsContext context)
        {
            ITypeExpression? type = null;
            if (context.TypeExpr != null)
            {
                type = this.VisitTypeExprWithoutStatic(context.TypeExpr);
            }
            else
            {
                this.ReportMissingRequired(context, "tyhpObjectShapeClassTypedConsts.TypeExpr");
            }

            return this.VisitObjectShapeClassConstList(
                context,
                context.Attributes,
                context.tyhpdefDeprecatedOrObsolete(),
                context.Modifiers,
                context.ConstList,
                type);
        }


        /// <summary>
        /// Visits a non-PHP-const shape member by delegating to
        /// <see cref="VisitTyhpdefClassStatement"/>.
        /// </summary>
        public override IClassMember VisitTyhpObjectShapeTyhpdefMember(
            [NotNull] TyhpParser.TyhpObjectShapeTyhpdefMemberContext context)
        {
            if (context.Member == null)
            {
                this.ReportMissingRequired(context, "tyhpObjectShapeTyhpdefMember.Member");
                return ErrorAst.Create(context, GetCurrentLanguageMode(context));
            }

            return this.VisitTyhpdefClassStatement(context.Member)
                ?? ErrorAst.Create(context, GetCurrentLanguageMode(context));
        }
