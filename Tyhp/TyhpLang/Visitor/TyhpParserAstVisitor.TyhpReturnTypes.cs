namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Enum;
    using Tyhp.TyhpLang.Parser;

    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {



        private IExpression? TryVisitTypeGuardIndex(TyhpParser.TyhpReturnTypeGuardContext context)
        {
            if (context.GuardIndexVariable != null)
            {
                return PhpVariableAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexVariable),
                    false,
                    context);
            }

            if (context.GuardIndexInt != null)
            {
                return PhpScalarAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexInt),
                    PhpScalarType.Integer,
                    context);
            }

            if (context.GuardIndexHex != null)
            {
                return PhpScalarAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexHex),
                    PhpScalarType.HexNumber,
                    context);
            }

            if (context.GuardIndexOct != null)
            {
                return PhpScalarAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexOct),
                    PhpScalarType.OctalNumber,
                    context);
            }

            if (context.GuardIndexBin != null)
            {
                return PhpScalarAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexBin),
                    PhpScalarType.BinaryNumber,
                    context);
            }

            if (context.GuardIndexString != null)
            {
                return PhpScalarAst.Create(
                    this.GetTokenValueAst(context, context.GuardIndexString),
                    PhpScalarType.String,
                    context);
            }

            return null;
        }
    }
}
