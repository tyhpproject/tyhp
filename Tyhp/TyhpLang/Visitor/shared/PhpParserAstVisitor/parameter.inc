// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Parser


        public override PhpParameterAst VisitParameter([NotNull] TyhpParser.ParameterContext context)
        {
            // Truncated parameter lists (e.g. `operator }` / `extension operator }`) can leave a
            // ParameterContext with a null Variable after ANTLR recovery. Visiting Variable.Text
            // unconditionally aborts with TYHP1003 instead of a normal parse diagnostic.
            if (context.Variable == null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename,
                    context.Start?.Line ?? 0,
                    context.Start?.Column ?? 0,
                    "parameter.Variable");
                return PhpParameterAst.Create(
                    "<error>",
                    null,
                    false,
                    false,
                    null,
                    null,
                    null,
                    context);
            }

            if (IsTyhpPostfixEllipsisOnValueParameter(context))
            {
                var ellipsis = context.IsVariadic!.Start;
                this.Diagnostics.AddError(
                    MessageCode.CheckerCallablePostfixEllipsisNotAllowed,
                    this._filename,
                    ellipsis?.Line ?? context.Start?.Line ?? 0,
                    ellipsis?.Column ?? context.Start?.Column ?? 0);
            }

            return PhpParameterAst.Create(
                context.Variable.Text,
                context.TypeExpr != null
                    ? this.VisitParameterTypeExpressionGrammarAddon(context.TypeExpr)
                    : null,
                context.IsRef?.TokenValue != null,
                context.IsVariadic?.TokenValue != null,
                context.ValueExpr != null ? this.VisitExpr(context.ValueExpr) : null,
                null,
                null,
                context
            );
        }
