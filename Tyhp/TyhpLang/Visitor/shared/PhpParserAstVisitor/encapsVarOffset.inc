// #class PhpParserAstVisitor
// #using Antlr4.Runtime.Misc
// #using Tyhp.Domain.Exceptions
// #using Tyhp.TyhpLang.Ast
// #using Tyhp.TyhpLang.Ast.Interfaces
// #using Tyhp.TyhpLang.Enum
// #using Tyhp.TyhpLang.Parser


        public override Ast.Interfaces.IExpression VisitEncapsVarOffset([NotNull] TyhpParser.EncapsVarOffsetContext context)
        {
            if (context.TokenValue == null)
            {
                this.Diagnostics.AddError(
                    MessageCode.VisitorMissingRequiredNode,
                    this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                    "encaps-var-offset-token", context.GetType().Name
                );
                return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
            }

            var token = this.GetTokenValueAst(context, context.TokenValue);

            // In PHP simple-syntax interpolation the offset is one of: a numeric literal ("$v[0]"), a
            // variable ("$v[$x]"), or a bare word treated as a string key ("$v[key]"). Build the node
            // shape that emits the same unquoted offset PHP expects inside the brackets.
            switch (context.TokenValue.Type)
            {
                case TyhpParser.T_NUM_STRING:
                    var number = PhpScalarAst.Create(token, PhpScalarType.Integer, context);
                    return context.IsNegative != null
                        ? PhpUnaryOpAst.Create(this.GetTokenValueAst(context, context.IsNegative), number, context)
                        : number;
                case TyhpParser.T_VARIABLE:
                    return PhpVariableAst.Create(token, false, context);
                default:
                    return PhpNameAst.Create(context.TokenValue, context);
            }
        }
