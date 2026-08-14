namespace Tyhp.CLI.ProjectTemplates
{
    /// <summary>
    /// Contract for <c>tyhp init</c> project templates.
    /// </summary>
    public interface IProjectTemplate
    {
        /// <summary>Template identifier (e.g. <c>basic</c>, <c>laravel</c>).</summary>
        string Name { get; }

        /// <summary>Human-readable description.</summary>
        string Description { get; }

        /// <summary>
        /// Default <c>tyhp.json</c> values as flat key-value pairs
        /// (e.g. <c>output.path</c>, <c>source.tagless</c>).
        /// </summary>
        Dictionary<string, string> GetDefaultConfig();

        /// <summary>
        /// Scaffold file paths relative to the project root, mapped to file content.
        /// Paths use the template's default source directory (<c>src/</c>);
        /// <see cref="InitAction"/> remaps them when <c>--src</c> is overridden.
        /// </summary>
        Dictionary<string, string> GetScaffoldFiles();

        /// <summary>Directory paths (relative to the project root) to create.</summary>
        List<string> GetDirectories();
    }
}
