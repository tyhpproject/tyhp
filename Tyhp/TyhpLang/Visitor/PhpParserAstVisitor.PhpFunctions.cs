namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Tyhp.Domain.Exceptions;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        public override PhpNameAst VisitFunctionName([NotNull] TyhpParser.FunctionNameContext context)
            => PhpNameAst.Create(context.TokenValue, context);

        public override PhpFunctionDeclAst VisitFunctionDeclarationStatement([NotNull] TyhpParser.FunctionDeclarationStatementContext context)
        {
            if (context.functionDeclarationStatementGrammarAddon() != null)
            {
                return this.VisitFunctionDeclarationStatementGrammarAddon(context.functionDeclarationStatementGrammarAddon());
            }

            if (IsErrorRecoveryContext(context))
            {
                // ANTLR recovery for this single-alt sequence rule can produce a wholly empty stub
                // (no Identifier/ParameterList/ReturnType/StatementList) when prediction fails
                // partway through a malformed declaration — e.g. a truncated return-type union
                // (`function demo(): Foo| {}`) throws mid-rule and unwinds before any field is
                // assigned. The parser already reported the real syntax diagnostic; walking the
                // null mandatory fields below would NRE (TYHP1003) instead of a clean recovery.
                return this.CreateErrorFunctionDecl(context);
            }

            return PhpFunctionDeclAst.Create(
                this.VisitFunctionName(context.Identifier).ValueString,
                this.VisitReturnsRef(context.ReturnsRef) != null,
                this.VisitParameterList(context.ParameterList),
                this.VisitReturnType(context.ReturnType),
                this.VisitInnerStatementList(context.StatementList),
                context,
                GetCurrentLanguageMode(context)
            ).WithGrammarAddon("modifiers", this.VisitFunctionModifiersGrammarAddon(context.functionModifiersGrammarAddon()))
            .WithGrammarAddon("identifier", this.VisitFunctionNameGrammarAddon(context.functionNameGrammarAddon()))
            .WithGrammarAddon("parameters", this.VisitFunctionParametersGrammarAddon(context.functionParametersGrammarAddon()));
        }

        public override TokenValueListAst? VisitFunctionModifiersGrammarAddon([NotNull] TyhpParser.FunctionModifiersGrammarAddonContext context)
            => null;

        public override IBase2Ast? VisitFunctionNameGrammarAddon([NotNull] TyhpParser.FunctionNameGrammarAddonContext context)
            => null;

        public override IBase2Ast? VisitFunctionParametersGrammarAddon([NotNull] TyhpParser.FunctionParametersGrammarAddonContext context)
            => null;

        public virtual PhpFunctionDeclAst VisitFunctionDeclarationStatementGrammarAddon([NotNull] TyhpParser.FunctionDeclarationStatementGrammarAddonContext context)
        {
            this.Diagnostics.AddError(
                MessageCode.VisitorUnsupportedConstruct,
                this._filename, context.Start?.Line ?? 0, context.Start?.Column ?? 0,
                "functionDeclarationStatementGrammarAddon", context.GetType().Name
            );
            return this.CreateErrorFunctionDecl(context);
        }

        private PhpFunctionDeclAst CreateErrorFunctionDecl(ParserRuleContext context)
        {
            var languageMode = GetCurrentLanguageMode(context);
            return PhpFunctionDeclAst.Create(
                string.Empty,
                false,
                PhpParameterListAst.Create([], context, languageMode),
                null,
                null,
                context,
                languageMode
            );
        }

        public override TokenValueAst? VisitIsReference([NotNull] TyhpParser.IsReferenceContext context)
            => context.TokenValue != null ? this.GetTokenValueAst(context, context.TokenValue) : null;

        public override TokenValueAst? VisitIsVariadic([NotNull] TyhpParser.IsVariadicContext context)
            => context.TokenValue != null ? this.GetTokenValueAst(context, context.TokenValue) : null;

        public override PhpInlineFunctionAst VisitInlineFunction([NotNull] TyhpParser.InlineFunctionContext context)
            => PhpInlineFunctionAst.Create(
                TokenValueListAst.Create([
                    .. this.VisitFunctionModifiersGrammarAddon(context.functionModifiersGrammarAddon())?.GetAll() ?? [],
                    .. new TokenValueAst?[] { context.IsStatic != null ? this.GetTokenValueAst(context, context.IsStatic) : null }.Where(x => x != null).Cast<TokenValueAst>(),
                ], context),
                this.VisitReturnsRef(context.ReturnsRef) != null,
                this.VisitFunctionNameGrammarAddon(context.functionNameGrammarAddon()),
                this.VisitParameterList(context.ParameterList),
                this.VisitReturnType(context.ReturnType),
                this.VisitLexicalVars(context.LexicalVars),
                this.VisitInnerStatementList(context.StatementList),
                context
            ).WithAttributes(context.Attributes != null ? this.VisitAttributes(context.Attributes) : null)
            .WithGrammarAddon("modifiers", this.VisitFunctionModifiersGrammarAddon(context.functionModifiersGrammarAddon()))
            .WithGrammarAddon("identifier", this.VisitFunctionNameGrammarAddon(context.functionNameGrammarAddon()));

        public override TokenValueAst VisitFn([NotNull] TyhpParser.FnContext context)
            => this.GetTokenValueAst(context, context.T_FN().Symbol);

        public override TokenValueAst VisitFunction([NotNull] TyhpParser.FunctionContext context)
            => this.GetTokenValueAst(context, context.T_FUNCTION().Symbol);

        public override TokenValueAst? VisitReturnsRef([NotNull] TyhpParser.ReturnsRefContext context)
            => context.ReturnsRef != null ? this.VisitAmpersand(context.ReturnsRef) : null;

        public override PhpVariableListAst? VisitLexicalVars([NotNull] TyhpParser.LexicalVarsContext context)
            => context.LexicalVarsList != null ? this.VisitLexicalVarList(context.LexicalVarsList) : null;

        public override PhpVariableListAst VisitLexicalVarList([NotNull] TyhpParser.LexicalVarListContext context)
            => PhpVariableListAst.Create(
                context._Items?.Select(this.VisitLexicalVar),
                context
            );

        public override PhpVariableAst VisitLexicalVar([NotNull] TyhpParser.LexicalVarContext context)
            => PhpVariableAst.Create(
                this.GetTokenValueAst(context, context.Variable),
                context.IsRef != null,
                context
            );
    }
}