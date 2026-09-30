using System.Text;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Read-only Layer 2 audit: compare committed Layer 1 / Layer 2 tyhpdefs against
    /// Psalm, PHPStan, PhpStorm, and Phan stubs. Writes a markdown report; does not
    /// regenerate overlays.
    /// </summary>
    public sealed class StubHarvestAuditService
    {
        private static readonly string[] CorpusIds =
        [
            StubCorpusCache.PsalmId,
            StubCorpusCache.PhpStanId,
            StubCorpusCache.PhpStormId,
            StubCorpusCache.PhanId,
        ];

        private readonly StubCorpusCache _cache;

        public StubHarvestAuditService()
            : this(new StubCorpusCache())
        {
        }

        internal StubHarvestAuditService(StubCorpusCache cache)
        {
            this._cache = cache;
        }

        public void Audit(
            string path,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            bool quiet,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(result);

            if (string.IsNullOrWhiteSpace(path))
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefAuditStubsPathNotFound", path ?? ""));
                Message.Error("CLI_TyhpdefAuditStubsPathNotFound", path ?? "");
                return;
            }

            string root;
            try
            {
                root = ResolveTyhpdefRoot(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    path,
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefAuditStubsPathNotFound", path));
                Message.Error("CLI_TyhpdefAuditStubsPathNotFound", path);
                Message.Debug(ex.Message);
                return;
            }

            if (!Directory.Exists(root))
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    root,
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefAuditStubsPathNotFound", path));
                Message.Error("CLI_TyhpdefAuditStubsPathNotFound", path);
                return;
            }

            var cacheOptions = options with { RequireStubs = true };
            var cacheDir = this._cache.Resolve(cacheOptions, result.Diagnostics, cancellationToken);
            if (cacheDir is null)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var corpusDirs = StubCorpusCache.CorpusDirectories(cacheDir);
            var stubIndex = StubHarvestTyhpdefEnricher.IndexCorpora(corpusDirs);

            var layer1Files = DiscoverLayer1(root);
            var layer2Files = DiscoverLayer2(root);
            var layer1 = StubAuditTyhpdefCatalog.ReadFiles(layer1Files, result.Diagnostics);
            var layer2 = StubAuditTyhpdefCatalog.ReadFiles(layer2Files, result.Diagnostics);
            if (result.Diagnostics.HasErrors)
            {
                return;
            }

            var facts = CollectFacts(layer1, layer2, stubIndex);
            var report = FormatReport(root, cacheDir, layer1Files.Count, layer2Files.Count, facts);
            result.AuditReport = report;

            var outPath = options.AuditOutPath;
            if (!string.IsNullOrWhiteSpace(outPath))
            {
                var fullOut = Path.GetFullPath(outPath);
                try
                {
                    var directory = Path.GetDirectoryName(fullOut);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(fullOut, report);
                    if (!quiet)
                    {
                        Message.Display("CLI_TyhpdefAuditStubsWrote", fullOut);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefOutputWriteError,
                        fullOut,
                        0,
                        0,
                        fullOut,
                        ex.Message);
                    return;
                }
            }
            else
            {
                Console.Out.Write(report);
                if (!report.EndsWith('\n'))
                {
                    Console.Out.WriteLine();
                }
            }

            if (!quiet)
            {
                Message.Display(
                    "CLI_TyhpdefAuditStubsSummary",
                    facts.Count,
                    facts.Count(f => f.Bucket == StubAuditBucket.Agree),
                    facts.Count(f => f.Bucket != StubAuditBucket.Agree));
            }
        }

        internal static List<StubAuditFact> CollectFacts(
            StubAuditIndex layer1,
            StubAuditIndex layer2,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> stubIndex)
        {
            var facts = new List<StubAuditFact>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (fqn, type) in layer1.Types.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                seen.Add(fqn);
                layer2.Types.TryGetValue(fqn, out var overlay);
                AddTypeFacts(facts, fqn, type, overlay, stubIndex);
            }

            foreach (var (fqn, overlay) in layer2.Types)
            {
                if (seen.Add(fqn) && !layer1.Types.ContainsKey(fqn))
                {
                    AddTypeFacts(facts, fqn, layer1Type: null, overlay, stubIndex);
                }
            }

            seen.Clear();
            foreach (var (fqn, function) in layer1.Functions.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                seen.Add(fqn);
                layer2.Functions.TryGetValue(fqn, out var overlay);
                AddCallableFacts(
                    facts,
                    fqn,
                    "function",
                    function,
                    overlay,
                    stubIndex,
                    hostAliases: null);
            }

            foreach (var (fqn, overlay) in layer2.Functions)
            {
                if (seen.Add(fqn) && !layer1.Functions.ContainsKey(fqn))
                {
                    AddCallableFacts(
                        facts,
                        fqn,
                        "function",
                        layer1Callable: null,
                        overlay,
                        stubIndex,
                        hostAliases: null);
                }
            }

            return facts;
        }

        private static void AddTypeFacts(
            List<StubAuditFact> facts,
            string fqn,
            StubAuditType? layer1Type,
            StubAuditType? overlay,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> stubIndex)
        {
            var typeMatches = StubHarvestTyhpdefEnricher.Collect(stubIndex, fqn);
            var ir = ToIr(layer1Type ?? overlay ?? new StubAuditType { Fqn = fqn });
            var plan = StubHarvestTyhpdefEnricher.ComputeTypePlan(ir, fqn, stubIndex);
            var harvestGenerics = StubHarvestTyhpdefEnricher.SpellGenericParameters(plan.Generics);
            var corpusTemplates = CorpusMap(typeMatches, StubHarvestTyhpdefEnricher.SpellTemplateTags);

                AddFact(facts, Classify(
                fqn,
                "generics",
                isTypeHeader: true,
                layer1Type?.Generics ?? "",
                overlay?.Generics ?? "",
                corpusTemplates,
                harvestGenerics,
                typeMatches));

            var methodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (layer1Type is not null)
            {
                foreach (var name in layer1Type.Methods.Keys)
                {
                    methodNames.Add(name);
                }
            }

            if (overlay is not null)
            {
                foreach (var name in overlay.Methods.Keys)
                {
                    methodNames.Add(name);
                }
            }

            foreach (var name in methodNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                StubAuditCallable? layer1Method = null;
                StubAuditCallable? overlayMethod = null;
                layer1Type?.Methods.TryGetValue(name, out layer1Method);
                overlay?.Methods.TryGetValue(name, out overlayMethod);
                AddCallableFacts(
                    facts,
                    fqn + "::" + name,
                    "method",
                    layer1Method,
                    overlayMethod,
                    stubIndex,
                    plan.Aliases);
            }

            var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (layer1Type is not null)
            {
                foreach (var name in layer1Type.Properties.Keys)
                {
                    propertyNames.Add(name);
                }
            }

            if (overlay is not null)
            {
                foreach (var name in overlay.Properties.Keys)
                {
                    propertyNames.Add(name);
                }
            }

            foreach (var name in propertyNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var propFqn = fqn + "::$" + name;
                var matches = StubHarvestTyhpdefEnricher.Collect(stubIndex, propFqn);
                var layer1 = layer1Type is not null && layer1Type.Properties.TryGetValue(name, out var l1)
                    ? l1
                    : "";
                var layer2 = overlay is not null && overlay.Properties.TryGetValue(name, out var l2)
                    ? l2
                    : "";
                var byCorpus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (corpus, symbol) in matches)
                {
                    var type = StubHarvestTyhpdefEnricher.RewriteTemplateAliases(
                        StubHarvestTyhpdefEnricher.EffectivePropertyType(symbol),
                        plan.Aliases);
                    if (!string.IsNullOrWhiteSpace(type))
                    {
                        byCorpus[corpus] = type;
                    }
                }

                var harvest = ConsensusOrEmpty(byCorpus, propFqn);
                    AddFact(facts, Classify(
                    propFqn,
                    "property $" + name,
                    isTypeHeader: false,
                    layer1,
                    layer2,
                    byCorpus,
                    harvest,
                    matches));
            }
        }

        private static void AddCallableFacts(
            List<StubAuditFact> facts,
            string fqn,
            string kind,
            StubAuditCallable? layer1Callable,
            StubAuditCallable? overlay,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> stubIndex,
            IReadOnlyDictionary<string, string>? hostAliases)
        {
            var matches = StubHarvestTyhpdefEnricher.Collect(stubIndex, fqn);
            var unification = StubHarvestTyhpdefEnricher.UnifyTemplates(matches);
            var aliases = MergeAliases(hostAliases, unification.Aliases);

            if (kind == "function")
            {
                var harvestGenerics = StubHarvestTyhpdefEnricher.SpellGenericParameters(unification.Parameters);
                var corpusTemplates = CorpusMap(matches, StubHarvestTyhpdefEnricher.SpellTemplateTags);
                    AddFact(facts, Classify(
                    fqn,
                    "generics",
                    isTypeHeader: false,
                    layer1Callable?.Generics ?? "",
                    overlay?.Generics ?? "",
                    corpusTemplates,
                    harvestGenerics,
                    matches));
            }

            var returnByCorpus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (corpus, symbol) in matches)
            {
                var type = StubHarvestTyhpdefEnricher.RewriteTemplateAliases(
                    StubHarvestTyhpdefEnricher.EffectiveType(symbol, isReturn: true),
                    aliases);
                if (!string.IsNullOrWhiteSpace(type))
                {
                    returnByCorpus[corpus] = type;
                }
            }

                AddFact(facts, Classify(
                fqn,
                "return",
                isTypeHeader: false,
                layer1Callable?.ReturnType ?? "",
                overlay?.ReturnType ?? "",
                returnByCorpus,
                ConsensusOrEmpty(returnByCorpus, fqn),
                matches));

            var paramNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in layer1Callable?.Parameters ?? [])
            {
                paramNames.Add(parameter.Name);
            }

            foreach (var parameter in overlay?.Parameters ?? [])
            {
                paramNames.Add(parameter.Name);
            }

            foreach (var name in paramNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var layer1 = layer1Callable?.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Type ?? "";
                var layer2 = overlay?.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Type ?? "";
                var byCorpus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (corpus, symbol) in matches)
                {
                    var type = StubHarvestTyhpdefEnricher.RewriteTemplateAliases(
                        StubHarvestTyhpdefEnricher.EffectiveParameterType(symbol, name),
                        aliases);
                    if (!string.IsNullOrWhiteSpace(type))
                    {
                        byCorpus[corpus] = type;
                    }
                }

                    AddFact(facts, Classify(
                    fqn,
                    "param $" + name,
                    isTypeHeader: false,
                    layer1,
                    layer2,
                    byCorpus,
                    ConsensusOrEmpty(byCorpus, fqn + "($" + name + ")"),
                    matches));
            }
        }

        private static void AddFact(List<StubAuditFact> facts, StubAuditFact? fact)
        {
            if (fact is not null)
            {
                facts.Add(fact);
            }
        }

        internal static StubAuditFact? Classify(
            string fqn,
            string fact,
            bool isTypeHeader,
            string layer1,
            string layer2,
            IReadOnlyDictionary<string, string> corpus,
            string harvest,
            IReadOnlyDictionary<string, StubSymbol> matches)
        {
            if (!isTypeHeader
                && layer1.Length > 0
                && !StubHarvestTyhpdefEnricher.IsWeakType(layer1))
            {
                harvest = "";
            }

            var bucket = ClassifyBucket(isTypeHeader, layer1, layer2, corpus, harvest, matches);
            if (bucket == StubAuditBucket.Agree
                && matches.Count == 0
                && SameSignature(layer1, layer2)
                && string.IsNullOrWhiteSpace(harvest)
                && string.IsNullOrWhiteSpace(layer2))
            {
                return null;
            }

            return new StubAuditFact(
                fqn,
                fact,
                isTypeHeader,
                layer1,
                layer2,
                CopyCorpus(corpus),
                harvest,
                bucket);
        }

        internal static StubAuditBucket ClassifyBucket(
            bool isTypeHeader,
            string layer1,
            string layer2,
            IReadOnlyDictionary<string, string> corpus,
            string harvest,
            IReadOnlyDictionary<string, StubSymbol> matches)
        {
            if (isTypeHeader && HasTemplateOrderMismatch(matches))
            {
                return StubAuditBucket.TemplateOrderMismatch;
            }

            if (isTypeHeader && HasTemplateArityMismatch(matches))
            {
                return StubAuditBucket.TemplateArityMismatch;
            }

            if (PsalmAndPhpStanDisagree(corpus))
            {
                return StubAuditBucket.PsalmPhpStanDisagree;
            }

            var hasStub = matches.Count > 0;
            var hasLayer2 = !string.IsNullOrWhiteSpace(layer2);
            if (hasLayer2 && !hasStub)
            {
                return StubAuditBucket.Layer2NoStub;
            }

            if (hasLayer2
                && !SameSignature(layer2, layer1)
                && !string.IsNullOrWhiteSpace(harvest)
                && !SameSignature(layer2, harvest))
            {
                return StubAuditBucket.FaithfulnessMismatch;
            }

            if (hasLayer2
                && !SameSignature(layer2, layer1)
                && string.IsNullOrWhiteSpace(harvest)
                && !isTypeHeader
                && layer1.Length > 0
                && !StubHarvestTyhpdefEnricher.IsWeakType(layer1))
            {
                return StubAuditBucket.FaithfulnessMismatch;
            }

            var contributing = CountNonWeak(corpus);
            var layer1Open = isTypeHeader
                || layer1.Length == 0
                || StubHarvestTyhpdefEnricher.IsWeakType(layer1);
            if (layer1Open
                && contributing == 1
                && (!string.IsNullOrWhiteSpace(harvest) || hasLayer2)
                && !SameSignature(harvest, layer1))
            {
                return StubAuditBucket.OneCorpus;
            }

            if (layer1Open
                && !string.IsNullOrWhiteSpace(harvest)
                && !SameSignature(harvest, layer1)
                && (!hasLayer2 || SameSignature(layer2, layer1)))
            {
                return StubAuditBucket.NotInLayer2;
            }

            return StubAuditBucket.Agree;
        }

        private static bool HasTemplateArityMismatch(IReadOnlyDictionary<string, StubSymbol> matches)
        {
            var arities = new List<int>();
            foreach (var id in new[] { StubCorpusCache.PsalmId, StubCorpusCache.PhpStanId })
            {
                if (!matches.TryGetValue(id, out var symbol))
                {
                    continue;
                }

                var count = StubHarvestTyhpdefEnricher.TemplateNames(symbol).Count;
                if (count > 0)
                {
                    arities.Add(count);
                }
            }

            return arities.Count >= 2 && arities.Distinct().Count() > 1;
        }

        private static bool HasTemplateOrderMismatch(IReadOnlyDictionary<string, StubSymbol> matches)
        {
            if (!matches.TryGetValue(StubCorpusCache.PsalmId, out var psalm)
                || !matches.TryGetValue(StubCorpusCache.PhpStanId, out var phpstan))
            {
                return false;
            }

            var psalmNames = StubHarvestTyhpdefEnricher.TemplateNames(psalm);
            var phpstanNames = StubHarvestTyhpdefEnricher.TemplateNames(phpstan);
            if (psalmNames.Count == 0
                || psalmNames.Count != phpstanNames.Count
                || psalmNames.SequenceEqual(phpstanNames, StringComparer.Ordinal))
            {
                return false;
            }

            var psalmSet = new HashSet<string>(psalmNames, StringComparer.Ordinal);
            return psalmSet.SetEquals(phpstanNames);
        }

        private static bool PsalmAndPhpStanDisagree(IReadOnlyDictionary<string, string> corpus)
        {
            corpus.TryGetValue(StubCorpusCache.PsalmId, out var psalm);
            corpus.TryGetValue(StubCorpusCache.PhpStanId, out var phpstan);
            if (string.IsNullOrWhiteSpace(psalm) || string.IsNullOrWhiteSpace(phpstan))
            {
                return false;
            }

            return !SameSignature(psalm, phpstan);
        }

        private static int CountNonWeak(IReadOnlyDictionary<string, string> corpus)
        {
            var count = 0;
            foreach (var type in corpus.Values)
            {
                if (!string.IsNullOrWhiteSpace(type) && !StubHarvestTyhpdefEnricher.IsWeakType(type))
                {
                    count++;
                }
            }

            return count;
        }

        internal static bool SameSignature(string? left, string? right)
        {
            var a = Compact(left);
            var b = Compact(right);
            if (a.Length == 0 && b.Length == 0)
            {
                return true;
            }

            if (a.Length == 0 || b.Length == 0)
            {
                return false;
            }

            if (a.StartsWith('<') || b.StartsWith('<'))
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(
                StubHarvestTyhpdefEnricher.CanonicalType(left),
                StubHarvestTyhpdefEnricher.CanonicalType(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string Compact(string? value)
            => (value ?? "").Replace(" ", "", StringComparison.Ordinal).Trim();

        private static string ConsensusOrEmpty(IReadOnlyDictionary<string, string> byCorpus, string fqn)
        {
            var nullable = byCorpus.ToDictionary(
                kv => kv.Key,
                kv => (string?)kv.Value,
                StringComparer.OrdinalIgnoreCase);
            var sink = new DiagnosticBag();
            return StubHarvestTyhpdefEnricher.ConsensusType(nullable, fqn, sink) ?? "";
        }

        private static Dictionary<string, string> CorpusMap(
            IReadOnlyDictionary<string, StubSymbol> matches,
            Func<StubSymbol, string> spell)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (corpus, symbol) in matches)
            {
                var text = spell(symbol);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    map[corpus] = text;
                }
            }

            return map;
        }

        private static Dictionary<string, string> CopyCorpus(IReadOnlyDictionary<string, string> corpus)
        {
            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in CorpusIds)
            {
                if (corpus.TryGetValue(id, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    copy[id] = value;
                }
            }

            foreach (var (key, value) in corpus)
            {
                copy.TryAdd(key, value);
            }

            return copy;
        }

        private static Dictionary<string, string> MergeAliases(
            IReadOnlyDictionary<string, string>? host,
            IReadOnlyDictionary<string, string> local)
        {
            if (host is null || host.Count == 0)
            {
                return local as Dictionary<string, string>
                    ?? new Dictionary<string, string>(local, StringComparer.Ordinal);
            }

            var merged = new Dictionary<string, string>(host, StringComparer.Ordinal);
            foreach (var (alias, canonical) in local)
            {
                merged[alias] = canonical;
            }

            return merged;
        }

        private static TyhpdefClassDeclaration ToIr(StubAuditType type)
        {
            return new TyhpdefClassDeclaration
            {
                Kind = type.Kind,
                Name = SimpleName(type.Fqn),
                Methods = type.Methods.Values
                    .Select(method => new TyhpdefMethod
                    {
                        Name = method.Name,
                        ReturnType = method.ReturnType,
                        Parameters = method.Parameters
                            .Select(p => new TyhpdefParameter { Name = p.Name, Type = p.Type })
                            .ToList(),
                    })
                    .ToList(),
                Properties = type.Properties
                    .Select(kv => new TyhpdefProperty { Name = kv.Key, Type = kv.Value })
                    .ToList(),
            };
        }

        private static string SimpleName(string fqn)
        {
            var trimmed = fqn.Trim().TrimStart('\\');
            var slash = trimmed.LastIndexOf('\\');
            return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        }

        internal static string ResolveTyhpdefRoot(string path)
        {
            var full = Path.GetFullPath(path.Trim());
            if (File.Exists(full))
            {
                full = Path.GetDirectoryName(full) ?? full;
            }

            if (Directory.Exists(Path.Combine(full, "overlays", "stubs")))
            {
                return full;
            }

            var nested = Path.Combine(full, "_tyhpdef");
            if (Directory.Exists(Path.Combine(nested, "overlays", "stubs")) || Directory.Exists(nested))
            {
                return nested;
            }

            if (string.Equals(Path.GetFileName(full), "stubs", StringComparison.OrdinalIgnoreCase))
            {
                var overlays = Path.GetDirectoryName(full);
                var parent = overlays is null ? null : Path.GetDirectoryName(overlays);
                if (parent is not null
                    && string.Equals(Path.GetFileName(overlays), "overlays", StringComparison.OrdinalIgnoreCase))
                {
                    return parent;
                }
            }

            return full;
        }

        internal static List<string> DiscoverLayer1(string root)
        {
            var files = new List<string>();
            if (!Directory.Exists(root))
            {
                return files;
            }

            files.AddRange(Directory.EnumerateFiles(root, "*.tyhpdef", SearchOption.TopDirectoryOnly));
            var extensions = Path.Combine(root, "extensions");
            if (Directory.Exists(extensions))
            {
                files.AddRange(Directory.EnumerateFiles(extensions, "*.tyhpdef", SearchOption.TopDirectoryOnly));
            }

            return files
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static List<string> DiscoverLayer2(string root)
        {
            var stubs = Path.Combine(root, "overlays", "stubs");
            if (!Directory.Exists(stubs))
            {
                return [];
            }

            return Directory.EnumerateFiles(stubs, "*.tyhpdef", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static string FormatReport(
            string root,
            string cacheDir,
            int layer1Files,
            int layer2Files,
            IReadOnlyList<StubAuditFact> facts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Layer 2 stub audit");
            sb.AppendLine();
            sb.AppendLine("Compares committed Layer 1 and Layer 2 tyhpdefs with Psalm, PHPStan, PhpStorm, and Phan stubs.");
            sb.AppendLine("php.net is not an oracle for generics. This report does not regenerate overlays.");
            sb.AppendLine();
            sb.AppendLine("- **Tyhpdef root:** `" + root + "`");
            sb.AppendLine("- **Stub cache:** `" + cacheDir + "`");
            sb.AppendLine("- **Layer 1 files:** " + layer1Files);
            sb.AppendLine("- **Layer 2 files:** " + layer2Files);
            sb.AppendLine("- **Facts compared:** " + facts.Count);
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("| Bucket | Count | Meaning |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (var bucket in Enum.GetValues<StubAuditBucket>())
            {
                var count = facts.Count(f => f.Bucket == bucket);
                sb.Append("| `").Append(Slug(bucket)).Append("` | ").Append(count)
                    .Append(" | ").Append(BucketMeaning(bucket)).AppendLine(" |");
            }

            AppendSection(sb, facts, StubAuditBucket.TemplateOrderMismatch, "Template order mismatch");
            AppendSection(sb, facts, StubAuditBucket.TemplateArityMismatch, "Template arity mismatch");
            AppendSection(sb, facts, StubAuditBucket.PsalmPhpStanDisagree, "Psalm vs PHPStan disagree");
            AppendSection(sb, facts, StubAuditBucket.FaithfulnessMismatch, "Layer 2 ≠ harvest consensus");
            AppendSection(sb, facts, StubAuditBucket.Layer2NoStub, "Layer 2 with no stub match");
            AppendSection(sb, facts, StubAuditBucket.OneCorpus, "One corpus only");
            AppendSection(sb, facts, StubAuditBucket.NotInLayer2, "Stub refinement not in Layer 2");

            var agreeTypes = facts
                .Where(f => f.Bucket == StubAuditBucket.Agree && f.IsTypeHeader && !string.IsNullOrWhiteSpace(f.Layer2))
                .OrderBy(f => f.Fqn, StringComparer.OrdinalIgnoreCase)
                .ToList();
            sb.AppendLine("## Agreeing type headers in Layer 2");
            sb.AppendLine();
            if (agreeTypes.Count == 0)
            {
                sb.AppendLine("None.");
                sb.AppendLine();
            }
            else
            {
                foreach (var fact in agreeTypes)
                {
                    sb.Append("- `").Append(fact.Fqn).Append("` ").Append(Display(fact.Layer2)).AppendLine();
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void AppendSection(
            StringBuilder sb,
            IReadOnlyList<StubAuditFact> facts,
            StubAuditBucket bucket,
            string title)
        {
            var rows = facts
                .Where(f => f.Bucket == bucket)
                .OrderBy(f => f.Fqn, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Fact, StringComparer.OrdinalIgnoreCase)
                .ToList();
            sb.AppendLine("## " + title);
            sb.AppendLine();
            if (rows.Count == 0)
            {
                sb.AppendLine("None.");
                sb.AppendLine();
                return;
            }

            foreach (var fact in rows)
            {
                sb.Append("- **`").Append(fact.Fqn).Append("`** · ").Append(fact.Fact).AppendLine();
                sb.Append("  - Layer 1: ").AppendLine(Display(fact.Layer1));
                sb.Append("  - Layer 2: ").AppendLine(Display(fact.Layer2));
                sb.Append("  - Harvest: ").AppendLine(Display(fact.Harvest));
                foreach (var id in CorpusIds)
                {
                    fact.Corpus.TryGetValue(id, out var value);
                    sb.Append("  - ").Append(id).Append(": ").AppendLine(Display(value));
                }
            }

            sb.AppendLine();
        }

        private static string Display(string? value)
            => string.IsNullOrWhiteSpace(value) ? "—" : "`" + value.Trim() + "`";

        private static string Slug(StubAuditBucket bucket)
            => bucket switch
            {
                StubAuditBucket.Agree => "agree",
                StubAuditBucket.PsalmPhpStanDisagree => "psalm-phpstan-disagree",
                StubAuditBucket.TemplateArityMismatch => "template-arity-mismatch",
                StubAuditBucket.TemplateOrderMismatch => "template-order-mismatch",
                StubAuditBucket.FaithfulnessMismatch => "faithfulness-mismatch",
                StubAuditBucket.OneCorpus => "one-corpus",
                StubAuditBucket.NotInLayer2 => "not-in-layer-2",
                StubAuditBucket.Layer2NoStub => "layer-2-no-stub",
                _ => bucket.ToString(),
            };

        private static string BucketMeaning(StubAuditBucket bucket)
            => bucket switch
            {
                StubAuditBucket.Agree => "Layer 2 matches harvest, or neither side refined Layer 1",
                StubAuditBucket.PsalmPhpStanDisagree => "Psalm and PHPStan both typed this and differ (harvest keeps Layer 1)",
                StubAuditBucket.TemplateArityMismatch => "Psalm and PHPStan declare a different number of `@template` parameters",
                StubAuditBucket.TemplateOrderMismatch => "Psalm and PHPStan use the same template names in a different order",
                StubAuditBucket.FaithfulnessMismatch => "Layer 2 wrote a type that is not the harvest consensus",
                StubAuditBucket.OneCorpus => "Only one corpus supplied a non-weak type",
                StubAuditBucket.NotInLayer2 => "Harvest would refine Layer 1 but Layer 2 does not",
                StubAuditBucket.Layer2NoStub => "Layer 2 has a signature with no matching stub symbol",
                _ => "",
            };
    }

    internal enum StubAuditBucket
    {
        Agree,
        TemplateOrderMismatch,
        TemplateArityMismatch,
        PsalmPhpStanDisagree,
        FaithfulnessMismatch,
        OneCorpus,
        NotInLayer2,
        Layer2NoStub,
    }

    internal sealed record StubAuditFact(
        string Fqn,
        string Fact,
        bool IsTypeHeader,
        string Layer1,
        string Layer2,
        IReadOnlyDictionary<string, string> Corpus,
        string Harvest,
        StubAuditBucket Bucket);
}
