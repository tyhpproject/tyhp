namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Antlr4.Runtime.Tree;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {
        protected Antlr4.Runtime.CommonTokenStream? _tokens;
        protected int _docCommentLastStop = 0;
        protected string _filename;
        protected string _fileHash;
        public virtual IStatementList<ITopStatement>? CurrentTopStatementList {get; set;} = null;

        /// <summary>
        /// Diagnostic bag for collecting errors and warnings during visitor execution.
        /// Shared across all partial classes.
        /// </summary>
        public DiagnosticBag Diagnostics { get; }

        public PhpParserAstVisitor(Antlr4.Runtime.CommonTokenStream? tokens, string filename, string fileHash, DiagnosticBag diagnostics)
        {
            this._tokens = tokens;
            this._filename = filename;
            this._fileHash = fileHash;
            this.Diagnostics = diagnostics;
        }

        public string? FindPossibleDocComment(Antlr4.Runtime.ParserRuleContext beforeContext)
            => this.FindPossibleDocComment(beforeContext.Start);

        /// <summary>
        /// Returns the docblock immediately preceding <paramref name="beforeToken"/>, or null when the
        /// declaration has none. Absence is null rather than an empty string so that a node without a
        /// docblock serializes identically to one that was never able to have one.
        /// </summary>
        public string? FindPossibleDocComment(Antlr4.Runtime.IToken? beforeToken)
        {
            if (beforeToken == null) {
                return null;
            }
            // Get the token position
            int currentIndex = beforeToken.TokenIndex;

            string? docCommentText = null;

            // Walk backward through the token stream looking for a DocBlockCommentsChannel token.
            // A block comment is a single token, so the nearest one is the whole docblock and is the
            // one that belongs to this declaration; stopping there keeps an earlier unrelated
            // docblock (a file header, say) from being appended to it.
            for (int i = currentIndex - 1; i >= this._docCommentLastStop; i--) {
                var previousToken = this._tokens?.Get(i);
                if (previousToken == null) {
                    break;
                }

                if (previousToken.Channel == TyhpLexer.DocBlockCommentsChannel) {
                    docCommentText = previousToken.Text;
                    break;
                }
            }

            // Claim the scanned range so a later declaration cannot re-use this docblock. Callers
            // must therefore look up a declaration's own docblock *before* visiting its children,
            // or a nested declaration will advance the cursor past it first.
            if (currentIndex > this._docCommentLastStop) {
                this._docCommentLastStop = currentIndex;
            }

            return docCommentText;
        }

        public void ResetDocComment(Antlr4.Runtime.ParserRuleContext context)
            => this.ResetDocComment(context.Start);

        public void ResetDocComment(Antlr4.Runtime.IToken? token)
        {
            this._docCommentLastStop = token?.TokenIndex ?? this._docCommentLastStop;
        }

        public static string GetCurrentLanguageMode(Antlr4.Runtime.RuleContext? context)
        {
            do {
                if (context is TyhpParser.TyhpdefBlockContext) {
                    return "tyhpdef";
                } else if (context is TyhpParser.TyhpdefTaglessFileContext) {
                    return "tyhpdef";
                } else if (context is TyhpParser.TyhpBlockContext) {
                    return "tyhp";
                } else if (context is TyhpParser.TyhpTaglessFileContext) {
                    return "tyhp";
                } else if (context is TyhpParser.PhpBlockContext) {
                    return "php";
                } else if (
                    context is TyhpParser.PhpSrcFileContext ||
                    context is TyhpParser.TyhpSrcFileContext ||
                    context is TyhpParser.TyhpdefSrcFileContext ||
                    context == null
                ) {
                    return "";
                }
                context = context?.Parent;
            } while (context != null);

            return "";
        }

        /// <summary>
        /// Builds a <see cref="TokenValueAst"/> from an ANTLR token. Returns null when
        /// <paramref name="contextToken"/> is null (common after error recovery, e.g. a reserved
        /// keyword where an identifier was expected), unless <paramref name="visitGrammarAddon"/>
        /// supplies an alternate token AST.
        /// </summary>
        protected Ast.TokenValueAst? GetTokenValueAst(ParserRuleContext context, IToken? contextToken, Func<Ast.TokenValueAst?>? visitGrammarAddon = null)
        {
            if (contextToken != null) {
                return TokenValueAst.Create(contextToken, context);
            } else if (visitGrammarAddon != null) {
                return visitGrammarAddon();
            }

            return null;
        }

        /// <summary>
        /// True when <paramref name="context"/> is an ANTLR error-recovery stub: the rule threw
        /// <see cref="RecognitionException"/> and/or the tree contains an <see cref="IErrorNode"/>.
        /// Visitors must not emit <see cref="Domain.Exceptions.MessageCode.VisitorUnexpectedAlternative"/>
        /// for these — the parser already reported the real syntax diagnostic (e.g. TYHP1002).
        /// </summary>
        protected static bool IsErrorRecoveryContext(ParserRuleContext? context)
        {
            if (context == null)
            {
                return false;
            }

            if (context.exception != null)
            {
                return true;
            }

            for (var i = 0; i < context.ChildCount; i++)
            {
                if (context.GetChild(i) is IErrorNode)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reports <see cref="Domain.Exceptions.MessageCode.VisitorUnexpectedAlternative"/> unless
        /// <paramref name="context"/> is an error-recovery stub (see <see cref="IsErrorRecoveryContext"/>).
        /// </summary>
        protected void ReportUnexpectedAlternative(
            ParserRuleContext context,
            string ruleName,
            string? alternativeName = null)
        {
            if (IsErrorRecoveryContext(context))
            {
                return;
            }

            this.Diagnostics.AddError(
                Domain.Exceptions.MessageCode.VisitorUnexpectedAlternative,
                this._filename,
                context.Start?.Line ?? 0,
                context.Start?.Column ?? 0,
                ruleName,
                alternativeName ?? context.GetType().Name);
        }

        protected IStatement HandleWithStatementTerminal(IStatement statement, TyhpParser.StatementTerminalContext? statementTerminalContext, ParserRuleContext context)
        {
            var statementTerminal = statementTerminalContext != null ? this.VisitStatementTerminal(statementTerminalContext) : null;
            if (statementTerminal == null) {
                return statement;
            }

            return PhpStatementBlockAst.Create([statement, statementTerminal], context, GetCurrentLanguageMode(context));
        }

        protected ITopStatement HandleWithStatementTerminal(ITopStatement statement, TyhpParser.StatementTerminalContext? statementTerminalContext, ParserRuleContext context)
        {
            var statementTerminal = statementTerminalContext != null ? this.VisitStatementTerminal(statementTerminalContext) : null;
            if (statementTerminal == null) {
                return statement;
            }

            return PhpTopStatementListAst.Create([statement, statementTerminal], context, GetCurrentLanguageMode(context));
        }

        #region Misc Overrides

        public override bool Equals(object? obj) => base.Equals(obj);
        public override int GetHashCode() => base.GetHashCode();
        public override string? ToString() => base.ToString();
        protected override Ast.Interfaces.IBase2Ast? DefaultResult => null;
        public override Ast.Interfaces.IBase2Ast? Visit(IParseTree tree) => base.Visit(tree);
        public override Ast.Interfaces.IBase2Ast? VisitChildren(IRuleNode node) => null;
        public override Ast.Interfaces.IBase2Ast? VisitTerminal(ITerminalNode node) => null;
        public override Ast.Interfaces.IBase2Ast? VisitErrorNode(IErrorNode node) => null;
        protected override Ast.Interfaces.IBase2Ast? AggregateResult(Ast.Interfaces.IBase2Ast? aggregate, Ast.Interfaces.IBase2Ast? nextResult) => null;
        protected override bool ShouldVisitNextChild(IRuleNode node, Ast.Interfaces.IBase2Ast? currentResult) => false;

        #endregion Misc Overrides
    }
}