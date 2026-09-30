using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.XDebugProxy.Translation
{
    public sealed partial class PathMapper
    {
        /// <summary>True when <paramref name="pathOrUri"/> uses the <c>file:</c> scheme.</summary>
        public bool IsFileUri(string pathOrUri)
        {
            ArgumentNullException.ThrowIfNull(pathOrUri);
            return pathOrUri.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when <paramref name="pathOrUri"/> is XDebug eval/synthetic code
        /// (<c>dbgp://</c>). Those URIs must pass through untranslated.
        /// </summary>
        public bool IsDbgpUri(string pathOrUri)
        {
            ArgumentNullException.ThrowIfNull(pathOrUri);
            return pathOrUri.StartsWith("dbgp:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Convert a <c>file://</c> URI or mixed-separator path to a filesystem path with
        /// <c>/</c> separators. RFC 8089-ish: <c>file:///path</c>, <c>file:///C:/...</c>,
        /// <c>file://localhost/path</c>, <c>file://server/share</c> (UNC).
        /// </summary>
        public string ToFileSystemPath(string pathOrUri)
        {
            ArgumentNullException.ThrowIfNull(pathOrUri);
            if (!this.IsFileUri(pathOrUri))
            {
                return this.Normalize(pathOrUri);
            }

            if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out Uri? uri) && uri.IsFile)
            {
                return FileUriToPath(uri);
            }

            return this.StripFileSchemeManually(pathOrUri);
        }

        /// <summary>
        /// Convert a filesystem path to a <c>file://</c> URI. Already-URI inputs are
        /// canonicalized via a filesystem round-trip.
        /// </summary>
        public string ToFileUri(string pathOrUri)
        {
            ArgumentNullException.ThrowIfNull(pathOrUri);
            string filesystemPath = this.ToFileSystemPath(pathOrUri);
            return PathToFileUri(filesystemPath);
        }

        /// <summary>
        /// If <paramref name="originalPathOrUri"/> was a <c>file://</c> URI, emit a URI for
        /// <paramref name="newFilesystemPath"/>; otherwise emit a normalized filesystem path.
        /// </summary>
        public string PreserveScheme(string originalPathOrUri, string newFilesystemPath)
        {
            ArgumentNullException.ThrowIfNull(originalPathOrUri);
            ArgumentNullException.ThrowIfNull(newFilesystemPath);

            string normalizedNew = this.Normalize(newFilesystemPath);
            return this.IsFileUri(originalPathOrUri)
                ? this.ToFileUri(normalizedNew)
                : normalizedNew;
        }

        private static string FileUriToPath(Uri uri)
        {
            string local = SourceMapFile.NormalizePath(uri.LocalPath);
            string host = uri.Host ?? string.Empty;

            bool isLocalHost = string.IsNullOrEmpty(host)
                || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);

            // Some runtimes fold file://localhost/path into LocalPath as //localhost/path.
            if (local.StartsWith("//localhost/", StringComparison.OrdinalIgnoreCase))
            {
                local = local["//localhost".Length..];
                isLocalHost = true;
            }
            else if (string.Equals(local, "//localhost", StringComparison.OrdinalIgnoreCase))
            {
                local = "/";
                isLocalHost = true;
            }

            if (!isLocalHost)
            {
                string uncPrefix = "//" + host;
                if (local.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return local.StartsWith("//", StringComparison.Ordinal)
                        ? local
                        : uncPrefix + local.TrimStart('/');
                }

                if (local.StartsWith("//", StringComparison.Ordinal))
                {
                    return local;
                }

                if (!local.StartsWith('/'))
                {
                    local = "/" + local;
                }

                return uncPrefix + local;
            }

            if (local.Length >= 3
                && local[0] == '/'
                && char.IsAsciiLetter(local[1])
                && local[2] == ':')
            {
                local = local[1..];
            }

            return local;
        }

        private string StripFileSchemeManually(string pathOrUri)
        {
            string rest = pathOrUri;
            const string prefix = "file:";
            if (rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                rest = rest[prefix.Length..];
            }

            rest = Uri.UnescapeDataString(rest);
            if (rest.StartsWith("//", StringComparison.Ordinal))
            {
                rest = rest[2..];
                if (rest.StartsWith("localhost/", StringComparison.OrdinalIgnoreCase))
                {
                    rest = rest["localhost".Length..];
                }
                else if (string.Equals(rest, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    rest = "/";
                }
            }

            return this.Normalize(rest.Length == 0 ? "/" : rest);
        }

        private static string PathToFileUri(string filesystemPath)
        {
            string normalized = SourceMapFile.NormalizePath(filesystemPath);
            if (normalized.StartsWith("//", StringComparison.Ordinal))
            {
                if (Uri.TryCreate("file:" + normalized, UriKind.Absolute, out Uri? unc))
                {
                    return unc.AbsoluteUri;
                }

                return "file:" + normalized;
            }

            if (normalized.Length >= 2
                && char.IsAsciiLetter(normalized[0])
                && normalized[1] == ':')
            {
                if (Uri.TryCreate("file:///" + normalized, UriKind.Absolute, out Uri? drive))
                {
                    return drive.AbsoluteUri;
                }

                return "file:///" + normalized;
            }

            string withSlash = normalized.StartsWith('/') ? normalized : "/" + normalized;
            if (Uri.TryCreate("file://" + withSlash, UriKind.Absolute, out Uri? unix))
            {
                return unix.AbsoluteUri;
            }

            return "file://" + withSlash;
        }
    }
}
