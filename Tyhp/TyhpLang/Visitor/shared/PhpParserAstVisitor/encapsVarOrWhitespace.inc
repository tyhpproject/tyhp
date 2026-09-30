// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override Ast.Interfaces.IEncapsVarOrString VisitEncapsVarOrWhitespace([NotNull] TyhpParser.EncapsVarOrWhitespaceContext context)
        {
            if (context.EncapsVar != null)
            {
                switch (context.EncapsVar)
                {
                    case TyhpParser.EncapsVarVariableTokenWithArrayIndexContext varWithIndex:
                        return this.VisitEncapsVarVariableTokenWithArrayIndex(varWithIndex);
                    case TyhpParser.EncapsVarObjectMemberContext objMember:
                        return this.VisitEncapsVarObjectMember(objMember);
                    case TyhpParser.EncapsVarVariableTokenContext varToken:
                        return this.VisitEncapsVarVariableToken(varToken);
                    case TyhpParser.EncapsVarDollarBraceExprContext dollarBrace:
                        return this.VisitEncapsVarDollarBraceExpr(dollarBrace);
                    case TyhpParser.EncapsVarBraceDollarExprWithArrayIndexContext braceDollar:
                        return this.VisitEncapsVarBraceDollarExprWithArrayIndex(braceDollar);
                    case TyhpParser.EncapsVarBraceVariableContext braceVar:
                        return this.VisitEncapsVarBraceVariable(braceVar);
                    default:
                        this.ReportUnexpectedAlternative(context.EncapsVar, "encaps-var");
                        return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
                }
            }

            if (context.EncapsWhitespace != null)
            {
                var tokenValue = this.GetTokenValueAst(context, context.EncapsWhitespace);
                if (tokenValue == null)
                {
                    this.Diagnostics.AddError(
                        MessageCode.VisitorMissingRequiredNode,
                        this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                        "encaps-whitespace-token", context.GetType().Name
                    );
                    return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
                }
                return PhpEncapsStringAst.Create(tokenValue, context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            this.ReportUnexpectedAlternative(context, "encaps-var-or-whitespace");
            return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
        }
