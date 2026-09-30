using System.Reflection;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Writes the embedded Reflection dumper to a temp <c>.php</c> file (never <c>php -r</c>).
    /// </summary>
    public static class PhpReflectionDumper
    {
        public const string ResourceSuffix = "PhpReflectionDump.php";

        public static string LoadScript()
        {
            var assembly = typeof(PhpReflectionDumper).Assembly;
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                // BaseDirectory stays valid for single-file publishes; Assembly.Location is empty there.
                var sibling = Path.Combine(AppContext.BaseDirectory, "PhpReflectionDump.php");
                if (File.Exists(sibling))
                {
                    return File.ReadAllText(sibling);
                }

                throw new InvalidOperationException("Embedded PHP Reflection dumper was not found.");
            }

            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("Embedded PHP Reflection dumper stream was null.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public static string WriteTempScript()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "tyhp-reflect-" + Guid.NewGuid().ToString("N") + ".php");
            File.WriteAllText(path, LoadScript());
            return path;
        }
    }
}
