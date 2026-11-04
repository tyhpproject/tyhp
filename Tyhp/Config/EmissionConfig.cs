using Microsoft.Extensions.Configuration;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Config
{
    /// <summary>
    /// How generated static-analysis docblocks are attached to emitted declarations.
    /// </summary>
    public enum DocblockEmitMode
    {
        /// <summary>Do not generate analysis tags. Author docblocks still follow <c>output.comments</c>.</summary>
        Off,

        /// <summary>Generate a tag only where it says more than the native PHP declaration.</summary>
        LossyOnly,

        /// <summary>Generate a tag for every spelled signature type, including ones PHP already states.</summary>
        All,
    }

    /// <summary>
    /// Which static-analysis dialect generated tags target.
    /// Shared tags stay unprefixed. A prefixed <c>@phpstan-</c> / <c>@psalm-</c> form is used only
    /// where the dialects disagree.
    /// </summary>
    public enum DocblockDialect
    {
        Phpstan,
        Psalm,
        Both,
        Phpdoc,
    }

    /// <summary>
    /// Emission options from <c>tyhp.json</c> <c>emit.*</c> keys.
    /// </summary>
    public sealed class EmissionConfig
    {
        /// <summary><c>emit.docblocks</c>. Default <see cref="DocblockEmitMode.LossyOnly"/>.</summary>
        public DocblockEmitMode Docblocks { get; set; } = DocblockEmitMode.LossyOnly;

        /// <summary><c>emit.docblockDialect</c>. Default <see cref="DocblockDialect.Both"/>.</summary>
        public DocblockDialect DocblockDialect { get; set; } = DocblockDialect.Both;

        internal void ApplyFrom(
            IConfiguration configuration,
            Action<MessageCode, object[]>? warn = null)
        {
            var mode = configuration["emit:docblocks"];
            if (!string.IsNullOrWhiteSpace(mode))
            {
                if (TryParseMode(mode, out var parsed))
                {
                    this.Docblocks = parsed;
                }
                else
                {
                    warn?.Invoke(MessageCode.ConfigInvalidValue, ["emit:docblocks", mode]);
                }
            }

            var dialect = configuration["emit:docblockDialect"];
            if (!string.IsNullOrWhiteSpace(dialect))
            {
                if (TryParseDialect(dialect, out var parsed))
                {
                    this.DocblockDialect = parsed;
                }
                else
                {
                    warn?.Invoke(MessageCode.ConfigInvalidValue, ["emit:docblockDialect", dialect]);
                }
            }
        }

        internal static bool TryParseMode(string text, out DocblockEmitMode mode)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "off":
                    mode = DocblockEmitMode.Off;
                    return true;
                case "lossy-only":
                case "lossyonly":
                    mode = DocblockEmitMode.LossyOnly;
                    return true;
                case "all":
                    mode = DocblockEmitMode.All;
                    return true;
                default:
                    mode = DocblockEmitMode.LossyOnly;
                    return false;
            }
        }

        internal static bool TryParseDialect(string text, out DocblockDialect dialect)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "phpstan":
                    dialect = DocblockDialect.Phpstan;
                    return true;
                case "psalm":
                    dialect = DocblockDialect.Psalm;
                    return true;
                case "both":
                    dialect = DocblockDialect.Both;
                    return true;
                case "phpdoc":
                    dialect = DocblockDialect.Phpdoc;
                    return true;
                default:
                    dialect = DocblockDialect.Both;
                    return false;
            }
        }
    }
}
