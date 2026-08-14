using Microsoft.Extensions.Configuration;
using Tyhp.Domain.Exceptions;
using Tyhp.Extensions;

namespace Tyhp.Config
{
    /// <summary>
    /// Build-specific configuration from <c>tyhp.json</c> <c>build.*</c> keys and CLI overrides.
    /// </summary>
    public sealed class BuildConfig
    {
        private static readonly HashSet<string> ValidProfiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "debug", "balanced", "release",
        };

        private static readonly HashSet<string> ValidOptimizeLevels = new(StringComparer.OrdinalIgnoreCase)
        {
            "none", "basic", "aggressive",
        };

        private static readonly HashSet<string> ValidDecimalBackings = new(StringComparer.OrdinalIgnoreCase)
        {
            "bcmath", "gmp",
        };

        private static readonly HashSet<string> ValidDecimalRoundings = new(StringComparer.OrdinalIgnoreCase)
        {
            "halfUp", "halfDown", "halfEven", "up", "down", "ceiling", "floor",
        };

        /// <summary>
        /// Auto-generate tyhpdef for compiled code. <c>null</c> until resolved from project type.
        /// </summary>
        public bool? GenerateTyhpdef { get; set; }

        /// <summary>Generate sourcemaps for emitted PHP.</summary>
        public bool GenerateSourcemap { get; set; } = false;

        /// <summary>Embed original source in generated <c>.map</c> files.</summary>
        public bool SourceMapIncludeContent { get; set; } = false;

        /// <summary>Generate or update <c>composer.json</c> for PSR-4 autoloading.</summary>
        public bool UpdateComposer { get; set; } = false;

        /// <summary>Autoloader paths keyed by logical name (e.g. <c>composer</c>).
        /// When omitted, entry points default to <c>vendor/autoload.php</c> under the output dir.
        /// Set <c>composer</c> to an empty string or <c>none</c> to disable injection.
        /// Per-file override: <c>declare(autoload="composer"|"none"|path|key);</c>.</summary>
        public Dictionary<string, string>? EntryPointAutoloader { get; set; }

        /// <summary>Struct backing strategy (<c>array</c> or custom class name).</summary>
        public string StructBacking { get; set; } = "array";

        /// <summary>Decimal backing library (<c>bcmath</c> or <c>gmp</c>).</summary>
        public string DecimalBacking { get; set; } = "bcmath";

        /// <summary>Default decimal scale.</summary>
        public int DecimalScale { get; set; } = 28;

        /// <summary>Default decimal rounding mode.</summary>
        public string DecimalRounding { get; set; } = "halfUp";

        /// <summary>Re-enable <c>eval()</c> usage.</summary>
        public bool AllowEval { get; set; } = false;

        /// <summary>Wipe output directory before building.</summary>
        public bool CleanBeforeBuild { get; set; } = false;

        /// <summary>Emit detailed build output.</summary>
        public bool Verbose { get; set; } = false;

        /// <summary>Check without writing output files.</summary>
        public bool DryRun { get; set; } = false;

        /// <summary>Treat warnings as errors.</summary>
        public bool StrictMode { get; set; } = false;

        /// <summary>Watch source files and rebuild on change (not yet implemented).</summary>
        public bool Watch { get; set; } = false;

        /// <summary>PSR-4 namespace to directory mappings.</summary>
        public Dictionary<string, string>? Psr4 { get; set; }

        /// <summary>Additional PSR-4 autoload paths.</summary>
        public List<string>? Psr4Includes { get; set; }

        /// <summary>Build profile: <c>debug</c>, <c>balanced</c>, or <c>release</c>.</summary>
        public string? Profile { get; set; }

        /// <summary>Optimization level: <c>none</c>, <c>basic</c>, or <c>aggressive</c>.</summary>
        public string? Optimize { get; set; }

        /// <summary>Per-module optimizer overrides.</summary>
        public Dictionary<string, bool>? Optimizations { get; set; }

        /// <summary>Anonymous class wrapper for <c>clone ... with</c> on readonly properties (PHP &lt; 8.5).</summary>
        public bool ExperimentalReadonlyCloneWith { get; set; } = false;

        /// <summary>Emit runtime type checks at generic boundaries.</summary>
        public bool RuntimeGenericChecks { get; set; } = false;

        internal void ApplyFrom(
            IConfiguration configuration,
            Action<MessageCode, object[]>? warn = null)
        {
            this.GenerateTyhpdef = ReadOptionalBool(configuration, "build:generateTyhpdef");
            this.GenerateSourcemap = ReadBool(configuration, "build:generateSourcemap", this.GenerateSourcemap);
            this.SourceMapIncludeContent = ReadBool(configuration, "build:sourcemapIncludeContent", this.SourceMapIncludeContent);
            this.UpdateComposer = ReadBool(configuration, "build:updateComposer", this.UpdateComposer);
            this.EntryPointAutoloader = ReadStringDictionary(
                configuration,
                "build:entryPointAutoloader",
                includeEmptyValues: true);

            this.StructBacking = configuration["build:structBacking"] ?? this.StructBacking;

            var decimalBacking = configuration["build:decimalBacking"];
            if (!String.IsNullOrWhiteSpace(decimalBacking))
            {
                if (ValidDecimalBackings.Contains(decimalBacking))
                {
                    this.DecimalBacking = decimalBacking;
                }
                else
                {
                    warn?.Invoke(MessageCode.ConfigInvalidValue, ["build:decimalBacking", decimalBacking]);
                }
            }

            if (configuration.GetSection("build:decimalScale").Exists()
                && Int32.TryParse(configuration["build:decimalScale"], out int decimalScale)
                && decimalScale >= 0)
            {
                this.DecimalScale = decimalScale;
            }
            else if (configuration.GetSection("build:decimalScale").Exists())
            {
                warn?.Invoke(MessageCode.ConfigInvalidValue, ["build:decimalScale", configuration["build:decimalScale"] ?? ""]);
            }

            var decimalRounding = configuration["build:decimalRounding"];
            if (!String.IsNullOrWhiteSpace(decimalRounding))
            {
                if (ValidDecimalRoundings.Contains(decimalRounding))
                {
                    this.DecimalRounding = decimalRounding;
                }
                else
                {
                    warn?.Invoke(MessageCode.ConfigInvalidValue, ["build:decimalRounding", decimalRounding]);
                }
            }

            this.AllowEval = ReadBool(configuration, "build:allowEval", this.AllowEval);
            this.Psr4 = ReadStringDictionary(configuration, "psr4");
            this.Psr4Includes = ReadIndexedStringList(configuration, "psr4Includes");

            var profile = configuration["build:profile"] ?? configuration["profile"];
            if (!String.IsNullOrWhiteSpace(profile))
            {
                if (ValidProfiles.Contains(profile))
                {
                    this.Profile = profile;
                }
                else
                {
                    warn?.Invoke(MessageCode.ConfigInvalidValue, ["build:profile", profile]);
                }
            }

            var optimize = configuration["build:optimize"] ?? configuration["optimize"];
            if (!String.IsNullOrWhiteSpace(optimize))
            {
                if (ValidOptimizeLevels.Contains(optimize))
                {
                    this.Optimize = optimize;
                }
                else
                {
                    warn?.Invoke(MessageCode.ConfigInvalidValue, ["build:optimize", optimize]);
                }
            }

            this.Optimizations = ReadBoolDictionary(configuration, "build:optimizations");
            var optimizations = this.Optimizations;
            MergeCommaSeparatedBoolOverrides(
                configuration["optimize-enable"],
                true,
                ref optimizations);
            MergeCommaSeparatedBoolOverrides(
                configuration["optimize-disable"],
                false,
                ref optimizations);
            this.Optimizations = optimizations;

            this.ExperimentalReadonlyCloneWith = ReadBool(
                configuration,
                "build:experimentalReadonlyCloneWith",
                this.ExperimentalReadonlyCloneWith);
            this.RuntimeGenericChecks = ReadBool(
                configuration,
                "build:runtimeGenericChecks",
                this.RuntimeGenericChecks);

            // CLI argument overlays (command line wins over tyhp.json)
            if (configuration.GetSection("clean").Exists())
            {
                this.CleanBeforeBuild = configuration["clean"].ParseBool();
            }

            if (configuration.GetSection("verbose").Exists())
            {
                this.Verbose = configuration["verbose"].ParseBool();
            }

            if (configuration.GetSection("dry-run").Exists())
            {
                this.DryRun = configuration["dry-run"].ParseBool();
            }

            if (configuration.GetSection("strict").Exists())
            {
                this.StrictMode = configuration["strict"].ParseBool();
            }

            if (configuration.GetSection("watch").Exists())
            {
                this.Watch = configuration["watch"].ParseBool();
            }
        }

        private static bool ReadBool(IConfiguration configuration, string key, bool current)
        {
            if (!configuration.GetSection(key).Exists())
            {
                return current;
            }

            return configuration[key].ParseBool();
        }

        private static bool? ReadOptionalBool(IConfiguration configuration, string key)
        {
            if (!configuration.GetSection(key).Exists())
            {
                return null;
            }

            return configuration[key].ParseBool();
        }

        private static Dictionary<string, string>? ReadStringDictionary(
            IConfiguration configuration,
            string sectionPrefix,
            bool includeEmptyValues = false)
        {
            var section = configuration.GetSection(sectionPrefix);
            if (!section.Exists())
            {
                return null;
            }

            var dict = new Dictionary<string, string>();
            foreach (var child in section.GetChildren())
            {
                if (child.Value is null)
                {
                    continue;
                }

                if (includeEmptyValues || !String.IsNullOrWhiteSpace(child.Value))
                {
                    dict[child.Key] = child.Value;
                }
            }

            return dict.Count > 0 ? dict : null;
        }

        private static Dictionary<string, bool>? ReadBoolDictionary(IConfiguration configuration, string sectionPrefix)
        {
            var section = configuration.GetSection(sectionPrefix);
            if (!section.Exists())
            {
                return null;
            }

            var dict = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var child in section.GetChildren())
            {
                dict[child.Key] = child.Value.ParseBool();
            }

            return dict.Count > 0 ? dict : null;
        }

        private static List<string>? ReadIndexedStringList(IConfiguration configuration, string sectionPrefix)
        {
            if (!configuration.GetSection($"{sectionPrefix}:0").Exists())
            {
                return null;
            }

            var list = new List<string>();
            for (int i = 0; i < 255; i++)
            {
                if (!configuration.GetSection($"{sectionPrefix}:{i}").Exists())
                {
                    break;
                }

                string? value = configuration.GetSection($"{sectionPrefix}:{i}").Value;
                if (!String.IsNullOrWhiteSpace(value))
                {
                    list.Add(value);
                }
            }

            return list.Count > 0 ? list : null;
        }

        private static void MergeCommaSeparatedBoolOverrides(
            string? value,
            bool enabled,
            ref Dictionary<string, bool>? optimizations)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return;
            }

            optimizations ??= new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var key in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!String.IsNullOrWhiteSpace(key))
                {
                    optimizations[key] = enabled;
                }
            }
        }
    }
}
