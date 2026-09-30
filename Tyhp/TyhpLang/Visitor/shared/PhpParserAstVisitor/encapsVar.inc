// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override Ast.Interfaces.IExpression VisitEncapsVarVariableTokenWithArrayIndex([NotNull] TyhpParser.EncapsVarVariableTokenWithArrayIndexContext context)
            => PhpDereferenceableAst.Create(
                PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context),
                PhpArrayAccessAst.Create(this.VisitEncapsVarOffset(context.ArrayIndex), context),
                context
            );


        public override Ast.Interfaces.IExpression VisitEncapsVarObjectMember([NotNull] TyhpParser.EncapsVarObjectMemberContext context)
            => PhpDereferenceableAst.Create(
                PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context),
                PhpInstanceMemberAccessAst.Create(
                    this.GetTokenValueAst(context, context.TokenValue),
                    PhpNameAst.Create(context.Identifier, context),
                    context
                ),
                context
            );


        public override Ast.Interfaces.IExpression VisitEncapsVarVariableToken([NotNull] TyhpParser.EncapsVarVariableTokenContext context)
        {
            if (context.Variable == null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "encaps-var-variable-token", context.GetType().Name
                );
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            return PhpVariableAst.Create(this.GetTokenValueAst(context, context.Variable), false, context);
        }


        public override Ast.Interfaces.IExpression VisitEncapsVarDollarBraceExpr([NotNull] TyhpParser.EncapsVarDollarBraceExprContext context)
        {
            if (context.Expr != null)
                return this.VisitExpr(context.Expr);

            if (context.VarName != null)
            {
                return PhpNameAst.Create(context.VarName, context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            this.ReportUnexpectedAlternative(context, "encaps-var-dollar-brace");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }


        public override Ast.Interfaces.IExpression VisitEncapsVarBraceDollarExprWithArrayIndex([NotNull] TyhpParser.EncapsVarBraceDollarExprWithArrayIndexContext context)
            => PhpDereferenceableAst.Create(
                PhpVariableAst.Create(
                    TokenValueAst.Create("$" + (context.VarName?.Text ?? ""), TyhpParser.T_VARIABLE, context),
                    false,
                    context
                ),
                PhpArrayAccessAst.Create(this.VisitExpr(context.Expr), context),
                context
            );


        public override Ast.Interfaces.IExpression VisitEncapsVarBraceVariable([NotNull] TyhpParser.EncapsVarBraceVariableContext context)
            => this.VisitVariable(context.Variable);
