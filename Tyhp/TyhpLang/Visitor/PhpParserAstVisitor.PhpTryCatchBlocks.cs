namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public override PhpCatchListAst VisitCatchList([NotNull] TyhpParser.CatchListContext context)
            => PhpCatchListAst.Create(
                context._Items.Select(this.VisitCatchBlock),
                context
            );

        public override PhpCatchClauseAst VisitCatchBlock([NotNull] TyhpParser.CatchBlockContext context)
            => PhpCatchClauseAst.Create(
                this.VisitCatchNameList(context.CatchNameList),
                this.VisitOptionalVariable(context.Variable),
                this.VisitInnerStatementList(context.StatementList),
                context
            );

        public override PhpCatchClauseAst VisitCatchBlockGrammarAddon([NotNull] TyhpParser.CatchBlockGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "catchBlockGrammarAddon", context.GetType().Name
            );
            var languageMode = GetCurrentLanguageMode(context);
            return PhpCatchClauseAst.Create(
                PhpClassNameListAst.Create(System.Array.Empty<IClassName>(), context, languageMode),
                null,
                PhpStatementBlockAst.CreateError(context, languageMode),
                context,
                languageMode
            );
        }

        public override PhpClassNameListAst VisitCatchNameList([NotNull] TyhpParser.CatchNameListContext context)
            => PhpClassNameListAst.Create(
                context._Items.Select(this.VisitClassName),
                context
            );

        public override PhpVariableAst? VisitOptionalVariable([NotNull] TyhpParser.OptionalVariableContext context)
            => context.TokenValue != null ? PhpVariableAst.Create(this.GetTokenValueAst(context, context.TokenValue), false, context) : null;

        public override PhpStatementBlockAst? VisitFinallyStatement([NotNull] TyhpParser.FinallyStatementContext context)
            => context.StatementList != null ? this.VisitInnerStatementList(context.StatementList) : null;
    }
}