namespace Tyhp.LanguageServer.Configuration
{
    using Tyhp.Config;
    using Tyhp.Domain.Services;

    /// <summary>
    /// LSP-specific configuration options for <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed class ServerConfiguration
    {
        /// <summary>
        /// Debounce delay in milliseconds for document-change analysis. Default 300.
        /// </summary>
        public int DebounceDelay { get; init; } = 300;

        /// <summary>
        /// Maximum concurrent file analysis operations. Default 4.
        /// </summary>
        public int MaxConcurrentAnalysis { get; init; } = 4;

        /// <summary>
        /// Whether to publish diagnostics on document change. Default true.
        /// </summary>
        public bool EnableDiagnostics { get; init; } = true;

        /// <summary>
        /// Path to <c>tyhp.json</c>, resolved from the workspace root or client
        /// initialization options.
        /// </summary>
        public string? TyhpProjectPath { get; set; }

        /// <summary>
        /// CLI project used to seed compilation options and source-file globs.
        /// Reloaded from disk when the workspace <c>tyhp.json</c> changes.
        /// </summary>
        public Project? Project { get; set; }

        /// <summary>
        /// Compilation options (PHP version, tyhpdef paths, tagless mode) reused by analysis.
        /// </summary>
        public CompilationOptions CompilationOptions { get; set; } = new() { EnableAstCache = false };

        /// <summary>
        /// Builds a default configuration from the current CLI project.
        /// </summary>
        public static ServerConfiguration FromProject(Project project)
        {
            ArgumentNullException.ThrowIfNull(project);
            return new ServerConfiguration
            {
                TyhpProjectPath = project.GetConfigValue("*project_file_path"),
                Project = project,
                CompilationOptions = CompilationOptions.FromProject(project),
            };
        }
    }
}
