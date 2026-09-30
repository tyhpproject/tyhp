namespace Tyhp.Domain.Services.PhpDoc
{
    /// <summary>
    /// Parsed PHPDoc comment: summary, description, and tags.
    /// </summary>
    public sealed class PhpDocBlock
    {
        public PhpDocBlock(
            string summary,
            string description,
            List<PhpDocTag> tags,
            List<PhpDocTag> paramTags,
            PhpDocTag? returnTag,
            List<PhpDocTag> throwsTags,
            List<PhpDocTag> templateTags,
            PhpDocTag? varTag,
            PhpDocTag? deprecatedTag,
            bool isInternal,
            bool isDeprecated,
            List<PhpDocTag> methodTags,
            List<PhpDocTag> propertyTags,
            List<PhpDocTag> typeAliasTags,
            List<PhpDocTag> importTypeTags)
        {
            this.Summary = summary;
            this.Description = description;
            this.Tags = tags;
            this.ParamTags = paramTags;
            this.ReturnTag = returnTag;
            this.ThrowsTags = throwsTags;
            this.TemplateTags = templateTags;
            this.VarTag = varTag;
            this.DeprecatedTag = deprecatedTag;
            this.IsInternal = isInternal;
            this.IsDeprecated = isDeprecated;
            this.MethodTags = methodTags;
            this.PropertyTags = propertyTags;
            this.TypeAliasTags = typeAliasTags;
            this.ImportTypeTags = importTypeTags;
        }

        /// <summary>Empty block used for missing or blank doc comments.</summary>
        public static PhpDocBlock Empty { get; } = new(
            summary: "",
            description: "",
            tags: [],
            paramTags: [],
            returnTag: null,
            throwsTags: [],
            templateTags: [],
            varTag: null,
            deprecatedTag: null,
            isInternal: false,
            isDeprecated: false,
            methodTags: [],
            propertyTags: [],
            typeAliasTags: [],
            importTypeTags: []);

        /// <summary>First non-empty line before any tag.</summary>
        public string Summary { get; }

        /// <summary>Remaining text between the summary and the first tag.</summary>
        public string Description { get; }

        /// <summary>Every parsed tag, in source order.</summary>
        public List<PhpDocTag> Tags { get; }

        /// <summary>
        /// Effective <c>@param</c> tags. <c>@phpstan-param</c> / <c>@psalm-param</c>
        /// take precedence over <c>@param</c> for the same parameter name.
        /// </summary>
        public List<PhpDocTag> ParamTags { get; }

        /// <summary>
        /// Effective <c>@return</c> tag. <c>@phpstan-return</c> / <c>@psalm-return</c>
        /// take precedence over <c>@return</c>.
        /// </summary>
        public PhpDocTag? ReturnTag { get; }

        /// <summary><c>@throws</c> tags (including PHPStan/Psalm variants).</summary>
        public List<PhpDocTag> ThrowsTags { get; }

        /// <summary>
        /// Effective <c>@template</c> tags. PHPStan/Psalm template tags take precedence
        /// for the same template name.
        /// </summary>
        public List<PhpDocTag> TemplateTags { get; }

        /// <summary>
        /// Effective <c>@var</c> tag. <c>@phpstan-var</c> / <c>@psalm-var</c> take precedence.
        /// </summary>
        public PhpDocTag? VarTag { get; }

        /// <summary>The <c>@deprecated</c> tag, if present.</summary>
        public PhpDocTag? DeprecatedTag { get; }

        /// <summary>True when the comment has an <c>@internal</c> tag.</summary>
        public bool IsInternal { get; }

        /// <summary>True when the comment has an <c>@deprecated</c> tag.</summary>
        public bool IsDeprecated { get; }

        /// <summary>Magic <c>@method</c> tags (PHPStan/Psalm variants take precedence by name).</summary>
        public List<PhpDocTag> MethodTags { get; }

        /// <summary>
        /// Magic <c>@property</c>, <c>@property-read</c>, and <c>@property-write</c> tags.
        /// </summary>
        public List<PhpDocTag> PropertyTags { get; }

        /// <summary>
        /// Local type aliases from <c>@phpstan-type</c> / <c>@psalm-type</c> /
        /// <c>@phan-type</c>. <see cref="PhpDocTag.ParameterName"/> is the alias;
        /// <see cref="PhpDocTag.TypeExpression"/> is the raw right-hand type.
        /// PHPStan tags win over Psalm, then Phan, for the same name.
        /// </summary>
        public List<PhpDocTag> TypeAliasTags { get; }

        /// <summary>
        /// <c>@phpstan-import-type</c> / <c>@psalm-import-type</c>.
        /// <see cref="PhpDocTag.ParameterName"/> is the local name (after <c>as</c>
        /// when present); <see cref="PhpDocTag.TypeExpression"/> is the source
        /// class; <see cref="PhpDocTag.Description"/> is the imported alias on
        /// that class (the name before <c>as</c>).
        /// </summary>
        public List<PhpDocTag> ImportTypeTags { get; }
    }
}
