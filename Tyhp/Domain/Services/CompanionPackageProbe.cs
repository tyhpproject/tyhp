using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Discovers whether a published <c>tyhp/&lt;vendor&gt;-&lt;name&gt;</c> companion exists
    /// and which constraint to require. Composer repositories first, Packagist HTTP fallback.
    /// </summary>
    public interface ICompanionPackageProbe
    {
        /// <summary>
        /// Returns a Composer constraint when a compatible companion is available.
        /// </summary>
        bool TryResolve(
            string companionPackageName,
            string? upstreamVersion,
            string projectRoot,
            out string constraint);
    }

    public sealed class CompanionPackageProbe : ICompanionPackageProbe
    {
        public const string DefaultPackagistP2Base = "https://repo.packagist.org/p2/";

        internal Func<Uri, string?>? Fetch { get; init; }

        public bool TryResolve(
            string companionPackageName,
            string? upstreamVersion,
            string projectRoot,
            out string constraint)
        {
            constraint = "";
            var versions = new List<string>();
            var packagistDisabled = false;

            foreach (var repo in ReadRepositories(projectRoot))
            {
                if (repo.DisablePackagist)
                {
                    packagistDisabled = true;
                    continue;
                }

                if (repo.Type == "path" && !string.IsNullOrWhiteSpace(repo.Url))
                {
                    CollectPathRepoVersions(projectRoot, repo.Url, companionPackageName, versions);
                    continue;
                }

                if (repo.Type == "composer" && !string.IsNullOrWhiteSpace(repo.Url))
                {
                    CollectHttpVersions(ComposerP2Uri(repo.Url, companionPackageName), companionPackageName, versions);
                }
            }

            if (versions.Count == 0 && !packagistDisabled)
            {
                CollectHttpVersions(
                    ComposerP2Uri(DefaultPackagistP2Base, companionPackageName),
                    companionPackageName,
                    versions);
            }

            if (versions.Count == 0)
            {
                return false;
            }

            var chosen = ChooseCompatible(versions, upstreamVersion);
            if (string.IsNullOrWhiteSpace(chosen))
            {
                return false;
            }

            constraint = chosen;
            return true;
        }

        internal static string? ChooseCompatible(IReadOnlyList<string> versions, string? upstreamVersion)
        {
            if (versions.Count == 0)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(upstreamVersion))
            {
                return versions[0];
            }

            var preferred = VendorTyhpdefLayout.CompanionConstraintForUpstream(upstreamVersion);
            var matches = versions
                .Where(v => VendorTyhpdefLayout.CompanionVersionMatchesUpstream(v, upstreamVersion))
                .ToList();
            if (matches.Count == 0)
            {
                return null;
            }

            if (preferred is not null)
            {
                var exact = matches.FirstOrDefault(v =>
                    string.Equals(StripV(v), StripV(preferred), StringComparison.OrdinalIgnoreCase));
                if (exact is not null)
                {
                    return exact;
                }
            }

            return matches
                .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        private void CollectPathRepoVersions(
            string projectRoot,
            string url,
            string companionPackageName,
            List<string> versions)
        {
            string root;
            try
            {
                root = Path.IsPathRooted(url)
                    ? Path.GetFullPath(url)
                    : Path.GetFullPath(Path.Combine(projectRoot, url));
            }
            catch (Exception)
            {
                return;
            }

            if (!Directory.Exists(root))
            {
                return;
            }

            TryAddPathManifest(Path.Combine(root, "composer.json"), companionPackageName, versions);

            try
            {
                foreach (var child in Directory.EnumerateDirectories(root))
                {
                    TryAddPathManifest(Path.Combine(child, "composer.json"), companionPackageName, versions);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static void TryAddPathManifest(string composerJson, string companionPackageName, List<string> versions)
        {
            if (!File.Exists(composerJson))
            {
                return;
            }

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(composerJson))?.AsObject();
                var name = root?["name"]?.GetValue<string>();
                if (!string.Equals(name, companionPackageName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var version = root?["version"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(version))
                {
                    versions.Add(version);
                }
                else
                {
                    versions.Add("@dev");
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
            }
        }

        private void CollectHttpVersions(Uri uri, string companionPackageName, List<string> versions)
        {
            string? json;
            try
            {
                json = (this.Fetch ?? FetchDefault)(uri);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("packages", out var packages)
                    || packages.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                if (!packages.TryGetProperty(companionPackageName, out var list)
                    || list.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("version", out var version)
                        && version.ValueKind == JsonValueKind.String)
                    {
                        var text = version.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            versions.Add(text);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
            }
        }

        internal static Uri ComposerP2Uri(string repositoryUrl, string packageName)
        {
            var baseUrl = repositoryUrl.TrimEnd('/') + "/";
            if (!baseUrl.Contains("/p2/", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl += "p2/";
            }

            return new Uri(baseUrl + packageName + ".json");
        }

        private static string? FetchDefault(Uri uri)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var response = client.GetAsync(uri).GetAwaiter().GetResult();
            if ((int)response.StatusCode == 404)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private static List<RepositoryRef> ReadRepositories(string projectRoot)
        {
            var result = new List<RepositoryRef>();
            var composerPath = Path.Combine(projectRoot, "composer.json");
            if (!File.Exists(composerPath))
            {
                return result;
            }

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(composerPath))?.AsObject();
                var repos = root?["repositories"];
                if (repos is JsonArray array)
                {
                    foreach (var node in array)
                    {
                        if (node is JsonObject obj)
                        {
                            result.Add(ParseRepoObject(obj));
                        }
                    }
                }
                else if (repos is JsonObject map)
                {
                    foreach (var property in map)
                    {
                        if (property.Key.Equals("packagist.org", StringComparison.OrdinalIgnoreCase)
                            && property.Value is JsonValue flag
                            && flag.TryGetValue<bool>(out var enabled)
                            && !enabled)
                        {
                            result.Add(new RepositoryRef { DisablePackagist = true });
                            continue;
                        }

                        if (property.Value is JsonObject obj)
                        {
                            result.Add(ParseRepoObject(obj));
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
            }

            return result;
        }

        private static RepositoryRef ParseRepoObject(JsonObject obj)
        {
            var type = obj["type"]?.GetValue<string>() ?? "";
            var url = obj["url"]?.GetValue<string>() ?? "";
            var packagist = obj["packagist.org"];
            var disable = false;
            if (packagist is JsonValue flag && flag.TryGetValue<bool>(out var enabled))
            {
                disable = !enabled;
            }

            return new RepositoryRef
            {
                Type = type.Trim().ToLowerInvariant(),
                Url = url.Trim(),
                DisablePackagist = disable,
            };
        }

        private static string StripV(string version)
        {
            var raw = version.Trim();
            if (raw.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                && raw.Length > 1
                && char.IsDigit(raw[1]))
            {
                return raw[1..];
            }

            return raw;
        }

        private sealed class RepositoryRef
        {
            public string Type { get; init; } = "";

            public string Url { get; init; } = "";

            public bool DisablePackagist { get; init; }
        }
    }
}
