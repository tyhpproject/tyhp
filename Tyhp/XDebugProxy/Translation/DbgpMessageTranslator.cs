using System.Collections.Concurrent;
using System.Xml.Linq;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.XDebugProxy.Translation
{
    /// <summary>
    /// Per-debug-session DBGp translator. Construct one instance per debug session so
    /// breakpoint-id tables and feature state stay independent.
    /// </summary>
    /// <remarks>
    /// PLACEHOLDER_STORY_19: Coordinate with LSP for shared debug adapter protocol
    /// </remarks>
    public sealed class DbgpMessageTranslator
    {
        private readonly SourceMapStore _store;
        private readonly PathMapper _pathMapper;
        private readonly TranslationSession _session;
        private readonly BreakpointTranslator _breakpoints;
        private readonly StackFrameTranslator _stackFrames;
        private readonly VariableTranslator _variables;
        private readonly SourceCommandTranslator _source;
        private readonly EvalTranslator _eval;
        private readonly Action<string>? _onWarning;

        /// <summary>
        /// Create a translator bound to one debug session.
        /// </summary>
        /// <param name="sourceMapStore">Project sourcemap cache (may be shared across sessions).</param>
        /// <param name="pathMapper">Tyhp source root ↔ PHP output root and <c>file://</c> mapping.</param>
        /// <param name="onWarning">
        /// Optional callback for non-fatal translation failures. Phase 5 may map these onto
        /// MessageCode 7405; Phase 3 does not emit CLI strings.
        /// </param>
        public DbgpMessageTranslator(
            SourceMapStore sourceMapStore,
            PathMapper pathMapper,
            Action<string>? onWarning = null)
        {
            ArgumentNullException.ThrowIfNull(sourceMapStore);
            ArgumentNullException.ThrowIfNull(pathMapper);

            this._store = sourceMapStore;
            this._pathMapper = pathMapper;
            this._onWarning = onWarning;
            this._session = new TranslationSession();
            this._breakpoints = new BreakpointTranslator(sourceMapStore, pathMapper, this._session, onWarning);
            this._stackFrames = new StackFrameTranslator(sourceMapStore, pathMapper, onWarning);
            this._variables = new VariableTranslator(sourceMapStore, pathMapper, onWarning);
            this._source = new SourceCommandTranslator(sourceMapStore, pathMapper, onWarning);
            this._eval = new EvalTranslator(sourceMapStore, pathMapper, onWarning);
        }

        public SourceMapStore SourceMapStore => this._store;

        public PathMapper PathMapper => this._pathMapper;

        /// <summary>
        /// Original <c>fileuri</c> from the XDebug <c>&lt;init&gt;</c> packet (PHP path / working
        /// file), captured before reverse-mapping.
        /// </summary>
        public string? InitFileUri => this._session.InitFileUri;

        /// <summary>Feature names captured from <c>feature_get</c> / <c>feature_set</c> (no behavior yet).</summary>
        public IReadOnlyDictionary<string, string> Features => this._session.Features;

        /// <summary>
        /// Translate an IDE command in place before forwarding to XDebug.
        /// Commands that do not need translation are left unmodified.
        /// </summary>
        public void TranslateIdeToXDebug(DbgpCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            try
            {
                switch (command.CommandName)
                {
                    case DbgpConstants.Commands.BreakpointSet:
                    case DbgpConstants.Commands.BreakpointUpdate:
                        this._breakpoints.TranslateCommand(command);
                        break;
                    case DbgpConstants.Commands.Eval:
                        this._eval.TranslateCommand(command);
                        break;
                    case DbgpConstants.Commands.FeatureSet:
                    case DbgpConstants.Commands.FeatureGet:
                        this.CaptureFeatureCommand(command);
                        break;
                }
            }
            catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
            {
                this.Warn($"IDE→XDebug translation failed for '{command.CommandName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Translate an XDebug response / init packet in place before forwarding to the IDE.
        /// </summary>
        public void TranslateXDebugToIde(DbgpResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);

            try
            {
                if (response.IsInit)
                {
                    this._session.InitFileUri = response.GetAttribute("fileuri");
                }

                this.CaptureFeatureResponse(response);

                string? command = response.Command;
                if (IsBreakpointResponse(command) || DbgpXml.ElementsByLocalName(response.RootElement, "breakpoint").Any())
                {
                    this._breakpoints.TranslateResponse(response);
                }

                this._stackFrames.TranslateResponse(response);

                if (IsVariableResponse(command) || DbgpXml.ElementsByLocalName(response.RootElement, "property").Any())
                {
                    this._variables.TranslateResponse(response);
                }
            }
            catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
            {
                this.Warn($"XDebug→IDE translation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// If non-null, the proxy should send this response to the IDE without forwarding the
        /// command to XDebug. Intercepts <c>source</c> when a sourcemap can supply Tyhp text.
        /// Eval is never intercepted.
        /// </summary>
        public DbgpResponse? InterceptCommand(DbgpCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            try
            {
                if (string.Equals(command.CommandName, DbgpConstants.Commands.Source, StringComparison.Ordinal))
                {
                    return this._source.TryIntercept(command);
                }

                if (string.Equals(command.CommandName, DbgpConstants.Commands.Eval, StringComparison.Ordinal))
                {
                    return this._eval.TryIntercept(command);
                }

                return null;
            }
            catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
            {
                this.Warn($"Command intercept failed for '{command.CommandName}': {ex.Message}");
                return null;
            }
        }

        private void CaptureFeatureCommand(DbgpCommand command)
        {
            string? name = command.GetArgument("-n");
            string? value = command.GetArgument("-v");
            if (!string.IsNullOrEmpty(name) && value is not null)
            {
                this._session.Features[name] = value;
            }
        }

        private void CaptureFeatureResponse(DbgpResponse response)
        {
            string? command = response.Command;
            if (!string.Equals(command, DbgpConstants.Commands.FeatureGet, StringComparison.Ordinal)
                && !string.Equals(command, DbgpConstants.Commands.FeatureSet, StringComparison.Ordinal))
            {
                return;
            }

            string? feature = response.GetAttribute("feature");
            if (string.IsNullOrEmpty(feature))
            {
                return;
            }

            string? value = response.GetChildValue("feature")
                ?? response.RootElement.Value
                ?? response.GetAttribute("encoding")
                ?? response.GetAttribute("max_data");
            if (value is not null)
            {
                this._session.Features[feature] = value.Trim();
            }
        }

        private static bool IsBreakpointResponse(string? command)
        {
            return command is DbgpConstants.Commands.BreakpointSet
                or DbgpConstants.Commands.BreakpointGet
                or DbgpConstants.Commands.BreakpointList
                or DbgpConstants.Commands.BreakpointUpdate
                or DbgpConstants.Commands.BreakpointRemove;
        }

        private static bool IsVariableResponse(string? command)
        {
            return command is DbgpConstants.Commands.ContextGet
                or DbgpConstants.Commands.PropertyGet
                or DbgpConstants.Commands.PropertyValue
                or DbgpConstants.Commands.PropertySet;
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

    /// <summary>
    /// Per-session mutable translation state (one instance per <see cref="DbgpMessageTranslator"/>).
    /// Backed by <see cref="ConcurrentDictionary{TKey,TValue}"/>: <see cref="DebugSession"/> relays
    /// the IDE→XDebug and XDebug→IDE directions as two independently-running tasks, and both
    /// directions read/write this state (for example <c>breakpoint_set</c> commands write
    /// <see cref="PendingByTransactionId"/> while the matching response moves the entry into
    /// <see cref="BreakpointsById"/>), so plain <see cref="Dictionary{TKey,TValue}"/> instances
    /// would not be safe here.
    /// </summary>
    internal sealed class TranslationSession
    {
        public ConcurrentDictionary<string, BreakpointOrigin> BreakpointsById { get; } =
            new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, BreakpointOrigin> PendingByTransactionId { get; } =
            new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, string> Features { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public string? InitFileUri { get; set; }
    }

    internal readonly record struct BreakpointOrigin(string TyhpPathOrUri, int DbgpLine);

    internal static class DbgpXml
    {
        public static string? GetAttr(XElement element, string name)
        {
            return element.Attribute(name)?.Value
                ?? element.Attribute(DbgpConstants.XmlNamespace + name)?.Value;
        }

        public static void SetAttr(XElement element, string name, string value)
        {
            element.SetAttributeValue(name, value);
        }

        public static IEnumerable<XElement> ElementsByLocalName(XElement root, string localName)
        {
            return root.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));
        }
    }
}
