using System.Text.Json;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder
{
    public partial class TyhpBinder
    {
        private readonly List<PendingFallbackFunction> _pendingFallbackFunctions = new();
        private readonly List<PendingFallbackConstant> _pendingFallbackConstants = new();
        private readonly Dictionary<string, FallbackPackageIdentity> _fallbackPackageIdentity = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _providedExtensions = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Chooses one package's <c>fallback function</c> signatures for each name.
        /// Include-layer fallbacks resolve before overlays. Overlay fallbacks resolve after
        /// overlays bind, so a plain overlay function can replace a harvested fallback first.
        /// </summary>
        internal void ResolveFallbackFunctions()
        {
            var order = ComposerInstalledInventory.TryReadFilesAutoloadPackageOrder(
                _compilationOptions?.ProjectPath);

            if (_pendingFallbackFunctions.Count > 0)
            {
                var pending = _pendingFallbackFunctions.ToList();
                _pendingFallbackFunctions.Clear();
                foreach (var group in pending.GroupBy(static item => item.GroupKey, StringComparer.Ordinal))
                {
                    ResolveFallbackGroup(group.ToList(), order);
                }
            }

            ResolveFallbackConstants(order);
        }

        private void DeferFallbackFunction(
            FunctionDeclarationSymbol symbol,
            TyhpdefImportFunctionDeclAst funcDecl,
            IBaseScope targetScope)
        {
            _pendingFallbackFunctions.Add(new PendingFallbackFunction
            {
                Symbol = symbol,
                Decl = funcDecl,
                TargetScope = targetScope,
                PackageSource = _currentTyhpdefPackageSource,
                FileName = _currentFileName,
                GroupKey = FallbackGroupKey(targetScope, symbol.Name),
            });
        }

        private void ResolveFallbackGroup(
            List<PendingFallbackFunction> group,
            IReadOnlyDictionary<string, int>? order)
        {
            var sample = group[0];
            var existing = FindExistingTyhpdefSymbol(sample.TargetScope, sample.Symbol.Name, wantFunction: true)
                as FunctionDeclarationSymbol;

            if (existing is { IsFallback: false })
            {
                ResolveFallbacksAgainstOrdinary(group, existing, order);
                return;
            }

            if (existing is { IsFallback: true })
            {
                ResolveFallbacksAgainstRegisteredFallback(group, existing, order);
                return;
            }

            ResolveFallbackOnly(group, order);
        }

        private void ResolveFallbackOnly(
            List<PendingFallbackFunction> group,
            IReadOnlyDictionary<string, int>? order)
        {
            var byPackage = group
                .GroupBy(static item => item.PackageSource, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (byPackage.Count == 1)
            {
                RegisterFallbackPackage(byPackage[0]);
                return;
            }

            if (!TryChooseWinner(byPackage.Select(static package => package.Key), order, out var winner))
            {
                ReportFallbackOrderUnknown(group);
                return;
            }

            var winnerDecls = byPackage.First(package =>
                string.Equals(package.Key, winner, StringComparison.OrdinalIgnoreCase));
            RegisterFallbackPackage(winnerDecls);
            var winnerSignatures = SignatureSet(winnerDecls.First().Symbol, winnerDecls);
            var winnerName = PackageDisplayName(winner);
            foreach (var package in byPackage)
            {
                if (string.Equals(package.Key, winner, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                WarnFallbackSignatureMismatches(package, winnerSignatures, winnerName);
            }
        }

        private void ResolveFallbacksAgainstOrdinary(
            List<PendingFallbackFunction> group,
            FunctionDeclarationSymbol existing,
            IReadOnlyDictionary<string, int>? order)
        {
            if (OrdinaryBeatsFallback(existing))
            {
                WarnAgainstSymbol(group, existing, PackageDisplayName(EnginePackageLabel(existing)));
                return;
            }

            if (!_tyhpdefRegistrar!.TryGetPackageSource(existing, out var ordinarySource))
            {
                ReportFallbackOrderUnknown(group);
                return;
            }

            var ordinarySignatures = SignatureSet(existing, Array.Empty<PendingFallbackFunction>());
            var ordinaryName = PackageDisplayName(ordinarySource);
            var ordinaryRanked = TryRankPackage(ordinarySource, order, out var ordinaryRank);

            foreach (var package in group.GroupBy(static item => item.PackageSource, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(package.Key, ordinarySource, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fallbackRanked = TryRankPackage(package.Key, order, out var fallbackRank);
                if (order is null || !ordinaryRanked || !fallbackRanked)
                {
                    ReportFallbackOrderUnknown(package);
                    continue;
                }

                if (fallbackRank < ordinaryRank)
                {
                    foreach (var item in package)
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.TyhpdefFallbackRedeclare,
                            item.Decl,
                            item.FileName,
                            item.Symbol.Name,
                            ordinaryName,
                            PackageDisplayName(package.Key));
                    }

                    continue;
                }

                WarnFallbackSignatureMismatches(package, ordinarySignatures, ordinaryName);
            }
        }

        private void ResolveFallbacksAgainstRegisteredFallback(
            List<PendingFallbackFunction> group,
            FunctionDeclarationSymbol existing,
            IReadOnlyDictionary<string, int>? order)
        {
            if (!_tyhpdefRegistrar!.TryGetPackageSource(existing, out var existingSource))
            {
                ReportFallbackOrderUnknown(group);
                return;
            }

            var packages = group
                .Select(static item => item.PackageSource)
                .Append(existingSource)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (packages.Count == 1)
            {
                RegisterFallbackPackage(group);
                return;
            }

            if (!TryChooseWinner(packages, order, out var winner))
            {
                ReportFallbackOrderUnknown(group);
                return;
            }

            var winnerName = PackageDisplayName(winner);
            if (!string.Equals(winner, existingSource, StringComparison.OrdinalIgnoreCase))
            {
                var winnerDecls = group.Where(item =>
                    string.Equals(item.PackageSource, winner, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!SignatureSet(existing, Array.Empty<PendingFallbackFunction>())
                    .SetEquals(SignatureSet(winnerDecls[0].Symbol, winnerDecls)))
                {
                    _diagnostics.AddWarningFromAst(
                        MessageCode.TyhpdefFallbackSignatureMismatch,
                        existing.DeclaringAstNode ?? winnerDecls[0].Decl,
                        existing.SourceFile,
                        existing.Name,
                        PackageDisplayName(existingSource),
                        winnerName);
                }

                RemoveTyhpdefSymbol(existing);
                RegisterFallbackPackage(winnerDecls);
            }
            else
            {
                RegisterFallbackPackage(group.Where(item =>
                    string.Equals(item.PackageSource, winner, StringComparison.OrdinalIgnoreCase)));
            }

            var kept = FindExistingTyhpdefSymbol(group[0].TargetScope, group[0].Symbol.Name, wantFunction: true)
                as FunctionDeclarationSymbol;
            var keptSignatures = kept is null
                ? SignatureSet(existing, Array.Empty<PendingFallbackFunction>())
                : SignatureSet(kept, Array.Empty<PendingFallbackFunction>());
            foreach (var package in group.GroupBy(static item => item.PackageSource, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(package.Key, winner, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                WarnFallbackSignatureMismatches(package, keptSignatures, winnerName);
            }
        }

        private bool TryChooseWinner(
            IEnumerable<string> packageSources,
            IReadOnlyDictionary<string, int>? order,
            out string winner)
        {
            winner = "";
            var sources = packageSources.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (sources.Count == 0 || order is null)
            {
                return false;
            }

            var bestRank = int.MaxValue;
            var bestCount = 0;
            foreach (var source in sources)
            {
                if (!TryRankPackage(source, order, out var rank))
                {
                    return false;
                }

                if (rank < bestRank)
                {
                    bestRank = rank;
                    bestCount = 1;
                    winner = source;
                }
                else if (rank == bestRank)
                {
                    bestCount++;
                }
            }

            return bestCount == 1;
        }

        private void RegisterFallbackPackage(IEnumerable<PendingFallbackFunction> items)
        {
            foreach (var item in items)
            {
                var savedPackage = _currentTyhpdefPackageSource;
                var savedFile = _currentFileName;
                _currentTyhpdefPackageSource = item.PackageSource;
                _currentFileName = item.FileName;
                try
                {
                    if (TryConsumeTyhpdefExternFunctionMerge(
                            item.Decl,
                            item.TargetScope,
                            item.Symbol.Name,
                            out _,
                            out _))
                    {
                        continue;
                    }

                    item.Symbol.IsFallback = true;
                    TryRegisterTyhpdefFunction(item.Symbol, item.Decl, item.TargetScope);
                }
                finally
                {
                    _currentTyhpdefPackageSource = savedPackage;
                    _currentFileName = savedFile;
                }
            }
        }

        private void WarnFallbackSignatureMismatches(
            IEnumerable<PendingFallbackFunction> losers,
            HashSet<string> winnerSignatures,
            string winnerName)
        {
            foreach (var item in losers)
            {
                if (winnerSignatures.Contains(SignatureKey(item.Symbol)))
                {
                    continue;
                }

                _diagnostics.AddWarningFromAst(
                    MessageCode.TyhpdefFallbackSignatureMismatch,
                    item.Decl,
                    item.FileName,
                    item.Symbol.Name,
                    PackageDisplayName(item.PackageSource),
                    winnerName);
            }
        }

        private void WarnAgainstSymbol(
            IEnumerable<PendingFallbackFunction> losers,
            FunctionDeclarationSymbol winner,
            string winnerName)
        {
            var signatures = SignatureSet(winner, Array.Empty<PendingFallbackFunction>());
            WarnFallbackSignatureMismatches(losers, signatures, winnerName);
        }

        private void ReportFallbackOrderUnknown(IEnumerable<PendingFallbackFunction> items)
        {
            foreach (var item in items)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefFallbackOrderUnknown,
                    item.Decl,
                    item.FileName,
                    item.Symbol.Name);
            }
        }

        private bool OrdinaryBeatsFallback(FunctionDeclarationSymbol existing)
        {
            if (_tyhpdefRegistrar?.TryGetPackageSource(existing, out var source) != true)
            {
                return true;
            }

            return IdentityFor(source).IsEngine;
        }

        private static string EnginePackageLabel(BaseSymbol existing)
            => existing.SourceFile.Length == 0 ? "tyhpdef/php" : existing.SourceFile;

        private bool TryRankPackage(
            string packageSource,
            IReadOnlyDictionary<string, int>? order,
            out int rank)
        {
            rank = int.MaxValue;
            if (order is null)
            {
                return false;
            }

            var identity = IdentityFor(packageSource);
            var found = false;
            IEnumerable<string> names = identity.UpstreamNames.Count > 0
                ? identity.UpstreamNames
                : [identity.DisplayName];
            foreach (var name in names)
            {
                if (!order.TryGetValue(name, out var index) || index >= rank)
                {
                    continue;
                }

                rank = index;
                found = true;
            }

            return found;
        }

        private string PackageDisplayName(string packageSource)
        {
            if (string.IsNullOrWhiteSpace(packageSource))
            {
                return "tyhpdef/php";
            }

            return IdentityFor(packageSource).DisplayName;
        }

        private FallbackPackageIdentity IdentityFor(string packageSource)
        {
            if (_fallbackPackageIdentity.TryGetValue(packageSource, out var cached))
            {
                return cached;
            }

            var display = Path.GetFileName(packageSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(display))
            {
                display = packageSource;
            }

            var upstream = new List<string>();
            var isEngine = packageSource.StartsWith("<embedded", StringComparison.Ordinal);
            var composerPath = Path.Combine(packageSource, "composer.json");
            if (File.Exists(composerPath))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(composerPath));
                    if (document.RootElement.TryGetProperty("name", out var nameNode)
                        && nameNode.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(nameNode.GetString()))
                    {
                        display = nameNode.GetString()!;
                    }

                    CollectUpstreamRequires(document.RootElement, "require", upstream);
                    CollectUpstreamRequires(document.RootElement, "require-dev", upstream);
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    // The directory name stays the display label.
                }
            }

            if (display.Equals("tyhpdef/php", StringComparison.OrdinalIgnoreCase)
                || ReadProvidedExtensions(packageSource).Count > 0)
            {
                isEngine = true;
            }

            var identity = new FallbackPackageIdentity(display, upstream, isEngine);
            _fallbackPackageIdentity[packageSource] = identity;
            return identity;
        }

        private static void CollectUpstreamRequires(JsonElement root, string property, List<string> upstream)
        {
            if (!root.TryGetProperty(property, out var section) || section.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var requirement in section.EnumerateObject())
            {
                if (requirement.Name.Equals("php", StringComparison.OrdinalIgnoreCase)
                    || requirement.Name.StartsWith("ext-", StringComparison.OrdinalIgnoreCase)
                    || requirement.Name.StartsWith("tyhpdef/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!upstream.Contains(requirement.Name, StringComparer.OrdinalIgnoreCase))
                {
                    upstream.Add(requirement.Name);
                }
            }
        }

        private static HashSet<string> SignatureSet(
            FunctionDeclarationSymbol primary,
            IEnumerable<PendingFallbackFunction> extras)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SignatureKey(primary),
            };
            foreach (var overload in primary.Overloads)
            {
                set.Add(SignatureKey(overload));
            }

            foreach (var extra in extras)
            {
                set.Add(SignatureKey(extra.Symbol));
            }

            return set;
        }

        private static string SignatureKey(FunctionDeclarationSymbol symbol)
            => PhpVersionGatedCallableSignature.SpellFunction(symbol.DeclaringAstNode);

        private static string FallbackGroupKey(IBaseScope scope, string symbolName)
        {
            var ns = "";
            for (var current = scope; current != null; current = current.ParentScope)
            {
                if (current.DeclarationSymbol is NamespaceSymbol namespaceSymbol
                    && !string.IsNullOrWhiteSpace(namespaceSymbol.Name))
                {
                    ns = namespaceSymbol.Name.Trim('\\');
                    break;
                }
            }

            var fqn = ns.Length == 0 ? "\\" + symbolName : "\\" + ns + "\\" + symbolName;
            return fqn.ToLowerInvariant();
        }

        internal void NoteTyhpdefPackages(IEnumerable<string> packageSources)
        {
            _providedExtensions.Clear();
            foreach (var source in packageSources)
            {
                foreach (var extension in ReadProvidedExtensions(source))
                {
                    _providedExtensions.Add(extension);
                }
            }
        }

        internal bool ExtensionIsPresent(string name)
            => _providedExtensions.Contains(name);

        private void DeferFallbackConstant(
            ConstantSymbol symbol,
            TyhpdefImportConstAst constDecl,
            IBaseScope targetScope)
        {
            _pendingFallbackConstants.Add(new PendingFallbackConstant
            {
                Symbol = symbol,
                Decl = constDecl,
                TargetScope = targetScope,
                PackageSource = _currentTyhpdefPackageSource,
                FileName = _currentFileName,
                GroupKey = FallbackConstKey(targetScope, symbol.Name),
            });
        }

        private void ResolveFallbackConstants(IReadOnlyDictionary<string, int>? order)
        {
            if (_pendingFallbackConstants.Count == 0)
            {
                return;
            }

            var pending = _pendingFallbackConstants.ToList();
            _pendingFallbackConstants.Clear();
            foreach (var group in pending.GroupBy(static item => item.GroupKey, StringComparer.Ordinal))
            {
                ResolveFallbackConstantGroup(group.ToList(), order);
            }
        }

        private void ResolveFallbackConstantGroup(
            List<PendingFallbackConstant> group,
            IReadOnlyDictionary<string, int>? order)
        {
            var sample = group[0];
            var existing = FindExistingConstant(sample.TargetScope, sample.Symbol.Name);
            if (existing is { IsFallback: false })
            {
                if (OrdinaryBeatsFallbackConstant(existing))
                {
                    WarnConstMismatches(group, existing, PackageDisplayName(EnginePackageLabel(existing)));
                    return;
                }

                if (!_tyhpdefRegistrar!.TryGetPackageSource(existing, out var ordinarySource))
                {
                    ReportConstOrderUnknown(group);
                    return;
                }

                var ordinaryRanked = TryRankPackage(ordinarySource, order, out var ordinaryRank);
                foreach (var package in group.GroupBy(static item => item.PackageSource, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.Equals(package.Key, ordinarySource, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var fallbackRanked = TryRankPackage(package.Key, order, out var fallbackRank);
                    if (order is null || !ordinaryRanked || !fallbackRanked)
                    {
                        ReportConstOrderUnknown(package);
                        continue;
                    }

                    if (fallbackRank < ordinaryRank)
                    {
                        foreach (var item in package)
                        {
                            _diagnostics.AddErrorFromAst(
                                MessageCode.TyhpdefFallbackConstRedeclare,
                                item.Decl,
                                item.FileName,
                                item.Symbol.Name,
                                PackageDisplayName(ordinarySource),
                                PackageDisplayName(package.Key));
                        }

                        continue;
                    }

                    WarnConstMismatches(package, existing, PackageDisplayName(ordinarySource));
                }

                return;
            }

            var byPackage = group.GroupBy(static item => item.PackageSource, StringComparer.OrdinalIgnoreCase).ToList();
            if (byPackage.Count == 1)
            {
                RegisterFallbackConstants(byPackage[0]);
                return;
            }

            if (!TryChooseWinner(byPackage.Select(static package => package.Key), order, out var winner))
            {
                ReportConstOrderUnknown(group);
                return;
            }

            RegisterFallbackConstants(byPackage.First(package =>
                string.Equals(package.Key, winner, StringComparison.OrdinalIgnoreCase)));
            var winnerValue = ConstKey(byPackage.First(package =>
                string.Equals(package.Key, winner, StringComparison.OrdinalIgnoreCase)).First().Symbol);
            foreach (var package in byPackage)
            {
                if (string.Equals(package.Key, winner, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var item in package)
                {
                    if (string.Equals(ConstKey(item.Symbol), winnerValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    _diagnostics.AddWarningFromAst(
                        MessageCode.TyhpdefFallbackConstMismatch,
                        item.Decl,
                        item.FileName,
                        item.Symbol.Name,
                        PackageDisplayName(item.PackageSource),
                        PackageDisplayName(winner));
                }
            }
        }

        private bool OrdinaryBeatsFallbackConstant(ConstantSymbol existing)
        {
            if (_tyhpdefRegistrar?.TryGetPackageSource(existing, out var source) != true)
            {
                return true;
            }

            return IdentityFor(source).IsEngine;
        }

        private void RegisterFallbackConstants(IEnumerable<PendingFallbackConstant> items)
        {
            foreach (var item in items)
            {
                var savedPackage = _currentTyhpdefPackageSource;
                var savedFile = _currentFileName;
                _currentTyhpdefPackageSource = item.PackageSource;
                _currentFileName = item.FileName;
                try
                {
                    item.Symbol.IsFallback = true;
                    TryRegisterTyhpdefTopLevelSymbol(item.Symbol, item.Decl, item.TargetScope);
                }
                finally
                {
                    _currentTyhpdefPackageSource = savedPackage;
                    _currentFileName = savedFile;
                }
            }
        }

        private void WarnConstMismatches(IEnumerable<PendingFallbackConstant> losers, ConstantSymbol winner, string winnerName)
        {
            var winnerValue = ConstKey(winner);
            foreach (var item in losers)
            {
                if (string.Equals(ConstKey(item.Symbol), winnerValue, StringComparison.Ordinal))
                {
                    continue;
                }

                _diagnostics.AddWarningFromAst(
                    MessageCode.TyhpdefFallbackConstMismatch,
                    item.Decl,
                    item.FileName,
                    item.Symbol.Name,
                    PackageDisplayName(item.PackageSource),
                    winnerName);
            }
        }

        private void ReportConstOrderUnknown(IEnumerable<PendingFallbackConstant> items)
        {
            foreach (var item in items)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.TyhpdefFallbackConstOrderUnknown,
                    item.Decl,
                    item.FileName,
                    item.Symbol.Name);
            }
        }

        private static string ConstKey(ConstantSymbol symbol)
        {
            var type = symbol.DeclaredType?.Identifier ?? "";
            var value = symbol.ValueExpression is PhpScalarAst scalar
                ? scalar.ValueString ?? scalar.ValueInt64?.ToString() ?? ""
                : "";
            return type + "=" + value;
        }

        private static string FallbackConstKey(IBaseScope scope, string symbolName)
        {
            var ns = "";
            for (var current = scope; current != null; current = current.ParentScope)
            {
                if (current.DeclarationSymbol is NamespaceSymbol namespaceSymbol
                    && !string.IsNullOrWhiteSpace(namespaceSymbol.Name))
                {
                    ns = namespaceSymbol.Name.Trim('\\');
                    break;
                }
            }

            return ns.Length == 0 ? "\\" + symbolName : "\\" + ns + "\\" + symbolName;
        }

        private static List<string> ReadProvidedExtensions(string packageSource)
        {
            var names = new List<string>();
            var composerPath = Path.Combine(packageSource, "composer.json");
            if (!File.Exists(composerPath))
            {
                return names;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(composerPath));
                CollectExtRequires(document.RootElement, "require", names);
                CollectExtRequires(document.RootElement, "require-dev", names);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return names;
            }

            return names;
        }

        private static void CollectExtRequires(JsonElement root, string property, List<string> names)
        {
            if (!root.TryGetProperty(property, out var section) || section.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var requirement in section.EnumerateObject())
            {
                if (!requirement.Name.StartsWith("ext-", StringComparison.OrdinalIgnoreCase) || requirement.Name.Length <= 4)
                {
                    continue;
                }

                var extension = requirement.Name[4..];
                if (!names.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(extension);
                }
            }
        }

        private sealed class PendingFallbackConstant
        {
            public required ConstantSymbol Symbol { get; init; }
            public required TyhpdefImportConstAst Decl { get; init; }
            public required IBaseScope TargetScope { get; init; }
            public required string PackageSource { get; init; }
            public required string FileName { get; init; }
            public required string GroupKey { get; init; }
        }

        private sealed class PendingFallbackFunction
        {
            public required FunctionDeclarationSymbol Symbol { get; init; }
            public required TyhpdefImportFunctionDeclAst Decl { get; init; }
            public required IBaseScope TargetScope { get; init; }
            public required string PackageSource { get; init; }
            public required string FileName { get; init; }
            public required string GroupKey { get; init; }
        }

        private sealed record FallbackPackageIdentity(
            string DisplayName,
            List<string> UpstreamNames,
            bool IsEngine);
    }
}
