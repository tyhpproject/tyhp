namespace Tyhp.CLI.ProjectTemplates
{
    /// <summary>
    /// Default <c>basic</c> project template for <c>tyhp init</c>.
    /// </summary>
    public sealed class BasicTemplate : IProjectTemplate
    {
        public const string TemplateName = "basic";

        public string Name => TemplateName;

        public string Description => "Minimal Tyhp application with src/, build/, and tyhpdef/";

        public Dictionary<string, string> GetDefaultConfig()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["include"] = "src/**/*.tyhp",
                ["exclude:0"] = "vendor/**",
                ["exclude:1"] = "node_modules/**",
                ["source.tagless"] = "false",
                ["output.path"] = "build/",
                ["output.phpVersion"] = "8.4",
                ["output.strictTypes"] = "true",
                ["output.comments"] = "true",
                ["namespace"] = @"App\",
            };
        }

        public Dictionary<string, string> GetScaffoldFiles()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/index.tyhp"] = IndexTyhpTemplate,
                ["composer.json"] = ComposerJsonTemplate,
            };
        }

        public List<string> GetDirectories()
        {
            return new List<string>
            {
                "src/",
                "build/",
                "tyhpdef/",
            };
        }

        /// <summary>
        /// Sample entry file. <c>{{NAMESPACE}}</c> is replaced by <see cref="InitAction"/>.
        /// </summary>
        internal const string IndexTyhpTemplate =
            """
            <?tyhp
            namespace {{NAMESPACE}};

            echo 'Hello, World!';

            """;

        internal const string ComposerJsonTemplate =
            """
            {
                "name": "app/tyhp-project",
                "description": "Tyhp project",
                "type": "project",
                "require": {
                    "php": "{{PHP_CONSTRAINT}}",
                    "tyhp/core": "@dev"
                },
                "require-dev": {
                    "tyhpdef/php": "@dev",
                    "tyhp/compiler": "{{COMPILER_PACKAGE_VERSION}}"
                },
                "config": {
                    "allow-plugins": {
                        "tyhp/core": true
                    }
                },
                "minimum-stability": "alpha",
                "prefer-stable": true,
                "scripts": {
                    "tyhp": "vendor/bin/tyhp",
                    "post-autoload-dump": [
                        "vendor/bin/tyhp --install-binary",
                        "vendor/bin/tyhp generate_tyhpdef --vendor"
                    ]
                },
                "scripts-descriptions": {
                    "tyhp": "Forward extra args to the Tyhp CLI (e.g. composer tyhp build)"
                }
            }

            """;
    }
}
