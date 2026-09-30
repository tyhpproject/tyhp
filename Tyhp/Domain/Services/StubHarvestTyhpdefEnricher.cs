using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services.PhpDoc;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Layer 2: harvest Psalm / PHPStan / Phan / PhpStorm stubs into
    /// <c>{output}/overlays/stubs/*.tyhpdef</c>. Never mutates the Layer 1 baseline file.
    /// </summary>
    public sealed class StubHarvestTyhpdefEnricher
    {
        /// <summary>
        /// Layer 1 types that stub harvest may replace. <c>resource</c> is not weak:
        /// streams, dir handles, and contexts are still resources in PHP 8.1+.
        /// Legacy resource-to-object conversions (pgsql connections, curl handles, …)
        /// are rewritten separately via <see cref="RewriteLegacyResourceType"/> when
        /// sibling Layer 1 signatures already use the object class.
        /// </summary>
        private static readonly HashSet<string> WeakTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "",
            "array",
            "mixed",
            "callable",
        };

        private readonly StubCorpusCache _cache;

        public StubHarvestTyhpdefEnricher()
            : this(new StubCorpusCache())
        {
        }

        internal StubHarvestTyhpdefEnricher(StubCorpusCache cache)
        {
            this._cache = cache;
        }

        /// <summary>
        /// Match Layer 1 FQNs against stub corpora and write overlay files under
        /// <c>overlays/stubs/</c> only. No-ops for PHP-source harvest (<c>--source</c> / <c>--package-path</c>).
        /// </summary>
        public void Enrich(
            TyhpdefFile layer1,
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            string extName,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(layer1);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(result);

            if (options.Mode != TyhpdefGenerationMode.PhpExtension)
            {
                return;
            }

            if (IsForbiddenOutputDirectory(options.OutputDirectory))
            {
                return;
            }

            var cacheDir = this._cache.Resolve(options, result.Diagnostics, cancellationToken);
            if (cacheDir is null)
            {
                return;
            }

            var corpusDirs = StubCorpusCache.CorpusDirectories(cacheDir);
            if (corpusDirs.Count == 0)
            {
                if (options.RequireStubs)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefStubCacheRequired,
                        "generate_tyhpdef",
                        0,
                        0,
                        cacheDir);
                }
                else
                {
                    result.Diagnostics.AddWarning(
                        MessageCode.TyhpdefStubCacheMissing,
                        "generate_tyhpdef",
                        0,
                        0,
                        cacheDir);
                }

                return;
            }

            var index = IndexCorpora(corpusDirs);
            var overlay = BuildOverlay(
                layer1,
                index,
                result.Diagnostics,
                options.IncludeDocComments);
            var used = StubCorpusCache.AllCorpora.Where(c => corpusDirs.ContainsKey(c.Id)).ToList();
            TyhpdefSourcesMarkdown.AppendStubCorpora(options.OutputDirectory, extName, used);

            if (!HasAnyDeclaration(overlay))
            {
                return;
            }

            var stubsDir = Path.Combine(options.OutputDirectory, "overlays", "stubs");
            if (!IsSafeStubsDirectory(stubsDir, options.OutputDirectory))
            {
                return;
            }

            var fileName = TyhpdefOutputLayout.StubOverlayFileName(options, extName);
            var path = Path.Combine(stubsDir, fileName);
            if (File.Exists(path) && !options.Overwrite)
            {
                Message.Warn("CLI_TyhpdefSkippedExistingFile", path);
                return;
            }

            overlay = overlay with
            {
                Header = BuildAttributionHeader(used),
            };

            try
            {
                Directory.CreateDirectory(stubsDir);
                var text = TyhpdefOutputWriter.Write(
                    overlay,
                    new TyhpdefOutputOptions { IncludeDocComments = options.IncludeDocComments });
                var parseDiagnostics = new DiagnosticBag();
                var ast = Tyhpdef.ParseContent(text, path, ParseMode.Tyhpdef, parseDiagnostics);
                if (ast is null || parseDiagnostics.HasErrors)
                {
                    foreach (var diagnostic in parseDiagnostics.All)
                    {
                        result.Diagnostics.Add(diagnostic);
                    }

                    var detail = parseDiagnostics.Errors.Count > 0
                        ? parseDiagnostics.Errors[0].Message
                        : "parse returned null";
                    Message.Error("CLI_TyhpdefParseFailed", path, detail);
                    if (!parseDiagnostics.HasErrors)
                    {
                        result.Diagnostics.AddError(
                            MessageCode.TyhpdefParseError,
                            path,
                            0,
                            0,
                            Message.Localize("CLI_TyhpdefParseFailed", path, detail));
                    }

                    return;
                }

                var undeclared = FindUndeclaredConventionalTemplates(text);
                if (undeclared.Count > 0)
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefStubUndeclaredTemplate,
                        path,
                        0,
                        0,
                        undeclared[0]);
                    return;
                }

                File.WriteAllText(path, text);
                result.GeneratedFiles.Add(path);
                Message.Info("CLI_TyhpdefStubOverlayWritten", path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefOutputWriteError,
                    path,
                    0,
                    0,
                    path,
                    ex.Message);
            }
        }

        internal static bool IsWeakType(string? type)
        {
            var trimmed = (type ?? "").Trim();
            if (trimmed.Length == 0)
            {
                return true;
            }

            return WeakTypes.Contains(trimmed);
        }

        /// <summary>
        /// Layer 1 constructors and destructors often omit a return type. Stub
        /// <c>@return void</c> is not a signature change worth a Layer 2 overlay.
        /// </summary>
        private static bool IsConstructorOrDestructorName(string? name)
            => string.Equals(name, "__construct", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "__destruct", StringComparison.OrdinalIgnoreCase);

        internal static string CanonicalType(string? type)
        {
            var rewritten = PhpAstTypeExtractor.ToTyhpdefType(type ?? "");
            var normalized = PhpDocTypeParser.Normalize(rewritten);
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains('<', StringComparison.Ordinal)
                || normalized.Contains('(', StringComparison.Ordinal)
                || normalized.Contains('&', StringComparison.Ordinal))
            {
                return normalized;
            }

            var parts = normalized
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return parts.Length == 0 ? normalized : string.Join("|", parts);
        }

        internal static string? ConsensusType(
            IReadOnlyDictionary<string, string?> byCorpus,
            string fqn,
            DiagnosticBag diagnostics)
        {
            byCorpus.TryGetValue(StubCorpusCache.PsalmId, out var psalm);
            byCorpus.TryGetValue(StubCorpusCache.PhpStanId, out var phpstan);
            var psalmCanon = CanonicalOrNull(psalm);
            var phpstanCanon = CanonicalOrNull(phpstan);

            if (psalmCanon is not null && phpstanCanon is not null)
            {
                if (string.Equals(psalmCanon, phpstanCanon, StringComparison.OrdinalIgnoreCase))
                {
                    return IsWeakType(psalmCanon) ? null : psalmCanon;
                }

                diagnostics.AddWarning(
                    MessageCode.TyhpdefStubCorpusDisagreement,
                    "generate_tyhpdef",
                    0,
                    0,
                    fqn);
                return null;
            }

            if (psalmCanon is not null && !IsWeakType(psalmCanon))
            {
                return psalmCanon;
            }

            if (phpstanCanon is not null && !IsWeakType(phpstanCanon))
            {
                return phpstanCanon;
            }

            if (byCorpus.TryGetValue(StubCorpusCache.PhpStormId, out var storm)
                && CanonicalOrNull(storm) is { } stormType
                && !IsWeakType(stormType))
            {
                return stormType;
            }

            if (byCorpus.TryGetValue(StubCorpusCache.PhanId, out var phan)
                && CanonicalOrNull(phan) is { } phanType
                && !IsWeakType(phanType))
            {
                return phanType;
            }

            return null;
        }

        private static string? CanonicalOrNull(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            return CanonicalType(type);
        }

        /// <summary>
        /// True when <paramref name="type"/> is only <c>resource</c>, optionally nullable
        /// (<c>?resource</c>, <c>resource|null</c>). Unions such as <c>false|resource</c>
        /// (stream-or-false returns) are not bare and must not be rewritten.
        /// </summary>
        internal static bool IsBareResourceType(string? type)
        {
            var canon = CanonicalOrNull(type);
            if (canon is null)
            {
                return false;
            }

            var sawResource = false;
            foreach (var part in EnumerateUnionParts(canon))
            {
                if (part.Equals("resource", StringComparison.OrdinalIgnoreCase))
                {
                    sawResource = true;
                    continue;
                }

                if (part.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return false;
            }

            return sawResource;
        }

        /// <summary>
        /// Maps a parameter name to the object class Layer 1 already uses for that name
        /// (e.g. <c>connection</c> → <c>\PgSql\Connection</c>). Ties between distinct
        /// class types are omitted.
        /// </summary>
        internal static IReadOnlyDictionary<string, string> BuildResourceObjectTypes(TyhpdefFile layer1)
        {
            var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var callable in EnumerateCallables(layer1))
            {
                foreach (var parameter in callable.Parameters ?? [])
                {
                    var objectType = TryGetObjectClassType(parameter.Type);
                    if (objectType is null || string.IsNullOrWhiteSpace(parameter.Name))
                    {
                        continue;
                    }

                    if (!counts.TryGetValue(parameter.Name, out var byType))
                    {
                        byType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        counts[parameter.Name] = byType;
                    }

                    byType.TryGetValue(objectType, out var n);
                    byType[objectType] = n + 1;
                }
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, byType) in counts)
            {
                string? best = null;
                var bestCount = 0;
                var tie = false;
                foreach (var (type, count) in byType)
                {
                    if (count > bestCount)
                    {
                        best = type;
                        bestCount = count;
                        tie = false;
                    }
                    else if (count == bestCount)
                    {
                        tie = true;
                    }
                }

                if (best is not null && !tie)
                {
                    result[name] = best;
                }
            }

            return result;
        }

        /// <summary>
        /// Community stubs still type PHP 8.0-era resources as <c>resource</c>. When Layer 1
        /// already uses an object class for the same parameter name, emit that class instead.
        /// </summary>
        internal static string RewriteLegacyResourceType(
            string consensus,
            string parameterName,
            IReadOnlyDictionary<string, string> resourceObjectTypes)
        {
            if (!IsBareResourceType(consensus)
                || string.IsNullOrWhiteSpace(parameterName)
                || resourceObjectTypes is null
                || !resourceObjectTypes.TryGetValue(parameterName, out var objectType)
                || string.IsNullOrWhiteSpace(objectType))
            {
                return consensus;
            }

            return IsNullableType(consensus) ? "?" + objectType : objectType;
        }

        private static IEnumerable<TyhpdefMethod> EnumerateCallables(TyhpdefFile file)
        {
            foreach (var function in file.GlobalFunctions ?? [])
            {
                foreach (var item in WithOverloads(function))
                {
                    yield return item;
                }
            }

            foreach (var type in file.GlobalTypes ?? [])
            {
                foreach (var item in EnumerateTypeCallables(type))
                {
                    yield return item;
                }
            }

            foreach (var ns in file.Namespaces ?? [])
            {
                foreach (var function in ns.Functions ?? [])
                {
                    foreach (var item in WithOverloads(function))
                    {
                        yield return item;
                    }
                }

                foreach (var type in ns.Classes ?? [])
                {
                    foreach (var item in EnumerateTypeCallables(type))
                    {
                        yield return item;
                    }
                }
            }

            foreach (var block in file.DeclareBlocks ?? [])
            {
                foreach (var function in block.Functions ?? [])
                {
                    foreach (var item in WithOverloads(function))
                    {
                        yield return item;
                    }
                }

                foreach (var type in block.Classes ?? [])
                {
                    foreach (var item in EnumerateTypeCallables(type))
                    {
                        yield return item;
                    }
                }

                foreach (var ns in block.Namespaces ?? [])
                {
                    foreach (var function in ns.Functions ?? [])
                    {
                        foreach (var item in WithOverloads(function))
                        {
                            yield return item;
                        }
                    }

                    foreach (var type in ns.Classes ?? [])
                    {
                        foreach (var item in EnumerateTypeCallables(type))
                        {
                            yield return item;
                        }
                    }
                }
            }
        }

        private static IEnumerable<TyhpdefMethod> EnumerateTypeCallables(TyhpdefClassDeclaration type)
        {
            foreach (var method in type.Methods ?? [])
            {
                foreach (var item in WithOverloads(method))
                {
                    yield return item;
                }
            }
        }

        private static IEnumerable<TyhpdefMethod> WithOverloads(TyhpdefMethod method)
        {
            yield return method;
            foreach (var overload in method.Overloads ?? [])
            {
                yield return overload;
            }
        }

        private static IEnumerable<string> EnumerateUnionParts(string type)
        {
            var stripped = type.StartsWith('?') ? type[1..] : type;
            foreach (var part in stripped.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return part.StartsWith('?') ? part[1..] : part;
            }
        }

        private static bool IsNullableType(string type)
        {
            var canon = CanonicalType(type);
            if (canon.StartsWith('?'))
            {
                return true;
            }

            foreach (var part in EnumerateUnionParts(canon))
            {
                if (part.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? TryGetObjectClassType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type) || IsWeakType(type) || IsBareResourceType(type))
            {
                return null;
            }

            var canon = CanonicalType(type);
            string? found = null;
            foreach (var part in EnumerateUnionParts(canon))
            {
                if (part.Equals("null", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("false", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsWeakType(part)
                    || PhpAstTypeExtractor.BuiltinTypes.Contains(part)
                    || part.Contains('<', StringComparison.Ordinal)
                    || part.Contains('(', StringComparison.Ordinal)
                    || part.Contains('&', StringComparison.Ordinal)
                    || !LooksLikeClassName(part))
                {
                    return null;
                }

                var qualified = part.StartsWith('\\') ? part : "\\" + part;
                if (found is not null && !string.Equals(found, qualified, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                found = qualified;
            }

            return found;
        }

        private static bool LooksLikeClassName(string type)
        {
            if (string.IsNullOrEmpty(type))
            {
                return false;
            }

            var i = type[0] == '\\' ? 1 : 0;
            if (i >= type.Length || (!char.IsAsciiLetter(type[i]) && type[i] != '_'))
            {
                return false;
            }

            for (; i < type.Length; i++)
            {
                var ch = type[i];
                if (ch != '\\' && !char.IsAsciiLetterOrDigit(ch) && ch != '_')
                {
                    return false;
                }
            }

            return true;
        }

        internal static Dictionary<string, Dictionary<string, StubSymbol>> IndexCorpora(
            IReadOnlyDictionary<string, string> corpusDirs)
        {
            var index = new Dictionary<string, Dictionary<string, StubSymbol>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (corpusId, dir) in corpusDirs)
            {
                var byFqn = new Dictionary<string, StubSymbol>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in StubCorpusCache.EnumerateStubFiles(dir))
                {
                    string text;
                    try
                    {
                        text = File.ReadAllText(file);
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    foreach (var symbol in StubPhpSymbolExtractor.ExtractFile(file, text))
                    {
                        byFqn[symbol.Fqn] = symbol;
                        foreach (var member in symbol.Members)
                        {
                            byFqn[member.Fqn] = member;
                        }
                    }
                }

                index[corpusId] = byFqn;
            }

            return index;
        }

        private static TyhpdefFile BuildOverlay(
            TyhpdefFile layer1,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> index,
            DiagnosticBag diagnostics,
            bool includeDocs)
        {
            var resourceObjectTypes = BuildResourceObjectTypes(layer1);
            var typePlans = BuildTypePlans(layer1, index);
            var functions = new List<TyhpdefFunction>();
            foreach (var function in layer1.GlobalFunctions ?? [])
            {
                var fqn = "\\" + function.Name.TrimStart('\\');
                if (TryEnrichFunction(
                    function,
                    fqn,
                    index,
                    diagnostics,
                    includeDocs,
                    resourceObjectTypes,
                    out var enriched))
                {
                    functions.Add(enriched);
                }
            }

            var types = new List<TyhpdefClassDeclaration>();
            foreach (var type in layer1.GlobalTypes ?? [])
            {
                var fqn = "\\" + type.Name.TrimStart('\\');
                if (TryEnrichType(
                    type,
                    fqn,
                    index,
                    diagnostics,
                    includeDocs,
                    resourceObjectTypes,
                    typePlans,
                    out var enriched))
                {
                    types.AddRange(enriched);
                }
            }

            var namespaces = new List<TyhpdefNamespace>();
            foreach (var ns in layer1.Namespaces ?? [])
            {
                var prefix = "\\" + (ns.Name ?? "").Trim().TrimStart('\\').TrimEnd('\\');
                var nsFunctions = new List<TyhpdefFunction>();
                foreach (var function in ns.Functions ?? [])
                {
                    var fqn = prefix + "\\" + function.Name.TrimStart('\\');
                    if (TryEnrichFunction(
                        function,
                        fqn,
                        index,
                        diagnostics,
                        includeDocs,
                        resourceObjectTypes,
                        out var enriched))
                    {
                        nsFunctions.Add(enriched);
                    }
                }

                var nsTypes = new List<TyhpdefClassDeclaration>();
                foreach (var type in ns.Classes ?? [])
                {
                    var fqn = prefix + "\\" + type.Name.TrimStart('\\');
                    if (TryEnrichType(
                        type,
                        fqn,
                        index,
                        diagnostics,
                        includeDocs,
                        resourceObjectTypes,
                        typePlans,
                        out var enriched))
                    {
                        nsTypes.AddRange(enriched);
                    }
                }

                if (nsFunctions.Count > 0 || nsTypes.Count > 0)
                {
                    namespaces.Add(new TyhpdefNamespace
                    {
                        Name = ns.Name ?? "",
                        Functions = nsFunctions,
                        Classes = nsTypes,
                    });
                }
            }

            return new TyhpdefFile
            {
                GlobalFunctions = functions,
                GlobalTypes = types,
                Namespaces = namespaces,
            };
        }

        private static bool TryEnrichFunction(
            TyhpdefFunction layer1,
            string fqn,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> index,
            DiagnosticBag diagnostics,
            bool includeDocs,
            IReadOnlyDictionary<string, string> resourceObjectTypes,
            out TyhpdefFunction enriched,
            IReadOnlySet<string>? hostGenericNames = null,
            IReadOnlyDictionary<string, string>? hostAliases = null)
        {
            var matches = Collect(index, fqn);
            if (matches.Count == 0)
            {
                enriched = layer1;
                return false;
            }

            var changed = false;
            var unification = UnifyTemplates(matches);
            var aliases = MergeAliasMaps(hostAliases, unification.Aliases);
            var generics = layer1.GenericParameters.ToList();
            var templates = unification.Parameters;
            if (hostGenericNames is { Count: > 0 } && templates.Count > 0)
            {
                templates = templates.Where(t => !hostGenericNames.Contains(t.Name)).ToList();
            }

            if (templates.Count > 0 && generics.Count == 0)
            {
                generics = templates;
                changed = true;
            }

            var inferredExclude = new HashSet<string>(aliases.Keys, StringComparer.Ordinal);
            if (hostGenericNames is not null)
            {
                foreach (var name in hostGenericNames)
                {
                    inferredExclude.Add(name);
                }
            }

            foreach (var parameter in generics)
            {
                inferredExclude.Add(parameter.Name);
            }

            if (MergeInferredConventionalTemplates(generics, matches, inferredExclude))
            {
                EnsureTrailingGenericDefaults(generics);
                changed = true;
            }

            var declaredGenericNames = new HashSet<string>(
                generics.Select(g => g.Name),
                StringComparer.Ordinal);
            if (hostGenericNames is not null)
            {
                foreach (var name in hostGenericNames)
                {
                    declaredGenericNames.Add(name);
                }
            }

            var returnType = layer1.ReturnType;
            if (IsWeakType(returnType) && !IsConstructorOrDestructorName(layer1.Name))
            {
                var byCorpus = matches.ToDictionary(
                    kv => kv.Key,
                    kv => RewriteTemplateAliases(EffectiveType(kv.Value, isReturn: true), aliases),
                    StringComparer.OrdinalIgnoreCase);
                var consensus = ConsensusType(byCorpus, fqn, diagnostics);
                if (consensus is not null
                    && !ReferencesUndeclaredTemplate(consensus, declaredGenericNames, hostGenericNames)
                    && !string.Equals(CanonicalType(returnType), consensus, StringComparison.OrdinalIgnoreCase))
                {
                    returnType = consensus;
                    changed = true;
                }
            }

            var parameters = layer1.Parameters.Select(p => p with { }).ToList();
            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (!IsWeakType(parameter.Type))
                {
                    continue;
                }

                var byCorpus = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (corpus, symbol) in matches)
                {
                    var stubParam = symbol.Parameters.FirstOrDefault(p =>
                        string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                    if (stubParam is null)
                    {
                        continue;
                    }

                    var phpDoc = symbol.PhpDoc.ParamTags.FirstOrDefault(t =>
                        string.Equals(t.ParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase));
                    byCorpus[corpus] = RewriteTemplateAliases(
                        FirstPresent(phpDoc?.TypeExpression, stubParam.NativeType),
                        aliases);
                }

                var consensus = ConsensusType(byCorpus, fqn + "($" + parameter.Name + ")", diagnostics);
                if (consensus is not null)
                {
                    consensus = RewriteLegacyResourceType(consensus, parameter.Name, resourceObjectTypes);
                }

                if (consensus is not null
                    && !ReferencesUndeclaredTemplate(consensus, declaredGenericNames, hostGenericNames)
                    && !string.Equals(CanonicalType(parameter.Type), consensus, StringComparison.OrdinalIgnoreCase))
                {
                    parameters[i] = parameter with { Type = consensus };
                    changed = true;
                }
            }

            var doc = MergeDocs(layer1.DocComment, matches, includeDocs, out _);
            if (!changed)
            {
                enriched = layer1;
                return false;
            }

            enriched = layer1 with
            {
                ReturnType = returnType,
                Parameters = parameters,
                GenericParameters = generics,
                DocComment = doc,
            };
            return true;
        }

        private static bool TryEnrichType(
            TyhpdefClassDeclaration layer1,
            string fqn,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> index,
            DiagnosticBag diagnostics,
            bool includeDocs,
            IReadOnlyDictionary<string, string> resourceObjectTypes,
            IReadOnlyDictionary<string, TypeHarvestPlan> typePlans,
            out List<TyhpdefClassDeclaration> enriched)
        {
            if (!typePlans.TryGetValue(fqn, out var plan))
            {
                plan = ComputeTypePlan(layer1, fqn, index);
            }

            var typeMatches = Collect(index, fqn);
            var classGenerics = plan.Generics;
            var aliases = plan.Aliases;
            var classGenericsChanged = !SameGenericParameters(layer1.GenericParameters, classGenerics);
            var classGenericNames = new HashSet<string>(
                classGenerics.Select(g => g.Name),
                StringComparer.Ordinal);

            var methods = new List<TyhpdefMethod>();
            foreach (var method in layer1.Methods ?? [])
            {
                var asFunction = new TyhpdefFunction
                {
                    Name = method.Name,
                    Modifiers = method.Modifiers,
                    Attributes = method.Attributes,
                    Parameters = method.Parameters,
                    ReturnType = method.ReturnType,
                    GenericParameters = method.GenericParameters,
                    DocComment = method.DocComment,
                    IsDeprecated = method.IsDeprecated,
                    IsObsolete = method.IsObsolete,
                    ReturnsReference = method.ReturnsReference,
                    IsAsync = method.IsAsync,
                    PhpGate = method.PhpGate,
                };
                if (TryEnrichFunction(
                    asFunction,
                    fqn + "::" + method.Name,
                    index,
                    diagnostics,
                    includeDocs,
                    resourceObjectTypes,
                    out var enrichedFn,
                    classGenericNames,
                    aliases))
                {
                    methods.Add(method with
                    {
                        Parameters = enrichedFn.Parameters,
                        ReturnType = enrichedFn.ReturnType,
                        GenericParameters = enrichedFn.GenericParameters,
                        DocComment = enrichedFn.DocComment,
                    });
                }
            }

            var properties = new List<TyhpdefProperty>();
            foreach (var property in layer1.Properties ?? [])
            {
                var propFqn = fqn + "::$" + property.Name;
                var matches = Collect(index, propFqn);
                if (matches.Count == 0)
                {
                    continue;
                }

                var type = property.Type;
                var typeChanged = false;
                if (IsWeakType(type))
                {
                    var byCorpus = matches.ToDictionary(
                        kv => kv.Key,
                        kv => RewriteTemplateAliases(
                            FirstPresent(kv.Value.PhpDoc.VarTag?.TypeExpression, kv.Value.NativeReturnType, kv.Value.PhpDocType),
                            aliases),
                        StringComparer.OrdinalIgnoreCase);
                    var consensus = ConsensusType(byCorpus, propFqn, diagnostics);
                    if (consensus is not null
                        && !ReferencesUndeclaredTemplate(consensus, classGenericNames, classGenericNames)
                        && !string.Equals(CanonicalType(type), consensus, StringComparison.OrdinalIgnoreCase))
                    {
                        type = consensus;
                        typeChanged = true;
                    }
                }

                if (!typeChanged)
                {
                    continue;
                }

                var doc = MergeDocs(property.DocComment, matches, includeDocs, out _);
                properties.Add(property with { Type = type, DocComment = doc });
            }

            var typeDoc = layer1.DocComment;
            var typeDocsChanged = false;
            if (typeMatches.Count > 0)
            {
                typeDoc = MergeDocs(layer1.DocComment, typeMatches, includeDocs, out typeDocsChanged);
            }

            var inheritanceChanged = TryApplyInheritanceGenerics(
                layer1,
                typeMatches,
                aliases,
                typePlans,
                diagnostics,
                fqn,
                out var extends,
                out var implements);
            var headerChanged = classGenericsChanged || inheritanceChanged;

            if (methods.Count == 0 && properties.Count == 0 && !headerChanged)
            {
                enriched = [];
                return false;
            }

            var overlays = new List<TyhpdefClassDeclaration>();
            if (headerChanged)
            {
                overlays.Add(new TyhpdefClassDeclaration
                {
                    Kind = layer1.Kind,
                    Name = layer1.Name,
                    IsPartial = true,
                    IsHeaderOnly = true,
                    GenericParameters = classGenericsChanged ? classGenerics : [],
                    Extends = inheritanceChanged ? extends : null,
                    Implements = inheritanceChanged ? implements : [],
                    DocComment = typeDocsChanged ? typeDoc : null,
                    PhpGate = layer1.PhpGate,
                });
            }

            if (methods.Count > 0 || properties.Count > 0)
            {
                overlays.Add(new TyhpdefClassDeclaration
                {
                    Kind = layer1.Kind,
                    Name = layer1.Name,
                    IsPartial = true,
                    Methods = methods,
                    Properties = properties,
                    DocComment = headerChanged ? null : (typeDocsChanged ? typeDoc : null),
                    PhpGate = layer1.PhpGate,
                });
            }

            enriched = overlays;
            return overlays.Count > 0;
        }

        internal static Dictionary<string, TypeHarvestPlan> BuildTypePlans(
            TyhpdefFile layer1,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> index)
        {
            var plans = new Dictionary<string, TypeHarvestPlan>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in layer1.GlobalTypes ?? [])
            {
                var fqn = "\\" + type.Name.TrimStart('\\');
                plans[fqn] = ComputeTypePlan(type, fqn, index);
            }

            foreach (var ns in layer1.Namespaces ?? [])
            {
                var prefix = "\\" + (ns.Name ?? "").Trim().TrimStart('\\').TrimEnd('\\');
                foreach (var type in ns.Classes ?? [])
                {
                    var fqn = prefix + "\\" + type.Name.TrimStart('\\');
                    plans[fqn] = ComputeTypePlan(type, fqn, index);
                }
            }

            return plans;
        }

        internal static TypeHarvestPlan ComputeTypePlan(
            TyhpdefClassDeclaration layer1,
            string fqn,
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> index)
        {
            var typeMatches = Collect(index, fqn);
            var unification = UnifyTemplates(typeMatches);
            var classGenerics = layer1.GenericParameters.ToList();
            if (classGenerics.Count == 0 && unification.Parameters.Count > 0)
            {
                classGenerics = unification.Parameters.Select(p => p with { }).ToList();
            }

            var inferredExclude = new HashSet<string>(unification.Aliases.Keys, StringComparer.Ordinal);
            foreach (var parameter in classGenerics)
            {
                inferredExclude.Add(parameter.Name);
            }

            foreach (var method in layer1.Methods ?? [])
            {
                var methodMatches = Collect(index, fqn + "::" + method.Name);
                var methodExclude = new HashSet<string>(inferredExclude, StringComparer.Ordinal);
                foreach (var name in UnifyTemplates(methodMatches).Parameters.Select(g => g.Name))
                {
                    methodExclude.Add(name);
                }

                if (MergeInferredConventionalTemplates(classGenerics, methodMatches, methodExclude))
                {
                    foreach (var parameter in classGenerics)
                    {
                        inferredExclude.Add(parameter.Name);
                    }
                }
            }

            foreach (var property in layer1.Properties ?? [])
            {
                MergeInferredConventionalTemplates(
                    classGenerics,
                    Collect(index, fqn + "::$" + property.Name),
                    inferredExclude);
            }

            EnsureTrailingGenericDefaults(classGenerics);
            return new TypeHarvestPlan(classGenerics, unification.Aliases);
        }

        internal static Dictionary<string, StubSymbol> Collect(
            IReadOnlyDictionary<string, Dictionary<string, StubSymbol>> index,
            string fqn)
        {
            var matches = new Dictionary<string, StubSymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var (corpus, byFqn) in index)
            {
                if (byFqn.TryGetValue(fqn, out var symbol))
                {
                    matches[corpus] = symbol;
                }
            }

            return matches;
        }

        internal static string? EffectiveType(StubSymbol symbol, bool isReturn)
        {
            if (isReturn)
            {
                return FirstPresent(symbol.PhpDoc.ReturnTag?.TypeExpression, symbol.NativeReturnType);
            }

            return FirstPresent(symbol.NativeReturnType);
        }

        internal static string? EffectiveParameterType(StubSymbol symbol, string parameterName)
        {
            var stubParam = symbol.Parameters.FirstOrDefault(p =>
                string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));
            var phpDoc = symbol.PhpDoc.ParamTags.FirstOrDefault(t =>
                string.Equals(t.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));
            return FirstPresent(phpDoc?.TypeExpression, stubParam?.NativeType);
        }

        internal static string? EffectivePropertyType(StubSymbol symbol)
            => FirstPresent(symbol.PhpDoc.VarTag?.TypeExpression, symbol.NativeReturnType, symbol.PhpDocType);

        internal static List<string> TemplateNames(StubSymbol symbol)
        {
            var names = new List<string>();
            foreach (var tag in symbol.PhpDoc.TemplateTags)
            {
                var name = (tag.ParameterName ?? "").Trim();
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }

            return names;
        }

        internal static string SpellTemplateTags(StubSymbol symbol)
        {
            var parts = new List<string>();
            foreach (var tag in symbol.PhpDoc.TemplateTags)
            {
                var name = (tag.ParameterName ?? "").Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                var text = name;
                if (!string.IsNullOrWhiteSpace(tag.TypeExpression))
                {
                    text += " of " + PhpDocTypeParser.Normalize(tag.TypeExpression);
                }

                parts.Add(text);
            }

            return parts.Count == 0 ? "" : "<" + string.Join(", ", parts) + ">";
        }

        internal static string SpellGenericParameters(IEnumerable<TyhpdefGenericParameter>? parameters)
        {
            var list = (parameters ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
            if (list.Count == 0)
            {
                return "";
            }

            var parts = list.Select(p =>
            {
                var text = p.Name;
                if (!string.IsNullOrWhiteSpace(p.Constraint))
                {
                    text += " extends " + CanonicalType(p.Constraint);
                }

                if (!string.IsNullOrWhiteSpace(p.Default))
                {
                    text += " = " + CanonicalType(p.Default);
                }

                return text;
            });
            return "<" + string.Join(", ", parts) + ">";
        }

        private static string? FirstPresent(params string?[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return PhpDocTypeParser.Normalize(candidate);
                }
            }

            return null;
        }

        internal sealed record TypeHarvestPlan(
            List<TyhpdefGenericParameter> Generics,
            Dictionary<string, string> Aliases);

        internal sealed record TemplateUnification(
            List<TyhpdefGenericParameter> Parameters,
            Dictionary<string, string> Aliases)
        {
            public static TemplateUnification Empty { get; } = new(
                [],
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        private static readonly string[] TemplateCorpusPreference =
        [
            StubCorpusCache.PhpStanId,
            StubCorpusCache.PsalmId,
            StubCorpusCache.PhpStormId,
            StubCorpusCache.PhanId,
        ];

        private static readonly HashSet<string> ImplementsTagNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "implements",
            "template-implements",
            "phpstan-implements",
            "psalm-implements",
            "phan-implements",
        };

        private static readonly HashSet<string> ExtendsTagNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "extends",
            "template-extends",
            "phpstan-extends",
            "psalm-extends",
            "phan-extends",
        };

        internal static TemplateUnification UnifyTemplates(IReadOnlyDictionary<string, StubSymbol> matches)
        {
            if (matches.Count == 0)
            {
                return TemplateUnification.Empty;
            }

            var slots = new List<(List<string> Names, string? Constraint)>();
            foreach (var corpus in OrderedCorpusIds(matches))
            {
                if (!matches.TryGetValue(corpus, out var symbol))
                {
                    continue;
                }

                var position = 0;
                foreach (var tag in symbol.PhpDoc.TemplateTags)
                {
                    var name = (tag.ParameterName ?? "").Trim();
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    var constraint = string.IsNullOrWhiteSpace(tag.TypeExpression)
                        ? null
                        : PhpDocTypeParser.Normalize(tag.TypeExpression);

                    if (position >= slots.Count)
                    {
                        slots.Add(([name], constraint));
                    }
                    else
                    {
                        var (names, existingConstraint) = slots[position];
                        if (!names.Exists(n => string.Equals(n, name, StringComparison.Ordinal)))
                        {
                            names.Add(name);
                        }

                        if (string.IsNullOrWhiteSpace(existingConstraint)
                            && !string.IsNullOrWhiteSpace(constraint))
                        {
                            slots[position] = (names, constraint);
                        }
                    }

                    position++;
                }
            }

            if (slots.Count == 0)
            {
                return TemplateUnification.Empty;
            }

            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            var parameters = new List<TyhpdefGenericParameter>();
            foreach (var (names, constraint) in slots)
            {
                var canonical = names[0];
                foreach (var name in names)
                {
                    aliases[name] = canonical;
                }

                parameters.Add(ApplyTemplateConstraintAndDefault(canonical, constraint));
            }

            EnsureTrailingGenericDefaults(parameters);
            return new TemplateUnification(parameters, aliases);
        }

        private static IEnumerable<string> OrderedCorpusIds(IReadOnlyDictionary<string, StubSymbol> matches)
        {
            foreach (var id in TemplateCorpusPreference)
            {
                if (matches.ContainsKey(id))
                {
                    yield return id;
                }
            }

            foreach (var id in matches.Keys)
            {
                if (!TemplateCorpusPreference.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    yield return id;
                }
            }
        }

        internal static TyhpdefGenericParameter ApplyTemplateConstraintAndDefault(
            string name,
            string? constraint)
        {
            if (string.IsNullOrWhiteSpace(constraint))
            {
                return new TyhpdefGenericParameter { Name = name };
            }

            var normalized = PhpDocTypeParser.Normalize(constraint);
            if (string.Equals(normalized, "mixed", StringComparison.OrdinalIgnoreCase))
            {
                return new TyhpdefGenericParameter { Name = name, Default = "mixed" };
            }

            return new TyhpdefGenericParameter
            {
                Name = name,
                Constraint = normalized,
                Default = normalized,
            };
        }

        private static void EnsureTrailingGenericDefaults(List<TyhpdefGenericParameter> parameters)
        {
            var firstDefault = parameters.FindIndex(p => !string.IsNullOrWhiteSpace(p.Default));
            if (firstDefault < 0)
            {
                return;
            }

            for (var i = firstDefault + 1; i < parameters.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(parameters[i].Default))
                {
                    parameters[i] = parameters[i] with { Default = "mixed" };
                }
            }
        }

        internal static string? RewriteTemplateAliases(
            string? type,
            IReadOnlyDictionary<string, string>? aliases)
        {
            if (string.IsNullOrWhiteSpace(type) || aliases is null || aliases.Count == 0)
            {
                return type;
            }

            var sb = new System.Text.StringBuilder(type.Length);
            var i = 0;
            while (i < type.Length)
            {
                if (type[i] == '\\')
                {
                    var start = i;
                    i++;
                    while (i < type.Length
                        && (char.IsAsciiLetterOrDigit(type[i]) || type[i] is '_' or '\\'))
                    {
                        i++;
                    }

                    sb.Append(type, start, i - start);
                    continue;
                }

                if (char.IsAsciiLetter(type[i]) || type[i] == '_')
                {
                    var start = i;
                    i++;
                    while (i < type.Length
                        && (char.IsAsciiLetterOrDigit(type[i]) || type[i] == '_'))
                    {
                        i++;
                    }

                    var id = type[start..i];
                    sb.Append(aliases.TryGetValue(id, out var canonical) ? canonical : id);
                    continue;
                }

                sb.Append(type[i]);
                i++;
            }

            return sb.ToString();
        }

        private static Dictionary<string, string> MergeAliasMaps(
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

        private static bool TryApplyInheritanceGenerics(
            TyhpdefClassDeclaration layer1,
            IReadOnlyDictionary<string, StubSymbol> typeMatches,
            IReadOnlyDictionary<string, string> aliases,
            IReadOnlyDictionary<string, TypeHarvestPlan> typePlans,
            DiagnosticBag diagnostics,
            string fqn,
            out string? extends,
            out List<string> implements)
        {
            extends = layer1.Extends;
            implements = [.. layer1.Implements ?? []];
            var extendsByParent = CollectInheritanceInstantiations(typeMatches, aliases, ExtendsTagNames);
            var implementsByParent = CollectInheritanceInstantiations(typeMatches, aliases, ImplementsTagNames);
            var changed = false;

            if (!string.IsNullOrWhiteSpace(layer1.Extends))
            {
                var key = UnqualifiedTypeName(StripGenericArgs(layer1.Extends));
                if ((extendsByParent.TryGetValue(key, out var byCorpus)
                    || implementsByParent.TryGetValue(key, out byCorpus))
                    && ConsensusType(byCorpus!, fqn + " extends " + key, diagnostics) is { } consensus
                    && TryAttachGenericArgs(layer1.Extends!, consensus, typePlans, out var attached)
                    && !string.Equals(attached, layer1.Extends, StringComparison.Ordinal))
                {
                    extends = attached;
                    changed = true;
                }
            }

            for (var i = 0; i < implements.Count; i++)
            {
                var original = implements[i];
                var key = UnqualifiedTypeName(StripGenericArgs(original));
                if ((!implementsByParent.TryGetValue(key, out var byCorpus)
                    && !extendsByParent.TryGetValue(key, out byCorpus))
                    || ConsensusType(byCorpus, fqn + " implements " + key, diagnostics) is not { } consensus
                    || !TryAttachGenericArgs(original, consensus, typePlans, out var attached)
                    || string.Equals(attached, original, StringComparison.Ordinal))
                {
                    continue;
                }

                implements[i] = attached;
                changed = true;
            }

            return changed;
        }

        private static Dictionary<string, Dictionary<string, string?>> CollectInheritanceInstantiations(
            IReadOnlyDictionary<string, StubSymbol> matches,
            IReadOnlyDictionary<string, string> aliases,
            IReadOnlySet<string> tagNames)
        {
            var byParent = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (corpus, symbol) in matches)
            {
                foreach (var tag in symbol.PhpDoc.Tags)
                {
                    if (!tagNames.Contains(tag.TagName))
                    {
                        continue;
                    }

                    var raw = tag.TypeExpression ?? tag.Description;
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    var rewritten = RewriteTemplateAliases(raw, aliases) ?? raw;
                    if (!TrySplitGenericInstantiation(rewritten, out var name, out var args))
                    {
                        continue;
                    }

                    var parentKey = UnqualifiedTypeName(name);
                    var canonical = args.Count == 0
                        ? parentKey
                        : parentKey + "<" + string.Join(", ", args) + ">";
                    if (!byParent.TryGetValue(parentKey, out var byCorpus))
                    {
                        byCorpus = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                        byParent[parentKey] = byCorpus;
                    }

                    byCorpus[corpus] = canonical;
                }
            }

            return byParent;
        }

        private static bool TryAttachGenericArgs(
            string originalParent,
            string stubInstantiation,
            IReadOnlyDictionary<string, TypeHarvestPlan> typePlans,
            out string result)
        {
            result = originalParent;
            if (!TrySplitGenericInstantiation(stubInstantiation, out _, out var args) || args.Count == 0)
            {
                return false;
            }

            var originalName = StripGenericArgs(originalParent);
            var arity = HarvestedArity(originalName, typePlans);
            if (arity == 0)
            {
                return false;
            }

            if (arity is int expected && expected != args.Count)
            {
                return false;
            }

            result = originalName + "<" + string.Join(", ", args) + ">";
            return true;
        }

        private static int? HarvestedArity(
            string parentRef,
            IReadOnlyDictionary<string, TypeHarvestPlan> typePlans)
        {
            var unqual = UnqualifiedTypeName(parentRef);
            if (typePlans.TryGetValue("\\" + unqual, out var direct))
            {
                return direct.Generics.Count;
            }

            TypeHarvestPlan? found = null;
            foreach (var (key, plan) in typePlans)
            {
                if (!string.Equals(UnqualifiedTypeName(key), unqual, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (found is not null)
                {
                    return null;
                }

                found = plan;
            }

            return found?.Generics.Count;
        }

        internal static bool TrySplitGenericInstantiation(
            string type,
            out string name,
            out List<string> args)
        {
            name = (type ?? "").Trim();
            args = [];
            if (name.Length == 0)
            {
                return false;
            }

            var open = name.IndexOf('<');
            if (open < 0)
            {
                return true;
            }

            if (!name.EndsWith('>'))
            {
                return false;
            }

            var inner = name[(open + 1)..^1];
            name = name[..open].Trim();
            args = SplitTypeArgumentList(inner);
            return name.Length > 0;
        }

        private static List<string> SplitTypeArgumentList(string inner)
        {
            var args = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < inner.Length; i++)
            {
                var ch = inner[i];
                if (ch is '<' or '(')
                {
                    depth++;
                }
                else if (ch is '>' or ')')
                {
                    depth--;
                }
                else if (ch == ',' && depth == 0)
                {
                    var piece = inner[start..i].Trim();
                    if (piece.Length > 0)
                    {
                        args.Add(piece);
                    }

                    start = i + 1;
                }
            }

            var last = inner[start..].Trim();
            if (last.Length > 0)
            {
                args.Add(last);
            }

            return args;
        }

        private static string StripGenericArgs(string type)
        {
            var trimmed = (type ?? "").Trim();
            var open = trimmed.IndexOf('<');
            return open < 0 ? trimmed : trimmed[..open].Trim();
        }

        private static string UnqualifiedTypeName(string type)
        {
            var stripped = StripGenericArgs(type).TrimStart('\\');
            var slash = stripped.LastIndexOf('\\');
            return slash < 0 ? stripped : stripped[(slash + 1)..];
        }

        /// <summary>
        /// True when <paramref name="type"/> uses a template placeholder that is not in
        /// <paramref name="declaredGenericNames"/>. Conventional <c>T</c>/<c>TValue</c> names
        /// are always placeholders; other identifiers (e.g. <c>Start</c>) are placeholders
        /// only when listed in <paramref name="knownPlaceholders"/> — typically the class's
        /// harvested <c>@template</c> names — so a member type is kept only if the class
        /// actually gained that generic.
        /// </summary>
        internal static bool ReferencesUndeclaredTemplate(
            string? type,
            IReadOnlySet<string> declaredGenericNames,
            IReadOnlySet<string>? knownPlaceholders = null)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            foreach (var id in EnumerateUnqualifiedIdentifiers(type))
            {
                if (PhpAstTypeExtractor.BuiltinTypes.Contains(id))
                {
                    continue;
                }

                if (declaredGenericNames.Contains(id))
                {
                    continue;
                }

                if (IsConventionalTemplateName(id)
                    || (knownPlaceholders is not null && knownPlaceholders.Contains(id)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Conventional PHPStan/Psalm placeholders: <c>T</c>, <c>TValue</c>, <c>TKey</c>,
        /// <c>TSend</c> — a leading <c>T</c> followed by end-of-name or an uppercase letter
        /// and mixed-case remainder. All-caps names such as <c>TYPE_DEFAULT</c> are PHP
        /// constants, not templates.
        /// </summary>
        internal static bool IsConventionalTemplateName(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] != 'T')
            {
                return false;
            }

            if (id.Length == 1)
            {
                return true;
            }

            if (!char.IsAsciiLetterUpper(id[1]) || id.Contains('_'))
            {
                return false;
            }

            var hasLower = false;
            for (var i = 1; i < id.Length; i++)
            {
                if (char.IsAsciiLetterLower(id[i]))
                {
                    hasLower = true;
                    break;
                }
            }

            return hasLower || id.Length == 2;
        }

        /// <summary>
        /// Locations in overlay tyhpdef text where a conventional template name is used
        /// but not declared on the enclosing type or function (e.g. <c>ArrayAccess::offsetExists: TKey</c>).
        /// </summary>
        internal static IReadOnlyList<string> FindUndeclaredConventionalTemplates(string tyhpdefText)
        {
            var issues = new List<string>();
            if (string.IsNullOrEmpty(tyhpdefText))
            {
                return issues;
            }

            var typeName = "";
            var typeGenerics = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rawLine in tyhpdefText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0
                    || line.StartsWith("//", StringComparison.Ordinal)
                    || line.StartsWith('*')
                    || line.StartsWith("/*", StringComparison.Ordinal)
                    || line.StartsWith("*/", StringComparison.Ordinal)
                    || line.StartsWith("<?", StringComparison.Ordinal)
                    || line.StartsWith("namespace ", StringComparison.Ordinal)
                    || line.StartsWith("use ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryParseTypeHeader(line, out var parsedName, out var parsedGenerics))
                {
                    if (parsedGenerics.Count == 0
                        && typeGenerics.Count > 0
                        && string.Equals(parsedName, typeName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    typeName = parsedName;
                    typeGenerics = parsedGenerics;
                    continue;
                }

                if (line == "}")
                {
                    typeName = "";
                    typeGenerics.Clear();
                    continue;
                }

                if (!line.Contains("function", StringComparison.Ordinal)
                    || !TryParseFunctionSignature(line, out var funcName, out var funcGenerics))
                {
                    continue;
                }

                var declared = new HashSet<string>(typeGenerics, StringComparer.Ordinal);
                foreach (var name in funcGenerics)
                {
                    declared.Add(name);
                }

                foreach (var id in EnumerateUnqualifiedIdentifiers(line))
                {
                    if (PhpAstTypeExtractor.BuiltinTypes.Contains(id) || declared.Contains(id))
                    {
                        continue;
                    }

                    if (IsConventionalTemplateName(id))
                    {
                        var location = string.IsNullOrEmpty(typeName) ? funcName : typeName + "::" + funcName;
                        issues.Add(location + ": " + id);
                    }
                }
            }

            return issues;
        }

        private static bool MergeInferredConventionalTemplates(
            List<TyhpdefGenericParameter> generics,
            IReadOnlyDictionary<string, StubSymbol> matches,
            IReadOnlySet<string>? excludeNames)
        {
            if (matches.Count == 0)
            {
                return false;
            }

            var names = new HashSet<string>(generics.Select(g => g.Name), StringComparer.Ordinal);
            var added = false;
            foreach (var symbol in matches.Values)
            {
                foreach (var type in EnumerateStubTypeStrings(symbol))
                {
                    foreach (var id in EnumerateUnqualifiedIdentifiers(type))
                    {
                        if (!IsConventionalTemplateName(id))
                        {
                            continue;
                        }

                        if (excludeNames is not null && excludeNames.Contains(id))
                        {
                            continue;
                        }

                        if (!names.Add(id))
                        {
                            continue;
                        }

                        generics.Add(new TyhpdefGenericParameter { Name = id });
                        added = true;
                    }
                }
            }

            return added;
        }

        private static IEnumerable<string> EnumerateStubTypeStrings(StubSymbol symbol)
        {
            if (!string.IsNullOrWhiteSpace(symbol.NativeReturnType))
            {
                yield return symbol.NativeReturnType;
            }

            if (!string.IsNullOrWhiteSpace(symbol.PhpDocType))
            {
                yield return symbol.PhpDocType;
            }

            if (!string.IsNullOrWhiteSpace(symbol.PhpDoc.ReturnTag?.TypeExpression))
            {
                yield return symbol.PhpDoc.ReturnTag!.TypeExpression!;
            }

            if (!string.IsNullOrWhiteSpace(symbol.PhpDoc.VarTag?.TypeExpression))
            {
                yield return symbol.PhpDoc.VarTag!.TypeExpression!;
            }

            foreach (var parameter in symbol.Parameters)
            {
                if (!string.IsNullOrWhiteSpace(parameter.NativeType))
                {
                    yield return parameter.NativeType;
                }
            }

            foreach (var tag in symbol.PhpDoc.ParamTags)
            {
                if (!string.IsNullOrWhiteSpace(tag.TypeExpression))
                {
                    yield return tag.TypeExpression!;
                }
            }
        }

        private static bool SameGenericParameters(
            IReadOnlyList<TyhpdefGenericParameter>? left,
            IReadOnlyList<TyhpdefGenericParameter>? right)
        {
            var a = left ?? [];
            var b = right ?? [];
            if (a.Count != b.Count)
            {
                return false;
            }

            for (var i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i].Name, b[i].Name, StringComparison.Ordinal)
                    || !string.Equals(a[i].Constraint ?? "", b[i].Constraint ?? "", StringComparison.Ordinal)
                    || !string.Equals(a[i].Default ?? "", b[i].Default ?? "", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseTypeHeader(
            string line,
            out string typeName,
            out HashSet<string> genericNames)
        {
            typeName = "";
            genericNames = new HashSet<string>(StringComparer.Ordinal);
            var index = IndexOfTypeKindKeyword(line);
            if (index < 0)
            {
                return false;
            }

            index = SkipWhitespace(line, index);
            if (index >= line.Length)
            {
                return false;
            }

            var nameStart = index;
            if (line[index] == '\\')
            {
                index++;
            }

            while (index < line.Length && (char.IsAsciiLetterOrDigit(line[index]) || line[index] is '_' or '\\'))
            {
                index++;
            }

            if (index == nameStart)
            {
                return false;
            }

            typeName = line[nameStart..index].TrimStart('\\');
            index = SkipWhitespace(line, index);
            if (index + 3 < line.Length
                && line.AsSpan(index, 2).Equals("as", StringComparison.Ordinal)
                && char.IsWhiteSpace(line[index + 2]))
            {
                index = SkipWhitespace(line, index + 2);
                while (index < line.Length && (char.IsAsciiLetterOrDigit(line[index]) || line[index] == '_'))
                {
                    index++;
                }

                index = SkipWhitespace(line, index);
            }

            if (index < line.Length && line[index] == '<')
            {
                var close = FindMatchingAngleClose(line, index);
                if (close < 0)
                {
                    return true;
                }

                genericNames = ParseGenericParameterNames(line[(index + 1)..close]);
            }

            return true;
        }

        private static int IndexOfTypeKindKeyword(string line)
        {
            foreach (var keyword in new[] { "interface ", "trait ", "enum ", "class " })
            {
                var index = 0;
                while ((index = line.IndexOf(keyword, index, StringComparison.Ordinal)) >= 0)
                {
                    if (index == 0 || char.IsWhiteSpace(line[index - 1]))
                    {
                        return index + keyword.Length;
                    }

                    index += keyword.Length;
                }
            }

            return -1;
        }

        private static bool TryParseFunctionSignature(
            string line,
            out string functionName,
            out HashSet<string> genericNames)
        {
            functionName = "";
            genericNames = new HashSet<string>(StringComparer.Ordinal);
            var keyword = line.IndexOf("function", StringComparison.Ordinal);
            if (keyword < 0)
            {
                return false;
            }

            var index = SkipWhitespace(line, keyword + "function".Length);
            if (index < line.Length && line[index] == '&')
            {
                index = SkipWhitespace(line, index + 1);
            }

            var nameStart = index;
            while (index < line.Length && (char.IsAsciiLetterOrDigit(line[index]) || line[index] == '_'))
            {
                index++;
            }

            if (index == nameStart)
            {
                return false;
            }

            functionName = line[nameStart..index];
            index = SkipWhitespace(line, index);
            if (index < line.Length && line[index] == '<')
            {
                var close = FindMatchingAngleClose(line, index);
                if (close >= 0)
                {
                    genericNames = ParseGenericParameterNames(line[(index + 1)..close]);
                }
            }

            return true;
        }

        private static HashSet<string> ParseGenericParameterNames(string list)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var depth = 0;
            var start = 0;
            for (var i = 0; i <= list.Length; i++)
            {
                char ch = i < list.Length ? list[i] : ',';
                if (ch == '<')
                {
                    depth++;
                }
                else if (ch == '>' && depth > 0)
                {
                    depth--;
                }
                else if (ch == ',' && depth == 0)
                {
                    var part = list[start..i].Trim();
                    var nameEnd = 0;
                    while (nameEnd < part.Length
                        && (char.IsAsciiLetterOrDigit(part[nameEnd]) || part[nameEnd] == '_'))
                    {
                        nameEnd++;
                    }

                    if (nameEnd > 0)
                    {
                        names.Add(part[..nameEnd]);
                    }

                    start = i + 1;
                }
            }

            return names;
        }

        private static int FindMatchingAngleClose(string text, int openIndex)
        {
            var depth = 1;
            for (var i = openIndex + 1; i < text.Length; i++)
            {
                if (text[i] == '<')
                {
                    depth++;
                }
                else if (text[i] == '>')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static int SkipWhitespace(string text, int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            return index;
        }

        private static IEnumerable<string> EnumerateUnqualifiedIdentifiers(string type)
        {
            var i = 0;
            while (i < type.Length)
            {
                if (type[i] == '\\')
                {
                    i++;
                    while (i < type.Length
                        && (char.IsAsciiLetterOrDigit(type[i]) || type[i] is '_' or '\\'))
                    {
                        i++;
                    }

                    continue;
                }

                if (char.IsAsciiLetter(type[i]) || type[i] == '_')
                {
                    var start = i;
                    i++;
                    while (i < type.Length
                        && (char.IsAsciiLetterOrDigit(type[i]) || type[i] == '_'))
                    {
                        i++;
                    }

                    yield return type[start..i];
                    continue;
                }

                i++;
            }
        }

        private static string? MergeDocs(
            string? layer1Doc,
            IReadOnlyDictionary<string, StubSymbol> matches,
            bool includeDocs,
            out bool changed)
        {
            changed = false;
            if (!includeDocs)
            {
                return layer1Doc;
            }

            var existing = PhpDocParser.Parse(layer1Doc);
            StubSymbol? stub = null;
            if (matches.TryGetValue(StubCorpusCache.PsalmId, out var psalm))
            {
                stub = psalm;
            }
            else if (matches.TryGetValue(StubCorpusCache.PhpStanId, out var phpstan))
            {
                stub = phpstan;
            }
            else if (matches.TryGetValue(StubCorpusCache.PhpStormId, out var storm))
            {
                stub = storm;
            }
            else
            {
                stub = matches.Values.FirstOrDefault();
            }

            if (stub is null)
            {
                return layer1Doc;
            }

            var stubDoc = stub.PhpDoc;
            var summary = existing.Summary;
            if (string.IsNullOrWhiteSpace(summary) && !string.IsNullOrWhiteSpace(stubDoc.Summary)
                && !LooksLikePhpVersionPreamble(stubDoc.Summary))
            {
                summary = stubDoc.Summary;
                changed = true;
            }

            var filledParams = false;
            var paramLines = new List<string>();
            var existingParams = IndexParamTags(existing.ParamTags);
            var stubParams = IndexParamTags(stubDoc.ParamTags);
            foreach (var name in existingParams.Keys.Union(stubParams.Keys, StringComparer.OrdinalIgnoreCase))
            {
                existingParams.TryGetValue(name, out var have);
                stubParams.TryGetValue(name, out var extra);
                var text = have?.Description;
                if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(extra?.Description))
                {
                    text = extra!.Description;
                    filledParams = true;
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    paramLines.Add("@param $" + name + (string.IsNullOrWhiteSpace(text) ? "" : " " + text));
                }
            }

            var returnText = existing.ReturnTag?.Description;
            if (string.IsNullOrWhiteSpace(returnText) && !string.IsNullOrWhiteSpace(stubDoc.ReturnTag?.Description))
            {
                returnText = stubDoc.ReturnTag!.Description;
                changed = true;
            }

            var throws = existing.ThrowsTags.ToList();
            if (throws.Count == 0 && stubDoc.ThrowsTags.Count > 0)
            {
                throws = stubDoc.ThrowsTags.ToList();
                changed = true;
            }

            changed |= filledParams;
            if (!changed)
            {
                return layer1Doc;
            }

            return RebuildDoc(existing, summary, paramLines, returnText, throws);
        }

        private static Dictionary<string, PhpDocTag> IndexParamTags(IEnumerable<PhpDocTag> tags)
        {
            var map = new Dictionary<string, PhpDocTag>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tags)
            {
                map[tag.ParameterName ?? ""] = tag;
            }

            return map;
        }

        private static bool LooksLikePhpVersionPreamble(string summary)
            => summary.Contains("(PHP ", StringComparison.Ordinal)
                || summary.StartsWith("PECL ", StringComparison.OrdinalIgnoreCase);

        private static string RebuildDoc(
            PhpDocBlock existing,
            string summary,
            List<string> paramLines,
            string? returnText,
            List<PhpDocTag> throws)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                lines.Add(summary);
            }

            if (!string.IsNullOrWhiteSpace(existing.Description))
            {
                if (lines.Count > 0)
                {
                    lines.Add("");
                }

                lines.Add(existing.Description);
            }

            if (paramLines.Count > 0 || !string.IsNullOrWhiteSpace(returnText) || throws.Count > 0)
            {
                if (lines.Count > 0)
                {
                    lines.Add("");
                }

                lines.AddRange(paramLines);
                if (!string.IsNullOrWhiteSpace(returnText))
                {
                    var type = existing.ReturnTag?.TypeExpression;
                    lines.Add("@return " + (string.IsNullOrWhiteSpace(type) ? returnText : type + " " + returnText));
                }

                foreach (var tag in throws)
                {
                    var piece = tag.TypeExpression ?? "";
                    if (!string.IsNullOrWhiteSpace(tag.Description))
                    {
                        piece = (piece + " " + tag.Description).Trim();
                    }

                    lines.Add("@throws " + piece);
                }
            }

            foreach (var tag in existing.Tags)
            {
                // Param/return/throws (and their phpstan-/psalm- aliases) were already rebuilt
                // above from the merged data; every other tag (@deprecated, @internal, @link,
                // @generated, @var, @template, @see, ...) must survive untouched. Layer 2 only
                // fills holes — it must never silently delete prose Layer 1 already populated
                // (e.g. a php.net manual @deprecated notice) just because some other piece of
                // the doc comment changed.
                if (tag.TagName is "param" or "phpstan-param" or "psalm-param"
                    or "return" or "phpstan-return" or "psalm-return"
                    or "throws" or "phpstan-throws" or "psalm-throws")
                {
                    continue;
                }

                lines.Add("@" + tag.TagName + (string.IsNullOrWhiteSpace(tag.RawContent) ? "" : " " + tag.RawContent.Trim()));
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("/**");
            foreach (var line in lines)
            {
                sb.AppendLine(string.IsNullOrEmpty(line) ? " *" : " * " + line);
            }

            sb.Append(" */");
            return sb.ToString();
        }

        private static string BuildAttributionHeader(IReadOnlyList<StubCorpusDescriptor> corpora)
        {
            var lines = new List<string>
            {
                "/**",
                " * LAYER 2 (stub harvest) — AUTO-GENERATED. DO NOT EDIT.",
                " * Path: _tyhpdef/overlays/stubs/  (community Psalm / PHPStan / Phan / PhpStorm)",
                " * Not Layer 1 baseline (_tyhpdef/*.tyhpdef) and not Layer 3 hand overlay",
                " * (_tyhpdef/overlays/*.tyhpdef).",
                " *",
                " * Sources:",
            };
            foreach (var corpus in corpora)
            {
                lines.Add($" * - {corpus.Title} ({corpus.License}) — {corpus.PageUrl}");
            }

            lines.Add(" * See SOURCES.md / NOTICE next to this tree and the Tyhp repository THIRD_PARTY.md.");
            lines.Add(" */");
            return string.Join("\n", lines);
        }

        private static bool HasAnyDeclaration(TyhpdefFile file)
            => (file.GlobalFunctions?.Count ?? 0) > 0
                || (file.GlobalTypes?.Count ?? 0) > 0
                || (file.Namespaces?.Count ?? 0) > 0;

        /// <summary>
        /// Harvest must not write into hand Layer 3
        /// (<c>_tyhpdef/overlays</c> excluding <c>overlays/stubs</c>).
        /// </summary>
        internal static bool IsForbiddenOutputDirectory(string? outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(outputDirectory);
            }
            catch (Exception)
            {
                return true;
            }

            var normalized = full.Replace('\\', '/').TrimEnd('/');
            return IsHandLayer3OverlayDirectory(normalized);
        }

        /// <summary>
        /// True for <c>…/_tyhpdef/overlays</c> and anything under it except
        /// <c>…/_tyhpdef/overlays/stubs</c> (Layer 2 is regenerable).
        /// </summary>
        private static bool IsHandLayer3OverlayDirectory(string normalized)
        {
            const string marker = "/_tyhpdef/overlays";
            var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var after = normalized[(index + marker.Length)..];
            if (after.Length == 0)
            {
                return true;
            }

            if (after.Equals("/stubs", StringComparison.OrdinalIgnoreCase)
                || after.StartsWith("/stubs/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static bool IsSafeStubsDirectory(string stubsDir, string outputDirectory)
        {
            try
            {
                var stubsFull = Path.GetFullPath(stubsDir);
                var outputFull = Path.GetFullPath(outputDirectory);
                var expected = Path.GetFullPath(Path.Combine(outputFull, "overlays", "stubs"));
                if (!string.Equals(stubsFull, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var parent = Path.GetDirectoryName(stubsFull);
                if (parent is not null
                    && string.Equals(Path.GetFileName(parent), "overlays", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetFileName(stubsFull), "stubs", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return !IsForbiddenOutputDirectory(stubsFull);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
