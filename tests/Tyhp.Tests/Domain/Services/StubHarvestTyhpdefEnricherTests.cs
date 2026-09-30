using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class StubHarvestTyhpdefEnricherTests
{
    [Fact]
    public void CanonicalType_ListGeneric_BecomesIntKeyedArray()
    {
        StubHarvestTyhpdefEnricher.CanonicalType("list<int>").Should().Be("array<int, int>");
        StubHarvestTyhpdefEnricher.CanonicalType("non-empty-list<string>").Should().Be("array<int, string>");
    }

    [Fact]
    public void GenerateFromJson_WithVendoredStubs_WritesOverlayAndKeepsManualSummary()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-").FullName;
        var handOverlay = Path.Combine(outputDir, "overlays", "HandKeep.tyhpdef");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(handOverlay)!);
            File.WriteAllText(handOverlay, "HAND_OVERLAY_MUST_SURVIVE\n");

            var (result, layer1Text, overlayText, sources) = GenerateJson(
                outputDir,
                TyhpdefGenFixtures.JsonStubsPath,
                includeDocs: true,
                requireStubs: false);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.GeneratedFiles.Should().Contain(p => p.EndsWith("ExtJson.tyhpdef", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}stubs{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            var overlayPath = Path.Combine(outputDir, "overlays", "stubs", "ExtJson.tyhpdef");
            result.GeneratedFiles.Should().Contain(overlayPath);
            File.Exists(overlayPath).Should().BeTrue();

            layer1Text.Should().Contain("Returns the JSON representation of a value");
            layer1Text.Should().Contain("function json_encode");
            overlayText.Should().NotContain("STUB-ONLY SUMMARY MUST NOT APPEAR");
            overlayText.Should().NotContain("Returns the JSON representation of a value");
            overlayText.Should().Contain("array|null|object");
            overlayText.Should().Contain("LAYER 2 (stub harvest)");
            overlayText.Should().Contain("https://github.com/vimeo/psalm/tree/6.x/stubs");
            overlayText.Should().Contain("partial ");

            sources.Should().Contain("PHP Documentation Group");
            sources.Should().Contain("https://github.com/vimeo/psalm/tree/6.x/stubs");
            sources.Should().Contain("https://github.com/phpstan/phpstan-src/tree/2.2.x/stubs");
            sources.Should().Contain("https://github.com/phan/phan/tree/v6/internal/stubs");
            sources.Should().Contain("https://github.com/jetbrains/phpstorm-stubs");
            File.ReadAllText(Path.Combine(outputDir, "NOTICE")).Should().Contain("Apache-2.0");

            File.ReadAllText(handOverlay).Should().Be("HAND_OVERLAY_MUST_SURVIVE\n");
            Directory.GetFiles(Path.Combine(outputDir, "overlays"), "*.tyhpdef")
                .Should()
                .ContainSingle(p => Path.GetFileName(p) == "HandKeep.tyhpdef");
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void MergedDocRebuild_PreservesDeprecatedAndOtherNonMergedTags()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-depr-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-depr-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "depr.phpstub"), """
                <?php

                /**
                 * @param mixed $value
                 * @param string $extra the extra value used for something
                 * @return mixed
                 */
                function depr_test_fn(mixed $value, string $extra): mixed {}
                """);

            var manualHtml = """
                <div id="function.depr-test-fn" class="refentry">
                  <div class="refnamediv">
                    <h1 class="refname">depr_test_fn</h1>
                    <p class="refpurpose"><span class="dc-title">Does a deprecated thing</span></p>
                  </div>
                  <div id="refsect1-function.depr-test-fn-description" class="refsect1 description">
                    <h3 class="title">Description</h3>
                    <p class="para rdfs-comment">Performs a legacy operation that is no longer recommended.</p>
                  </div>
                  <div class="warning"><p>This function has been deprecated as of PHP 9.0. Use new_fn() instead.</p></div>
                  <div id="refsect1-function.depr-test-fn-parameters" class="refsect1 parameters">
                    <h3 class="title">Parameters</h3>
                    <p class="para">
                      <dl>
                        <dt><code class="parameter">value</code></dt>
                        <dd><p class="para">The primary value.</p></dd>
                      </dl>
                    </p>
                  </div>
                </div>
                """;

            var schema = """
                {
                  "schemaVersion": 1,
                  "phpVersion": "8.3.11",
                  "extension": "depr",
                  "extensionVersion": "8.3.11",
                  "constants": [],
                  "functions": [
                    {
                      "name": "depr_test_fn",
                      "params": [
                        { "name": "value", "type": { "kind": "named", "text": "mixed", "name": "mixed", "builtin": true, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
                        { "name": "extra", "type": { "kind": "named", "text": "mixed", "name": "mixed", "builtin": true, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] }
                      ],
                      "returnType": { "kind": "named", "text": "mixed", "name": "mixed", "builtin": true, "nullable": false, "types": [] },
                      "returnByRef": false,
                      "tentativeReturn": false,
                      "deprecated": false,
                      "docComment": null,
                      "attributes": [],
                      "modifiers": []
                    }
                  ],
                  "classes": []
                }
                """;

            var options = new TyhpdefGenerationOptions
            {
                Mode = TyhpdefGenerationMode.PhpExtension,
                ExtensionName = "depr",
                OutputDirectory = outputDir,
                IncludeDocComments = true,
                IncludeDeprecated = true,
                Overwrite = true,
                Split = "file",
                Locale = "en",
                FetchStubCache = false,
                StubCacheDirectory = stubCacheDir,
                RequireStubs = false,
            };
            var result = new TyhpdefGenerationResult();
            var runtime = new PhpRuntimeInfo
            {
                Path = "/tmp/php",
                Version = "8.3.11",
                IsManaged = true,
                LoadedExtensions = ["depr"],
            };
            var extractor = new PhpManualDocExtractor();
            extractor.LoadHtml(manualHtml, "en");

            var generator = new PhpDelegationTyhpdefGenerator(
                new PhpRuntimeManager(new MissingTransport()),
                new PhpRuntimeDetector(),
                extractor);
            generator.GenerateFromJson(schema, options, result, runtime);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));

            var layer1Path = Path.Combine(outputDir, "ExtDepr.tyhpdef");
            File.Exists(layer1Path).Should().BeTrue();
            var layer1Text = File.ReadAllText(layer1Path);
            layer1Text.Should().Contain("@deprecated");

            var overlayPath = Path.Combine(outputDir, "overlays", "stubs", "ExtDepr.tyhpdef");
            File.Exists(overlayPath).Should().BeTrue();
            var overlayText = File.ReadAllText(overlayPath);

            // A type change (`$extra`: mixed → string) emits the overlay. The stub also fills
            // the missing `$extra` param description; the manual's @deprecated notice must
            // survive that rebuild — Layer 2 only fills holes, it must never delete existing
            // Layer 1 prose.
            overlayText.Should().Contain("extra value used for something");
            overlayText.Should().Contain("@deprecated");
            overlayText.Should().Contain("string $extra");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void MergeDocs_DuplicateEmptyParamNames_DoesNotThrow()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-dup-param-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-dup-param-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "dup.phpstub"), """
                <?php

                /**
                 * @param
                 * @param
                 * @param mixed $value filled from stub
                 * @return mixed
                 */
                function dup_param_fn(mixed $value): mixed {}
                """);

            var schema = """
                {
                  "schemaVersion": 1,
                  "phpVersion": "8.3.11",
                  "extension": "dup",
                  "extensionVersion": "8.3.11",
                  "constants": [],
                  "functions": [
                    {
                      "name": "dup_param_fn",
                      "params": [
                        { "name": "value", "type": { "kind": "named", "text": "mixed", "name": "mixed", "builtin": true, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] }
                      ],
                      "returnType": { "kind": "named", "text": "mixed", "name": "mixed", "builtin": true, "nullable": false, "types": [] },
                      "returnByRef": false,
                      "tentativeReturn": false,
                      "deprecated": false,
                      "docComment": null,
                      "attributes": [],
                      "modifiers": []
                    }
                  ],
                  "classes": []
                }
                """;

            var options = new TyhpdefGenerationOptions
            {
                Mode = TyhpdefGenerationMode.PhpExtension,
                ExtensionName = "dup",
                OutputDirectory = outputDir,
                IncludeDocComments = true,
                IncludeDeprecated = true,
                Overwrite = true,
                Split = "file",
                Locale = "en",
                FetchStubCache = false,
                StubCacheDirectory = stubCacheDir,
                RequireStubs = false,
            };
            var result = new TyhpdefGenerationResult();
            var runtime = new PhpRuntimeInfo
            {
                Path = "/tmp/php",
                Version = "8.3.11",
                IsManaged = true,
                LoadedExtensions = ["dup"],
            };
            var generator = new PhpDelegationTyhpdefGenerator(
                new PhpRuntimeManager(new MissingTransport()),
                new PhpRuntimeDetector());
            generator.GenerateFromJson(schema, options, result, runtime);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            File.Exists(Path.Combine(outputDir, "ExtDup.tyhpdef")).Should().BeTrue();
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_IdenticalSignature_DoesNotWriteOverlayEvenWhenStubHasDocs()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-same-sig-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-same-sig-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "call.phpstub"), """
                <?php

                /**
                 * @param callable $callback the callable to be called from the stub
                 * @param mixed $args extra stub description for args
                 * @return mixed
                 */
                function call_user_func(callable $callback, mixed ...$args): mixed {}
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalFunctions =
                [
                    new TyhpdefFunction
                    {
                        Name = "call_user_func",
                        Parameters =
                        [
                            new TyhpdefParameter { Name = "callback", Type = "callable" },
                            new TyhpdefParameter { Name = "args", Type = "mixed", IsVariadic = true },
                        ],
                        ReturnType = "mixed",
                        DocComment = """
                            /**
                             * Call the callback given by the first parameter
                             * @param $callback The callable to be called.
                             * @param $args Zero or more parameters.
                             * @return Returns the return value of the callback.
                             */
                            """,
                    },
                ],
            };

            var enricher = new StubHarvestTyhpdefEnricher();
            var result = new TyhpdefGenerationResult();
            enricher.Enrich(
                layer1,
                new TyhpdefGenerationOptions
                {
                    Mode = TyhpdefGenerationMode.PhpExtension,
                    OutputDirectory = outputDir,
                    IncludeDocComments = true,
                    Overwrite = true,
                    StubCacheDirectory = stubCacheDir,
                    FetchStubCache = false,
                },
                result,
                "standard");

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var overlayPath = Path.Combine(outputDir, "overlays", "stubs", "ExtStandard.tyhpdef");
            if (File.Exists(overlayPath))
            {
                File.ReadAllText(overlayPath).Should().NotContain("function call_user_func(");
            }
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_IdenticalConstructor_DoesNotWriteOverlayEvenWhenStubHasDocs()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-same-ctor-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-same-ctor-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "closure.phpstub"), """
                <?php

                final class Closure
                {
                    /**
                     * This method exists only to disallow instantiation of the Closure class.
                     * @return void
                     */
                    private function __construct() {}

                    /**
                     * This is for consistency with other classes that implement calling magic.
                     */
                    public function __invoke() {}
                }
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalTypes =
                [
                    new TyhpdefClassDeclaration
                    {
                        Kind = "class",
                        Name = "Closure",
                        Modifiers = ["final"],
                        Methods =
                        [
                            new TyhpdefMethod
                            {
                                Name = "__construct",
                                Modifiers = ["private"],
                            },
                            new TyhpdefMethod
                            {
                                Name = "__invoke",
                                Modifiers = ["public"],
                            },
                        ],
                    },
                ],
            };

            var enricher = new StubHarvestTyhpdefEnricher();
            var result = new TyhpdefGenerationResult();
            enricher.Enrich(
                layer1,
                new TyhpdefGenerationOptions
                {
                    Mode = TyhpdefGenerationMode.PhpExtension,
                    OutputDirectory = outputDir,
                    IncludeDocComments = true,
                    Overwrite = true,
                    StubCacheDirectory = stubCacheDir,
                    FetchStubCache = false,
                },
                result,
                "core");

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var overlayPath = Path.Combine(outputDir, "overlays", "stubs", "ExtCore.tyhpdef");
            if (File.Exists(overlayPath))
            {
                var overlayText = File.ReadAllText(overlayPath);
                overlayText.Should().NotContain("function __construct");
                overlayText.Should().NotContain("function __invoke");
                overlayText.Should().NotContain("class Closure");
            }
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void MissingCache_WithoutRequireStubs_WarnsAndKeepsLayer1()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-miss-").FullName;
        try
        {
            var missing = Path.Combine(Path.GetTempPath(), "tyhp-no-stubs-" + Guid.NewGuid().ToString("N"));
            var (result, layer1Text, _, _) = GenerateJson(
                outputDir,
                missing,
                includeDocs: false,
                requireStubs: false);

            result.Success.Should().BeTrue();
            result.Diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefStubCacheMissing);
            result.GeneratedFiles.Should().Contain(p => p.EndsWith("ExtJson.tyhpdef", StringComparison.Ordinal));
            result.GeneratedFiles.Should().NotContain(p => p.Contains($"{Path.DirectorySeparatorChar}stubs{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            layer1Text.Should().Contain("function json_encode");
            Directory.Exists(Path.Combine(outputDir, "overlays", "stubs")).Should().BeFalse();
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void MissingCache_WithRequireStubs_IsError()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-req-").FullName;
        try
        {
            var missing = Path.Combine(Path.GetTempPath(), "tyhp-no-stubs-" + Guid.NewGuid().ToString("N"));
            var (result, _, _, _) = GenerateJson(
                outputDir,
                missing,
                includeDocs: false,
                requireStubs: true);

            result.Success.Should().BeFalse();
            result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefStubCacheRequired);
            Directory.Exists(Path.Combine(outputDir, "overlays", "stubs")).Should().BeFalse();
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    /// <summary>
    /// A gated Layer 1 method (<c>#[\Tyhp\Php]</c>) that the stub corpus also enriches (forcing a
    /// rewrite) must keep its gate in the emitted overlay. <c>TryEnrichType</c> used to
    /// reconstruct <see cref="TyhpdefMethod"/> objects by hand instead of using a <c>with</c>
    /// expression, silently dropping <see cref="TyhpdefMethod.PhpGate"/> — which then let the
    /// overlay's ungated redeclaration collide with the (still-tracked) Layer 1 gate at bind time
    /// (TYHP4303 / TYHP3002) once another PHP-version band of the same method was also emitted.
    /// </summary>
    [Fact]
    public void Enrich_GatedMethodThatCorpusAlsoChanges_KeepsPhpGateInOverlay()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-gate-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-gate-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "gated.phpstub"), """
                <?php

                class GatedClass
                {
                    /**
                     * @return array<string, int>
                     */
                    public function gatedMethod(): array {}
                }
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalTypes =
                [
                    new TyhpdefClassDeclaration
                    {
                        Kind = "class",
                        Name = "GatedClass",
                        Methods =
                        [
                            new TyhpdefMethod
                            {
                                Name = "gatedMethod",
                                Modifiers = ["public"],
                                ReturnType = "mixed",
                                PhpGate = ">=8.4",
                            },
                        ],
                    },
                ],
            };

            var enricher = new StubHarvestTyhpdefEnricher();
            var result = new TyhpdefGenerationResult();
            enricher.Enrich(
                layer1,
                new TyhpdefGenerationOptions
                {
                    Mode = TyhpdefGenerationMode.PhpExtension,
                    OutputDirectory = outputDir,
                    IncludeDocComments = false,
                    Overwrite = true,
                    StubCacheDirectory = stubCacheDir,
                    FetchStubCache = false,
                },
                result,
                "gated");

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var overlayPath = Path.Combine(outputDir, "overlays", "stubs", "ExtGated.tyhpdef");
            File.Exists(overlayPath).Should().BeTrue();
            var overlayText = File.ReadAllText(overlayPath);
            overlayText.Should().Contain("gatedMethod");
            overlayText.Should().Contain("#[\\Tyhp\\Php(\">=8.4\")]");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_ClassLevelTemplate_EmitsGenericOnClassAndKeepsMemberType()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-tmpl-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-tmpl-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "sensitive.phpstub"), """
                <?php

                /**
                 * @template TValue
                 */
                final class SensitiveParameterValue
                {
                    /**
                     * @param TValue $value
                     */
                    public function __construct($value) {}

                    /**
                     * @return TValue
                     */
                    public function getValue() {}
                }
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalTypes =
                [
                    new TyhpdefClassDeclaration
                    {
                        Kind = "class",
                        Name = "SensitiveParameterValue",
                        Modifiers = ["final"],
                        Methods =
                        [
                            new TyhpdefMethod
                            {
                                Name = "__construct",
                                Modifiers = ["public"],
                                Parameters =
                                [
                                    new TyhpdefParameter { Name = "value", Type = "mixed" },
                                ],
                            },
                            new TyhpdefMethod
                            {
                                Name = "getValue",
                                Modifiers = ["public"],
                                ReturnType = "mixed",
                            },
                        ],
                    },
                ],
            };

            var overlayText = EnrichToOverlay(layer1, outputDir, stubCacheDir, "core");
            overlayText.Should().Contain("partial class SensitiveParameterValue<TValue>;");
            overlayText.Should().Contain("partial class SensitiveParameterValue {");
            overlayText.Should().NotContain("final class SensitiveParameterValue");
            overlayText.Should().Contain("function getValue(): TValue");
            overlayText.Should().Contain("function __construct(TValue $value)");
            overlayText.Should().NotContain("function getValue<TValue>");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_ClassLevelTemplateWithConstraint_EmitsExtendsBound()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-start-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-start-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "date.phpstub"), """
                <?php

                /**
                 * @template Start of \DateTimeInterface
                 */
                class DatePeriod
                {
                    /**
                     * @param Start $start
                     * @param mixed $interval
                     * @param mixed $end
                     * @param int $options
                     */
                    public function __construct($start, $interval, $end, $options = 0) {}
                }
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalTypes =
                [
                    new TyhpdefClassDeclaration
                    {
                        Kind = "class",
                        Name = "DatePeriod",
                        Methods =
                        [
                            new TyhpdefMethod
                            {
                                Name = "__construct",
                                Modifiers = ["public"],
                                Parameters =
                                [
                                    new TyhpdefParameter { Name = "start", Type = "mixed" },
                                    new TyhpdefParameter { Name = "interval", Type = "mixed" },
                                    new TyhpdefParameter { Name = "end", Type = "mixed" },
                                    new TyhpdefParameter { Name = "options", Type = "mixed" },
                                ],
                            },
                        ],
                    },
                ],
            };

            var overlayText = EnrichToOverlay(layer1, outputDir, stubCacheDir, "date");
            overlayText.Should().Contain("partial class DatePeriod<Start extends \\DateTimeInterface = \\DateTimeInterface>;");
            overlayText.Should().Contain("function __construct(Start $start,");
            overlayText.Should().NotContain("function __construct<Start>");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_MemberTemplatePlaceholderWithoutClassTemplate_DeclaresClassGenerics()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-orphan-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-orphan-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "orphan.phpstub"), """
                <?php

                class OrphanBox
                {
                    /**
                     * @return TValue
                     */
                    public function getValue() {}
                }
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalTypes =
                [
                    new TyhpdefClassDeclaration
                    {
                        Kind = "class",
                        Name = "OrphanBox",
                        Methods =
                        [
                            new TyhpdefMethod
                            {
                                Name = "getValue",
                                Modifiers = ["public"],
                                ReturnType = "mixed",
                            },
                        ],
                    },
                ],
            };

            var overlayText = EnrichToOverlay(layer1, outputDir, stubCacheDir, "orphan");
            overlayText.Should().Contain("partial class OrphanBox<TValue>;");
            overlayText.Should().Contain("function getValue(): TValue");
            overlayText.Should().NotContain("function getValue<TValue>");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_SynonymTemplatesAcrossCorpora_UnifyToPhpStanNamesWithConstraintDefault()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-sos-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-sos-stubs-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(stubCacheDir, "psalm"));
            Directory.CreateDirectory(Path.Combine(stubCacheDir, "phpstan"));
            Directory.CreateDirectory(Path.Combine(stubCacheDir, "phan"));
            File.WriteAllText(Path.Combine(stubCacheDir, "psalm", "spl.phpstub"), """
                <?php

                /**
                 * @template TObject as object
                 * @template TArrayValue
                 * @template-implements ArrayAccess<TObject, TArrayValue>
                 * @template-implements Iterator<int, TObject>
                 */
                class SplObjectStorage implements Countable, Iterator, Serializable, ArrayAccess {
                    /**
                     * @param TObject $object
                     * @param TArrayValue $info
                     */
                    public function attach($object, $info = null) {}
                    /**
                     * @return TArrayValue
                     */
                    public function getInfo() {}
                    /**
                     * @return TObject
                     */
                    public function current() {}
                }
                """);
            File.WriteAllText(Path.Combine(stubCacheDir, "phpstan", "SplObjectStorage.stub"), """
                <?php

                /**
                 * @template TObject of object
                 * @template TData
                 * @template-implements Iterator<int, TObject>
                 * @template-implements ArrayAccess<TObject, TData>
                 */
                class SplObjectStorage implements Countable, Iterator, Serializable, ArrayAccess {
                    /**
                     * @param TObject $object
                     * @param TData $data
                     */
                    public function attach(object $object, $data = null) {}
                    /**
                     * @return TData
                     */
                    public function getInfo() {}
                    /**
                     * @return TObject
                     */
                    public function current() {}
                }
                """);
            File.WriteAllText(Path.Combine(stubCacheDir, "phan", "spl.php"), """
                <?php

                /**
                 * @template TObject of object
                 * @template TValue
                 * @implements Iterator<int, TObject>
                 * @implements ArrayAccess<TObject, TValue>
                 */
                class SplObjectStorage implements \Countable, \Iterator, \Serializable, \ArrayAccess {
                    /**
                     * @param TObject $object
                     * @param TValue $info
                     */
                    public function attach(object $object, mixed $info = null) {}
                    /**
                     * @return TValue
                     */
                    public function getInfo() {}
                    /**
                     * @return TObject
                     */
                    public function current() {}
                }
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalTypes =
                [
                    new TyhpdefClassDeclaration
                    {
                        Kind = "class",
                        Name = "SplObjectStorage",
                        Implements = ["\\Countable", "\\Iterator", "\\Serializable", "\\ArrayAccess"],
                        Methods =
                        [
                            new TyhpdefMethod
                            {
                                Name = "attach",
                                Modifiers = ["public"],
                                Parameters =
                                [
                                    new TyhpdefParameter { Name = "object", Type = "object" },
                                    new TyhpdefParameter { Name = "info", Type = "mixed", DefaultValue = "null" },
                                ],
                                ReturnType = "void",
                            },
                            new TyhpdefMethod
                            {
                                Name = "getInfo",
                                Modifiers = ["public"],
                                ReturnType = "mixed",
                            },
                            new TyhpdefMethod
                            {
                                Name = "current",
                                Modifiers = ["public"],
                                ReturnType = "object",
                            },
                        ],
                    },
                ],
            };

            var overlayText = EnrichToOverlay(layer1, outputDir, stubCacheDir, "spl");
            overlayText.Should().Contain(
                "partial class SplObjectStorage<TObject extends object = object, TData = mixed> implements \\Countable, \\Iterator<int, TObject>, \\Serializable, \\ArrayAccess<TObject, TData>;");
            overlayText.Should().NotContain("TArrayValue");
            overlayText.Should().NotContain("TValue");
            overlayText.Should().Contain("partial class SplObjectStorage {");
            overlayText.Should().Contain("function attach(object $object, TData $info = null): void");
            overlayText.Should().Contain("function getInfo(): TData");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_TemplateConstrainedToMixed_EmitsDefaultWithoutConstraint()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-mixed-t-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-mixed-t-stubs-").FullName;
        try
        {
            var phpstanDir = Path.Combine(stubCacheDir, "phpstan");
            Directory.CreateDirectory(phpstanDir);
            File.WriteAllText(Path.Combine(phpstanDir, "box.stub"), """
                <?php

                /**
                 * @template T of mixed
                 */
                class MixedBox {
                    /**
                     * @return T
                     */
                    public function get() {}
                }
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalTypes =
                [
                    new TyhpdefClassDeclaration
                    {
                        Kind = "class",
                        Name = "MixedBox",
                        Methods =
                        [
                            new TyhpdefMethod
                            {
                                Name = "get",
                                Modifiers = ["public"],
                                ReturnType = "mixed",
                            },
                        ],
                    },
                ],
            };

            var overlayText = EnrichToOverlay(layer1, outputDir, stubCacheDir, "box");
            overlayText.Should().Contain("partial class MixedBox<T = mixed>;");
            overlayText.Should().NotContain("extends mixed");
            overlayText.Should().Contain("function get(): T");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_ImplementsArgsSkippedWhenParentArityMismatchesThisRun()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-arity-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-arity-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "iter.phpstub"), """
                <?php

                /**
                 * @template TValue
                 */
                interface Iterator {
                    /**
                     * @return TValue
                     */
                    public function current() {}
                }

                /**
                 * @template TObject of object
                 * @template-implements Iterator<int, TObject>
                 */
                class ObjectList implements Iterator {
                    /**
                     * @return TObject
                     */
                    public function current() {}
                }
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalTypes =
                [
                    new TyhpdefClassDeclaration
                    {
                        Kind = "interface",
                        Name = "Iterator",
                        Methods =
                        [
                            new TyhpdefMethod
                            {
                                Name = "current",
                                Modifiers = ["public"],
                                ReturnType = "mixed",
                            },
                        ],
                    },
                    new TyhpdefClassDeclaration
                    {
                        Kind = "class",
                        Name = "ObjectList",
                        Implements = ["\\Iterator"],
                        Methods =
                        [
                            new TyhpdefMethod
                            {
                                Name = "current",
                                Modifiers = ["public"],
                                ReturnType = "object",
                            },
                        ],
                    },
                ],
            };

            var overlayText = EnrichToOverlay(layer1, outputDir, stubCacheDir, "iter");
            overlayText.Should().Contain("partial interface Iterator<TValue>;");
            overlayText.Should().Contain("partial class ObjectList<TObject extends object = object>;");
            overlayText.Should().NotContain("implements \\Iterator<int, TObject>");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_MemberKeyValuePlaceholdersWithoutClassTemplate_DeclaresBothOnType()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-aaccess-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-aaccess-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "arrayaccess.phpstub"), """
                <?php

                interface ArrayAccess
                {
                    /**
                     * @param TKey $offset
                     */
                    public function offsetExists($offset) {}

                    /**
                     * @param TKey $offset
                     * @return TValue
                     */
                    public function offsetGet($offset) {}

                    /**
                     * @param TKey $offset
                     * @param TValue $value
                     */
                    public function offsetSet($offset, $value) {}

                    /**
                     * @param TKey $offset
                     */
                    public function offsetUnset($offset) {}
                }
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalTypes =
                [
                    new TyhpdefClassDeclaration
                    {
                        Kind = "interface",
                        Name = "ArrayAccess",
                        Methods =
                        [
                            new TyhpdefMethod
                            {
                                Name = "offsetExists",
                                Modifiers = ["abstract", "public"],
                                Parameters =
                                [
                                    new TyhpdefParameter { Name = "offset", Type = "mixed" },
                                ],
                                ReturnType = "bool",
                            },
                            new TyhpdefMethod
                            {
                                Name = "offsetGet",
                                Modifiers = ["abstract", "public"],
                                Parameters =
                                [
                                    new TyhpdefParameter { Name = "offset", Type = "mixed" },
                                ],
                                ReturnType = "mixed",
                            },
                            new TyhpdefMethod
                            {
                                Name = "offsetSet",
                                Modifiers = ["abstract", "public"],
                                Parameters =
                                [
                                    new TyhpdefParameter { Name = "offset", Type = "mixed" },
                                    new TyhpdefParameter { Name = "value", Type = "mixed" },
                                ],
                                ReturnType = "void",
                            },
                            new TyhpdefMethod
                            {
                                Name = "offsetUnset",
                                Modifiers = ["abstract", "public"],
                                Parameters =
                                [
                                    new TyhpdefParameter { Name = "offset", Type = "mixed" },
                                ],
                                ReturnType = "void",
                            },
                        ],
                    },
                ],
            };

            var overlayText = EnrichToOverlay(layer1, outputDir, stubCacheDir, "core");
            overlayText.Should().Contain("partial interface ArrayAccess<TKey, TValue>;");
            overlayText.Should().Contain("partial interface ArrayAccess {");
            overlayText.Should().Contain("function offsetExists(TKey $offset): bool");
            overlayText.Should().Contain("function offsetGet(TKey $offset): TValue");
            overlayText.Should().Contain("function offsetSet(TKey $offset, TValue $value): void");
            overlayText.Should().Contain("function offsetUnset(TKey $offset): void");
            StubHarvestTyhpdefEnricher.FindUndeclaredConventionalTemplates(overlayText).Should().BeEmpty();
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_FunctionTemplatePlaceholderWithoutDeclaration_DeclaresFunctionGenerics()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-fn-tmpl-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-fn-tmpl-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "reduce.phpstub"), """
                <?php

                /**
                 * @template TIn
                 * @template TReturn
                 * @param array<TValue> $array
                 * @param callable $callback
                 * @param TCarry $initial
                 * @return TReturn
                 */
                function array_reduce($array, $callback, $initial = null) {}
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalFunctions =
                [
                    new TyhpdefFunction
                    {
                        Name = "array_reduce",
                        Parameters =
                        [
                            new TyhpdefParameter { Name = "array", Type = "array" },
                            new TyhpdefParameter { Name = "callback", Type = "callable" },
                            new TyhpdefParameter { Name = "initial", Type = "mixed" },
                        ],
                        ReturnType = "mixed",
                    },
                ],
            };

            var overlayText = EnrichToOverlay(layer1, outputDir, stubCacheDir, "standard");
            overlayText.Should().Contain("function array_reduce<");
            overlayText.Should().Contain("TValue");
            overlayText.Should().Contain("TCarry");
            overlayText.Should().Contain("TReturn");
            StubHarvestTyhpdefEnricher.FindUndeclaredConventionalTemplates(overlayText).Should().BeEmpty();
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void FindUndeclaredConventionalTemplates_ReportsMemberPlaceholderWithoutTypeGenerics()
    {
        var invalid = """
            <?tyhpdef
            partial interface ArrayAccess {
                abstract public function offsetExists(TKey $offset): bool;
            }
            """;
        StubHarvestTyhpdefEnricher.FindUndeclaredConventionalTemplates(invalid)
            .Should()
            .Contain("ArrayAccess::offsetExists: TKey");

        var valid = """
            <?tyhpdef
            partial interface ArrayAccess<TKey, TValue> {
                abstract public function offsetExists(TKey $offset): bool;
                abstract public function offsetGet(TKey $offset): TValue;
            }
            """;
        StubHarvestTyhpdefEnricher.FindUndeclaredConventionalTemplates(valid).Should().BeEmpty();
    }

    [Fact]
    public void Enrich_MethodLevelTemplate_StaysOnMethodNotClass()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-mgen-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-mgen-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "ident.phpstub"), """
                <?php

                class IdentBox
                {
                    /**
                     * @template T
                     * @param T $value
                     * @return T
                     */
                    public function identity($value) {}
                }
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalTypes =
                [
                    new TyhpdefClassDeclaration
                    {
                        Kind = "class",
                        Name = "IdentBox",
                        Methods =
                        [
                            new TyhpdefMethod
                            {
                                Name = "identity",
                                Modifiers = ["public"],
                                Parameters =
                                [
                                    new TyhpdefParameter { Name = "value", Type = "mixed" },
                                ],
                                ReturnType = "mixed",
                            },
                        ],
                    },
                ],
            };

            var overlayText = EnrichToOverlay(layer1, outputDir, stubCacheDir, "ident");
            overlayText.Should().Contain("partial class IdentBox {");
            overlayText.Should().NotContain("IdentBox<");
            overlayText.Should().Contain("function identity<T>(T $value): T");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void ReferencesUndeclaredTemplate_TValueWithoutDeclaration()
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        StubHarvestTyhpdefEnricher.ReferencesUndeclaredTemplate("TValue", declared).Should().BeTrue();
        StubHarvestTyhpdefEnricher.ReferencesUndeclaredTemplate("?TValue", declared).Should().BeTrue();
        StubHarvestTyhpdefEnricher.ReferencesUndeclaredTemplate("string", declared).Should().BeFalse();
        StubHarvestTyhpdefEnricher.ReferencesUndeclaredTemplate("\\DateTimeInterface", declared).Should().BeFalse();

        declared.Add("TValue");
        StubHarvestTyhpdefEnricher.ReferencesUndeclaredTemplate("TValue", declared).Should().BeFalse();
        StubHarvestTyhpdefEnricher.IsConventionalTemplateName("TYPE_DEFAULT").Should().BeFalse();
        StubHarvestTyhpdefEnricher.IsConventionalTemplateName("TKey").Should().BeTrue();
        StubHarvestTyhpdefEnricher.ReferencesUndeclaredTemplate(
            "Start",
            declared,
            new HashSet<string>(["Start"], StringComparer.Ordinal)).Should().BeTrue();
        declared.Add("Start");
        StubHarvestTyhpdefEnricher.ReferencesUndeclaredTemplate(
            "Start",
            declared,
            new HashSet<string>(["Start"], StringComparer.Ordinal)).Should().BeFalse();
    }

    [Fact]
    public void ComposerPackageMode_DoesNotWriteStubOverlays()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-b-").FullName;
        try
        {
            var enricher = new StubHarvestTyhpdefEnricher();
            var result = new TyhpdefGenerationResult();
            enricher.Enrich(
                new TyhpdefFile
                {
                    GlobalFunctions = [new TyhpdefFunction { Name = "json_decode", ReturnType = "mixed" }],
                },
                new TyhpdefGenerationOptions
                {
                    Mode = TyhpdefGenerationMode.ComposerPackage,
                    OutputDirectory = outputDir,
                    StubCacheDirectory = TyhpdefGenFixtures.JsonStubsPath,
                    FetchStubCache = false,
                    Overwrite = true,
                },
                result,
                "json");

            result.GeneratedFiles.Should().BeEmpty();
            Directory.Exists(Path.Combine(outputDir, "overlays", "stubs")).Should().BeFalse();
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void PsalmPhpStanDisagreement_KeepsLayer1AndWarns()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-dis-").FullName;
        try
        {
            var (result, _, overlayText, _) = GenerateJson(
                outputDir,
                TyhpdefGenFixtures.DisagreeStubsPath,
                includeDocs: false,
                requireStubs: false);

            result.Success.Should().BeTrue();
            result.Diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefStubCorpusDisagreement);
            if (overlayText.Length > 0)
            {
                overlayText.Should().NotContain("function json_decode");
            }
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void ConsensusType_PsalmPhpStanAgree_UsesSharedType()
    {
        var diagnostics = new DiagnosticBag();
        var type = StubHarvestTyhpdefEnricher.ConsensusType(
            new Dictionary<string, string?>
            {
                [StubCorpusCache.PsalmId] = "array|object|null",
                [StubCorpusCache.PhpStanId] = "object|array|null",
            },
            "\\json_decode",
            diagnostics);

        type.Should().Be("array|null|object");
        diagnostics.HasWarnings.Should().BeFalse();
    }

    [Theory]
    [InlineData("resource", true)]
    [InlineData("?resource", true)]
    [InlineData("resource|null", true)]
    [InlineData("null|resource", true)]
    [InlineData("false|resource", false)]
    [InlineData("mixed", false)]
    [InlineData("\\PgSql\\Connection", false)]
    public void IsBareResourceType_OnlyNullableResource(string type, bool expected)
    {
        StubHarvestTyhpdefEnricher.IsBareResourceType(type).Should().Be(expected);
    }

    [Fact]
    public void BuildResourceObjectTypes_UsesSiblingConnectionClass()
    {
        var layer1 = new TyhpdefFile
        {
            GlobalFunctions =
            [
                new TyhpdefFunction
                {
                    Name = "pg_query",
                    Parameters =
                    [
                        new TyhpdefParameter { Name = "connection", Type = "mixed" },
                        new TyhpdefParameter { Name = "query", Type = "string" },
                    ],
                    ReturnType = "\\PgSql\\Result|false",
                },
                new TyhpdefFunction
                {
                    Name = "pg_cancel_query",
                    Parameters =
                    [
                        new TyhpdefParameter { Name = "connection", Type = "\\PgSql\\Connection" },
                    ],
                    ReturnType = "bool",
                },
            ],
        };

        var aliases = StubHarvestTyhpdefEnricher.BuildResourceObjectTypes(layer1);
        aliases.Should().ContainKey("connection");
        aliases["connection"].Should().Be("\\PgSql\\Connection");
    }

    [Fact]
    public void RewriteLegacyResourceType_ReplacesResourceWithSiblingClass()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["connection"] = "\\PgSql\\Connection",
        };

        StubHarvestTyhpdefEnricher.RewriteLegacyResourceType("resource", "connection", aliases)
            .Should().Be("\\PgSql\\Connection");
        StubHarvestTyhpdefEnricher.RewriteLegacyResourceType("?resource", "connection", aliases)
            .Should().Be("?\\PgSql\\Connection");
        StubHarvestTyhpdefEnricher.RewriteLegacyResourceType("false|resource", "connection", aliases)
            .Should().Be("false|resource");
        StubHarvestTyhpdefEnricher.RewriteLegacyResourceType("resource", "stream", aliases)
            .Should().Be("resource");
    }

    /// <summary>
    /// PHP 8.1+ pgsql connections are <c>\PgSql\Connection</c>. Community stubs still
    /// type overloaded <c>$connection</c> parameters as <c>resource</c>; Layer 2 must
    /// rewrite that using the class already present on sibling Layer 1 signatures.
    /// </summary>
    [Fact]
    public void Enrich_PgsqlStubResourceConnection_RewritesToPgSqlConnection()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-pgsql-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-pgsql-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "pgsql.phpstub"), """
                <?php

                /**
                 * @param resource $connection
                 * @param string $query
                 * @return mixed
                 */
                function pg_query($connection, $query) {}
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalFunctions =
                [
                    new TyhpdefFunction
                    {
                        Name = "pg_query",
                        Parameters =
                        [
                            new TyhpdefParameter { Name = "connection", Type = "mixed" },
                            new TyhpdefParameter { Name = "query", Type = "mixed" },
                        ],
                        ReturnType = "\\PgSql\\Result|false",
                    },
                    new TyhpdefFunction
                    {
                        Name = "pg_cancel_query",
                        Parameters =
                        [
                            new TyhpdefParameter { Name = "connection", Type = "\\PgSql\\Connection" },
                        ],
                        ReturnType = "bool",
                    },
                ],
            };

            var enricher = new StubHarvestTyhpdefEnricher();
            var result = new TyhpdefGenerationResult();
            enricher.Enrich(
                layer1,
                new TyhpdefGenerationOptions
                {
                    Mode = TyhpdefGenerationMode.PhpExtension,
                    OutputDirectory = outputDir,
                    IncludeDocComments = false,
                    Overwrite = true,
                    StubCacheDirectory = stubCacheDir,
                    FetchStubCache = false,
                },
                result,
                "pgsql");

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var overlayPath = Path.Combine(outputDir, "overlays", "stubs", "ExtPgsql.tyhpdef");
            File.Exists(overlayPath).Should().BeTrue();
            var overlayText = File.ReadAllText(overlayPath);
            overlayText.Should().Contain("function pg_query(\\PgSql\\Connection $connection, string $query)");
            overlayText.Should().NotContain("resource $connection");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_PhpStormHtmlAfterArrayReturn_DoesNotEmitArrayPGeneric()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-html-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-html-stubs-").FullName;
        try
        {
            var phpstormDir = Path.Combine(stubCacheDir, "phpstorm");
            Directory.CreateDirectory(phpstormDir);
            File.WriteAllText(Path.Combine(phpstormDir, "core.php"), """
                <?php

                /**
                 * @param int $options
                 * @return array <p>
                 * Returns an associative array.
                 * </p>
                 */
                function html_bt($options): array {}
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalFunctions =
                [
                    new TyhpdefFunction
                    {
                        Name = "html_bt",
                        Parameters =
                        [
                            new TyhpdefParameter { Name = "options", Type = "mixed" },
                        ],
                        ReturnType = "array",
                    },
                ],
            };

            var enricher = new StubHarvestTyhpdefEnricher();
            var result = new TyhpdefGenerationResult();
            enricher.Enrich(
                layer1,
                new TyhpdefGenerationOptions
                {
                    Mode = TyhpdefGenerationMode.PhpExtension,
                    OutputDirectory = outputDir,
                    IncludeDocComments = false,
                    Overwrite = true,
                    StubCacheDirectory = stubCacheDir,
                    FetchStubCache = false,
                },
                result,
                "htmlharvest");

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var overlayPath = Path.Combine(outputDir, "overlays", "stubs", "ExtHtmlharvest.tyhpdef");
            File.Exists(overlayPath).Should().BeTrue();
            var overlayText = File.ReadAllText(overlayPath);
            overlayText.Should().Contain("function html_bt(int $options): array");
            overlayText.Should().NotContain("array<p>");
            overlayText.Should().NotContain("array<b>");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void Enrich_StreamResourceWithoutObjectAlias_KeepsResource()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-l2-stream-").FullName;
        var stubCacheDir = Directory.CreateTempSubdirectory("tyhpdef-l2-stream-stubs-").FullName;
        try
        {
            var psalmDir = Path.Combine(stubCacheDir, "psalm");
            Directory.CreateDirectory(psalmDir);
            File.WriteAllText(Path.Combine(psalmDir, "stream.phpstub"), """
                <?php

                /**
                 * @param resource $stream
                 * @return bool
                 */
                function fclose($stream) {}
                """);

            var layer1 = new TyhpdefFile
            {
                GlobalFunctions =
                [
                    new TyhpdefFunction
                    {
                        Name = "fclose",
                        Parameters =
                        [
                            new TyhpdefParameter { Name = "stream", Type = "mixed" },
                        ],
                        ReturnType = "bool",
                    },
                ],
            };

            var enricher = new StubHarvestTyhpdefEnricher();
            var result = new TyhpdefGenerationResult();
            enricher.Enrich(
                layer1,
                new TyhpdefGenerationOptions
                {
                    Mode = TyhpdefGenerationMode.PhpExtension,
                    OutputDirectory = outputDir,
                    IncludeDocComments = false,
                    Overwrite = true,
                    StubCacheDirectory = stubCacheDir,
                    FetchStubCache = false,
                },
                result,
                "standard");

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var overlayPath = Path.Combine(outputDir, "overlays", "stubs", "ExtStandard.tyhpdef");
            File.Exists(overlayPath).Should().BeTrue();
            File.ReadAllText(overlayPath).Should().Contain("resource $stream");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(stubCacheDir);
        }
    }

    [Fact]
    public void IsForbiddenOutputDirectory_RejectsRuntimeOverlayTrees()
    {
        StubHarvestTyhpdefEnricher.IsForbiddenOutputDirectory(
            Path.Combine("vendor", "acme", "widget", "_tyhpdef", "overlays")).Should().BeTrue();
        StubHarvestTyhpdefEnricher.IsForbiddenOutputDirectory(
            Path.Combine("vendor", "acme", "widget", "_tyhpdef", "overlays", "Hand.tyhpdef")).Should().BeTrue();
        StubHarvestTyhpdefEnricher.IsForbiddenOutputDirectory(
            Path.Combine("vendor", "acme", "calendar", "_tyhpdef", "overlays")).Should().BeTrue();
        StubHarvestTyhpdefEnricher.IsForbiddenOutputDirectory(
            Path.Combine("vendor", "acme", "widget", "_tyhpdef", "overlays", "stubs")).Should().BeFalse();
        StubHarvestTyhpdefEnricher.IsForbiddenOutputDirectory(
            Path.Combine("vendor", "acme", "widget", "_tyhpdef", "overlays", "stubs", "Hand.tyhpdef")).Should().BeFalse();
        StubHarvestTyhpdefEnricher.IsForbiddenOutputDirectory(
            Path.Combine("vendor", "acme", "widget", "_tyhpdef")).Should().BeFalse();
        StubHarvestTyhpdefEnricher.IsForbiddenOutputDirectory(
            Path.Combine(Path.GetTempPath(), "tyhpdef")).Should().BeFalse();
        StubHarvestTyhpdefEnricher.IsForbiddenOutputDirectory(
            Path.Combine(Path.GetTempPath(), "tyhpdef", "overlays", "stubs")).Should().BeFalse();
    }

    private static string EnrichToOverlay(
        TyhpdefFile layer1,
        string outputDir,
        string stubCacheDir,
        string extName)
    {
        var enricher = new StubHarvestTyhpdefEnricher();
        var result = new TyhpdefGenerationResult();
        enricher.Enrich(
            layer1,
            new TyhpdefGenerationOptions
            {
                Mode = TyhpdefGenerationMode.PhpExtension,
                OutputDirectory = outputDir,
                IncludeDocComments = false,
                Overwrite = true,
                StubCacheDirectory = stubCacheDir,
                FetchStubCache = false,
            },
            result,
            extName);

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var overlayPath = Path.Combine(
            outputDir,
            "overlays",
            "stubs",
            TyhpdefOutputLayout.ExtensionFileName(extName));
        File.Exists(overlayPath).Should().BeTrue();
        return File.ReadAllText(overlayPath);
    }

    private static (TyhpdefGenerationResult Result, string Layer1, string Overlay, string Sources) GenerateJson(
        string outputDir,
        string stubCache,
        bool includeDocs,
        bool requireStubs)
    {
        var options = new TyhpdefGenerationOptions
        {
            Mode = TyhpdefGenerationMode.PhpExtension,
            ExtensionName = "json",
            OutputDirectory = outputDir,
            IncludeDocComments = includeDocs,
            IncludeDeprecated = true,
            Overwrite = true,
            Split = "file",
            Locale = "en",
            FetchStubCache = false,
            StubCacheDirectory = stubCache,
            RequireStubs = requireStubs,
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
        if (includeDocs)
        {
            extractor.LoadHtml(File.ReadAllText(TyhpdefGenFixtures.ManualExcerptPath), "en");
        }

        var generator = new PhpDelegationTyhpdefGenerator(
            new PhpRuntimeManager(new MissingTransport()),
            new PhpRuntimeDetector(),
            extractor);
        generator.GenerateFromJson(
            File.ReadAllText(TyhpdefGenFixtures.JsonSchemaPath),
            options,
            result,
            runtime);

        var layer1Path = Path.Combine(outputDir, "ExtJson.tyhpdef");
        var overlayPath = Path.Combine(outputDir, "overlays", "stubs", "ExtJson.tyhpdef");
        var sourcesPath = Path.Combine(outputDir, "SOURCES.md");
        return (
            result,
            File.Exists(layer1Path) ? File.ReadAllText(layer1Path) : "",
            File.Exists(overlayPath) ? File.ReadAllText(overlayPath) : "",
            File.Exists(sourcesPath) ? File.ReadAllText(sourcesPath) : "");
    }

    private static void TryDelete(string outputDir)
    {
        try
        {
            Directory.Delete(outputDir, recursive: true);
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
