namespace Tyhp.TyhpLang.Parser
{
    /// <summary>
    /// Chooses the parse entry point for a <c>.tyhp</c> file when the project is tagless.
    /// A file whose first six characters are <c>&lt;?tyhp</c> is a tagged file: it is lexed
    /// and parsed with <c>tyhpSrcFile</c>, so it can close a section with <c>?&gt;</c>, hold
    /// inline HTML and <c>&lt;?=</c>, and reopen with <c>&lt;?tyhp</c>. Any other file is
    /// code and uses <c>tyhpTaglessSrcFile</c>. The check runs before the lexer starts.
    /// </summary>
    public static class TaglessEntryPoint
    {
        private const string TyhpOpenTag = "<?tyhp";

        /// <summary>
        /// True when a <c>.tyhp</c> file should use the tagless lexer start mode and the
        /// <c>tyhpTaglessSrcFile</c> entry point.
        /// </summary>
        /// <param name="projectTagless">The project's <c>source.tagless</c> setting.</param>
        /// <param name="content">The file content, after any byte order mark was removed.</param>
        public static bool UsesTaglessEntryPoint(bool projectTagless, string content)
            => projectTagless && !content.StartsWith(TyhpOpenTag, StringComparison.Ordinal);
    }
}
