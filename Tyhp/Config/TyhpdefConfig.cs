namespace Tyhp.Config
{
    /// <summary>
    /// Tyhpdef discovery configuration from <c>tyhp.json</c>.
    /// </summary>
    public sealed class TyhpdefConfig
    {
        /// <summary>
        /// Glob patterns for tyhpdef files to load.
        /// Class default is <c>**/*.tyhpdef</c>; when omitted from <c>tyhp.json</c> the list stays empty
        /// for backward compatibility with pre-Story-10 projects.
        /// </summary>
        public List<string> Include { get; set; } = ["**/*.tyhpdef"];

        /// <summary>Glob patterns for tyhpdef files to exclude after discovery.</summary>
        public List<string> Exclude { get; set; } = [];
    }
}
