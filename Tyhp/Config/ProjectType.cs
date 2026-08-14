namespace Tyhp.Config
{
    /// <summary>
    /// Describes how a Tyhp project is compiled and distributed.
    /// </summary>
    public enum ProjectType
    {
        /// <summary>Compiled for direct execution (default).</summary>
        Application,

        /// <summary>Compiled as a reusable Composer package.</summary>
        Library,
    }
}
