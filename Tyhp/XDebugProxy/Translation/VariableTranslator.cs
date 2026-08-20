using System.Xml.Linq;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.XDebugProxy.Translation
{
    /// <summary>
    /// Translates variable <c>&lt;property&gt;</c> trees in <c>context_get</c> /
    /// <c>property_get</c> / <c>property_value</c> responses.
    /// </summary>
    public sealed class VariableTranslator
    {
        private const string DecimalClassName = "Tyhp\\Decimal";

        private readonly SourceMapStore _store;
        private readonly PathMapper _pathMapper;
        private readonly Action<string>? _onWarning;

        internal VariableTranslator(
            SourceMapStore store,
            PathMapper pathMapper,
            Action<string>? onWarning)
        {
            this._store = store;
            this._pathMapper = pathMapper;
            this._onWarning = onWarning;
        }

        /// <summary>
        /// Reverse-map property filename/lineno, surface Decimal <c>$value</c> as the primary
        /// display, and conservatively rewrite Tyhp struct array keys. Ordinary PHP arrays are
        /// left unchanged.
        /// </summary>
        public void TranslateResponse(DbgpResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);

            try
            {
                foreach (XElement property in DbgpXml.ElementsByLocalName(response.RootElement, "property"))
                {
                    this.TranslateProperty(property);
                }
            }
            catch (Exception ex) when (ex is not ArgumentNullException and not ObjectDisposedException)
            {
                this.Warn($"Variable translation failed: {ex.Message}");
            }
        }

        private void TranslateProperty(XElement property)
        {
            string? filename = DbgpXml.GetAttr(property, "filename");
            string? lineno = DbgpXml.GetAttr(property, "lineno");
            string? name = DbgpXml.GetAttr(property, "name");
            string? phpPathOrUri = filename;
            string? mappedName = null;

            if (!string.IsNullOrWhiteSpace(filename)
                && int.TryParse(lineno, out int dbgpLine)
                && dbgpLine >= 1
                && this._pathMapper.TryMapPhpToTyhp(
                    this._store,
                    filename,
                    dbgpLine,
                    out string tyhpPathOrUri,
                    out int tyhpDbgpLine,
                    out mappedName))
            {
                DbgpXml.SetAttr(property, "filename", tyhpPathOrUri);
                DbgpXml.SetAttr(property, "lineno", tyhpDbgpLine.ToString());
            }

            this.SurfaceDecimalDisplay(property);
            this.RewriteStructKeysIfConservative(property, phpPathOrUri);
            this.ApplySimpleNameLookup(property, name, mappedName, phpPathOrUri);

            // PLACEHOLDER: extension-method $this renaming needs reliable detection of the
            // synthetic first parameter (emitter rewrite of $value->ext() to
            // ExtensionClass::ext($value)). Do not rename ordinary parameters.
        }

        /// <summary>
        /// When <c>classname</c> is <c>Tyhp\Decimal</c>, copy the inner <c>$value</c> string
        /// onto the property as a scalar display. Unexpected shapes are left unchanged.
        /// </summary>
        private void SurfaceDecimalDisplay(XElement property)
        {
            if (!IsDecimalClass(DbgpXml.GetAttr(property, "classname")))
            {
                return;
            }

            try
            {
                XElement? valueChild = FindNamedChild(property, "value");
                if (valueChild is null)
                {
                    return;
                }

                string display = valueChild.Value;
                if (string.IsNullOrEmpty(display))
                {
                    return;
                }

                DbgpXml.SetAttr(property, "type", "string");
                DbgpXml.SetAttr(property, "children", "0");
                property.Attribute("numchildren")?.Remove();
                property.RemoveNodes();
                property.Add(new XCData(display));
            }
            catch
            {
            }
        }

        /// <summary>
        /// Rewrite array keys to sourcemap <c>names</c> only when the property looks like a Tyhp
        /// struct (classname/facet/fullname) or the child keys exactly match that file's
        /// <c>names</c>. Numeric / ordinary PHP arrays are not rewritten.
        /// </summary>
        private void RewriteStructKeysIfConservative(XElement property, string? phpPathOrUri)
        {
            string? type = DbgpXml.GetAttr(property, "type");
            if (!string.Equals(type, "array", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "hash", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            List<XElement> children = DirectPropertyChildren(property);
            if (children.Count == 0)
            {
                return;
            }

            SourceMapFile? map = this.MapForProperty(property, phpPathOrUri);
            HashSet<string> names = ToNameSet(map?.Names);
            List<string> childKeys = [.. children.Select(ChildKeyName).Where(k => k.Length > 0)];
            if (childKeys.Count == 0)
            {
                return;
            }

            bool looksLikeStruct = SuggestsTyhpStruct(property);
            bool keysMatchNames = names.Count > 0 && names.SetEquals(childKeys);
            if (!looksLikeStruct && !keysMatchNames)
            {
                return;
            }

            if (names.Count == 0)
            {
                return;
            }

            // PLACEHOLDER: sourcemap `names` is not a per-struct field list, so
            // index→property rename is unsafe without more metadata (Phase 6 review).
            foreach (XElement child in children)
            {
                string key = ChildKeyName(child);
                if (key.Length == 0)
                {
                    continue;
                }

                if (names.Contains(key))
                {
                    continue;
                }

                // Compiled PHP name unknown — leave the token unchanged.
            }
        }

        private SourceMapFile? MapForProperty(XElement property, string? phpPathOrUri)
        {
            if (!string.IsNullOrWhiteSpace(phpPathOrUri) && !this._pathMapper.IsDbgpUri(phpPathOrUri))
            {
                SourceMapFile? phpMap = this._pathMapper.GetMapForPhp(this._store, phpPathOrUri);
                if (phpMap is not null)
                {
                    return phpMap;
                }

                IReadOnlyList<SourceMapFile> byTyhp = this._store.GetMapForTyhpFile(
                    this._pathMapper.ToFileSystemPath(phpPathOrUri));
                if (byTyhp.Count > 0)
                {
                    return byTyhp[0];
                }
            }

            string? filename = DbgpXml.GetAttr(property, "filename");
            if (string.IsNullOrWhiteSpace(filename) || this._pathMapper.IsDbgpUri(filename))
            {
                return null;
            }

            return this._pathMapper.GetMapForPhp(this._store, filename)
                ?? (this._store.GetMapForTyhpFile(this._pathMapper.ToFileSystemPath(filename)).Count > 0
                    ? this._store.GetMapForTyhpFile(this._pathMapper.ToFileSystemPath(filename))[0]
                    : null);
        }

        private void ApplySimpleNameLookup(
            XElement property,
            string? propertyName,
            string? mappedName,
            string? phpPathOrUri)
        {
            if (!string.IsNullOrEmpty(mappedName)
                && string.IsNullOrEmpty(propertyName))
            {
                DbgpXml.SetAttr(property, "name", mappedName);
                return;
            }

            if (string.IsNullOrEmpty(propertyName) || string.IsNullOrWhiteSpace(phpPathOrUri))
            {
                return;
            }

            SourceMapFile? map = this._pathMapper.GetMapForPhp(this._store, phpPathOrUri);
            if (map is null)
            {
                return;
            }

            foreach (string originalName in map.Names)
            {
                if (string.Equals(originalName, propertyName, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        private static bool IsDecimalClass(string? classname)
        {
            if (string.IsNullOrWhiteSpace(classname))
            {
                return false;
            }

            string trimmed = classname.Trim().TrimStart('\\');
            return string.Equals(trimmed, DecimalClassName, StringComparison.Ordinal);
        }

        private static bool SuggestsTyhpStruct(XElement property)
        {
            return ContainsStructHint(DbgpXml.GetAttr(property, "classname"))
                || ContainsStructHint(DbgpXml.GetAttr(property, "facet"))
                || ContainsStructHint(DbgpXml.GetAttr(property, "fullname"));
        }

        private static bool ContainsStructHint(string? value)
        {
            return !string.IsNullOrEmpty(value)
                && value.Contains("struct", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> ToNameSet(IReadOnlyList<string>? names)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (names is null)
            {
                return set;
            }

            foreach (string name in names)
            {
                string key = name.Trim().TrimStart('$');
                if (key.Length > 0)
                {
                    set.Add(key);
                }
            }

            return set;
        }

        private static List<XElement> DirectPropertyChildren(XElement property)
        {
            var children = new List<XElement>();
            foreach (XElement child in property.Elements())
            {
                if (string.Equals(child.Name.LocalName, "property", StringComparison.Ordinal))
                {
                    children.Add(child);
                }
            }

            return children;
        }

        private static XElement? FindNamedChild(XElement property, string name)
        {
            foreach (XElement child in DirectPropertyChildren(property))
            {
                string key = ChildKeyName(child);
                if (string.Equals(key, name, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private static string ChildKeyName(XElement child)
        {
            string? name = DbgpXml.GetAttr(child, "name");
            return string.IsNullOrEmpty(name) ? string.Empty : name.TrimStart('$');
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
