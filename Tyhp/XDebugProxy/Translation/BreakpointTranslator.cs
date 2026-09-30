using System.Xml.Linq;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.XDebugProxy.Translation
{
    /// <summary>
    /// Translates breakpoint commands (IDE → XDebug) and breakpoint responses (XDebug → IDE).
    /// </summary>
    public sealed class BreakpointTranslator
    {
        private readonly SourceMapStore _store;
        private readonly PathMapper _pathMapper;
        private readonly TranslationSession _session;
        private readonly Action<string>? _onWarning;

        internal BreakpointTranslator(
            SourceMapStore store,
            PathMapper pathMapper,
            TranslationSession session,
            Action<string>? onWarning)
        {
            this._store = store;
            this._pathMapper = pathMapper;
            this._session = session;
            this._onWarning = onWarning;
        }

        /// <summary>
        /// Translate <c>breakpoint_set</c> (Tyhp <c>-f</c>/<c>-n</c> to PHP) and
        /// <c>breakpoint_update</c> (Tyhp <c>-n</c> to PHP via the <c>-d</c> breakpoint id's
        /// recorded origin — <c>breakpoint_update</c> carries no <c>-f</c> per the DBGp spec).
        /// Already-<c>.php</c> paths, unknown ids, and files without sourcemaps pass through.
        /// </summary>
        public void TranslateCommand(DbgpCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            try
            {
                this.TranslateCommandCore(command);
            }
            catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
            {
                this.Warn($"Breakpoint command translation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Reverse-map <c>&lt;breakpoint&gt;</c> filename/lineno to Tyhp, and record breakpoint
        /// ids from <c>breakpoint_set</c> / element <c>id</c> attributes.
        /// </summary>
        public void TranslateResponse(DbgpResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);

            try
            {
                this.RecordBreakpointIdFromResponse(response);
                foreach (XElement breakpoint in DbgpXml.ElementsByLocalName(response.RootElement, "breakpoint"))
                {
                    this.TranslateBreakpointElement(breakpoint);
                }
            }
            catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
            {
                this.Warn($"Breakpoint response translation failed: {ex.Message}");
            }
        }

        /// <summary><c>-d</c> means <c>breakpoint_id</c> for this command (it means <c>data_length</c> elsewhere in DBGp).</summary>
        private const string BreakpointIdFlag = "-d";

        private void TranslateCommandCore(DbgpCommand command)
        {
            if (string.Equals(command.CommandName, DbgpConstants.Commands.BreakpointUpdate, StringComparison.Ordinal))
            {
                this.TranslateUpdateCommand(command);
                return;
            }

            string? breakpointType = command.GetArgument(DbgpConstants.ArgumentFlags.Type);
            if (string.Equals(breakpointType, DbgpConstants.BreakpointTypes.Exception, StringComparison.Ordinal))
            {
                // Exception class (-x) is a PHP name; pass through unmodified. Do not treat -x
                // as a path and do not rewrite -d (breakpoint_update uses -d as the id).
                return;
            }

            string? filename = command.Filename;
            string originalFilename = filename ?? string.Empty;
            int dbgpLine = 0;
            if (!string.IsNullOrWhiteSpace(filename) && !this._pathMapper.IsDbgpUri(filename)
                && !this._pathMapper.IsPhpFile(filename))
            {
                string tyhpFs = this._pathMapper.ToFileSystemPath(filename);
                bool isKnownTyhp = this._pathMapper.IsTyhpFile(filename)
                    || this._store.GetMapForTyhpFile(tyhpFs).Count > 0;
                if (isKnownTyhp)
                {
                    TryParseDbgpLine(command.LineNumber, out dbgpLine);
                    if (dbgpLine >= 1
                        && this._pathMapper.TryMapTyhpToPhp(
                            this._store,
                            filename,
                            dbgpLine,
                            out string phpPathOrUri,
                            out int phpDbgpLine))
                    {
                        command.Filename = phpPathOrUri;
                        command.LineNumber = phpDbgpLine.ToString();
                    }

                    this.RememberPendingIfSet(command, originalFilename, dbgpLine);
                }
            }

            this.RewriteConditionalExpression(command, originalFilename);
        }

        /// <summary>
        /// <c>breakpoint_set -t conditional</c> carries a PHP expression in <c>--</c> data.
        /// Best-effort rewrite Tyhp identifiers from sourcemap <c>names</c>. Unknown compiled
        /// names are left unchanged. Never throws.
        /// </summary>
        private void RewriteConditionalExpression(DbgpCommand command, string originalFilename)
        {
            string? type = command.GetArgument(DbgpConstants.ArgumentFlags.Type);
            if (!string.Equals(type, DbgpConstants.BreakpointTypes.Conditional, StringComparison.Ordinal))
            {
                return;
            }

            byte[]? data = command.Data;
            if (data is null || data.Length == 0)
            {
                return;
            }

            try
            {
                string expression = DbgpMessageParser.Utf8.GetString(data);
                IReadOnlyList<string> names = this.CollectNamesForFile(originalFilename, command.Filename);
                string rewritten = ExpressionIdentifierRewriter.Rewrite(expression, names);
                if (!string.Equals(rewritten, expression, StringComparison.Ordinal))
                {
                    command.Data = DbgpMessageParser.Utf8.GetBytes(rewritten);
                }
            }
            catch
            {
            }
        }

        private IReadOnlyList<string> CollectNamesForFile(string originalFilename, string? mappedFilename)
        {
            var names = new List<string>();
            this.AddNamesFromPath(names, originalFilename);
            if (!string.IsNullOrWhiteSpace(mappedFilename)
                && !string.Equals(mappedFilename, originalFilename, StringComparison.Ordinal))
            {
                this.AddNamesFromPath(names, mappedFilename);
            }

            return names;
        }

        private void AddNamesFromPath(List<string> names, string pathOrUri)
        {
            if (string.IsNullOrWhiteSpace(pathOrUri) || this._pathMapper.IsDbgpUri(pathOrUri))
            {
                return;
            }

            SourceMapFile? phpMap = this._pathMapper.GetMapForPhp(this._store, pathOrUri);
            if (phpMap is not null)
            {
                names.AddRange(phpMap.Names);
                return;
            }

            foreach (SourceMapFile map in this._store.GetMapForTyhpFile(this._pathMapper.ToFileSystemPath(pathOrUri)))
            {
                names.AddRange(map.Names);
            }
        }

        /// <summary>
        /// <c>breakpoint_update -i tid -d id [-n lineno] [...]</c> carries no filename — the
        /// new <c>-n</c> (if present) is a Tyhp line number for whichever file the <paramref
        /// name="command"/>'s <c>-d</c> id was originally set on. Look that origin up in the
        /// session table and re-run the Tyhp→PHP mapping for the new line. Ids not tracked as
        /// Tyhp (unknown, or originally a <c>.php</c>/<c>dbgp://</c> breakpoint) pass through.
        /// </summary>
        private void TranslateUpdateCommand(DbgpCommand command)
        {
            string? id = command.GetArgument(BreakpointIdFlag);
            string? lineArg = command.GetArgument(DbgpConstants.ArgumentFlags.LineNumber);
            if (string.IsNullOrEmpty(id)
                || string.IsNullOrWhiteSpace(lineArg)
                || !TryParseDbgpLine(lineArg, out int newTyhpLine)
                || !this._session.BreakpointsById.TryGetValue(id, out BreakpointOrigin origin)
                || this._pathMapper.IsDbgpUri(origin.TyhpPathOrUri)
                || this._pathMapper.IsPhpFile(origin.TyhpPathOrUri))
            {
                return;
            }

            if (this._pathMapper.TryMapTyhpToPhp(
                    this._store,
                    origin.TyhpPathOrUri,
                    newTyhpLine,
                    out _,
                    out int phpDbgpLine))
            {
                command.SetArgument(DbgpConstants.ArgumentFlags.LineNumber, phpDbgpLine.ToString());
                this._session.BreakpointsById[id] = new BreakpointOrigin(origin.TyhpPathOrUri, newTyhpLine);
            }
        }

        private void RememberPendingIfSet(DbgpCommand command, string tyhpPathOrUri, int dbgpLine)
        {
            if (!string.Equals(
                    command.CommandName,
                    DbgpConstants.Commands.BreakpointSet,
                    StringComparison.Ordinal))
            {
                return;
            }

            this._session.PendingByTransactionId[command.TransactionId] =
                new BreakpointOrigin(tyhpPathOrUri, dbgpLine);
        }

        private void RecordBreakpointIdFromResponse(DbgpResponse response)
        {
            if (!string.Equals(response.Command, DbgpConstants.Commands.BreakpointSet, StringComparison.Ordinal))
            {
                return;
            }

            string? transactionId = response.TransactionId;
            if (string.IsNullOrEmpty(transactionId)
                || !this._session.PendingByTransactionId.TryRemove(transactionId, out BreakpointOrigin origin))
            {
                return;
            }

            string? id = response.GetAttribute("id");
            if (!string.IsNullOrEmpty(id))
            {
                this._session.BreakpointsById[id] = origin;
            }
        }

        private void TranslateBreakpointElement(XElement breakpoint)
        {
            string? id = DbgpXml.GetAttr(breakpoint, "id");
            if (!string.IsNullOrEmpty(id)
                && this._session.BreakpointsById.TryGetValue(id, out BreakpointOrigin stored)
                && stored.DbgpLine >= 1)
            {
                DbgpXml.SetAttr(breakpoint, "filename", stored.TyhpPathOrUri);
                DbgpXml.SetAttr(breakpoint, "lineno", stored.DbgpLine.ToString());
                return;
            }

            string? filename = DbgpXml.GetAttr(breakpoint, "filename");
            string? lineno = DbgpXml.GetAttr(breakpoint, "lineno");
            if (!string.IsNullOrWhiteSpace(filename)
                && TryParseDbgpLine(lineno, out int dbgpLine)
                && this._pathMapper.TryMapPhpToTyhp(
                    this._store,
                    filename,
                    dbgpLine,
                    out string tyhpPathOrUri,
                    out int tyhpDbgpLine,
                    out _))
            {
                DbgpXml.SetAttr(breakpoint, "filename", tyhpPathOrUri);
                DbgpXml.SetAttr(breakpoint, "lineno", tyhpDbgpLine.ToString());
            }
            else if (!string.IsNullOrEmpty(id)
                && this._session.BreakpointsById.TryGetValue(id, out BreakpointOrigin originOnly))
            {
                DbgpXml.SetAttr(breakpoint, "filename", originOnly.TyhpPathOrUri);
                if (originOnly.DbgpLine >= 1)
                {
                    DbgpXml.SetAttr(breakpoint, "lineno", originOnly.DbgpLine.ToString());
                }
            }

            this.RememberIdFromElement(breakpoint, id);
        }

        private void RememberIdFromElement(XElement breakpoint, string? id)
        {
            if (string.IsNullOrEmpty(id) || this._session.BreakpointsById.ContainsKey(id))
            {
                return;
            }

            string? filename = DbgpXml.GetAttr(breakpoint, "filename");
            if (string.IsNullOrWhiteSpace(filename))
            {
                return;
            }

            TryParseDbgpLine(DbgpXml.GetAttr(breakpoint, "lineno"), out int dbgpLine);
            this._session.BreakpointsById[id] = new BreakpointOrigin(filename, dbgpLine);
        }

        private static bool TryParseDbgpLine(string? lineno, out int dbgpLine)
        {
            return int.TryParse(lineno, out dbgpLine) && dbgpLine >= 1;
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
