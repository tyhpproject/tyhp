using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Versioning;

namespace Tyhp.CLI
{
    /// <summary>
    /// Root-project <c>composer.json</c> create/merge for <c>tyhp init</c> (Story 21.5 shape A).
    /// Distinct from <see cref="ComposerJsonService"/> output-directory manifests.
    /// </summary>
    internal static class InitComposerManifest
    {
        internal const string PhpPackage = "tyhpdef/php";
        internal const string CorePackage = "tyhp/core";
        internal const string CompilerPackage = "tyhp/compiler";
        internal const string DevConstraint = "@dev";
        internal const string TyhpScriptName = "tyhp";
        internal const string PostAutoloadDumpScript = "post-autoload-dump";
        internal const string DefaultBinDir = "vendor/bin";
        internal const string VendorTyhpdefIncludeGlob = "./vendor-tyhpdef/**/*.tyhpdef";
        internal const string OverlayTyhpdefGlob = "./tyhpdef/**/*.tyhpdef";
        internal const string VendorTyhpdefGitignoreEntry = "vendor-tyhpdef/";

        /// <summary>
        /// Locked <c>scripts-descriptions.tyhp</c> text written into the project manifest
        /// (Composer displays this; it is not a CLI localization string).
        /// </summary>
        internal const string TyhpScriptDescription =
            "Forward extra args to the Tyhp CLI (e.g. composer tyhp build)";

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private static readonly JsonDocumentOptions ParseOptions = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        internal enum ParseStatus
        {
            Ok,
            InvalidJson,
            NotObject,
        }

        internal static ParseStatus TryParse(string json, out JsonObject? root, out string? jsonError)
        {
            root = null;
            jsonError = null;
            try
            {
                var node = JsonNode.Parse(json, documentOptions: ParseOptions);
                if (node is JsonObject obj)
                {
                    root = obj;
                    return ParseStatus.Ok;
                }

                return ParseStatus.NotObject;
            }
            catch (JsonException ex)
            {
                jsonError = ex.Message;
                return ParseStatus.InvalidJson;
            }
        }

        internal static string ToJson(JsonObject root)
            => root.ToJsonString(WriteOptions) + Environment.NewLine;

        internal static string ResolveTyhpBinPath(JsonObject root)
        {
            var binDir = DefaultBinDir;
            if (root["config"] is JsonObject config
                && TryGetString(config["bin-dir"], out var custom)
                && !String.IsNullOrWhiteSpace(custom))
            {
                var normalized = custom.Replace('\\', '/').Trim().Trim('/');
                if (normalized.Length > 0)
                {
                    binDir = normalized;
                }
            }

            return binDir + "/tyhp";
        }

        internal static string InstallBinaryCommand(string tyhpBin)
            => tyhpBin + " --install-binary";

        internal static string GenerateVendorCommand(string tyhpBin)
            => tyhpBin + " generate_tyhpdef --vendor";

        /// <summary>
        /// Applies init merge rules in place. Existing pins, <c>scripts.tyhp</c>, and unrelated
        /// keys are left untouched. <paramref name="onPhpConstraintUnsatisfied"/> is invoked when
        /// <c>require.php</c> is present but cannot satisfy <paramref name="phpVersion"/>.
        /// </summary>
        internal static void Merge(
            JsonObject root,
            string phpVersion,
            Action<string, string>? onPhpConstraintUnsatisfied)
        {
            var tyhpBin = ResolveTyhpBinPath(root);
            var phpConstraint = ComposerJsonService.PhpConstraintForPhpVersion(phpVersion);

            var require = GetOrCreateObject(root, "require");
            if (require is not null)
            {
                MergePhpConstraint(require, phpConstraint, phpVersion, onPhpConstraintUnsatisfied);
                AddIfMissingUnlessSelf(require, root, CorePackage, DevConstraint);
            }

            var requireDev = GetOrCreateObject(root, "require-dev");
            if (requireDev is not null)
            {
                if (!HasPackage(require, PhpPackage) && !HasPackage(requireDev, PhpPackage))
                {
                    AddIfMissingUnlessSelf(requireDev, root, PhpPackage, DevConstraint);
                }

                AddIfMissingUnlessSelf(
                    requireDev,
                    root,
                    CompilerPackage,
                    ComposerJsonService.ResolveCompilerPackageVersion());
            }

            MergeAllowPlugins(root);

            var scripts = GetOrCreateObject(root, "scripts");
            var hasTyhpScript = false;
            if (scripts is not null)
            {
                hasTyhpScript = scripts.ContainsKey(TyhpScriptName);
                AddIfMissing(scripts, TyhpScriptName, tyhpBin);
                hasTyhpScript = hasTyhpScript || scripts.ContainsKey(TyhpScriptName);
                MergePostAutoloadDump(scripts, tyhpBin);
            }

            if (hasTyhpScript)
            {
                var descriptions = GetOrCreateObject(root, "scripts-descriptions");
                if (descriptions is not null)
                {
                    AddIfMissing(descriptions, TyhpScriptName, TyhpScriptDescription);
                }
            }
        }

        /// <summary>
        /// Ensures <c>config.allow-plugins.tyhp/core</c> is <see langword="true"/> when the key is
        /// missing. Existing values (including an explicit <c>false</c>) are left untouched.
        /// </summary>
        internal static void MergeAllowPlugins(JsonObject root)
        {
            var config = GetOrCreateObject(root, "config");
            if (config is null)
            {
                return;
            }

            var allowPlugins = GetOrCreateObject(config, "allow-plugins");
            if (allowPlugins is null || allowPlugins.ContainsKey(CorePackage))
            {
                return;
            }

            allowPlugins[CorePackage] = true;
        }

        /// <summary>
        /// True when <c>config.allow-plugins.tyhp/core</c> is already present (any value).
        /// </summary>
        internal static bool HasTyhpCoreAllowPluginsKey(JsonObject root)
            => root["config"] is JsonObject config
                && config["allow-plugins"] is JsonObject allowPlugins
                && allowPlugins.ContainsKey(CorePackage);

        /// <summary>
        /// Writes <c>config.allow-plugins.tyhp/core: true</c> when the key is missing. Does not
        /// overwrite an explicit <c>false</c> (or a non-object <c>config</c> / <c>allow-plugins</c>).
        /// Missing files are a no-op success so callers can patch before invoking Composer.
        /// </summary>
        internal static bool TryEnsureAllowPlugins(string composerJsonPath, out string? error)
        {
            error = null;
            if (!File.Exists(composerJsonPath))
            {
                return true;
            }

            try
            {
                var json = File.ReadAllText(composerJsonPath);
                var status = TryParse(json, out var root, out var jsonError);
                if (status != ParseStatus.Ok || root is null)
                {
                    error = jsonError ?? "root is not a JSON object";
                    return false;
                }

                if (HasTyhpCoreAllowPluginsKey(root))
                {
                    return true;
                }

                MergeAllowPlugins(root);
                if (!HasTyhpCoreAllowPluginsKey(root))
                {
                    return true;
                }

                File.WriteAllText(composerJsonPath, ToJson(root));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void MergePhpConstraint(
            JsonObject require,
            string desiredConstraint,
            string phpVersion,
            Action<string, string>? onPhpConstraintUnsatisfied)
        {
            if (!require.ContainsKey("php"))
            {
                require["php"] = desiredConstraint;
                return;
            }

            if (!TryGetString(require["php"], out var existing))
            {
                // Present with a non-string value (malformed composer.json); leave it rather
                // than guessing at overwriting something we cannot evaluate.
                return;
            }

            if (String.IsNullOrWhiteSpace(existing))
            {
                // Present but blank: there is nothing to weaken, so treat it the same as a
                // missing key instead of leaving an unusable placeholder forever.
                require["php"] = desiredConstraint;
                return;
            }

            var evaluation = PhpVersionConstraint.Evaluate(phpVersion, existing);
            if (!evaluation.ConstraintIsValid || !evaluation.IsSatisfied)
            {
                onPhpConstraintUnsatisfied?.Invoke(existing, phpVersion);
            }
        }

        private static void MergePostAutoloadDump(JsonObject scripts, string tyhpBin)
        {
            var installBinary = InstallBinaryCommand(tyhpBin);
            var generateVendor = GenerateVendorCommand(tyhpBin);

            if (scripts[PostAutoloadDumpScript] is null)
            {
                scripts[PostAutoloadDumpScript] = new JsonArray { installBinary, generateVendor };
                return;
            }

            if (scripts[PostAutoloadDumpScript] is JsonValue value
                && value.TryGetValue<string>(out var asString)
                && asString is not null)
            {
                scripts[PostAutoloadDumpScript] = new JsonArray { asString };
            }

            if (scripts[PostAutoloadDumpScript] is not JsonArray array)
            {
                return;
            }

            if (!ContainsInstallBinary(array))
            {
                array.Add(installBinary);
            }

            if (!ContainsGenerateVendor(array))
            {
                array.Add(generateVendor);
            }
        }

        private static bool ContainsInstallBinary(JsonArray array)
        {
            foreach (var item in array)
            {
                if (TryGetString(item, out var text)
                    && text.Contains("--install-binary", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsGenerateVendor(JsonArray array)
        {
            foreach (var item in array)
            {
                if (TryGetString(item, out var text)
                    && text.Contains("generate_tyhpdef", StringComparison.Ordinal)
                    && text.Contains("--vendor", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the object at <paramref name="key"/>, creating it when missing. Returns
        /// <see langword="null"/> when the key exists as a non-object so callers do not clobber it.
        /// </summary>
        private static JsonObject? GetOrCreateObject(JsonObject parent, string key)
        {
            if (parent[key] is JsonObject existing)
            {
                return existing;
            }

            if (parent.ContainsKey(key))
            {
                return null;
            }

            var created = new JsonObject();
            parent[key] = created;
            return created;
        }

        private static void AddIfMissing(JsonObject section, string key, string value)
        {
            if (section.ContainsKey(key))
            {
                return;
            }

            section[key] = value;
        }

        private static void AddIfMissingUnlessSelf(
            JsonObject section,
            JsonObject root,
            string key,
            string value)
        {
            if (ComposerExtraRequireGraph.IsSelfRequire(key, ReadRootPackageName(root)))
            {
                return;
            }

            AddIfMissing(section, key, value);
        }

        private static string ReadRootPackageName(JsonObject root)
        {
            if (root["name"] is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var text)
                && text is not null)
            {
                return text;
            }

            return "";
        }

        private static bool HasPackage(JsonObject? section, string packageName)
            => section is not null && section.ContainsKey(packageName);

        private static bool TryGetString(JsonNode? node, out string value)
        {
            value = "";
            if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) && text is not null)
            {
                value = text;
                return true;
            }

            return false;
        }
    }
}
