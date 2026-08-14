namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public override IExpression VisitExpr([NotNull] TyhpParser.ExprContext context)
            => this.VisitPhpExprPrec(context.phpExprPrec());

        public override IExpression VisitPhpTopExpr([NotNull] TyhpParser.PhpTopExprContext context)
            => this.VisitPhpExprPrec(context.phpExprPrec());

        public override TokenValueAst? VisitPhpExprUnaryPreOps([NotNull] TyhpParser.PhpExprUnaryPreOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprUnaryPreOpsGrammarAddon(context.phpExprUnaryPreOpsGrammarAddon())
            );

        public override TokenValueAst? VisitPhpExprUnaryPreOpsGrammarAddon([NotNull] TyhpParser.PhpExprUnaryPreOpsGrammarAddonContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue
            );

        public override TokenValueAst? VisitPhpExprUnaryPostOps([NotNull] TyhpParser.PhpExprUnaryPostOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprUnaryPostOpsGrammarAddon(context.phpExprUnaryPostOpsGrammarAddon())
            );

        public override TokenValueAst? VisitPhpExprUnaryPostOpsGrammarAddon([NotNull] TyhpParser.PhpExprUnaryPostOpsGrammarAddonContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue
            );

        public override TokenValueAst? VisitPhpExprBinaryMulDivOps([NotNull] TyhpParser.PhpExprBinaryMulDivOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprBinaryMulDivOpsGrammarAddon(context.phpExprBinaryMulDivOpsGrammarAddon())
            );

        public override TokenValueAst? VisitPhpExprBinaryMulDivOpsGrammarAddon([NotNull] TyhpParser.PhpExprBinaryMulDivOpsGrammarAddonContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue
            );

        public override TokenValueAst? VisitPhpExprBinaryAddSubOps([NotNull] TyhpParser.PhpExprBinaryAddSubOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprBinaryAddSubOpsGrammarAddon(context.phpExprBinaryAddSubOpsGrammarAddon())
            );

        public override TokenValueAst? VisitPhpExprBinaryAddSubOpsGrammarAddon([NotNull] TyhpParser.PhpExprBinaryAddSubOpsGrammarAddonContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue
            );

        public override TokenValueAst? VisitPhpExprBinaryShiftOps([NotNull] TyhpParser.PhpExprBinaryShiftOpsContext context)
            => context.TokenValue.Type == TyhpLang.Parser.TyhpParser.T_SYM_GT && context.IsSR?.Type == TyhpLang.Parser.TyhpParser.T_SYM_GT ?
                TokenValueAst.Create(">>", TyhpLang.Parser.TyhpParser.T_SR, context) :
                this.GetTokenValueAst(
                    context,
                    context.TokenValue,
                    () => this.VisitPhpExprBinaryShiftOpsGrammarAddon(context.phpExprBinaryShiftOpsGrammarAddon())
                );

        public override TokenValueAst? VisitPhpExprBinaryShiftOpsGrammarAddon([NotNull] TyhpParser.PhpExprBinaryShiftOpsGrammarAddonContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue
            );

        public override TokenValueAst? VisitPhpExprBinaryConcatOps([NotNull] TyhpParser.PhpExprBinaryConcatOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprBinaryConcatOpsGrammarAddon(context.phpExprBinaryConcatOpsGrammarAddon())
            );

        public override TokenValueAst? VisitPhpExprBinaryConcatOpsGrammarAddon([NotNull] TyhpParser.PhpExprBinaryConcatOpsGrammarAddonContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue
            );

        public override TokenValueAst? VisitPhpExprCompareSizeOps([NotNull] TyhpParser.PhpExprCompareSizeOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprCompareSizeOpsGrammarAddon(context.phpExprCompareSizeOpsGrammarAddon())
            );

        public override TokenValueAst? VisitPhpExprCompareSizeOpsGrammarAddon([NotNull] TyhpParser.PhpExprCompareSizeOpsGrammarAddonContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue
            );

        public override TokenValueAst? VisitPhpExprCompareEqualityOps([NotNull] TyhpParser.PhpExprCompareEqualityOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprCompareEqualityOpsGrammarAddon(context.phpExprCompareEqualityOpsGrammarAddon())
            );

        public override TokenValueAst? VisitPhpExprCompareEqualityOpsGrammarAddon([NotNull] TyhpParser.PhpExprCompareEqualityOpsGrammarAddonContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue
            );

        public override TokenValueAst? VisitPhpExprAssignmentOps([NotNull] TyhpParser.PhpExprAssignmentOpsContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue,
                () => this.VisitPhpExprAssignmentOpsGrammarAddon(context.phpExprAssignmentOpsGrammarAddon())
            );

        public override TokenValueAst? VisitPhpExprAssignmentOpsGrammarAddon([NotNull] TyhpParser.PhpExprAssignmentOpsGrammarAddonContext context)
            => this.GetTokenValueAst(
                context,
                context.TokenValue
            );

        public IExpression VisitPhpExprPrec(TyhpParser.PhpExprPrecContext? context)
        {
            // Truncated `|` / `&` after recovery commonly leaves BinaryOr / Ampersand children null;
            // Antlr's Visit(null) throws NRE (TYHP1003) on this runtime, so guard here.
            if (context == null)
            {
                return this.VisitPhpExprPrecAlt(null);
            }

            return context switch
            {
                TyhpParser.PhpExprCloneCallContext phpExprCloneCallContext => this.VisitPhpExprCloneCall(phpExprCloneCallContext),
                TyhpParser.PhpExprCloneContext phpExprCloneContext => this.VisitPhpExprClone(phpExprCloneContext),
                TyhpParser.PhpExprUnaryPreOpGrammarAddon001HandlerContext phpExprUnaryPreOpGrammarAddon001HandlerContext => this.VisitPhpExprUnaryPreOpGrammarAddon001Handler(phpExprUnaryPreOpGrammarAddon001HandlerContext),
                TyhpParser.PhpExprUnaryPostOpGrammarAddon001HandlerContext phpExprUnaryPostOpGrammarAddon001HandlerContext => this.VisitPhpExprUnaryPostOpGrammarAddon001Handler(phpExprUnaryPostOpGrammarAddon001HandlerContext),
                TyhpParser.PhpExprBinaryOpGrammarAddon001HandlerContext phpExprBinaryOpGrammarAddon001HandlerContext => this.VisitPhpExprBinaryOpGrammarAddon001Handler(phpExprBinaryOpGrammarAddon001HandlerContext),
                TyhpParser.PhpExprPowContext phpExprPowContext => this.VisitPhpExprPow(phpExprPowContext),
                TyhpParser.PhpExprUnaryPreOpContext phpExprUnaryPreOpContext => this.VisitPhpExprUnaryPreOp(phpExprUnaryPreOpContext),
                TyhpParser.PhpExprUnaryPostOpContext phpExprUnaryPostOpContext => this.VisitPhpExprUnaryPostOp(phpExprUnaryPostOpContext),
                TyhpParser.PhpExprInstanceOfContext phpExprInstanceOfContext => this.VisitPhpExprInstanceOf(phpExprInstanceOfContext),
                TyhpParser.PhpExprUnaryPreOpGrammarAddon002HandlerContext phpExprUnaryPreOpGrammarAddon002HandlerContext => this.VisitPhpExprUnaryPreOpGrammarAddon002Handler(phpExprUnaryPreOpGrammarAddon002HandlerContext),
                TyhpParser.PhpExprUnaryPostOpGrammarAddon002HandlerContext phpExprUnaryPostOpGrammarAddon002HandlerContext => this.VisitPhpExprUnaryPostOpGrammarAddon002Handler(phpExprUnaryPostOpGrammarAddon002HandlerContext),
                TyhpParser.PhpExprBinaryOpGrammarAddon002HandlerContext phpExprBinaryOpGrammarAddon002HandlerContext => this.VisitPhpExprBinaryOpGrammarAddon002Handler(phpExprBinaryOpGrammarAddon002HandlerContext),
                TyhpParser.PhpExprNotContext phpExprNotContext => this.VisitPhpExprNot(phpExprNotContext),
                TyhpParser.PhpExprBinaryMulDivContext phpExprBinaryMulDivContext => this.VisitPhpExprBinaryMulDiv(phpExprBinaryMulDivContext),
                TyhpParser.PhpExprBinaryAddSubContext phpExprBinaryAddSubContext => this.VisitPhpExprBinaryAddSub(phpExprBinaryAddSubContext),
                TyhpParser.PhpExprBinaryShiftContext phpExprBinaryShiftContext => this.VisitPhpExprBinaryShift(phpExprBinaryShiftContext),
                TyhpParser.PhpExprBinaryConcatContext phpExprBinaryConcatContext => this.VisitPhpExprBinaryConcat(phpExprBinaryConcatContext),
                TyhpParser.PhpExprPipeContext phpExprPipeContext => this.VisitPhpExprPipe(phpExprPipeContext),
                TyhpParser.PhpExprCompareSizeContext phpExprCompareSizeContext => this.VisitPhpExprCompareSize(phpExprCompareSizeContext),
                TyhpParser.PhpExprCompareEqualityContext phpExprCompareEqualityContext => this.VisitPhpExprCompareEquality(phpExprCompareEqualityContext),
                TyhpParser.PhpExprUnaryPreOpGrammarAddon003HandlerContext phpExprUnaryPreOpGrammarAddon003HandlerContext => this.VisitPhpExprUnaryPreOpGrammarAddon003Handler(phpExprUnaryPreOpGrammarAddon003HandlerContext),
                TyhpParser.PhpExprUnaryPostOpGrammarAddon003HandlerContext phpExprUnaryPostOpGrammarAddon003HandlerContext => this.VisitPhpExprUnaryPostOpGrammarAddon003Handler(phpExprUnaryPostOpGrammarAddon003HandlerContext),
                TyhpParser.PhpExprBinaryOpGrammarAddon003HandlerContext phpExprBinaryOpGrammarAddon003HandlerContext => this.VisitPhpExprBinaryOpGrammarAddon003Handler(phpExprBinaryOpGrammarAddon003HandlerContext),
                TyhpParser.PhpExprAmpersandContext phpExprAmpersandContext => this.VisitPhpExprAmpersand(phpExprAmpersandContext),
                TyhpParser.PhpExprBitwiseAndContext phpExprBitwiseAndContext => this.VisitPhpExprBitwiseAnd(phpExprBitwiseAndContext),
                TyhpParser.PhpExprBinaryXorContext phpExprBinaryXorContext => this.VisitPhpExprBinaryXor(phpExprBinaryXorContext),
                TyhpParser.PhpExprBinaryOrContext phpExprBinaryOrContext => this.VisitPhpExprBinaryOr(phpExprBinaryOrContext),
                TyhpParser.PhpExprBooleanAndContext phpExprBooleanAndContext => this.VisitPhpExprBooleanAnd(phpExprBooleanAndContext),
                TyhpParser.PhpExprBooleanOrContext phpExprBooleanOrContext => this.VisitPhpExprBooleanOr(phpExprBooleanOrContext),
                TyhpParser.PhpExprCoalesceContext phpExprCoalesceContext => this.VisitPhpExprCoalesce(phpExprCoalesceContext),
                TyhpParser.PhpExprUnaryPreOpGrammarAddon004HandlerContext phpExprUnaryPreOpGrammarAddon004HandlerContext => this.VisitPhpExprUnaryPreOpGrammarAddon004Handler(phpExprUnaryPreOpGrammarAddon004HandlerContext),
                TyhpParser.PhpExprUnaryPostOpGrammarAddon004HandlerContext phpExprUnaryPostOpGrammarAddon004HandlerContext => this.VisitPhpExprUnaryPostOpGrammarAddon004Handler(phpExprUnaryPostOpGrammarAddon004HandlerContext),
                TyhpParser.PhpExprBinaryOpGrammarAddon004HandlerContext phpExprBinaryOpGrammarAddon004HandlerContext => this.VisitPhpExprBinaryOpGrammarAddon004Handler(phpExprBinaryOpGrammarAddon004HandlerContext),
                TyhpParser.PhpExprTernaryContext phpExprTernaryContext => this.VisitPhpExprTernary(phpExprTernaryContext),
                TyhpParser.PhpExprAssignmentContext phpExprAssignmentContext => this.VisitPhpExprAssignment(phpExprAssignmentContext),
                TyhpParser.PhpExprYieldFromContext phpExprYieldFromContext => this.VisitPhpExprYieldFrom(phpExprYieldFromContext),
                TyhpParser.PhpExprYieldValueContext phpExprYieldValueContext => this.VisitPhpExprYieldValue(phpExprYieldValueContext),
                TyhpParser.PhpExprPrintContext phpExprPrintContext => this.VisitPhpExprPrint(phpExprPrintContext),
                TyhpParser.PhpExprUnaryPreOpGrammarAddon005HandlerContext phpExprUnaryPreOpGrammarAddon005HandlerContext => this.VisitPhpExprUnaryPreOpGrammarAddon005Handler(phpExprUnaryPreOpGrammarAddon005HandlerContext),
                TyhpParser.PhpExprUnaryPostOpGrammarAddon005HandlerContext phpExprUnaryPostOpGrammarAddon005HandlerContext => this.VisitPhpExprUnaryPostOpGrammarAddon005Handler(phpExprUnaryPostOpGrammarAddon005HandlerContext),
                TyhpParser.PhpExprBinaryOpGrammarAddon005HandlerContext phpExprBinaryOpGrammarAddon005HandlerContext => this.VisitPhpExprBinaryOpGrammarAddon005Handler(phpExprBinaryOpGrammarAddon005HandlerContext),
                TyhpParser.PhpExprLogicalAndContext phpExprLogicalAndContext => this.VisitPhpExprLogicalAnd(phpExprLogicalAndContext),
                TyhpParser.PhpExprLogicalXorContext phpExprLogicalXorContext => this.VisitPhpExprLogicalXor(phpExprLogicalXorContext),
                TyhpParser.PhpExprLogicalOrContext phpExprLogicalOrContext => this.VisitPhpExprLogicalOr(phpExprLogicalOrContext),
                TyhpParser.InternalFunctionIncludeContext internalFunctionIncludeContext => this.VisitInternalFunctionInclude(internalFunctionIncludeContext),
                TyhpParser.InternalFunctionIncludeOnceContext internalFunctionIncludeOnceContext => this.VisitInternalFunctionIncludeOnce(internalFunctionIncludeOnceContext),
                TyhpParser.InternalFunctionRequireContext internalFunctionRequireContext => this.VisitInternalFunctionRequire(internalFunctionRequireContext),
                TyhpParser.InternalFunctionRequireOnceContext internalFunctionRequireOnceContext => this.VisitInternalFunctionRequireOnce(internalFunctionRequireOnceContext),
                TyhpParser.PhpExprInlineFunctionShortContext phpExprInlineFunctionShortContext => this.VisitPhpExprInlineFunctionShort(phpExprInlineFunctionShortContext),
                TyhpParser.PhpExprThrowContext phpExprThrowContext => this.VisitPhpExprThrow(phpExprThrowContext),
                TyhpParser.PhpExprUnaryPreOpGrammarAddon006HandlerContext phpExprUnaryPreOpGrammarAddon006HandlerContext => this.VisitPhpExprUnaryPreOpGrammarAddon006Handler(phpExprUnaryPreOpGrammarAddon006HandlerContext),
                TyhpParser.PhpExprUnaryPostOpGrammarAddon006HandlerContext phpExprUnaryPostOpGrammarAddon006HandlerContext => this.VisitPhpExprUnaryPostOpGrammarAddon006Handler(phpExprUnaryPostOpGrammarAddon006HandlerContext),
                TyhpParser.PhpExprBinaryOpGrammarAddon006HandlerContext phpExprBinaryOpGrammarAddon006HandlerContext => this.VisitPhpExprBinaryOpGrammarAddon006Handler(phpExprBinaryOpGrammarAddon006HandlerContext),
                TyhpParser.PhpExprBaseHandlerContext phpExprBaseHandlerContext => this.VisitPhpExprBaseHandler(phpExprBaseHandlerContext),
                _ => this.VisitPhpExprPrecAlt(context),
            };
        }

        public virtual IExpression VisitPhpExprPrecAlt(TyhpParser.PhpExprPrecContext? context)
        {
            if (context == null)
            {
                return ErrorAst.Create(
                    "Missing expression after error recovery",
                    Domain.Exceptions.MessageCode.VisitorMissingRequiredNode,
                    0,
                    0);
            }

            return (this.Visit(context) as IExpression) ?? UnexpectedNodeAst.Create(context);
        }

        public override PhpUnaryOpAst VisitPhpExprCloneCall([NotNull] TyhpParser.PhpExprCloneCallContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitCloneArgumentList(context.ArgumentList),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprClone([NotNull] TyhpParser.PhpExprCloneContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPreOpGrammarAddon001Handler([NotNull] TyhpParser.PhpExprUnaryPreOpGrammarAddon001HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPostOpGrammarAddon001Handler([NotNull] TyhpParser.PhpExprUnaryPostOpGrammarAddon001HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                false,
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryOpGrammarAddon001Handler([NotNull] TyhpParser.PhpExprBinaryOpGrammarAddon001HandlerContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprPow([NotNull] TyhpParser.PhpExprPowContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPreOp([NotNull] TyhpParser.PhpExprUnaryPreOpContext context)
            => PhpUnaryOpAst.Create(
                this.VisitPhpExprUnaryPreOps(context.Op),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPostOp([NotNull] TyhpParser.PhpExprUnaryPostOpContext context)
            => PhpUnaryOpAst.Create(
                this.VisitPhpExprUnaryPostOps(context.Op),
                this.VisitPhpExprPrec(context.L),
                false,
                context
            );

        public override PhpBinaryOpAst VisitPhpExprInstanceOf([NotNull] TyhpParser.PhpExprInstanceOfContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPreOpGrammarAddon002Handler([NotNull] TyhpParser.PhpExprUnaryPreOpGrammarAddon002HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPostOpGrammarAddon002Handler([NotNull] TyhpParser.PhpExprUnaryPostOpGrammarAddon002HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                false,
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryOpGrammarAddon002Handler([NotNull] TyhpParser.PhpExprBinaryOpGrammarAddon002HandlerContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpUnaryOpAst VisitPhpExprNot([NotNull] TyhpParser.PhpExprNotContext context)
        => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryMulDiv([NotNull] TyhpParser.PhpExprBinaryMulDivContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryAddSub([NotNull] TyhpParser.PhpExprBinaryAddSubContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryShift([NotNull] TyhpParser.PhpExprBinaryShiftContext context)
            => PhpBinaryOpAst.Create(
                // this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprBinaryShiftOps(context.Op) ?? TokenValueAst.Create("", -1, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryConcat([NotNull] TyhpParser.PhpExprBinaryConcatContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprPipe([NotNull] TyhpParser.PhpExprPipeContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprCompareSize([NotNull] TyhpParser.PhpExprCompareSizeContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprCompareEquality([NotNull] TyhpParser.PhpExprCompareEqualityContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPreOpGrammarAddon003Handler([NotNull] TyhpParser.PhpExprUnaryPreOpGrammarAddon003HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPostOpGrammarAddon003Handler([NotNull] TyhpParser.PhpExprUnaryPostOpGrammarAddon003HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                false,
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryOpGrammarAddon003Handler([NotNull] TyhpParser.PhpExprBinaryOpGrammarAddon003HandlerContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpUnaryOpAst VisitPhpExprAmpersand([NotNull] TyhpParser.PhpExprAmpersandContext context)
        {
            // Truncated return-type / intersection-shaped `&` (e.g. `function demo(): Foo& {}`,
            // `function demo(): & {}`) is often recovered as a unary ampersand with null Op and/or R.
            // Dereferencing those fields aborted with TYHP1003.
            if (context.Op == null || context.R == null)
            {
                return PhpUnaryOpAst.Create(
                    context.Op != null ? this.GetTokenValueAst(context.Op, context.Op.TokenValue) : null,
                    context.R != null
                        ? this.VisitPhpExprPrec(context.R)
                        : ErrorAst.Create(context, GetCurrentLanguageMode(context)),
                    true,
                    context
                );
            }

            return PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );
        }

        public override PhpBinaryOpAst VisitPhpExprBitwiseAnd([NotNull] TyhpParser.PhpExprBitwiseAndContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryXor([NotNull] TyhpParser.PhpExprBinaryXorContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryOr([NotNull] TyhpParser.PhpExprBinaryOrContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBooleanAnd([NotNull] TyhpParser.PhpExprBooleanAndContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBooleanOr([NotNull] TyhpParser.PhpExprBooleanOrContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprCoalesce([NotNull] TyhpParser.PhpExprCoalesceContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPreOpGrammarAddon004Handler([NotNull] TyhpParser.PhpExprUnaryPreOpGrammarAddon004HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPostOpGrammarAddon004Handler([NotNull] TyhpParser.PhpExprUnaryPostOpGrammarAddon004HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                false,
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryOpGrammarAddon004Handler([NotNull] TyhpParser.PhpExprBinaryOpGrammarAddon004HandlerContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );


        public override PhpTernaryOpAst VisitPhpExprTernary([NotNull] TyhpParser.PhpExprTernaryContext context)
            => PhpTernaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op1),
                this.GetTokenValueAst(context, context.Op2),
                this.VisitPhpExprPrec(context.L),
                context.T != null ? this.VisitPhpExprPrec(context.T) : null,
                this.VisitPhpExprPrec(context.F),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprAssignment([NotNull] TyhpParser.PhpExprAssignmentContext context)
            => PhpBinaryOpAst.Create(
                this.VisitPhpExprAssignmentOps(context.Op)
                    ?? this.GetTokenValueAst(context.Op, context.Op.TokenValue!),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpUnaryOpAst VisitPhpExprYieldFrom([NotNull] TyhpParser.PhpExprYieldFromContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprYieldValue([NotNull] TyhpParser.PhpExprYieldValueContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprPrint([NotNull] TyhpParser.PhpExprPrintContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPreOpGrammarAddon005Handler([NotNull] TyhpParser.PhpExprUnaryPreOpGrammarAddon005HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPostOpGrammarAddon005Handler([NotNull] TyhpParser.PhpExprUnaryPostOpGrammarAddon005HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                false,
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryOpGrammarAddon005Handler([NotNull] TyhpParser.PhpExprBinaryOpGrammarAddon005HandlerContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );


        public override PhpBinaryOpAst VisitPhpExprLogicalAnd([NotNull] TyhpParser.PhpExprLogicalAndContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprLogicalXor([NotNull] TyhpParser.PhpExprLogicalXorContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpBinaryOpAst VisitPhpExprLogicalOr([NotNull] TyhpParser.PhpExprLogicalOrContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );

        public override PhpUnaryOpAst VisitInternalFunctionInclude([NotNull] TyhpParser.InternalFunctionIncludeContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitInternalFunctionIncludeOnce([NotNull] TyhpParser.InternalFunctionIncludeOnceContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitInternalFunctionRequire([NotNull] TyhpParser.InternalFunctionRequireContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitInternalFunctionRequireOnce([NotNull] TyhpParser.InternalFunctionRequireOnceContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpInlineFunctionAst VisitPhpExprInlineFunctionShort([NotNull] TyhpParser.PhpExprInlineFunctionShortContext context)
            => PhpInlineFunctionAst.Create(
                TokenValueListAst.Create([
                    .. this.VisitFunctionModifiersGrammarAddon(context.functionModifiersGrammarAddon())?.GetAll() ?? [],
                    .. new TokenValueAst?[] { context.IsStatic != null ? this.GetTokenValueAst(context, context.IsStatic) : null }.Where(x => x != null).Cast<TokenValueAst>(),
                ], context),
                this.VisitReturnsRef(context.ReturnsRef) != null,
                this.VisitFunctionNameGrammarAddon(context.functionNameGrammarAddon()),
                this.VisitParameterList(context.ParameterList),
                this.VisitReturnType(context.returnType()),
                this.VisitPhpExprPrec(context.R),
                context
            ).WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null);

        public override PhpUnaryOpAst VisitPhpExprThrow([NotNull] TyhpParser.PhpExprThrowContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.Op),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPreOpGrammarAddon006Handler([NotNull] TyhpParser.PhpExprUnaryPreOpGrammarAddon006HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.R),
                true,
                context
            );

        public override PhpUnaryOpAst VisitPhpExprUnaryPostOpGrammarAddon006Handler([NotNull] TyhpParser.PhpExprUnaryPostOpGrammarAddon006HandlerContext context)
            => PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                false,
                context
            );

        public override PhpBinaryOpAst VisitPhpExprBinaryOpGrammarAddon006Handler([NotNull] TyhpParser.PhpExprBinaryOpGrammarAddon006HandlerContext context)
            => PhpBinaryOpAst.Create(
                this.GetTokenValueAst(context.Op, context.Op.TokenValue),
                this.VisitPhpExprPrec(context.L),
                this.VisitPhpExprPrec(context.R),
                context
            );


        public override IExpression VisitPhpExprBaseHandler([NotNull] TyhpParser.PhpExprBaseHandlerContext context)
            => this.VisitPhpExprBase(context.phpExprBase());

        public IExpression VisitPhpExprBase([NotNull] TyhpParser.PhpExprBaseContext context)
            => context switch {
                TyhpParser.ExprNewNonDRefContext exprNewNonDRefContext => this.VisitExprNewNonDRef(exprNewNonDRefContext),
                TyhpParser.PhpExprVariableContext phpExprVariableContext => this.VisitPhpExprVariable(phpExprVariableContext),
                TyhpParser.PhpExprScalarContext phpExprScalarContext => this.VisitPhpExprScalar(phpExprScalarContext),
                TyhpParser.PhpExprFunctionContext phpExprFunctionContext => this.VisitPhpExprFunction(phpExprFunctionContext),
                TyhpParser.PhpExprInternalFunctionContext phpExprInternalFunctionContext => this.VisitPhpExprInternalFunction(phpExprInternalFunctionContext),
                TyhpParser.PhpExprExitContext phpExprExitContext => this.VisitPhpExprExit(phpExprExitContext),
                TyhpParser.PhpExprMatchCheckContext phpExprMatchCheckContext => this.VisitPhpExprMatchCheck(phpExprMatchCheckContext),
                TyhpParser.PhpExprListContext phpExprListContext => this.VisitPhpExprList(phpExprListContext),
                TyhpParser.PhpExprPrecBaseGrammarAddonHandlerContext phpExprPrecBaseGrammarAddonHandlerContext => this.VisitPhpExprPrecBaseGrammarAddonHandler(phpExprPrecBaseGrammarAddonHandlerContext),
                _ => this.VisitPhpExprBaseAlt(context),
            };

        public virtual IExpression VisitPhpExprBaseAlt(TyhpParser.PhpExprBaseContext context)
            => (this.Visit(context) as IExpression) ?? UnexpectedNodeAst.Create(context);

        public override PhpNewAst VisitExprNewNonDRef([NotNull] TyhpParser.ExprNewNonDRefContext context)
            => this.VisitNewNonDereferenceable(context.Statement);

        public override IExpression VisitPhpExprVariable([NotNull] TyhpParser.PhpExprVariableContext context)
            => this.VisitFullyDereferenceable(context.Variable);

        public override IExpression VisitPhpExprScalar([NotNull] TyhpParser.PhpExprScalarContext context)
            => this.VisitScalar(context.Scalar);

        public override PhpInlineFunctionAst VisitPhpExprFunction([NotNull] TyhpParser.PhpExprFunctionContext context)
            => this.VisitInlineFunction(context.Function);

        public override IExpression VisitPhpExprInternalFunction([NotNull] TyhpParser.PhpExprInternalFunctionContext context)
            => (IExpression)this.VisitInternalFunctions(context.Function);

        public override PhpUnaryOpAst VisitPhpExprExit([NotNull] TyhpParser.PhpExprExitContext context)
        {
            // Bare `exit;` / `die;` — no ArgumentList. Call-like forms use full `argumentList`
            // (same shape as ctor_arguments): empty `()`, args, named args, or `...` FCC.
            // Empty `()` visits to null from VisitArgumentList; materialize an empty list so
            // bare vs empty-call remain distinguishable in the AST.
            PhpArgumentListAst? arguments = null;
            if (context.ArgumentList != null)
            {
                arguments = this.VisitArgumentList(context.ArgumentList)
                    ?? PhpArgumentListAst.Create([], context.ArgumentList);
            }

            return PhpUnaryOpAst.Create(
                this.GetTokenValueAst(context, context.T_EXIT().Symbol),
                arguments,
                true,
                context
            );
        }

        public override PhpConditionalAst VisitPhpExprMatchCheck([NotNull] TyhpParser.PhpExprMatchCheckContext context)
            => this.VisitMatchCheck(context.Expr);

        public override PhpArrayPairListAst VisitPhpExprList([NotNull] TyhpParser.PhpExprListContext context)
            => this.VisitArrayPairList(context.ArrayPairList);

        public override IExpression VisitPhpExprPrecBaseGrammarAddonHandler([NotNull] TyhpParser.PhpExprPrecBaseGrammarAddonHandlerContext context)
            => this.VisitPhpExprPrecBaseGrammarAddon(context.phpExprPrecBaseGrammarAddon());

        public override IExpression VisitPhpExprPrecBaseGrammarAddon([NotNull] TyhpParser.PhpExprPrecBaseGrammarAddonContext context)
            => UnexpectedNodeAst.Create(context); // No grammar addon defined for this expression

        public override IExpression? VisitOptionalExpr([NotNull] TyhpParser.OptionalExprContext context)
            => context.Expr != null ? this.VisitExpr(context.Expr) : null;
    }
}