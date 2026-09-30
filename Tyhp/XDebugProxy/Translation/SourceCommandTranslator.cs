using System.Text;
using System.Xml.Linq;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.XDebugProxy.Translation
{
    /// <summary>
    /// Intercepts DBGp <c>source</c> so the IDE receives Tyhp text instead of compiled PHP.
    /// Returns null when the file has no map, the Tyhp text cannot be read, or the URI is
    /// <c>dbgp://</c> — the proxy then forwards the command to XDebug.
    /// </summary>
    public sealed class SourceCommandTranslator
    {
        private readonly SourceMapStore _store;
        private readonly PathMapper _pathMapper;
        private readonly Action<string>? _onWarning;

        internal SourceCommandTranslator(
            SourceMapStore store,
            PathMapper pathMapper,
            Action<string>? onWarning)
        {
            this._store = store;
            this._pathMapper = pathMapper;
            this._onWarning = onWarning;
        }

        /// <summary>
        /// If this <c>source</c> command can be answered from a sourcemap / Tyhp file on disk,
        /// return a success response with base64 UTF-8 Tyhp source; otherwise null.
        /// </summary>
        public DbgpResponse? TryIntercept(DbgpCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            try
            {
                if (!string.Equals(
                        command.CommandName,
                        DbgpConstants.Commands.Source,
                        StringComparison.Ordinal))
                {
                    return null;
                }

                string? filename = command.Filename;
                if (string.IsNullOrWhiteSpace(filename) || this._pathMapper.IsDbgpUri(filename))
                {
                    return null;
                }

                if (!this.TryGetTyhpSource(filename, out string tyhpText))
                {
                    return null;
                }

                return BuildSuccessResponse(command.TransactionId, tyhpText);
            }
            catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
            {
                this.Warn($"source intercept failed: {ex.Message}");
                return null;
            }
        }

        private bool TryGetTyhpSource(string pathOrUri, out string tyhpText)
        {
            tyhpText = string.Empty;
            string fsPath = this._pathMapper.ToFileSystemPath(pathOrUri);

            if (this._pathMapper.IsPhpFile(pathOrUri))
            {
                SourceMapFile? phpMap = this._pathMapper.GetMapForPhp(this._store, pathOrUri);
                return phpMap is not null && this.TryReadFromMap(phpMap, tyhpHint: null, out tyhpText);
            }

            IReadOnlyList<SourceMapFile> tyhpMaps = this._store.GetMapForTyhpFile(fsPath);
            if (tyhpMaps.Count > 0)
            {
                foreach (SourceMapFile map in tyhpMaps)
                {
                    if (this.TryReadFromMap(map, fsPath, out tyhpText))
                    {
                        return true;
                    }
                }

                return false;
            }

            SourceMapFile? fallbackPhp = this._pathMapper.GetMapForPhp(this._store, pathOrUri);
            return fallbackPhp is not null && this.TryReadFromMap(fallbackPhp, tyhpHint: null, out tyhpText);
        }

        private bool TryReadFromMap(SourceMapFile map, string? tyhpHint, out string tyhpText)
        {
            tyhpText = string.Empty;
            string? embedded = map.GetEmbeddedSourceContent(tyhpHint);
            if (embedded is not null)
            {
                tyhpText = embedded;
                return true;
            }

            string? diskPath = this.ResolveTyhpDiskPath(map, tyhpHint);
            if (string.IsNullOrWhiteSpace(diskPath) || !File.Exists(diskPath))
            {
                return false;
            }

            try
            {
                tyhpText = File.ReadAllText(diskPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this.Warn($"Could not read Tyhp source '{diskPath}': {ex.Message}");
                return false;
            }
        }

        private string? ResolveTyhpDiskPath(SourceMapFile map, string? tyhpHint)
        {
            if (!string.IsNullOrWhiteSpace(tyhpHint))
            {
                string hinted = this._pathMapper.ResolveOriginalTyhpPath(tyhpHint);
                if (File.Exists(hinted))
                {
                    return hinted;
                }
            }

            if (map.Sources.Count == 0)
            {
                return null;
            }

            int index = 0;
            if (!string.IsNullOrWhiteSpace(tyhpHint) && map.ReferencesSource(tyhpHint))
            {
                for (int i = 0; i < map.Sources.Count; i++)
                {
                    if (SourceMapFile.PathsMatch(map.Sources[i], tyhpHint))
                    {
                        index = i;
                        break;
                    }
                }
            }

            string source = map.Sources[index];
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            string withRoot = string.IsNullOrEmpty(map.SourceRoot)
                || PathMapper.IsRootedPath(this._pathMapper.Normalize(source))
                    ? source
                    : this._pathMapper.Combine(map.SourceRoot, source);

            return this._pathMapper.ResolveOriginalTyhpPath(withRoot);
        }

        private static DbgpResponse BuildSuccessResponse(string transactionId, string tyhpText)
        {
            string base64 = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(tyhpText),
                Base64FormattingOptions.None);

            XElement root = new(
                DbgpConstants.Name("response"),
                new XAttribute("command", DbgpConstants.Commands.Source),
                new XAttribute("transaction_id", transactionId),
                new XAttribute("encoding", "base64"),
                new XAttribute("success", "1"),
                base64);

            return new DbgpResponse(root);
        }

        private void Warn(string message)
        {
            try
            {
                this._onWarning?.Invoke(message);
            }
            catch
            {
            }
        }
    }
}
