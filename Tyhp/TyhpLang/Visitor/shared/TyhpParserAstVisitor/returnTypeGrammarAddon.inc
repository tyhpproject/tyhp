// #class TyhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        /// <summary>
        /// Visits a Tyhp return type guard: <c>: $variable is SomeType</c> or
        /// <c>: $array[$key] is SomeType</c>.
        /// </summary>
        public override TyhpReturnTypeGuardAst VisitTyhpReturnTypeGuard([NotNull] TyhpParser.TyhpReturnTypeGuardContext context)
        {
            ITypeExpression typeExpr = context.TypeExpr != null
                ? this.VisitTypeExpr(context.TypeExpr)
                : ErrorAst.Create(context, GetCurrentLanguageMode(context));

            var baseVar = PhpVariableAst.Create(
                this.GetTokenValueAst(context, context.GuardVariable),
                false,
                context);

            var indexExpr = TryVisitTypeGuardIndex(context);
            IExpression subject = indexExpr is null
                ? baseVar
                : PhpDereferenceableAst.Create(
                    baseVar,
                    PhpArrayAccessAst.Create(indexExpr, context),
                    context);

            return TyhpReturnTypeGuardAst.Create(subject, typeExpr, context);
        }
