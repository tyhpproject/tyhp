namespace Tyhp.XDebugProxy.SourceMap
{
    /// <summary>
    /// One decoded Source Map v3 mapping segment: a generated PHP position and, when present,
    /// the originating Tyhp source position (and optional <c>names</c> index).
    /// </summary>
    /// <remarks>
    /// All coordinates are 0-based, matching the source map spec. A 1-field segment records
    /// only a generated column (unmapped); original fields are then <see langword="null"/>.
    /// </remarks>
    public readonly record struct MappingEntry(
        int GeneratedLine,
        int GeneratedColumn,
        int? OriginalSourceIndex,
        int? OriginalLine,
        int? OriginalColumn,
        int? OriginalNameIndex)
    {
        /// <summary>
        /// True when this segment maps to an original source position (4- or 5-field VLQ).
        /// </summary>
        public bool HasOriginalPosition =>
            OriginalSourceIndex is not null
            && OriginalLine is not null
            && OriginalColumn is not null;
    }

    /// <summary>
    /// A Tyhp source location resolved from a generated PHP position.
    /// Coordinates are 0-based.
    /// </summary>
    public readonly record struct OriginalPosition(
        string SourceFile,
        int Line,
        int Column,
        string? Name = null);

    /// <summary>
    /// A generated PHP location resolved from a Tyhp source position.
    /// Coordinates are 0-based.
    /// </summary>
    public readonly record struct GeneratedPosition(
        string GeneratedFile,
        int Line,
        int Column);
}
