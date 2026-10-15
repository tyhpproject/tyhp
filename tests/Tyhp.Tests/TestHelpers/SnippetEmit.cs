using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.TestHelpers;

/// <summary>
/// Compiles in-memory Tyhp snippets (checked) and returns diagnostics plus emitted PHP.
/// </summary>
public static class SnippetEmit
{
    public sealed record Result(
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings,
        string Php,
        IReadOnlyList<PHPOutputFile>? Files = null);

    public static Result Compile(string tyhp, string phpVersion = "8.2")
    {
        return Compile(new Dictionary<string, string>(StringComparer.Ordinal) { ["probe.tyhp"] = tyhp }, phpVersion);
    }

    public static Result Compile(IReadOnlyDictionary<string, string> files, string phpVersion = "8.2")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                files.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                IsolatedCompilation.CreateOptions(tempDir, phpVersion));

            var errors = result.Diagnostics.Errors.Select(d => $"{(int)d.Code}: {d.Message}").ToList();
            var warnings = result.Diagnostics.Warnings.Select(d => $"{(int)d.Code}: {d.Message}").ToList();
            if (errors.Count > 0)
            {
                return new Result(errors, warnings, string.Empty);
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["output:phpVersion"] = phpVersion })
                .Build();
            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                new Project(configuration),
                result.RequiresRuntimeGenericTracking,
                result.RequiresWeakReferenceCapture,
                result.RequiresDisposableTryFinally,
                result.AsyncForeachKinds,
                result.RequiresGenericVariant,
                result.GenericCallTargets,
                result.InferredClosureSignatures,
                result.ExpressionTypes,
                result.NativeTypeTests);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return new Result(
                errors,
                warnings,
                string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty)),
                outputFiles.ToList());
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
