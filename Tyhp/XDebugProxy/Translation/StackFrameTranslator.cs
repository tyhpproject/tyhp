using System.Xml.Linq;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.XDebugProxy.Translation
{
    /// <summary>
        /// Reverse-maps PHP <c>filename</c>/<c>lineno</c> on stack frames, status=<c>break</c>
        /// responses, and <c>&lt;init fileuri&gt;</c> to Tyhp source locations. Each frame is
        /// mapped independently so multi-file traces can mix Tyhp frames with vendor PHP
        /// (unmapped) frames that stay as PHP.
    /// </summary>
    public sealed class StackFrameTranslator
    {
        private readonly SourceMapStore _store;
        private readonly PathMapper _pathMapper;
        private readonly Action<string>? _onWarning;

        internal StackFrameTranslator(
            SourceMapStore store,
            PathMapper pathMapper,
            Action<string>? onWarning)
        {
            this._store = store;
            this._pathMapper = pathMapper;
            this._onWarning = onWarning;
        }

        /// <summary>
        /// Translate stack frames, root <c>filename</c>/<c>lineno</c> (break status), and init
        /// <c>fileuri</c>. Vendor PHP without maps is left as PHP.
        /// </summary>
        public void TranslateResponse(DbgpResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);

            try
            {
                if (response.IsInit)
                {
                    this.TranslateInitFileUri(response);
                }

                this.TranslateFilenameLine(response.RootElement, updateWhere: false);

                foreach (XElement stack in DbgpXml.ElementsByLocalName(response.RootElement, "stack"))
                {
                    this.TranslateFilenameLine(stack, updateWhere: true);
                }
            }
            catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
            {
                this.Warn($"Stack frame translation failed: {ex.Message}");
            }
        }

        private void TranslateInitFileUri(DbgpResponse response)
        {
            string? fileuri = response.GetAttribute("fileuri");
            if (string.IsNullOrWhiteSpace(fileuri) || this._pathMapper.IsDbgpUri(fileuri))
            {
                return;
            }

            if (this._pathMapper.TryMapPhpFileToTyhpFile(this._store, fileuri, out string tyhpPathOrUri))
            {
                response.SetAttribute("fileuri", tyhpPathOrUri);
            }
        }

        private void TranslateFilenameLine(XElement element, bool updateWhere)
        {
            string? filename = DbgpXml.GetAttr(element, "filename");
            string? lineno = DbgpXml.GetAttr(element, "lineno");
            if (string.IsNullOrWhiteSpace(filename)
                || !int.TryParse(lineno, out int dbgpLine)
                || dbgpLine < 1)
            {
                return;
            }

            if (this._pathMapper.IsDbgpUri(filename))
            {
                return;
            }

            if (!this._pathMapper.TryMapPhpToTyhp(
                    this._store,
                    filename,
                    dbgpLine,
                    out string tyhpPathOrUri,
                    out int tyhpDbgpLine,
                    out string? name))
            {
                return;
            }

            DbgpXml.SetAttr(element, "filename", tyhpPathOrUri);
            DbgpXml.SetAttr(element, "lineno", tyhpDbgpLine.ToString());
            if (updateWhere && !string.IsNullOrEmpty(name))
            {
                DbgpXml.SetAttr(element, "where", name);
            }
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
