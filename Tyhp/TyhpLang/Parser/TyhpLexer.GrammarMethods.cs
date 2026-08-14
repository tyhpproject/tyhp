namespace Tyhp.TyhpLang.Parser {
    using System.Text.RegularExpressions;
    using Antlr4.Runtime;
    using System.Text;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.Domain.Exceptions;

    public partial class TyhpLexer : Lexer {
        protected string _languageMode = "";
        protected bool _taglessMode = false;
        protected string _taglessLanguageMode = "";
        protected DiagnosticBag? _taglessDiagnostics;
        protected string _taglessFileName = "";
        protected Queue<IToken> _pendingTokensQueue = new Queue<IToken>();
        protected Queue<IToken> _encapsTokensQueue = new Queue<IToken>();

        protected string? _heredocLabel = null;
        protected Stack<(
            Tyhp.TyhpLang.Enum.BraceType type,
            int line,
            int column,
            string? popModeBackTo,
            bool additionalPopMode
        )> _nestingStack = new Stack<(Tyhp.TyhpLang.Enum.BraceType type, int line, int column, string? popModeBackTo, bool additionalPopMode)>();

        private int _prepareLessMark = -1;
        private int _prepareLessIndex = -1;
        private int _prepareLessLine = -1;
        private int _prepareLessColumn = -1;

        private List<int> shouldPopList = new List<int>();

        [ThreadStatic]
        private static HashSet<int>? _combineSubsequentTokens;

        protected static HashSet<int> combineSubsequentTokens {
            get {
                _combineSubsequentTokens ??= new HashSet<int> {
                    TyhpLexer.T_ENCAPSED_AND_WHITESPACE,
                    TyhpLexer.T_INLINE_HTML
                };
                return _combineSubsequentTokens;
            }
        }

        // Add a shared buffer for ToLiteral method
        // private static readonly literalBuffer = Buffer.alloc(1024 * 8); // 8KB buffer for string processing

        protected IToken? SingleNextToken(bool skipEncapsQueue = false) {
            IToken? nextToken = null;

            if (!skipEncapsQueue && !this._encapsTokensQueue.TryDequeue(out nextToken)) {
                nextToken = null;
            }

            if (nextToken == null && !this._pendingTokensQueue.TryDequeue(out nextToken)) {
                nextToken = null;
            }

            if (nextToken == null) {
                // try for the next 2 tokens and add them to the queue
                IToken baseToken = base.NextToken();
                this._pendingTokensQueue.Enqueue(baseToken);
                // baseToken = base.NextToken();
                // this._pendingTokensQueue.Enqueue(baseToken);

                // try again
                // getting a token from the main stream may add items to the pending queue, so we pull from there
                this._pendingTokensQueue.TryDequeue(out nextToken);
            }

            return nextToken;
        }

        protected int? PeekNextTokenType(bool skipEncapsQueue = false) {
            IToken? nextToken = null;

            if (!skipEncapsQueue && !this._encapsTokensQueue.TryPeek(out nextToken)) {
                nextToken = null;
            }

            if (nextToken == null && !this._pendingTokensQueue.TryPeek(out nextToken)) {
                nextToken = null;
            }

            if (nextToken == null) {
                // the queue is empty, so we need to get the next token from the base lexer
                // and add it to the queue
                nextToken = base.NextToken();
                this._pendingTokensQueue.Enqueue(nextToken);

                // try again
                // getting a token from the main stream may add items to the pending queue, so we peek from there
                this._pendingTokensQueue.TryPeek(out nextToken);
            }

            return nextToken?.Type;
        }

        public void ConfigureTagless(
            bool enabled,
            string languageMode,
            DiagnosticBag? diagnostics = null,
            string? fileName = null
        )
        {
            this._taglessMode = enabled;
            this._taglessLanguageMode = languageMode ?? "";
            this._taglessDiagnostics = diagnostics;
            this._taglessFileName = fileName ?? "";

            if (enabled)
            {
                this.ApplyTaglessStartMode();
            }
            else
            {
                this._taglessLanguageMode = "";
                this._taglessDiagnostics = null;
                this._taglessFileName = "";
            }
        }

        /// <summary>
        /// Chooses the lexer's starting mode for a tagless source file. If the file begins
        /// with a literal open tag, we start in ST_TYHP_TAGLESS so the tag is consumed
        /// before transitioning to ST_IN_SCRIPTING. Otherwise we start directly in
        /// ST_IN_SCRIPTING so the entire file (including any leading whitespace) is lexed
        /// natively, which keeps line/column tracking accurate.
        /// </summary>
        private void ApplyTaglessStartMode()
        {
            if (!this._taglessMode)
            {
                return;
            }

            if (this.HasLiteralOpenTagAtStart())
            {
                this.Mode(TyhpLexer.ST_TYHP_TAGLESS);
            }
            else
            {
                this._languageMode = this._taglessLanguageMode;
                this.Mode(TyhpLexer.ST_IN_SCRIPTING);
            }
        }

        public override void Reset()
        {
            base.Reset();
            this._languageMode = "";
            this.ApplyTaglessStartMode();
        }

        public override IToken NextToken() {
            IToken? token = this.SingleNextToken();

            // fix line for ending heredoc
            if (token is CommonToken commonToken && commonToken.Type == TyhpLexer.T_END_HEREDOC) {
                commonToken.Line = commonToken.Line + 1;
                commonToken.Column = 0;
                commonToken.StartIndex = commonToken.StartIndex + 1;
            }

            // detect if we have a constant string in double quotes
            if (token?.Type == TyhpLexer.T_DOUBLE_QUOTE) {
                int endType = token.Type;
                int totalLength = token.Text.Length;

                // Collect all encapsed tokens first
                while (this.PeekNextTokenType(true) == TyhpLexer.T_ENCAPSED_AND_WHITESPACE) {
                    IToken? nextToken = this.SingleNextToken(true);
                    if (nextToken == null) break;
                    totalLength += nextToken.Text.Length;
                    this._encapsTokensQueue.Enqueue(nextToken);
                }

                if (this.PeekNextTokenType(true) == endType) {
                    // we have a constant string, so lets return that token
                    IToken? endToken = this.SingleNextToken(true);
                    if (endToken != null) {
                        totalLength += endToken.Text.Length;
                        this._encapsTokensQueue.Enqueue(endToken);
                    }

                    // Create a new token with pre-allocated StringBuilder
                    var textBuilder = new StringBuilder(token.Text, totalLength);
                    int stopIndex = token.StopIndex;
                    while (this._encapsTokensQueue.TryDequeue(out IToken? t)) {
                        textBuilder.Append(t.Text);
                        stopIndex = t.StopIndex;
                    }

                    token = new CommonToken(TyhpLexer.T_CONSTANT_ENCAPSED_STRING, textBuilder.ToString()) {
                        Line = token.Line,
                        Column = token.Column,
                        StartIndex = token.StartIndex,
                        StopIndex = stopIndex,
                        Channel = token.Channel
                    };
                }
            }

            // combine subsequent tokens
            if (token != null && combineSubsequentTokens.Contains(token.Type))
            {
                int tokenType = token.Type;
                // First, count how many consecutive tokens we have and calculate total length
                int totalLength = token.Text.Length;
                int consecutiveTokens = 0;
                var tokenBuffer = new List<IToken>();
                tokenBuffer.Add(token);

                while (true) {
                    var nextType = this.PeekNextTokenType();
                    if (nextType != tokenType) {
                        break;
                    }

                    token = this.SingleNextToken();
                    if (token == null) {
                        break;
                    }

                    tokenBuffer.Add(token);
                    totalLength += token.Text.Length;
                    consecutiveTokens++;
                }

                // If we found additional tokens, combine them
                if (consecutiveTokens > 0) {
                    // Create StringBuilder with exact capacity needed
                    var textBuilder = new StringBuilder(totalLength);
                    foreach (var t in tokenBuffer) {
                        textBuilder.Append(t.Text);
                    }

                    // Create single new token with combined text
                    token = new CommonToken(tokenType, textBuilder.ToString()) {
                        Line = tokenBuffer[0].Line,
                        Column = tokenBuffer[0].Column,
                        StartIndex = tokenBuffer[0].StartIndex,
                        StopIndex = tokenBuffer[^1].StopIndex,
                        Channel = tokenBuffer[0].Channel
                    };
                }
            }

            return token ?? throw new Exception("unexpected null token");
        }

        public bool checkShouldPop(bool clear = true, bool doPop = true) {
            bool result = this.shouldPopList.Contains(this.CurrentMode);
            if (result) {
                if (clear) {
                    this.shouldPopList = this.shouldPopList.Where(m => m != this.CurrentMode).ToList();
                }
                if (doPop) {
                    this.PopMode();
                }
            }
            return result;
        }

        public void addShouldPop(int mode) {
            if (mode >= 0) {
                this.shouldPopList.Add(mode);
            }
        }

        public void addShouldPop(string mode) {
            this.addShouldPop(Array.IndexOf(TyhpLexer.modeNames, mode));
        }

        public void startHereDoc(string heredocStart) {
            string label = heredocStart.Trim();
            label = label.Replace("<<<", "");
            label = label.Trim(' ', '\n', '\r', '\t', '"', '\'');
            this._heredocLabel = label; 
        }

        public bool checkEndHereDoc(string label) {
            return label == this._heredocLabel || (
                label.TrimStart() == this._heredocLabel
            );
        }

        public bool closeTagHandler() {
            if (this._taglessMode && this._taglessDiagnostics != null)
            {
                this._taglessDiagnostics.AddError(
                    MessageCode.LexerCloseTagNotAllowedInTaglessMode,
                    this._taglessFileName,
                    this.Line,
                    this.Column);
            }

            CommonToken token = new CommonToken(TyhpLexer.T_SYM_SEMICOLON, ";");
            token.Line = this.Line;
            token.Column = this.Column;
            token.StartIndex = this.InputStream.Index;
            token.StopIndex = this.InputStream.Index + 1;
            token.Channel = this.Channel;
            this._pendingTokensQueue.Enqueue(token);

            return true;
        }

        public bool endHereDoc() {
            // we want to keep whitespace at the beginning so that we can remove indentations from the heredoc
            if (this.Text.StartsWith("\n") || this.Text.StartsWith("\r")) {
                    CommonToken token = new CommonToken(TyhpLexer.T_ENCAPSED_AND_WHITESPACE, "\n");
                    token.Line = this.Line - 1;
                    token.Column = this.Column;
                    token.StartIndex = this.InputStream.Index;
                    token.StopIndex = this.InputStream.Index + 1;
                    token.Channel = this.Channel;
                    this._pendingTokensQueue.Enqueue(token);
                    this.Text = this.Text.TrimStart('\n', '\r');
                }
                this._heredocLabel = null;
            return true;
        }

        protected string streamPeek(int start = 0, int numChars = 1) {
            if (numChars == 0) {
                // early exit
                return "";
            }

            string result = "";
            int idx = start;

            while(numChars > 0) {
                if (idx == 0) {
                    idx = 1;
                }
                int laChar = this.InputStream.LA(idx);
                if (laChar < 0) {
                    break;
                }
                result += ((char) laChar).ToString();
                idx++;
                numChars--;
            }
            
            return result;
        }

        public bool streamLA(int length, string strPattern, bool ignoreStartingWhitespace = false, int startIdx = 0, bool unused = false) {
            string subject = this.streamPeek(startIdx, length);
            if (ignoreStartingWhitespace) {
                int offset = 0;

                // `subject` can be shorter than `offset + 1` once the peek runs past EOF
                // (streamPeek stops growing once the input stream is exhausted), so bounds-check
                // before indexing instead of assuming there is always another character to skip.
                while (offset < subject.Length && Regex.IsMatch(subject.Substring(offset, 1), @"[ \n\r\t]")) {
                    offset++;
                    subject = this.streamPeek(startIdx, length + offset);
                }
                subject = subject.TrimStart();

                // Console.WriteLine("subject: (" + subject + "), pattern: (" + strPattern + ")");
            }

            
            return Regex.IsMatch(subject, @"^" + strPattern + @"$");
        }

        public bool streamLAEq(string match, bool debug = false) {
            if (match.Length == 0) {
                return true;
            }
            return match == this.streamPeek(0, match.Length);
        }

        public bool streamLAAny(IEnumerable<(int? length, string? pattern, int? startIdx)> strPatterns, bool ignoreStartingWhitespace = false) {
            foreach (var s in strPatterns) {
                if (this.streamLA(s.length ?? 1, s.pattern ?? "", ignoreStartingWhitespace, s.startIdx ?? 0, true)) {
                        return true;
                    }
                }
            return false;
        }

        public bool isFollowedByVarOrVarArg() {
            // we do it this way so we do not include the whitespace or comments in this token
            // we are looking to see if the next token that is not whitespace or a comment is a `$` or `...`
            int currentIdx = 0;
            string nextChar = this.streamPeek(currentIdx, 1);
            while(nextChar != "") {
                if (nextChar == "$") {
                    return true;
                }
                
                if (nextChar == "." && this.streamPeek(currentIdx + 1, 2) == "..") {
                    return true;
                }

                // check if it is not whitespace
                if (!Regex.IsMatch(nextChar, @"[ \r\n\t]")) {
                    // check for single line and multi line comments
                    if (
                        (nextChar == "/" && this.streamPeek(currentIdx + 1, 1) == "/") ||
                        (nextChar == "#" && this.streamPeek(currentIdx + 1, 1) != "[")
                        ) {
                        // single line comment or hash comment
                        // continue past the new line
                        while (nextChar != "" && nextChar != "\n" && nextChar != "\r") {
                            currentIdx++;
                            nextChar = this.streamPeek(currentIdx, 1);
                        }
                    } else if (nextChar == "/" && this.streamPeek(currentIdx + 1, 1) == "*") {
                        // multi line comment
                        // continue past the */
                        while (nextChar != "" && nextChar != "*/") {
                            // we advance by one, but check two chars at a time
                            currentIdx++;
                            nextChar = this.streamPeek(currentIdx, 2);
                        }

                        if (nextChar != "") {
                            // get past the slash after the asterisk
                            currentIdx++;
                        }
                    } else {
                        // any other char
                        return false;
                    }
                }

                if (nextChar != "") {
                    currentIdx++;
                    nextChar = this.streamPeek(currentIdx, 1);
                }
            }

            return false;

        }

        public string unescapeString(string value) {
            if (value[0] == 'b' && value[1] == '\'') {
                value = value.Substring(2, value.Length - 3);
            } else if (value[0] == '\'') {
                value = value.Substring(1, value.Length - 2);
            }

            return this.ToLiteral(value);
        }

        private string ToLiteral(string valueTextForCompiler) {
            int len = valueTextForCompiler.Length;

            string result = "";
            for (int i = 0; i < len; i++) {
                char? charValue = valueTextForCompiler[i];
                if (charValue == '\\') {
                    char? nextChar = valueTextForCompiler[++i];
                    result += (nextChar == '\'' || nextChar == '\\') ? nextChar : "\\" + nextChar;
                } else {
                    result += charValue.ToString();
                }
            }
            return result;
        }

        public bool nestingStackIsOpenCurly() {
            return this._nestingStack.Peek().type == Tyhp.TyhpLang.Enum.BraceType.curly;
        }

        public void enterNesting(Tyhp.TyhpLang.Enum.BraceType type, string? popModeBackTo = null, bool additionalPopMode = false) {
            this._nestingStack.Push((type, this.Line, this.Column, popModeBackTo, additionalPopMode));
        }

        public bool exitNesting(Tyhp.TyhpLang.Enum.BraceType type) {
            if (this._nestingStack.Count == 0) {
                throw new Exception("unexpected close " + type + " brace at " + this.Line.ToString() + ":" + this.Column.ToString());
            }

            var last = this._nestingStack.Peek();
            if (last.type != type) {
                throw new Exception("unexpected close " + type + " brace at " + this.Line.ToString() + ":" + this.Column.ToString() + ", expecting " + last.type + " to match open at " + last.line.ToString() + ":" + last.column.ToString());
            }
            
            this._nestingStack.Pop();

            if (!String.IsNullOrWhiteSpace(last.popModeBackTo)) {
                int targetMode = Array.IndexOf(TyhpLexer.modeNames, last.popModeBackTo);
                if (targetMode >= 0 && this.ModeStack.Contains(targetMode)) {
                    while (this.CurrentMode != targetMode) {
                        this.PopMode();
                    }
                } else {
                    throw new Exception("unexpected close " + type + " brace at " + this.Line.ToString() + ":" + this.Column.ToString() + ", expecting pop back to lexer mode '" + last.popModeBackTo + "' but mode not found in mode stack.");
                }
            }

            if (last.additionalPopMode) {
                this.PopMode();
            }

            return true;
        }

        public bool prepareLess() {
            this._prepareLessMark = this.InputStream.Mark();
            this._prepareLessIndex = this.InputStream.Index;
            this._prepareLessLine = this.Line;
            this._prepareLessColumn = this.Column;
            return true;
        }

        public void doPreparedLess() {
            if (this._prepareLessIndex != -1) {
                this.InputStream.Seek(this._prepareLessIndex);
                this.Line = this._prepareLessLine;
                this.Column = this._prepareLessColumn;
            }
            this.InputStream.Release(this._prepareLessMark);
            this._prepareLessIndex = -1;
            this._prepareLessMark = -1;
        }


        public void less(int numChars = 1, string debugName = "") {
            this.InputStream.Seek(this.InputStream.Index - numChars);
        }

        /// <summary>
        /// Looks ahead (without consuming) to determine whether a tagless source file
        /// begins with a literal Tyhp/Tyhpdef open tag (<c>&lt;?tyhp</c> or
        /// <c>&lt;?tyhpdef</c>), allowing optional leading whitespace. Used by
        /// <see cref="ApplyTaglessStartMode"/> to decide whether to start in
        /// ST_TYHP_TAGLESS (consume the tag) or ST_IN_SCRIPTING (no tag).
        /// A <c>&lt;?php</c> tag is intentionally not treated as a tagless open tag: tagless
        /// source is pure Tyhp/Tyhpdef code, so it is left to be lexed in scripting mode.
        /// </summary>
        private bool HasLiteralOpenTagAtStart()
        {
            if (this.InputStream == null)
            {
                return false;
            }

            int idx = 0;
            this.SkipLeadingWhitespace(ref idx);

            if (!this.StreamMatchesAt(idx, "<?"))
            {
                return false;
            }

            idx += 2;

            if (this.StreamMatchesAt(idx, "tyhpdef"))
            {
                idx += 7;
                return this.HasTyhpOpenTagBoundaryAt(idx);
            }

            if (this.StreamMatchesAt(idx, "tyhp"))
            {
                idx += 4;
                return this.HasTyhpOpenTagBoundaryAt(idx);
            }

            return false;
        }

        private void SkipLeadingWhitespace(ref int idx)
        {
            while (true)
            {
                int la = this.InputStream.LA(idx + 1);
                if (la < 0)
                {
                    return;
                }

                char c = (char)la;
                if (c is not (' ' or '\t' or '\n' or '\r'))
                {
                    return;
                }

                idx++;
            }
        }

        private bool StreamMatchesAt(int startIdx, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                int la = this.InputStream.LA(startIdx + i + 1);
                if (la < 0 || (char)la != value[i])
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasTyhpOpenTagBoundaryAt(int startIdx)
        {
            int la = this.InputStream.LA(startIdx + 1);
            if (la < 0)
            {
                return true;
            }

            char c = (char)la;
            return c is ' ' or '\t' or '\n' or '\r';
        }
    }
}