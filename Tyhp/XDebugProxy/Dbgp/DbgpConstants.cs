using System.Xml.Linq;

namespace Tyhp.XDebugProxy.Dbgp
{
    /// <summary>
    /// DBGp protocol names: commands, breakpoint types, engine status values, and features.
    /// </summary>
    public static class DbgpConstants
    {
        /// <summary>Default XML namespace for DBGp packets (<c>urn:debugger_protocol_v1</c>).</summary>
        public const string XmlNamespaceUri = "urn:debugger_protocol_v1";

        /// <summary>XName namespace corresponding to <see cref="XmlNamespaceUri"/>.</summary>
        public static readonly XNamespace XmlNamespace = XmlNamespaceUri;

        /// <summary>Wire-format terminator and length/XML separator (NUL).</summary>
        public const byte NullByte = 0;

        /// <summary>Separates command arguments from optional base64 payload data.</summary>
        public const string DataSeparator = "--";

        /// <summary>Default character encoding for DBGp command text and serialized XML.</summary>
        public const string DefaultEncodingName = "UTF-8";

        public static class Commands
        {
            public const string BreakpointSet = "breakpoint_set";
            public const string BreakpointGet = "breakpoint_get";
            public const string BreakpointRemove = "breakpoint_remove";
            public const string BreakpointList = "breakpoint_list";
            public const string BreakpointUpdate = "breakpoint_update";
            public const string Run = "run";
            public const string StepInto = "step_into";
            public const string StepOver = "step_over";
            public const string StepOut = "step_out";
            public const string Stop = "stop";
            public const string Detach = "detach";
            public const string StackGet = "stack_get";
            public const string StackDepth = "stack_depth";
            public const string ContextNames = "context_names";
            public const string ContextGet = "context_get";
            public const string PropertyGet = "property_get";
            public const string PropertySet = "property_set";
            public const string PropertyValue = "property_value";
            public const string Source = "source";
            public const string FeatureGet = "feature_get";
            public const string FeatureSet = "feature_set";
            public const string Eval = "eval";
            public const string Status = "status";
            public const string TypemapGet = "typemap_get";
        }

        public static class BreakpointTypes
        {
            public const string Line = "line";
            public const string Call = "call";
            public const string Return = "return";
            public const string Exception = "exception";
            public const string Conditional = "conditional";
            public const string Watch = "watch";
        }

        public static class Status
        {
            public const string Starting = "starting";
            public const string Stopping = "stopping";
            public const string Stopped = "stopped";
            public const string Running = "running";
            public const string Break = "break";
        }

        public static class Features
        {
            public const string LanguageSupportsThreads = "language_supports_threads";
            public const string LanguageName = "language_name";
            public const string LanguageVersion = "language_version";
            public const string Encoding = "encoding";
            public const string ProtocolVersion = "protocol_version";
            public const string SupportsAsync = "supports_async";
            public const string DataEncoding = "data_encoding";
            public const string BreakpointLanguages = "breakpoint_languages";
            public const string BreakpointTypes = "breakpoint_types";
            public const string MultipleSessions = "multiple_sessions";
            public const string MaxChildren = "max_children";
            public const string MaxData = "max_data";
            public const string MaxDepth = "max_depth";
        }

        public static class ArgumentFlags
        {
            public const string TransactionId = "-i";
            public const string Filename = "-f";
            public const string LineNumber = "-n";
            public const string Type = "-t";
            public const string DataLength = "-d";
            public const string Exception = "-x";
        }

        /// <summary>
        /// Build an <see cref="XName"/> in the DBGp default namespace.
        /// </summary>
        public static XName Name(string localName)
        {
            ArgumentNullException.ThrowIfNull(localName);
            return XmlNamespace + localName;
        }
    }
}
