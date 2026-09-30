using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Downloads the php.net HTML manual and assembles tyhpdef <c>/** */</c> comments.
    /// Port of <c>tools/genTyhpdef.php</c> <c>loadDocs</c> / <c>phpDocHtmlNodeToMarkdown</c>
    /// (that PHP file is not executed).
    /// </summary>
    public class PhpManualDocExtractor
    {
        private readonly IPhpRuntimeTransport _transport;
        private HtmlDocument? _document;
        private string _locale = "en";
        private bool _loadAttempted;
        private bool _loadFailed;

        public PhpManualDocExtractor()
            : this(new PhpRuntimeTransport())
        {
        }

        internal PhpManualDocExtractor(IPhpRuntimeTransport transport)
        {
            this._transport = transport;
        }

        /// <summary>Override the manual cache root. Default: LocalApplicationData/Tyhp/php-manuals.</summary>
        public string? CacheDirectory { get; init; }

        public bool ManualLoaded => this._document is not null && !this._loadFailed;

        public void LoadHtml(string html, string locale)
        {
            this._locale = locale;
            this._document = ParseHtml(html);
            this._loadAttempted = true;
            this._loadFailed = this._document is null;
        }

        public bool TryEnsureManual(
            string locale,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            if (this._loadAttempted && this._locale == locale)
            {
                return this.ManualLoaded;
            }

            this._locale = locale;
            this._loadAttempted = true;
            try
            {
                var html = this.LoadManualHtml(locale, diagnostics, cancellationToken);
                if (html is null)
                {
                    this._loadFailed = true;
                    return false;
                }

                this._document = ParseHtml(html);
                this._loadFailed = this._document is null;
                return this.ManualLoaded;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException)
            {
                this._loadFailed = true;
                Message.Warn("CLI_TyhpdefManualUnavailable", locale, ex.Message);
                return false;
            }
        }

        public void Attach(
            TyhpdefFile file,
            string locale,
            string phpVersion,
            string extensionName,
            string? extensionVersion,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(file);
            var phpNetLocale = PhpManualLocale.Normalize(locale, out var fellBack);
            if (fellBack)
            {
                Message.Warn("CLI_TyhpdefUnknownLocale", locale ?? "", "en");
            }

            this.TryEnsureManual(phpNetLocale, diagnostics, cancellationToken);
            var generated = FormatGenerated(phpVersion, extensionName, extensionVersion);

            foreach (var constant in file.GlobalConstants ?? [])
            {
                constant.DocComment = this.ResolveComment("constant", constant.Name, null, constant.DocComment, generated);
            }

            foreach (var function in file.GlobalFunctions ?? [])
            {
                function.DocComment = this.ResolveComment("function", function.Name, null, function.DocComment, generated);
            }

            foreach (var type in file.GlobalTypes ?? [])
            {
                this.AttachType(type, "", generated);
            }

            foreach (var ns in file.Namespaces ?? [])
            {
                foreach (var constant in ns.Constants ?? [])
                {
                    constant.DocComment = this.ResolveComment(
                        "constant",
                        ns.Name + "\\" + constant.Name,
                        null,
                        constant.DocComment,
                        generated);
                }

                foreach (var function in ns.Functions ?? [])
                {
                    function.DocComment = this.ResolveComment(
                        "function",
                        ns.Name + "\\" + function.Name,
                        null,
                        function.DocComment,
                        generated);
                }

                foreach (var type in ns.Classes ?? [])
                {
                    this.AttachType(type, ns.Name, generated);
                }
            }
        }

        internal string? LookupAssembledComment(string kind, string name, string? member, string generated)
            => this.ResolveComment(kind, name, member, reflectionComment: null, generated);

        private void AttachType(TyhpdefClassDeclaration type, string ns, string generated)
        {
            var className = string.IsNullOrWhiteSpace(ns) ? type.Name : ns + "\\" + type.Name;
            type.DocComment = this.ResolveComment("class", className, null, type.DocComment, generated);
            foreach (var constant in type.Constants ?? [])
            {
                constant.DocComment = this.ResolveComment(
                    "class-constant",
                    className,
                    constant.Name,
                    constant.DocComment,
                    generated);
            }

            foreach (var property in type.Properties ?? [])
            {
                property.DocComment = this.ResolveComment(
                    "property",
                    className,
                    property.Name,
                    property.DocComment,
                    generated);
            }

            foreach (var method in type.Methods ?? [])
            {
                method.DocComment = this.ResolveComment(
                    "method",
                    className,
                    method.Name,
                    method.DocComment,
                    generated);
            }

            foreach (var enumCase in type.EnumCases ?? [])
            {
                enumCase.DocComment = this.ResolveComment(
                    "class-constant",
                    className,
                    enumCase.Name,
                    enumCase.DocComment,
                    generated);
            }
        }

        private string? ResolveComment(
            string kind,
            string name,
            string? member,
            string? reflectionComment,
            string generated)
        {
            var page = this.TryLoadPage(kind, name, member);
            if (page is not null)
            {
                return AssembleComment(page, generated);
            }

            return AppendGenerated(reflectionComment, generated);
        }

        private ManualPage? TryLoadPage(string kind, string name, string? member)
        {
            if (this._document is null)
            {
                return null;
            }

            var id = kind switch
            {
                "function" => "function." + ToId(name),
                "class" => "class." + ToId(name),
                "method" => ToId(name) + "." + ToId(member ?? ""),
                "property" => ToId(name) + ".props." + ToId(member ?? ""),
                "class-constant" => ToId(name) + ".constants." + ToId(member ?? ""),
                "constant" => "constant." + ToId(name),
                _ => "function." + ToId(name),
            };

            var node = this._document.GetElementbyId(id);
            if (node is null)
            {
                return null;
            }

            return ExtractPage(node, id, kind, this._locale);
        }

        private string? LoadManualHtml(string locale, DiagnosticBag diagnostics, CancellationToken cancellationToken)
        {
            _ = diagnostics;
            var cacheDir = this.CacheDirectory
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Tyhp",
                    "php-manuals",
                    locale);
            Directory.CreateDirectory(cacheDir);
            var gzPath = Path.Combine(cacheDir, PhpManualLocale.ManualFileName(locale));
            if (!File.Exists(gzPath) || new FileInfo(gzPath).Length == 0)
            {
                var url = PhpManualLocale.ManualUrl(locale);
                var ok = this._transport.DownloadToFileAsync(
                    new Uri(url),
                    gzPath,
                    expectedSha256: null,
                    cancellationToken).GetAwaiter().GetResult();
                if (!ok)
                {
                    Message.Warn("CLI_TyhpdefManualUnavailable", locale, url);
                    return null;
                }
            }

            return DecompressGz(gzPath);
        }

        internal static string DecompressGz(string gzPath)
        {
            using var file = File.OpenRead(gzPath);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        internal static HtmlDocument ParseHtml(string html)
        {
            var document = new HtmlDocument
            {
                OptionFixNestedTags = true,
                OptionCheckSyntax = false,
            };
            document.LoadHtml(html);
            return document;
        }

        internal static string ToId(string name)
        {
            var trimmed = name.Trim().TrimStart('\\');
            var last = trimmed.LastIndexOf('\\');
            if (last >= 0)
            {
                trimmed = trimmed[(last + 1)..];
            }

            return trimmed.Replace('_', '-').ToLowerInvariant();
        }

        internal static string FormatGenerated(string phpVersion, string extensionName, string? extensionVersion)
        {
            var ext = string.IsNullOrWhiteSpace(extensionVersion)
                ? extensionName
                : extensionName + " v" + extensionVersion;
            return "@generated from PHP v" + phpVersion + ", EXT: " + ext;
        }

        private static string? AppendGenerated(string? reflectionComment, string generated)
        {
            if (string.IsNullOrWhiteSpace(reflectionComment))
            {
                return null;
            }

            var trimmed = reflectionComment.Trim();
            if (trimmed.Contains("@generated", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (trimmed.EndsWith("*/", StringComparison.Ordinal))
            {
                var inner = trimmed[..^2].TrimEnd();
                return inner + "\n * \n * " + generated + "\n */";
            }

            return "/**\n * " + trimmed + "\n * \n * " + generated + "\n */";
        }

        private static string AssembleComment(ManualPage page, string generated)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(page.Overview))
            {
                lines.Add(page.Overview);
                lines.Add("");
            }

            if (!string.IsNullOrWhiteSpace(page.Description))
            {
                lines.Add(page.Description);
                lines.Add("");
            }

            foreach (var note in page.Notes)
            {
                lines.Add(note);
                lines.Add("");
            }

            foreach (var warning in page.Warnings)
            {
                lines.Add(warning);
                lines.Add("");
            }

            foreach (var deprecated in page.Deprecated)
            {
                lines.Add("@deprecated " + deprecated);
                lines.Add("");
            }

            foreach (var (paramName, text) in page.Params)
            {
                lines.Add("@param $" + paramName.TrimStart('$') + " " + text);
            }

            if (!string.IsNullOrWhiteSpace(page.Return))
            {
                lines.Add("@return " + page.Return);
            }

            foreach (var throws in page.Throws)
            {
                lines.Add("@throws " + throws);
            }

            if (!string.IsNullOrWhiteSpace(page.Link))
            {
                if (lines.Count > 0 && lines[^1] != "")
                {
                    lines.Add("");
                }

                lines.Add("@link " + page.Link);
            }

            lines.Add("");
            lines.Add(generated);

            var sb = new StringBuilder();
            sb.AppendLine("/**");
            foreach (var line in lines)
            {
                if (line.Length == 0)
                {
                    sb.AppendLine(" *");
                }
                else
                {
                    sb.AppendLine(" * " + line);
                }
            }

            sb.Append(" */");
            return sb.ToString();
        }

        /// <summary>
        /// php.net's all-in-one HTML nests every method <c>refentry</c> inside the class
        /// <c>reference</c> node (e.g. <c>id="class.imagick"</c>). Descendant queries must not
        /// walk into a <em>different</em> refentry, or member <c>@param</c> / <c>@deprecated</c>
        /// is concatenated onto the class.
        /// </summary>
        private static string OutsideOtherRefentries(string id)
            => "not(ancestor::div[contains(@class,'refentry')][not(@id='" + id.Replace("'", "") + "')])";

        private static bool PageHasCallSignature(string kind)
            => kind is "function" or "method";

        private static ManualPage ExtractPage(HtmlNode node, string id, string kind, string locale)
        {
            var page = new ManualPage
            {
                Link = PhpManualLocale.ManualPageLink(locale, id),
            };
            var own = OutsideOtherRefentries(id);

            page.Overview = NormalizeText(HtmlToMarkdown(SelectFirst(
                node,
                $".//div[contains(@class,'refnamediv') and {own}]//span[contains(@class,'dc-title')]")));
            if (page.Overview.Length == 0 && kind == "class")
            {
                page.Overview = NormalizeText(HtmlToMarkdown(SelectFirst(
                    node,
                    $".//h1[contains(@class,'title') and {own}]")));
            }

            page.Description = NormalizeText(HtmlToMarkdown(SelectFirst(
                node,
                $".//div[@id='refsect1-{id}-description']//p[contains(@class,'rdfs-comment')]")));
            if (page.Description.Length == 0 && kind == "class")
            {
                page.Description = CollectClassDescription(node, own);
            }

            foreach (var note in node.SelectNodes($".//div[@id='refsect1-{id}-description']//blockquote[contains(@class,'note')]") ?? Enumerable.Empty<HtmlNode>())
            {
                var text = NormalizeText(HtmlToMarkdown(note));
                if (text.Length > 0)
                {
                    page.Notes.Add(text);
                }
            }

            foreach (var caution in node.SelectNodes($".//div[@id='refsect1-{id}-description']//div[contains(@class,'caution')]") ?? Enumerable.Empty<HtmlNode>())
            {
                var text = NormalizeText(HtmlToMarkdown(caution));
                if (text.Length > 0)
                {
                    page.Warnings.Add(text);
                }
            }

            // Class constants are documented as <dl><dt>/<dd> pairs directly in the class page
            // (they have no refentry of their own), so `own` alone does not scope out a constant's
            // deprecation notice; excluding `dl` ancestors keeps per-constant warnings off the class.
            foreach (var warning in node.SelectNodes($".//div[contains(@class,'warning') and {own} and not(ancestor::dl)]") ?? Enumerable.Empty<HtmlNode>())
            {
                var text = NormalizeText(HtmlToMarkdown(warning));
                if (text.Length == 0)
                {
                    continue;
                }

                if (text.Contains("deprecat", StringComparison.OrdinalIgnoreCase))
                {
                    page.Deprecated.Add(text);
                }
                else if (kind == "class")
                {
                    page.Warnings.Add(text);
                }
            }

            if (PageHasCallSignature(kind))
            {
                ExtractParams(node, id, page);
                var returnNode = SelectFirst(node, $".//div[@id='refsect1-{id}-returnvalues']//p[contains(@class,'para')]");
                if (returnNode is not null)
                {
                    page.Return = NormalizeText(HtmlToMarkdown(returnNode));
                }

                foreach (var errors in node.SelectNodes($".//div[@id='refsect1-{id}-errors']//p[contains(@class,'para')]") ?? Enumerable.Empty<HtmlNode>())
                {
                    var text = NormalizeText(HtmlToMarkdown(errors));
                    if (text.Length > 0)
                    {
                        page.Throws.Add(text);
                    }
                }
            }

            return page;
        }

        private static string CollectClassDescription(HtmlNode node, string own)
        {
            var parts = new List<string>();

            // `not(ancestor::dl)` excludes per-constant descriptions: php.net documents class
            // constants as <dl><dt>/<dd> pairs inline in the partintro (no refentry of their own),
            // so without this every constant's blurb would be concatenated onto the class summary.
            var xpath = ".//div[contains(@class,'partintro')]//p[contains(@class,'para') and "
                + own
                + " and not(ancestor::div[contains(@class,'classsynopsis')])"
                + " and not(ancestor::table) and not(ancestor::dl)]";
            foreach (var para in node.SelectNodes(xpath) ?? Enumerable.Empty<HtmlNode>())
            {
                var text = NormalizeText(HtmlToMarkdown(para));
                if (text.Length > 0)
                {
                    parts.Add(text);
                }
            }

            return string.Join(" ", parts);
        }

        private static void ExtractParams(HtmlNode node, string id, ManualPage page)
        {
            var container = SelectFirst(node, $".//div[@id='refsect1-{id}-parameters']");
            if (container is null)
            {
                return;
            }

            foreach (var dl in container.SelectNodes(".//dl") ?? Enumerable.Empty<HtmlNode>())
            {
                string? key = null;
                foreach (var child in dl.ChildNodes)
                {
                    if (child.Name == "dt")
                    {
                        key = NormalizeText(HtmlToMarkdown(child)).Trim('`', '$', ' ');
                    }
                    else if (child.Name == "dd" && !string.IsNullOrWhiteSpace(key))
                    {
                        page.Params[key] = NormalizeText(HtmlToMarkdown(child));
                    }
                }
            }
        }

        private static HtmlNode? SelectFirst(HtmlNode node, string xpath)
        {
            try
            {
                return node.SelectSingleNode(xpath);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return null;
            }
        }

        internal static string HtmlFragmentToMarkdown(string html)
        {
            var document = ParseHtml(html);
            return HtmlToMarkdown(document.DocumentNode.SelectSingleNode("//p") ?? document.DocumentNode);
        }

        internal static string HtmlToMarkdown(HtmlNode? node)
        {
            if (node is null)
            {
                return "";
            }

            return NormalizeText(HtmlNodeToMarkdown(node, normalizeTextNodes: true, skipCodeTagFormatting: false, inCodeBlock: false));
        }

        internal static string HtmlNodeToMarkdown(
            HtmlNode node,
            bool normalizeTextNodes,
            bool skipCodeTagFormatting,
            bool inCodeBlock)
        {
            var result = " ";
            var closeText = "";
            var className = node.GetAttributeValue("class", "");

            if (node.Name == "code"
                && !skipCodeTagFormatting
                && className.Contains("parameter", StringComparison.Ordinal)
                && !inCodeBlock)
            {
                result += "`$";
                closeText = "`";
                inCodeBlock = true;
            }
            else if (((node.Name == "span" && className.Contains("function", StringComparison.Ordinal))
                      || (node.Name == "code" && !skipCodeTagFormatting))
                     && !inCodeBlock)
            {
                result += "`";
                closeText = "`";
                inCodeBlock = true;
            }
            else if (node.Name == "div" && className.Contains("phpcode", StringComparison.Ordinal) && !inCodeBlock)
            {
                result += "```php\n";
                closeText = "\n```";
                normalizeTextNodes = false;
                skipCodeTagFormatting = true;
                inCodeBlock = true;
            }
            else if (node.Name == "div" && className.Contains("screen", StringComparison.Ordinal) && !inCodeBlock)
            {
                result += "```plaintext\n";
                closeText = "\n```";
                normalizeTextNodes = false;
                skipCodeTagFormatting = true;
                inCodeBlock = true;
            }
            else if (node.Name == "pre" && !inCodeBlock)
            {
                result += "```plaintext\n";
                closeText = "\n```";
                normalizeTextNodes = false;
                skipCodeTagFormatting = true;
                inCodeBlock = true;
            }
            else if (node.Name == "strong" && !inCodeBlock)
            {
                result += "**";
                closeText = "**";
            }
            else if (node.Name == "em" && !inCodeBlock)
            {
                result += "*";
                closeText = "*";
            }
            else if (node.Name is "br")
            {
                result += "\n";
            }
            else if (node.NodeType == HtmlNodeType.Text)
            {
                var text = WebUtility.HtmlDecode(node.InnerText ?? "");
                result += normalizeTextNodes ? NormalizeText(text) : text.Replace("&nbsp;", " ", StringComparison.Ordinal);
            }

            var first = true;
            foreach (var child in node.ChildNodes)
            {
                var childResult = HtmlNodeToMarkdown(child, normalizeTextNodes, skipCodeTagFormatting, inCodeBlock);
                if (first)
                {
                    childResult = childResult.TrimStart();
                    first = false;
                }

                result += childResult;
            }

            if (closeText.Length > 0)
            {
                result = result.TrimEnd();
                closeText += " ";
            }

            result = result.Replace("*/", "* /", StringComparison.Ordinal);
            return result + closeText;
        }

        internal static string NormalizeText(string text)
        {
            text = text.Replace("&nbsp;", " ", StringComparison.Ordinal);
            text = text.Trim();
            text = text.Replace("\n", " ", StringComparison.Ordinal)
                .Replace("\r", "", StringComparison.Ordinal)
                .Replace("\t", " ", StringComparison.Ordinal);
            while (text.Contains("  ", StringComparison.Ordinal))
            {
                text = text.Replace("  ", " ", StringComparison.Ordinal);
            }

            text = text
                .Replace(" .", ".", StringComparison.Ordinal)
                .Replace(" !", "!", StringComparison.Ordinal)
                .Replace(" ?", "?", StringComparison.Ordinal)
                .Replace("( ", "(", StringComparison.Ordinal)
                .Replace(" )", ")", StringComparison.Ordinal)
                .Replace("[ ", "[", StringComparison.Ordinal)
                .Replace(" ]", "]", StringComparison.Ordinal)
                .Replace("{ ", "{", StringComparison.Ordinal)
                .Replace(" }", "}", StringComparison.Ordinal);
            return WebUtility.HtmlDecode(text);
        }

        private sealed class ManualPage
        {
            public string Overview { get; set; } = "";

            public string Description { get; set; } = "";

            public List<string> Notes { get; } = [];

            public List<string> Warnings { get; } = [];

            public List<string> Deprecated { get; } = [];

            public Dictionary<string, string> Params { get; } = new(StringComparer.Ordinal);

            public string Return { get; set; } = "";

            public List<string> Throws { get; } = [];

            public string Link { get; set; } = "";
        }
    }
}
