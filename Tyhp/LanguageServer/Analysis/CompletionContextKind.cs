namespace Tyhp.LanguageServer.Analysis
{
    /// <summary>
    /// Cursor context that selects which completion catalog to build.
    /// </summary>
    public enum CompletionContextKind
    {
        Global,
        Variable,
        InstanceMember,
        StaticMember,
        Type,
        NewClass,
        Extends,
        Implements,
        TraitUse,
        UseImport,
        Namespace,
    }
}
