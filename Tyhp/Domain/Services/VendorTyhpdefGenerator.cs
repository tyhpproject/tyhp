using System.Text.Json;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Orchestrates <c>generate_tyhpdef --vendor</c>: inventory, runtime requires, companions,
    /// then Reflection / PHP-source harvest into <c>vendor-tyhpdef/</c>.
    /// </summary>
    public sealed class VendorTyhpdefGenerator
    {
        internal NativeTyhpdefGenerator NativeGenerator { get; init; } = new();

        internal PhpDelegationTyhpdefGenerator ExtensionGenerator { get; init; } = new();

        internal ICompanionPackageProbe CompanionProbe { get; init; } = new CompanionPackageProbe();

        internal IComposerCommandRunner ComposerRunner { get; init; } = new ComposerCommandRunner();

        /// <summary>Override for first-party <c>tyhpdef/php-ext-*</c> extension names (tests).</summary>
        internal IReadOnlySet<string>? FirstPartyPhpExtensions { get; init; }

        /// <summary>Test hook replacing Reflection harvest so tests never download PHP.</summary>
        internal Action<TyhpdefGenerationOptions, TyhpdefGenerationResult, CancellationToken>? ExtensionGenerateHook { get; init; }

        internal Func<string, string?>? ReadEnvironment { get; init; }

        internal Func<string, IReadOnlySet<string>>? AlwaysPresentExtensions { get; init; }

        public void Generate(
            Project project,
            string vendorDirectory,
            TyhpdefGenerationOptions template,
            TyhpdefGenerationResult result,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(result);

            cancellationToken.ThrowIfCancellationRequested();

            var env = this.ReadEnvironment ?? Environment.GetEnvironmentVariable;
            if (VendorTyhpdefLayout.IsEnvironmentFlagSet(
                    env(VendorTyhpdefLayout.VendorRunningEnvironmentVariable)))
            {
                return;
            }

            // Dump-script opt-out: Composer sets COMPOSER_DEV_MODE when running scripts.
            // Direct `tyhp generate_tyhpdef --vendor` still runs even if the env is set.
            if (VendorTyhpdefLayout.IsEnvironmentFlagSet(
                    env(VendorTyhpdefLayout.NoVendorEnvironmentVariable))
                && env("COMPOSER_DEV_MODE") is not null)
            {
                return;
            }

            var projectRoot = project.GetProjectPath();
            var installedPath = ComposerInstalledInventory.InstalledJsonPath(vendorDirectory);
            if (!File.Exists(installedPath))
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefVendorInstalledJsonMissing,
                    installedPath,
                    0,
                    0,
                    installedPath);
                return;
            }

            ComposerInstalledInventory.Snapshot snapshot;
            try
            {
                snapshot = ComposerInstalledInventory.Load(vendorDirectory, projectRoot);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefVendorInstalledJsonInvalid,
                    installedPath,
                    0,
                    0,
                    installedPath,
                    ex.Message);
                return;
            }

            var outputDir = VendorTyhpdefLayout.OutputDirectory(projectRoot);
            Directory.CreateDirectory(outputDir);
            var alwaysPresent = this.AlwaysPresentExtensions?.Invoke(vendorDirectory)
                ?? LoadAlwaysPresentExtensions(snapshot);
            var phpExtCatalog = this.FirstPartyPhpExtensions ?? DiscoverFirstPartyPhpExtensions();

            this.EnsureComposerBatches(
                projectRoot,
                snapshot,
                phpExtCatalog,
                alwaysPresent,
                result);

            try
            {
                snapshot = ComposerInstalledInventory.Load(vendorDirectory, projectRoot);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
                result.Diagnostics.AddWarning(
                    MessageCode.TyhpdefVendorComposerRequireFailed,
                    installedPath,
                    0,
                    0,
                    "installed.json",
                    ex.Message);
            }

            var attempted = 0;
            var failed = 0;

            foreach (var package in snapshot.Packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShouldSkipLibrary(package, snapshot))
                {
                    continue;
                }

                attempted++;
                if (!this.ProcessLibrary(package, snapshot, projectRoot, outputDir, template, result, cancellationToken))
                {
                    failed++;
                }
            }

            foreach (var extensionName in snapshot.ExtensionNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsAlwaysPresent(extensionName, alwaysPresent))
                {
                    continue;
                }

                attempted++;
                if (!this.ProcessExtension(
                    extensionName,
                    snapshot,
                    phpExtCatalog,
                    outputDir,
                    template,
                    project.PhpVersion,
                    result,
                    cancellationToken))
                {
                    failed++;
                }
            }

            if (attempted > 0 && failed == attempted)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefVendorAllCandidatesFailed,
                    "generate_tyhpdef",
                    0,
                    0,
                    attempted);
            }
        }

        private void EnsureComposerBatches(
            string projectRoot,
            ComposerInstalledInventory.Snapshot snapshot,
            IReadOnlySet<string> phpExtCatalog,
            IReadOnlySet<string> alwaysPresent,
            TyhpdefGenerationResult result)
        {
            var require = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in VendorTyhpdefLayout.RuntimeRequirePackages)
            {
                if (!snapshot.NamesPackage(package))
                {
                    require[package] = "@dev";
                }
            }

            var requireDev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in VendorTyhpdefLayout.TyhpdefRequireDevPackages)
            {
                if (!snapshot.NamesPackage(package))
                {
                    requireDev[package] = "@dev";
                }
            }

            foreach (var extensionName in snapshot.ExtensionNames)
            {
                if (IsAlwaysPresent(extensionName, alwaysPresent))
                {
                    continue;
                }

                if (!CatalogContains(phpExtCatalog, extensionName))
                {
                    continue;
                }

                var companion = VendorTyhpdefLayout.PhpExtCompanionName(extensionName);
                if (!snapshot.NamesPackage(companion))
                {
                    requireDev[companion] = "@dev";
                }
            }

            foreach (var package in snapshot.Packages)
            {
                if (ShouldSkipLibrary(package, snapshot))
                {
                    continue;
                }

                var installComposer = Path.Combine(package.InstallPath, ComposerExtraTyhpPackageManifest.ComposerJsonFileName);
                if (ComposerExtraTyhpPackageManifest.HasPackageObjectAtInstallPath(package.InstallPath))
                {
                    var implName = VendorTyhpdefLayout.ImplCompanionName(package.Name);
                    if (snapshot.IsInstalled(implName))
                    {
                        result.Diagnostics.AddWarning(
                            MessageCode.TyhpdefBundledPackagePreferred,
                            File.Exists(installComposer) ? installComposer : "composer.json",
                            0,
                            0,
                            package.Name,
                            implName);
                    }

                    continue;
                }

                var pointer = ComposerExtraTyhpPackageManifest.TryReadTyhpdefPointer(installComposer);
                if (!string.IsNullOrWhiteSpace(pointer))
                {
                    // The sibling carries extra.tyhp.package. Do not install the community companion.
                    if (!VendorTyhpdefLayout.IsImplPackageName(pointer)
                        && !snapshot.NamesPackage(pointer)
                        && !snapshot.IsInstalled(pointer))
                    {
                        requireDev[pointer] = "*";
                    }

                    continue;
                }

                var companion = VendorTyhpdefLayout.LibraryCompanionName(package.Name);
                if (VendorTyhpdefLayout.IsImplPackageName(companion)
                    || snapshot.IsInstalled(companion)
                    || snapshot.NamesPackage(companion))
                {
                    continue;
                }

                var exact = ExactUpstreamConstraint(package.Version);
                if (exact is null)
                {
                    continue;
                }

                if (this.CompanionProbe.TryResolve(companion, package.Version, projectRoot, out _))
                {
                    requireDev[companion] = exact;
                }
            }

            this.RunRequireBatch(projectRoot, require, dev: false, result);
            this.RunRequireBatch(projectRoot, requireDev, dev: true, result);
        }

        private void RunRequireBatch(
            string projectRoot,
            Dictionary<string, string> packages,
            bool dev,
            TyhpdefGenerationResult result)
        {
            if (packages.Count == 0)
            {
                return;
            }

            foreach (var name in packages.Keys.Where(VendorTyhpdefLayout.IsImplPackageName).ToList())
            {
                packages.Remove(name);
            }

            if (packages.Count == 0)
            {
                return;
            }

            var args = new List<string> { "require", "--no-scripts", "--no-interaction" };
            if (dev)
            {
                args.Add("--dev");
            }

            foreach (var (name, constraint) in packages.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                args.Add(name + ":" + constraint);
            }

            var env = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [VendorTyhpdefLayout.VendorRunningEnvironmentVariable] = "1",
            };

            ComposerCommandResult commandResult;
            try
            {
                commandResult = this.ComposerRunner.Run(projectRoot, args, env);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result.Diagnostics.AddWarning(
                    MessageCode.TyhpdefVendorComposerRequireFailed,
                    "composer.json",
                    0,
                    0,
                    string.Join(" ", packages.Keys.Order(StringComparer.OrdinalIgnoreCase)),
                    ex.Message);
                return;
            }

            if (commandResult.ExitCode == 0)
            {
                return;
            }

            var detail = string.IsNullOrWhiteSpace(commandResult.StandardError)
                ? commandResult.StandardOutput
                : commandResult.StandardError;
            result.Diagnostics.AddWarning(
                MessageCode.TyhpdefVendorComposerRequireFailed,
                "composer.json",
                0,
                0,
                string.Join(" ", packages.Keys.Order(StringComparer.OrdinalIgnoreCase)),
                Truncate(detail));
        }

        private bool ProcessLibrary(
            ComposerInstalledInventory.Package package,
            ComposerInstalledInventory.Snapshot snapshot,
            string projectRoot,
            string outputDir,
            TyhpdefGenerationOptions template,
            TyhpdefGenerationResult result,
            CancellationToken cancellationToken)
        {
            var stubPath = Path.Combine(outputDir, VendorTyhpdefLayout.PackageStubFileName(package.Name));
            var externsPath = Path.Combine(outputDir, VendorTyhpdefLayout.PackageExternsFileName(package.Name));
            var companion = VendorTyhpdefLayout.LibraryCompanionName(package.Name);
            var implCompanion = VendorTyhpdefLayout.ImplCompanionName(package.Name);
            var installComposer = Path.Combine(package.InstallPath, ComposerExtraTyhpPackageManifest.ComposerJsonFileName);

            if (ComposerExtraTyhpPackageManifest.HasPackageObjectAtInstallPath(package.InstallPath))
            {
                // A previous --vendor run may have written this stub. User includes load
                // vendor-tyhpdef after package manifests, so the stale file would override
                // the PHP package's extra.tyhp.package.
                DeleteStub(stubPath);
                DeleteStub(externsPath);
                return true;
            }

            // Sibling types: load extra.tyhp.package on the pointed-at package, not a generated stub
            // and not the community companion. extra.tyhp.impl is not a require.
            if (!string.IsNullOrWhiteSpace(ComposerExtraTyhpPackageManifest.TryReadTyhpdefPointer(installComposer)))
            {
                DeleteStub(stubPath);
                DeleteStub(externsPath);
                return true;
            }

            if (snapshot.IsInstalled(companion) || snapshot.IsInstalled(implCompanion))
            {
                DeleteStub(stubPath);
                DeleteStub(externsPath);
                return true;
            }

            var identity = VendorTyhpdefLayout.Identity(package.Name, package.Version);
            if (!template.Overwrite && VendorTyhpdefLayout.FileHasMatchingIdentity(stubPath, identity))
            {
                return true;
            }

            if (!HasAutoloadablePhp(package.InstallPath, template.IncludeDev))
            {
                return true;
            }

            var inner = new TyhpdefGenerationResult();
            var options = template with
            {
                Mode = TyhpdefGenerationMode.ComposerPackage,
                PackagePath = package.InstallPath,
                ExtensionName = null,
                SourcePaths = [],
                OutputDirectory = outputDir,
                OutputFileName = stubPath,
                Overwrite = true,
                PreferPhpRuntime = false,
            };

            try
            {
                this.NativeGenerator.Generate(options, inner, projectRoot, cancellationToken);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                WarnCandidate(result, package.Name, ex.Message);
                return false;
            }

            RelocateSharedExterns(outputDir, externsPath, inner);

            if (inner.Diagnostics.HasErrors)
            {
                WarnCandidate(result, package.Name, FirstError(inner));
                TryDeletePartial(inner, stubPath, externsPath, outputDir);
                return false;
            }

            VendorTyhpdefLayout.StampGeneratedFile(stubPath, identity);
            if (File.Exists(externsPath))
            {
                VendorTyhpdefLayout.StampGeneratedFile(externsPath, identity);
            }

            MergeGeneratedFiles(result, inner, stubPath, externsPath);
            return true;
        }

        private bool ProcessExtension(
            string extensionName,
            ComposerInstalledInventory.Snapshot snapshot,
            IReadOnlySet<string> phpExtCatalog,
            string outputDir,
            TyhpdefGenerationOptions template,
            string? projectPhpVersion,
            TyhpdefGenerationResult result,
            CancellationToken cancellationToken)
        {
            var stubPath = Path.Combine(outputDir, VendorTyhpdefLayout.ExtensionStubFileName(extensionName));
            var companion = VendorTyhpdefLayout.PhpExtCompanionName(extensionName);

            if (CatalogContains(phpExtCatalog, extensionName) || snapshot.IsInstalled(companion))
            {
                DeleteStub(stubPath);
                return true;
            }

            // Fall back to the project's configured/defaulted PHP version (not a hardcoded
            // constant) so the identity stamp — and the managed PHP minor actually used to
            // generate — change when the project's target PHP version changes, even though
            // `--vendor` runs almost never pass an explicit `--php-version`.
            var targetPhpVersion = template.PhpVersion ?? projectPhpVersion ?? "php";
            var identity = VendorTyhpdefLayout.Identity("ext-" + extensionName, targetPhpVersion);
            if (!template.Overwrite && VendorTyhpdefLayout.FileHasMatchingIdentity(stubPath, identity))
            {
                return true;
            }

            var inner = new TyhpdefGenerationResult();
            var options = template with
            {
                Mode = TyhpdefGenerationMode.PhpExtension,
                ExtensionName = extensionName,
                PackagePath = null,
                SourcePaths = [],
                OutputDirectory = outputDir,
                OutputFileName = stubPath,
                Overwrite = true,
                PreferPhpRuntime = true,
            };

            try
            {
                if (this.ExtensionGenerateHook is not null)
                {
                    this.ExtensionGenerateHook(options, inner, cancellationToken);
                }
                else if (options.PhpTargets.Count > 0)
                {
                    this.ExtensionGenerator.GenerateFromPhpTargets(options, inner, cancellationToken);
                }
                else
                {
                    this.ExtensionGenerator.GenerateFromExtension(
                        options,
                        inner,
                        projectPhpVersion,
                        cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                WarnCandidate(result, "ext-" + extensionName, ex.Message);
                return false;
            }

            if (inner.Diagnostics.HasErrors)
            {
                var phpUnavailable = inner.Diagnostics.Errors.Any(d =>
                    d.Code is MessageCode.TyhpdefPhpNotFound
                        or MessageCode.TyhpdefPhpRuntimeDownloadFailed
                        or MessageCode.TyhpdefPhpExtensionNotProvisioned);
                WarnCandidate(result, "ext-" + extensionName, FirstError(inner));
                TryDeletePartial(inner, stubPath, null, outputDir);
                // Managed PHP missing is warn-and-continue, not a whole-run failure.
                return phpUnavailable;
            }

            VendorTyhpdefLayout.StampGeneratedFile(stubPath, identity);
            MergeGeneratedFiles(result, inner, stubPath, null);
            return true;
        }

        private static void RelocateSharedExterns(
            string outputDir,
            string destPath,
            TyhpdefGenerationResult inner)
        {
            var shared = Path.Combine(outputDir, TyhpdefOutputLayout.ExternsFileName);
            if (!File.Exists(shared))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Move(shared, destPath, overwrite: true);
                for (var i = 0; i < inner.GeneratedFiles.Count; i++)
                {
                    if (string.Equals(inner.GeneratedFiles[i], shared, StringComparison.OrdinalIgnoreCase))
                    {
                        inner.GeneratedFiles[i] = destPath;
                    }
                }

                if (!inner.GeneratedFiles.Contains(destPath, StringComparer.OrdinalIgnoreCase))
                {
                    inner.GeneratedFiles.Add(destPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(shared);
            }
        }

        private static void MergeGeneratedFiles(
            TyhpdefGenerationResult outer,
            TyhpdefGenerationResult inner,
            string stubPath,
            string? externsPath)
        {
            foreach (var file in inner.GeneratedFiles)
            {
                if (!outer.GeneratedFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    outer.GeneratedFiles.Add(file);
                }
            }

            if (File.Exists(stubPath) && !outer.GeneratedFiles.Contains(stubPath, StringComparer.OrdinalIgnoreCase))
            {
                outer.GeneratedFiles.Add(stubPath);
            }

            if (!string.IsNullOrWhiteSpace(externsPath)
                && File.Exists(externsPath)
                && !outer.GeneratedFiles.Contains(externsPath, StringComparer.OrdinalIgnoreCase))
            {
                outer.GeneratedFiles.Add(externsPath);
            }

            outer.ClassCount += inner.ClassCount;
            outer.FunctionCount += inner.FunctionCount;
            outer.ConstantCount += inner.ConstantCount;
            outer.TotalDeclarations += inner.TotalDeclarations;
        }

        private static void TryDeletePartial(
            TyhpdefGenerationResult inner,
            string stubPath,
            string? externsPath,
            string outputDir)
        {
            foreach (var file in inner.GeneratedFiles)
            {
                TryDelete(file);
            }

            TryDelete(stubPath);
            if (!string.IsNullOrWhiteSpace(externsPath))
            {
                TryDelete(externsPath);
            }

            TryDelete(Path.Combine(outputDir, TyhpdefOutputLayout.ExternsFileName));
        }

        private static string? ExactUpstreamConstraint(string? installedVersion)
        {
            var raw = (installedVersion ?? "").Trim();
            if (raw.Length == 0 || raw.StartsWith("dev-", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return raw;
        }

        private static bool ShouldSkipLibrary(
            ComposerInstalledInventory.Package package,
            ComposerInstalledInventory.Snapshot snapshot)
        {
            if (string.Equals(package.Name, "php", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(package.Name, VendorTyhpdefLayout.CompilerPackageName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.RootPackageName)
                && string.Equals(package.Name, snapshot.RootPackageName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (package.Name.StartsWith("ext-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (VendorTyhpdefLayout.IsTyhpdefPackage(package.Name))
            {
                return true;
            }

            return false;
        }

        private static bool HasAutoloadablePhp(string installPath, bool includeDev)
        {
            var collected = ComposerAutoloadPhpCollector.Collect(installPath, includeDev);
            if (collected.ErrorKey is not null)
            {
                return false;
            }

            return collected.Files.Any(f => f.EndsWith(".php", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsAlwaysPresent(string extensionName, IReadOnlySet<string> alwaysPresent)
            => CatalogContains(alwaysPresent, extensionName);

        private static bool CatalogContains(IReadOnlySet<string> catalog, string name)
        {
            if (catalog.Contains(name))
            {
                return true;
            }

            foreach (var item in catalog)
            {
                if (item.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlySet<string> DiscoverFirstPartyPhpExtensions()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var packageName in ComposerJsonService.GetRuntimePackagePathMap().Keys)
            {
                if (packageName.StartsWith(VendorTyhpdefLayout.PhpExtCompanionPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(packageName[VendorTyhpdefLayout.PhpExtCompanionPrefix.Length..]);
                }
            }

            return names;
        }

        private static IReadOnlySet<string> LoadAlwaysPresentExtensions(
            ComposerInstalledInventory.Snapshot snapshot)
        {
            var names = new HashSet<string>(
                VendorTyhpdefLayout.AlwaysPresentPhpExtensions,
                StringComparer.OrdinalIgnoreCase);

            if (snapshot.TryGetInstalled("tyhpdef/php", out var phpPackage))
            {
                TryAddExtensionsFromComposerJson(Path.Combine(phpPackage.InstallPath, "composer.json"), names);
            }

            var map = ComposerJsonService.GetRuntimePackagePathMap();
            if (map.TryGetValue("tyhpdef/php", out var runtimePhp))
            {
                TryAddExtensionsFromComposerJson(Path.Combine(runtimePhp, "composer.json"), names);
            }

            return names;
        }

        private static void TryAddExtensionsFromComposerJson(string composerJson, HashSet<string> names)
        {
            if (!File.Exists(composerJson))
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(composerJson));
                if (!document.RootElement.TryGetProperty("extra", out var extra)
                    || extra.ValueKind != JsonValueKind.Object
                    || !extra.TryGetProperty("tyhp", out var tyhp)
                    || tyhp.ValueKind != JsonValueKind.Object
                    || !tyhp.TryGetProperty("extensions", out var extensions)
                    || extensions.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                foreach (var item in extensions.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var name = item.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            names.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
            }
        }

        private static void WarnCandidate(TyhpdefGenerationResult result, string name, string detail)
        {
            result.Diagnostics.AddWarning(
                MessageCode.TyhpdefVendorCandidateFailed,
                "generate_tyhpdef",
                0,
                0,
                name,
                Truncate(detail));
        }

        private static string FirstError(TyhpdefGenerationResult inner)
        {
            var error = inner.Diagnostics.Errors.FirstOrDefault();
            return error?.Message ?? "generation failed";
        }

        private static string Truncate(string text)
        {
            var trimmed = (text ?? "").Trim().Replace('\n', ' ').Replace('\r', ' ');
            return trimmed.Length <= 240 ? trimmed : trimmed[..237] + "...";
        }

        private static void DeleteStub(string path) => TryDelete(path);

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
