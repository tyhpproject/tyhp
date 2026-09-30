namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Antlr4.Runtime.Tree;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Enum;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpdefIncludedPhpVisits : TyhpdefParserBaseVisitor<Ast.Interfaces.IBase2Ast?>
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

        public TyhpdefIncludedPhpVisits(Antlr4.Runtime.CommonTokenStream? tokens, string filename, string fileHash, DiagnosticBag diagnostics)
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

                if (previousToken.Channel == TyhpdefLexer.DocBlockCommentsChannel) {
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

        /// <summary>
        /// Returns the compact <c>// @overlay-against:</c> stamp immediately preceding
        /// <paramref name="beforeToken"/>, or null when none is present.
        /// </summary>
        public string? FindPossibleOverlayAgainst(Antlr4.Runtime.IToken? beforeToken)
        {
            if (beforeToken == null || this._tokens == null)
            {
                return null;
            }

            string? stamp = null;
            for (int i = beforeToken.TokenIndex - 1; i >= 0; i--)
            {
                var previousToken = this._tokens.Get(i);
                if (previousToken == null)
                {
                    break;
                }

                if (previousToken.Channel == TyhpdefLexer.WhiteSpaceChannel
                    || previousToken.Channel == TokenConstants.HiddenChannel)
                {
                    continue;
                }

                if (previousToken.Channel == TyhpdefLexer.SimpleCommentsChannel)
                {
                    var extracted = ExtractOverlayAgainstStamp(previousToken.Text);
                    if (extracted != null)
                    {
                        stamp = extracted;
                    }

                    continue;
                }

                if (previousToken.Channel == TyhpdefLexer.DocBlockCommentsChannel)
                {
                    continue;
                }

                break;
            }

            return stamp;
        }

        /// <summary>
        /// Returns the <c>// @provided-by:</c> package name immediately preceding
        /// <paramref name="beforeToken"/>, or null when none is present. Unknown
        /// <c>@</c> tags are skipped; the nearest matching comment wins.
        /// </summary>
        public string? FindPossibleProvidedBy(Antlr4.Runtime.IToken? beforeToken)
        {
            if (beforeToken == null || this._tokens == null)
            {
                return null;
            }

            for (int i = beforeToken.TokenIndex - 1; i >= 0; i--)
            {
                var previousToken = this._tokens.Get(i);
                if (previousToken == null)
                {
                    break;
                }

                if (previousToken.Channel == TyhpdefLexer.WhiteSpaceChannel
                    || previousToken.Channel == TokenConstants.HiddenChannel)
                {
                    continue;
                }

                if (previousToken.Channel == TyhpdefLexer.SimpleCommentsChannel)
                {
                    var extracted = ExtractProvidedByPackage(previousToken.Text);
                    if (extracted != null)
                    {
                        return extracted;
                    }

                    continue;
                }

                if (previousToken.Channel == TyhpdefLexer.DocBlockCommentsChannel)
                {
                    continue;
                }

                break;
            }

            return null;
        }

        internal static string? ExtractOverlayAgainstStamp(string? commentText)
        {
            if (string.IsNullOrWhiteSpace(commentText))
            {
                return null;
            }

            var text = commentText.Trim();
            if (text.StartsWith("//", StringComparison.Ordinal))
            {
                text = text[2..].TrimStart();
            }
            else if (text.StartsWith("/*", StringComparison.Ordinal) && text.EndsWith("*/", StringComparison.Ordinal))
            {
                text = text[2..^2].Trim();
            }

            const string marker = "@overlay-against:";
            if (!text.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return text[marker.Length..].Trim();
        }

        internal static string? ExtractProvidedByPackage(string? commentText)
        {
            if (string.IsNullOrWhiteSpace(commentText))
            {
                return null;
            }

            var text = commentText.Trim();
            if (text.StartsWith("//", StringComparison.Ordinal))
            {
                text = text[2..].TrimStart();
            }
            else if (text.StartsWith("/*", StringComparison.Ordinal) && text.EndsWith("*/", StringComparison.Ordinal))
            {
                text = text[2..^2].Trim();
            }

            const string marker = "@provided-by:";
            if (!text.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var package = text[marker.Length..].Trim();
            return string.IsNullOrEmpty(package) ? null : package;
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
                if (context is TyhpdefParser.TyhpdefBlockContext
                    || context is TyhpdefParser.TyhpdefTaglessFileContext) {
                    return "tyhpdef";
                } else if (context is TyhpdefParser.TyhpdefSrcFileContext || context == null) {
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

        protected IStatement HandleWithStatementTerminal(IStatement statement, TyhpdefParser.StatementTerminalContext? statementTerminalContext, ParserRuleContext context)
        {
            var statementTerminal = statementTerminalContext != null ? this.VisitStatementTerminal(statementTerminalContext) : null;
            if (statementTerminal == null) {
                return statement;
            }

            return PhpStatementBlockAst.Create([statement, statementTerminal], context, GetCurrentLanguageMode(context));
        }

        protected ITopStatement HandleWithStatementTerminal(ITopStatement statement, TyhpdefParser.StatementTerminalContext? statementTerminalContext, ParserRuleContext context)
        {
            var statementTerminal = statementTerminalContext != null ? this.VisitStatementTerminal(statementTerminalContext) : null;
            if (statementTerminal == null) {
                return statement;
            }

            return PhpTopStatementListAst.Create([statement, statementTerminal], context, GetCurrentLanguageMode(context));
        }

        /// <summary>
        /// Maps a <see cref="TyhpdefParser"/> token type to a modifier.
        /// Token integers differ from <see cref="TyhpParser"/>, so the shared
        /// <see cref="PhpModifierExtensions.FromToken(int)"/> table is the wrong vocabulary here.
        /// </summary>
        protected static PhpModifier FromToken(int token)
            => token switch
            {
                TyhpdefParser.T_PUBLIC => PhpModifier.Public,
                TyhpdefParser.T_PROTECTED => PhpModifier.Protected,
                TyhpdefParser.T_PRIVATE => PhpModifier.Private,
                TyhpdefParser.T_STATIC => PhpModifier.Static,
                TyhpdefParser.T_ABSTRACT => PhpModifier.Abstract,
                TyhpdefParser.T_FINAL => PhpModifier.Final,
                TyhpdefParser.T_READONLY => PhpModifier.Readonly,
                TyhpdefParser.T_VAR => PhpModifier.Var,
                TyhpdefParser.T_PUBLIC_SET => PhpModifier.PublicSet,
                TyhpdefParser.T_PROTECTED_SET => PhpModifier.ProtectedSet,
                TyhpdefParser.T_PRIVATE_SET => PhpModifier.PrivateSet,
                TyhpdefParser.T_TYHP_INTERNAL => PhpModifier.Internal,
                _ => PhpModifier.None,
            };

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