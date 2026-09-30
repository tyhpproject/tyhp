namespace Tyhp.Domain.Services.PhpDoc
{
    /// <summary>
    /// One PHPDoc tag extracted from a doc comment (<c>@param</c>, <c>@return</c>, …).
    /// </summary>
    public sealed class PhpDocTag
    {
        public PhpDocTag(
            string tagName,
            string? typeExpression,
            string? parameterName,
            string? description,
            string rawContent)
        {
            this.TagName = tagName;
            this.TypeExpression = typeExpression;
            this.ParameterName = parameterName;
            this.Description = description;
            this.RawContent = rawContent;
        }

        /// <summary>Tag name without the leading <c>@</c> (lowercase), e.g. <c>param</c>, <c>return</c>.</summary>
        public string TagName { get; }

        /// <summary>
        /// Type expression as written in the doc comment (e.g. <c>string|int</c>,
        /// <c>array&lt;string, mixed&gt;</c>). Null when the tag has no type.
        /// </summary>
        public string? TypeExpression { get; }

        /// <summary>
        /// Parameter, property, template, or magic-method name without a leading
        /// <c>$</c>. Null when the tag has no name.
        /// </summary>
        public string? ParameterName { get; }

        /// <summary>Prose after the type and name. Null when absent.</summary>
        public string? Description { get; }

        /// <summary>Entire raw content after the tag name, including newlines.</summary>
        public string RawContent { get; }
    }
}
