namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Which installed package's <c>extra.tyhp.package</c> supplies types when a PHP
    /// package and <c>tyhpdef/&lt;vendor&gt;-&lt;name&gt;-impl</c> would both bind.
    /// </summary>
    internal static class TyhpdefPackageOwnership
    {
        internal readonly record struct SupersededImpl(
            string ManifestPath,
            string PhpPackageName,
            string ImplPackageName);

        /// <summary>
        /// Impl manifests to skip when the matching PHP package already has
        /// <c>extra.tyhp.package</c>. <c>extra.tyhp.impl</c> on a public metapackage
        /// is not consulted; loading stays the installed package's
        /// <c>extra.tyhp.package</c>.
        /// </summary>
        internal static List<SupersededImpl> FindImplsSupersededByBundledPhp(
            IEnumerable<string> manifestPaths)
        {
            var infos = new List<(string Path, string Name, bool BundledUpstream)>();
            foreach (var path in manifestPaths)
            {
                var name = ComposerExtraTyhpPackageManifest.TryReadPackageName(path);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var bundledUpstream = ComposerExtraTyhpPackageManifest.HasPackageObject(path)
                    && !VendorTyhpdefLayout.IsFirstPartyComposerPackage(name);
                infos.Add((path, name, bundledUpstream));
            }

            var implToPhp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in infos)
            {
                if (!info.BundledUpstream)
                {
                    continue;
                }

                implToPhp[VendorTyhpdefLayout.ImplCompanionName(info.Name)] = info.Name;
            }

            var skipped = new List<SupersededImpl>();
            foreach (var info in infos)
            {
                if (implToPhp.TryGetValue(info.Name, out var phpName))
                {
                    skipped.Add(new SupersededImpl(info.Path, phpName, info.Name));
                }
            }

            return skipped;
        }
    }
}
