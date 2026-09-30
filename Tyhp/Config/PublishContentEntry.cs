using Microsoft.Extensions.Configuration;
using Tyhp.Domain.Exceptions;
using Tyhp.Extensions;

namespace Tyhp.Config
{
    /// <summary>
    /// How an existing destination file is treated when copying
    /// <c>output.publishContent</c> into <c>output.publishPath</c>.
    /// </summary>
    public enum PublishContentPreserve
    {
        /// <summary>Copy when the destination is missing or the source is newer.</summary>
        Newest = 0,

        /// <summary>Always copy (still subject to <see cref="PublishContentEntry.SkipUnchanged"/>).</summary>
        Always = 1,

        /// <summary>Copy only when the destination does not exist.</summary>
        Never = 2,
    }

    /// <summary>
    /// One <c>output.publishContent</c> copy rule from <c>tyhp.json</c>.
    /// </summary>
    public sealed class PublishContentEntry
    {
        /// <summary>
        /// Source files, directories, or globs. Relative paths are from the project root.
        /// </summary>
        public IReadOnlyList<string> Src { get; init; } = [];

        /// <summary>
        /// Destination under <c>output.publishPath</c>. Empty, <c>"."</c>, or omitted means the
        /// publish root. A file name (last segment has an extension) is a rename and is only
        /// valid when this entry resolves to a single non-glob file.
        /// </summary>
        public string Dst { get; init; } = ".";

        /// <summary>Globs subtracted from <see cref="Src"/> matches (project-root relative).</summary>
        public IReadOnlyList<string> Exclude { get; init; } = [];

        /// <summary>
        /// When true, matched files are copied into <see cref="Dst"/> using only the file name.
        /// Default false: a <c>**</c> glob keeps the path matched by <c>**</c>.
        /// </summary>
        public bool Flat { get; init; }

        /// <summary>Whether to overwrite an existing destination. Default <see cref="PublishContentPreserve.Newest"/>.</summary>
        public PublishContentPreserve Preserve { get; init; } = PublishContentPreserve.Newest;

        /// <summary>
        /// Skip the copy when the destination exists and size plus last-write time match the
        /// source. Default true.
        /// </summary>
        public bool SkipUnchanged { get; init; } = true;

        /// <summary>Replace a read-only destination file. Default true.</summary>
        public bool OverwriteReadOnly { get; init; } = true;

        /// <summary>When true, matching nothing is a build error. Default false (warning).</summary>
        public bool Required { get; init; }

        /// <summary>
        /// When true, copy the symlink target's bytes. When false, recreate the symlink at the
        /// destination. Default false.
        /// </summary>
        public bool FollowSymlinks { get; init; }

        /// <summary>True when <see cref="Dst"/> was written with a trailing slash (always a directory).</summary>
        internal bool DstForcedDirectory { get; init; }

        internal static PublishContentEntry? TryParse(
            IConfigurationSection section,
            int index,
            Action<MessageCode, object[]>? warn)
        {
            var src = ReadStringList(section, "src");
            if (src.Count == 0)
            {
                warn?.Invoke(
                    MessageCode.ConfigInvalidValue,
                    ["output.publishContent.src", $"entry {index} has no src"]);
                return null;
            }

            var rawDst = section["dst"];
            var dstForcedDirectory = rawDst is not null
                && (rawDst.EndsWith('/') || rawDst.EndsWith('\\'));
            var dst = NormalizeDst(rawDst);

            var preserve = ParsePreserve(section["preserve"], index, warn);

            return new PublishContentEntry
            {
                Src = src,
                Dst = dst,
                Exclude = ReadStringList(section, "exclude"),
                Flat = ReadBool(section, "flat", defaultValue: false),
                Preserve = preserve,
                SkipUnchanged = ReadBool(section, "skipUnchanged", defaultValue: true),
                OverwriteReadOnly = ReadBool(section, "overwriteReadOnly", defaultValue: true),
                Required = ReadBool(section, "required", defaultValue: false),
                FollowSymlinks = ReadBool(section, "followSymlinks", defaultValue: false),
                DstForcedDirectory = dstForcedDirectory,
            };
        }

        internal string ToConfigHashString()
        {
            return string.Join('\u001f',
                string.Join('|', this.Src),
                this.Dst,
                string.Join('|', this.Exclude),
                this.Flat,
                this.Preserve,
                this.SkipUnchanged,
                this.OverwriteReadOnly,
                this.Required,
                this.FollowSymlinks,
                this.DstForcedDirectory);
        }

        internal static string NormalizeDst(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return ".";
            }

            var trimmed = raw.Trim().TrimEnd('/', '\\');
            return string.IsNullOrEmpty(trimmed) ? "." : trimmed;
        }

        internal static bool LooksLikeFileName(string dst)
        {
            var fileName = Path.GetFileName(dst.TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(fileName) || fileName is "." or "..")
            {
                return false;
            }

            return fileName.Contains('.', StringComparison.Ordinal);
        }

        private static PublishContentPreserve ParsePreserve(
            string? raw,
            int index,
            Action<MessageCode, object[]>? warn)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return PublishContentPreserve.Newest;
            }

            switch (raw.Trim().ToLowerInvariant())
            {
                case "newest":
                    return PublishContentPreserve.Newest;
                case "always":
                    return PublishContentPreserve.Always;
                case "never":
                    return PublishContentPreserve.Never;
                default:
                    warn?.Invoke(
                        MessageCode.ConfigInvalidValue,
                        [$"output.publishContent[{index}].preserve", raw.Trim()]);
                    return PublishContentPreserve.Newest;
            }
        }

        private static bool ReadBool(IConfiguration section, string key, bool defaultValue)
        {
            if (!section.GetSection(key).Exists())
            {
                return defaultValue;
            }

            return section[key].ParseBool();
        }

        private static List<string> ReadStringList(IConfiguration section, string key)
        {
            var child = section.GetSection(key);
            if (!child.Exists())
            {
                return [];
            }

            if (child.GetSection("0").Exists())
            {
                var list = new List<string>();
                for (var i = 0; i < 255; i++)
                {
                    var item = child.GetSection(i.ToString());
                    if (!item.Exists())
                    {
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(item.Value))
                    {
                        list.Add(item.Value.Trim());
                    }
                }

                return list;
            }

            return string.IsNullOrWhiteSpace(child.Value)
                ? []
                : [child.Value.Trim()];
        }
    }
}
