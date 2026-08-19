namespace Tyhp.TyhpLang.Emitter.SourceMap
{
    /// <summary>
    /// One Source Map v3 mapping segment: a generated PHP position mapped to an original
    /// <c>.tyhp</c> start position (and optionally a <c>names</c> entry).
    /// </summary>
    /// <remarks>
    /// All coordinates are 0-based, matching the source map spec. AST <c>Line</c> values are
    /// 1-based and must be converted before construction. <see cref="NameIndex"/> is omitted
    /// (<see langword="null"/>) when the segment does not name a symbol.
    /// </remarks>
    public readonly record struct SourceMapping(
        int GeneratedLine,
        int GeneratedColumn,
        int SourceIndex,
        int OriginalLine,
        int OriginalColumn,
        int? NameIndex = null);
}
