namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Represents progress information during the compilation process.
    /// Used for reporting compilation status to progress callbacks.
    /// </summary>
    public class CompilationProgress
    {
        /// <summary>
        /// Gets the number of files that have been processed so far.
        /// </summary>
        public int FilesProcessed { get; init; }

        /// <summary>
        /// Gets the total number of files to be processed.
        /// </summary>
        public int TotalFiles { get; init; }

        /// <summary>
        /// Gets the total count of errors encountered so far.
        /// </summary>
        public int ErrorCount { get; init; }

        /// <summary>
        /// Gets the total count of warnings encountered so far.
        /// </summary>
        public int WarningCount { get; init; }

        /// <summary>
        /// Gets the current file being processed.
        /// </summary>
        public string CurrentFile { get; init; } = string.Empty;

        /// <summary>
        /// Gets the current memory usage in bytes.
        /// </summary>
        public long MemoryUsage { get; init; }

        /// <summary>
        /// Number of files loaded from the AST cache during parsing.
        /// </summary>
        public int AstCacheHits { get; init; }

        /// <summary>
        /// Number of files re-parsed because the AST cache missed.
        /// </summary>
        public int AstCacheMisses { get; init; }
    }
}
