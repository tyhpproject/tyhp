using System.Xml.Linq;

namespace Tyhp.XDebugProxy.Dbgp
{
    /// <summary>
    /// An XDebug-to-IDE DBGp packet: a <c>&lt;response&gt;</c>, <c>&lt;init&gt;</c>,
    /// <c>&lt;stream&gt;</c>, or <c>&lt;notify&gt;</c> XML document.
    /// </summary>
    public sealed class DbgpResponse
    {
        public DbgpResponse(XElement rootElement)
        {
            ArgumentNullException.ThrowIfNull(rootElement);
            this.RootElement = rootElement;
        }

        /// <summary>Parsed XML root element (namespace-aware).</summary>
        public XElement RootElement { get; }

        /// <summary>True when the root element is <c>&lt;init&gt;</c>.</summary>
        public bool IsInit =>
            string.Equals(this.RootElement.Name.LocalName, "init", StringComparison.Ordinal);

        /// <summary>True when the root element is <c>&lt;response&gt;</c>.</summary>
        public bool IsResponseElement =>
            string.Equals(this.RootElement.Name.LocalName, "response", StringComparison.Ordinal);

        /// <summary><c>transaction_id</c> attribute, if present.</summary>
        public string? TransactionId
        {
            get => this.GetAttribute("transaction_id");
            set => this.SetAttribute("transaction_id", value);
        }

        /// <summary><c>command</c> attribute (echo of the command that produced this packet).</summary>
        public string? Command
        {
            get => this.GetAttribute("command");
            set => this.SetAttribute("command", value);
        }

        /// <summary><c>status</c> attribute (engine status or command result).</summary>
        public string? Status
        {
            get => this.GetAttribute("status");
            set => this.SetAttribute("status", value);
        }

        /// <summary><c>reason</c> attribute, if present.</summary>
        public string? Reason
        {
            get => this.GetAttribute("reason");
            set => this.SetAttribute("reason", value);
        }

        /// <summary>Local name of the root element (<c>response</c>, <c>init</c>, …).</summary>
        public string RootLocalName => this.RootElement.Name.LocalName;

        public string? GetAttribute(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return this.RootElement.Attribute(name)?.Value
                ?? this.RootElement.Attribute(DbgpConstants.XmlNamespace + name)?.Value;
        }

        public void SetAttribute(string name, string? value)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (value is null)
            {
                this.RootElement.Attribute(name)?.Remove();
                this.RootElement.Attribute(DbgpConstants.XmlNamespace + name)?.Remove();
            }
            else
            {
                this.RootElement.SetAttributeValue(name, value);
            }
        }

        /// <summary>
        /// First direct child whose local name matches, regardless of XML namespace.
        /// </summary>
        public XElement? GetChild(string localName)
        {
            ArgumentNullException.ThrowIfNull(localName);
            return this.RootElement.Element(DbgpConstants.Name(localName))
                ?? this.RootElement.Element(localName)
                ?? this.RootElement.Elements()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));
        }

        /// <summary>
        /// Direct children whose local name matches, regardless of XML namespace.
        /// </summary>
        public IEnumerable<XElement> GetChildren(string localName)
        {
            ArgumentNullException.ThrowIfNull(localName);
            return this.RootElement.Elements()
                .Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));
        }

        /// <summary>
        /// Text/CDATA of the named child, or null if the child is absent.
        /// </summary>
        public string? GetChildValue(string localName)
        {
            return this.GetChild(localName)?.Value;
        }

        /// <summary>
        /// Attribute on a descendant matched by local name. Returns the first match.
        /// </summary>
        public string? GetChildAttribute(string childLocalName, string attributeName)
        {
            ArgumentNullException.ThrowIfNull(childLocalName);
            ArgumentNullException.ThrowIfNull(attributeName);
            XElement? child = this.GetChild(childLocalName);
            return child?.Attribute(attributeName)?.Value;
        }
    }
}
