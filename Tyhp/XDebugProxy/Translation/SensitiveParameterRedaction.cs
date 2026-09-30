using System.Xml.Linq;
using Tyhp.XDebugProxy.Dbgp;

namespace Tyhp.XDebugProxy.Translation
{
    /// <summary>
    /// Fail-closed handling of PHP <c>\SensitiveParameterValue</c> in DBGp property trees.
    /// Never copies inner <c>$value</c> / <c>getValue</c> onto a wrapper (unlike Decimal
    /// display flattening). E.2 / Story 23 must call this helper when synthesizing frames
    /// or reconstructing eval arguments — do not forward raw marked values.
    /// </summary>
    public static class SensitiveParameterRedaction
    {
        /// <summary>PHP engine wrapper class name, without a leading backslash.</summary>
        public const string ClassName = "SensitiveParameterValue";

        /// <summary>
        /// True when <paramref name="classname"/> is <c>SensitiveParameterValue</c> or
        /// <c>\SensitiveParameterValue</c> (leading <c>\</c> optional; other namespaces
        /// do not match).
        /// </summary>
        public static bool IsSensitiveParameterValueClass(string? classname)
        {
            if (string.IsNullOrWhiteSpace(classname))
            {
                return false;
            }

            string trimmed = classname.Trim().TrimStart('\\');
            return string.Equals(trimmed, ClassName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when display flattening must skip this classname. Parallel to Decimal's
        /// class check; run this <em>before</em> any unwrap.
        /// </summary>
        public static bool MustNotUnwrap(string? classname) =>
            IsSensitiveParameterValueClass(classname);

        /// <summary>
        /// True when <paramref name="property"/> is a <c>\SensitiveParameterValue</c>
        /// wrapper and must not be flattened.
        /// </summary>
        public static bool MustNotUnwrap(XElement property)
        {
            ArgumentNullException.ThrowIfNull(property);
            return IsSensitiveParameterValueClass(DbgpXml.GetAttr(property, "classname"));
        }

        /// <summary>
        /// If <paramref name="property"/> is a <c>\SensitiveParameterValue</c> wrapper,
        /// leave it as that object, hide children so inner <c>$value</c> is not shown,
        /// and return <c>true</c> (caller must skip Decimal-style flatten). Non-wrappers
        /// are left unchanged and return <c>false</c>.
        /// </summary>
        public static bool TryPreserveWrappedProperty(XElement property)
        {
            ArgumentNullException.ThrowIfNull(property);
            if (!MustNotUnwrap(property))
            {
                return false;
            }

            HideChildren(property);
            return true;
        }

        /// <summary>
        /// Unwrap is refused. Never copies inner <c>$value</c> / <c>getValue</c> onto the
        /// parent. <paramref name="innerValue"/> is always <c>null</c>.
        /// </summary>
        public static bool TryUnwrap(XElement property, out string? innerValue)
        {
            ArgumentNullException.ThrowIfNull(property);
            innerValue = null;
            return false;
        }

        /// <summary>
        /// Build an opaque <c>\SensitiveParameterValue</c> DBGp property for reconstructed
        /// arguments (E.2 / Story 23). Does not accept a raw value — the secret is never
        /// placed in XML.
        /// </summary>
        public static XElement WrapOpaque(string? name = null, string? fullname = null)
        {
            var property = new XElement(DbgpConstants.XmlNamespace + "property");
            if (!string.IsNullOrEmpty(name))
            {
                property.SetAttributeValue("name", name);
            }

            if (!string.IsNullOrEmpty(fullname))
            {
                property.SetAttributeValue("fullname", fullname);
            }

            property.SetAttributeValue("type", "object");
            property.SetAttributeValue("classname", ClassName);
            property.SetAttributeValue("children", "0");
            return property;
        }

        /// <summary>
        /// Replace a raw argument property with an opaque wrapper in place. Name and
        /// fullname are kept; children and text are discarded so the secret cannot remain
        /// in the tree. For E.2 when reconstructing args without PHP's wrapper already present.
        /// </summary>
        public static void ReplaceWithOpaqueWrapper(XElement property)
        {
            ArgumentNullException.ThrowIfNull(property);
            DbgpXml.SetAttr(property, "type", "object");
            DbgpXml.SetAttr(property, "classname", ClassName);
            HideChildren(property);
        }

        /// <summary>
        /// Redact a proxy-synthesized or rewritten frame argument. Prefers PHP's wrapper
        /// when present. Without a wrapper, wrap opaque when the parameter is marked
        /// sensitive or when compile/sourcemap metadata is missing
        /// (<paramref name="parameterIsSensitive"/> is <c>null</c>). Known-not-sensitive
        /// arguments are left unchanged. Never copies a raw secret onto a new node.
        /// </summary>
        public static void ApplyToSynthesizedArgument(XElement property, bool? parameterIsSensitive)
        {
            ArgumentNullException.ThrowIfNull(property);
            if (MustNotUnwrap(property))
            {
                TryPreserveWrappedProperty(property);
                return;
            }

            if (parameterIsSensitive == false)
            {
                return;
            }

            ReplaceWithOpaqueWrapper(property);
        }

        /// <summary>
        /// Preserve every <c>\SensitiveParameterValue</c> wrapper in <paramref name="root"/>
        /// (including <paramref name="root"/> itself when it is a property). Inner
        /// <c>$value</c> children are hidden. Non-wrappers are left unchanged.
        /// </summary>
        public static void RedactPropertyTree(XElement root)
        {
            ArgumentNullException.ThrowIfNull(root);
            if (IsPropertyElement(root))
            {
                TryPreserveWrappedProperty(root);
            }

            // Materialize before mutating, and isolate each property: one malformed
            // property must not abort the walk and leave a later sibling's
            // \SensitiveParameterValue wrapper unredacted (fail closed).
            foreach (XElement property in DbgpXml.ElementsByLocalName(root, "property").ToList())
            {
                try
                {
                    TryPreserveWrappedProperty(property);
                }
                catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
                {
                    // Fail closed on the unexpected shape: hide the property outright rather
                    // than leaving a possibly-sensitive value un-redacted.
                    try
                    {
                        HideChildren(property);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static bool IsPropertyElement(XElement element) =>
            string.Equals(element.Name.LocalName, "property", StringComparison.Ordinal);

        private static void HideChildren(XElement property)
        {
            DbgpXml.SetAttr(property, "children", "0");
            property.Attribute("numchildren")?.Remove();
            property.Attribute(DbgpConstants.XmlNamespace + "numchildren")?.Remove();
            property.RemoveNodes();
        }
    }
}
