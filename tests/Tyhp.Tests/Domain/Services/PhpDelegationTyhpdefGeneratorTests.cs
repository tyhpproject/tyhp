using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class PhpDelegationTyhpdefGeneratorTests
{
    [Fact]
    public void GenerateFromJson_WritesExtJsonAndParses()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-json-").FullName;
        try
        {
            var options = new TyhpdefGenerationOptions
            {
                Mode = TyhpdefGenerationMode.PhpExtension,
                ExtensionName = "json",
                OutputDirectory = outputDir,
                IncludeDocComments = true,
                IncludeDeprecated = true,
                Overwrite = true,
                Split = "file",
                Locale = "en",
                FetchStubCache = false,
                StubCacheDirectory = MissingStubCache(),
            };
            var result = new TyhpdefGenerationResult();
            var runtime = new PhpRuntimeInfo
            {
                Path = "/tmp/php",
                Version = "8.3.11",
                IsManaged = true,
                LoadedExtensions = ["json"],
            };
            var extractor = new PhpManualDocExtractor();
            extractor.LoadHtml(File.ReadAllText(TyhpdefGenFixtures.ManualExcerptPath), "en");
            var generator = new PhpDelegationTyhpdefGenerator(
                new PhpRuntimeManager(new MissingTransport()),
                new PhpRuntimeDetector(),
                extractor);

            generator.GenerateFromJson(
                File.ReadAllText(TyhpdefGenFixtures.JsonSchemaPath),
                options,
                result,
                runtime);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.GeneratedFiles.Should().Contain(p => p.EndsWith("ExtJson.tyhpdef", StringComparison.Ordinal));
            var path = result.GeneratedFiles.Single(p => p.EndsWith("ExtJson.tyhpdef", StringComparison.Ordinal));
            var text = File.ReadAllText(path);
            text.Should().StartWith("<?tyhpdef");
            text.Should().Contain("function json_encode");
            text.Should().Contain("Returns the JSON representation of a value");
            text.Should().Contain("@link https://www.php.net/manual/en/function.json-encode.php");
            var parsed = ParserTestHelper.ParseTyhpdefContent(text, path);
            parsed.Diagnostics.Errors.Should().BeEmpty();
            result.FunctionCount.Should().Be(2);
            result.ConstantCount.Should().Be(2);
            result.ClassCount.Should().Be(2);
        }
        finally
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void GenerateFromJson_NoDocs_OmitsCommentsAndDoesNotNeedManual()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-nodocs-").FullName;
        try
        {
            var options = new TyhpdefGenerationOptions
            {
                Mode = TyhpdefGenerationMode.PhpExtension,
                ExtensionName = "json",
                OutputDirectory = outputDir,
                IncludeDocComments = false,
                Overwrite = true,
                Split = "file",
                FetchStubCache = false,
                StubCacheDirectory = MissingStubCache(),
            };
            var result = new TyhpdefGenerationResult();
            var runtime = new PhpRuntimeInfo
            {
                Path = "/tmp/php",
                Version = "8.3.11",
                IsManaged = true,
                LoadedExtensions = ["json"],
            };
            var generator = new PhpDelegationTyhpdefGenerator();
            generator.GenerateFromJson(
                File.ReadAllText(TyhpdefGenFixtures.JsonSchemaPath),
                options,
                result,
                runtime);

            result.Success.Should().BeTrue();
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().NotContain("@link https://www.php.net/manual");
            text.Should().NotContain("Returns the JSON representation of a value");
        }
        finally
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void GenerateFromJson_SkipsExistingWithoutOverwrite()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-skip-").FullName;
        try
        {
            var path = Path.Combine(outputDir, "ExtJson.tyhpdef");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(path, "<?tyhpdef\n/** existing */\n");
            var options = new TyhpdefGenerationOptions
            {
                Mode = TyhpdefGenerationMode.PhpExtension,
                ExtensionName = "json",
                OutputDirectory = outputDir,
                IncludeDocComments = false,
                Overwrite = false,
                Split = "file",
                FetchStubCache = false,
                StubCacheDirectory = MissingStubCache(),
            };
            var result = new TyhpdefGenerationResult();
            var runtime = new PhpRuntimeInfo
            {
                Path = "/tmp/php",
                Version = "8.3.11",
                IsManaged = true,
                LoadedExtensions = ["json"],
            };
            var generator = new PhpDelegationTyhpdefGenerator();
            generator.GenerateFromJson(
                File.ReadAllText(TyhpdefGenFixtures.JsonSchemaPath),
                options,
                result,
                runtime);

            result.GeneratedFiles.Should().BeEmpty();
            File.ReadAllText(path).Should().Contain("existing");
        }
        finally
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

        [Fact]
        public void ResolveRuntime_UserBinaryMissing_Is7501()
        {
            var generator = new PhpDelegationTyhpdefGenerator();
            var diagnostics = new DiagnosticBag();
            var info = generator.ResolveRuntime(
                new TyhpdefGenerationOptions
                {
                    PhpExecutablePath = Path.Combine(Path.GetTempPath(), "no-such-php-" + Guid.NewGuid().ToString("N")),
                },
                projectPhpVersion: "8.4",
                diagnostics,
                CancellationToken.None);

            info.Should().BeNull();
            diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPhpNotFound);
        }

        [Fact]
        public void GenerateFromJson_StaticAndNullableStaticReturns_RewritesToClassFqnAndParses()
        {
            var outputDir = Directory.CreateTempSubdirectory("tyhpdef-static-").FullName;
            try
            {
                var result = GenerateUriLike(
                    outputDir,
                    """
                    {
                      "schemaVersion": 1,
                      "phpVersion": "8.5.8",
                      "extension": "uri",
                      "extensionVersion": "8.5.8",
                      "constants": [],
                      "functions": [],
                      "classes": [
                        {
                          "kind": "class",
                          "name": "Uri",
                          "fqn": "\\Uri\\Rfc3986\\Uri",
                          "modifiers": ["final", "readonly"],
                          "extends": null,
                          "implements": [],
                          "uses": [],
                          "isAnonymous": false,
                          "backingType": null,
                          "docComment": null,
                          "deprecated": false,
                          "attributes": [],
                          "constants": [],
                          "properties": [],
                          "methods": [
                            {
                              "name": "withScheme",
                              "params": [
                                {
                                  "name": "scheme",
                                  "type": { "kind": "named", "text": "?string", "name": "string", "builtin": true, "nullable": true, "types": [] },
                                  "optional": false,
                                  "default": null,
                                  "variadic": false,
                                  "byRef": false,
                                  "promoted": false,
                                  "attributes": []
                                }
                              ],
                              "returnType": { "kind": "named", "text": "static", "name": "static", "builtin": false, "nullable": false, "types": [] },
                              "returnByRef": false,
                              "deprecated": false,
                              "docComment": null,
                              "attributes": [],
                              "modifiers": ["public"]
                            },
                            {
                              "name": "parse",
                              "params": [],
                              "returnType": { "kind": "nullable", "text": "?static", "name": "static", "builtin": false, "nullable": true, "types": [] },
                              "returnByRef": false,
                              "deprecated": false,
                              "docComment": null,
                              "attributes": [],
                              "modifiers": ["public", "static"]
                            },
                            {
                              "name": "tryParse",
                              "params": [],
                              "returnType": { "kind": "union", "text": "static|false", "name": null, "builtin": false, "nullable": false, "types": [] },
                              "returnByRef": false,
                              "deprecated": false,
                              "docComment": null,
                              "attributes": [],
                              "modifiers": ["public", "static"]
                            }
                          ],
                          "enumCases": []
                        }
                      ]
                    }
                    """);

                result.Diagnostics.HasErrors.Should().BeFalse(
                    string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
                result.GeneratedFiles.Should().ContainSingle();
                var text = File.ReadAllText(result.GeneratedFiles[0]);
                text.Should().Contain("function withScheme(?string $scheme): \\Uri\\Rfc3986\\Uri;");
                text.Should().Contain("function parse(): ?\\Uri\\Rfc3986\\Uri;");
                text.Should().Contain("function tryParse(): \\Uri\\Rfc3986\\Uri|false;");
                text.Should().NotContain(": static");
                text.Should().NotContain("?static");
                var parsed = ParserTestHelper.ParseTyhpdefContent(text, result.GeneratedFiles[0]);
                parsed.Diagnostics.Errors.Should().BeEmpty();
                parsed.Ast.Should().NotBeNull();
            }
            finally
            {
                TryDelete(outputDir);
            }
        }

        [Fact]
        public void GenerateFromJson_UnparseableReturnType_ReportsTyhpdefParseError()
        {
            var outputDir = Directory.CreateTempSubdirectory("tyhpdef-parsefail-").FullName;
            try
            {
                var result = GenerateUriLike(
                    outputDir,
                    """
                    {
                      "schemaVersion": 1,
                      "phpVersion": "8.5.8",
                      "extension": "uri",
                      "extensionVersion": "8.5.8",
                      "constants": [],
                      "functions": [],
                      "classes": [
                        {
                          "kind": "class",
                          "name": "Uri",
                          "fqn": "\\Uri\\Rfc3986\\Uri",
                          "modifiers": [],
                          "extends": null,
                          "implements": [],
                          "uses": [],
                          "isAnonymous": false,
                          "backingType": null,
                          "docComment": null,
                          "deprecated": false,
                          "attributes": [],
                          "constants": [],
                          "properties": [],
                          "methods": [
                            {
                              "name": "broken",
                              "params": [],
                              "returnType": { "kind": "named", "text": "<<<", "name": "<<<", "builtin": false, "nullable": false, "types": [] },
                              "returnByRef": false,
                              "deprecated": false,
                              "docComment": null,
                              "attributes": [],
                              "modifiers": ["public"]
                            }
                          ],
                          "enumCases": []
                        }
                      ]
                    }
                    """);

                result.Success.Should().BeFalse();
                result.GeneratedFiles.Should().BeEmpty();
                result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefParseError);
                result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefGenerationError);
                result.Diagnostics.Errors.Should().Contain(d =>
                    d.Code == MessageCode.TyhpdefParseError
                    && d.Message.Contains("failed to parse", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                TryDelete(outputDir);
            }
        }

        private static TyhpdefGenerationResult GenerateUriLike(string outputDir, string json)
        {
            var options = new TyhpdefGenerationOptions
            {
                Mode = TyhpdefGenerationMode.PhpExtension,
                ExtensionName = "uri",
                OutputDirectory = outputDir,
                IncludeDocComments = false,
                Overwrite = true,
                Split = "file",
                FetchStubCache = false,
                StubCacheDirectory = MissingStubCache(),
            };
            var result = new TyhpdefGenerationResult();
            var runtime = new PhpRuntimeInfo
            {
                Path = "/tmp/php",
                Version = "8.5.8",
                IsManaged = true,
                LoadedExtensions = ["uri"],
            };
            new PhpDelegationTyhpdefGenerator().GenerateFromJson(json, options, result, runtime);
            return result;
        }

        private static string MissingStubCache()
            => Path.Combine(Path.GetTempPath(), "tyhp-no-stubs-" + Guid.NewGuid().ToString("N"));

        private static void TryDelete(string dir)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }

    private sealed class MissingTransport : IPhpRuntimeTransport
    {
        public Task<string?> GetStringAsync(
            Uri url,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
            => Task.FromResult<string?>(null);

        public Task<byte[]?> GetBytesAsync(
            Uri url,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
            => Task.FromResult<byte[]?>(null);

        public Task<bool> DownloadToFileAsync(
            Uri url,
            string destinationPath,
            string? expectedSha256,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
            => Task.FromResult(false);
    }
}
