using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// <c>--verify</c>: regenerate/load golden Layer 1, apply overlays to the existing tree,
    /// and compatibility-check merged symbols (not file text). Overlay apply is comparison-only.
    /// </summary>
    public sealed class TyhpdefVerifyService
    {
        public void Verify(
            TyhpdefGenerationOptions options,
            TyhpdefGenerationResult result,
            TyhpdefFile? golden,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(result);

            if (golden is null)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefVerifyNoGolden"));
                Message.Error("CLI_TyhpdefVerifyNoGolden");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var outputDir = options.OutputDirectory;
            if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir))
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefVerifyNoExisting", outputDir ?? ""));
                Message.Error("CLI_TyhpdefVerifyNoExisting", outputDir ?? "");
                return;
            }

            var baselinePaths = TyhpdefOverlayApplier.ResolveBaselinePaths(outputDir, result.Diagnostics);
            if (baselinePaths.Count == 0)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    outputDir,
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefVerifyNoExisting", outputDir));
                Message.Error("CLI_TyhpdefVerifyNoExisting", outputDir);
                return;
            }

            var finalApi = TyhpdefAstApiReader.ReadFiles(baselinePaths, result.Diagnostics);
            if (result.Diagnostics.HasErrors)
            {
                return;
            }

            var overlayPaths = TyhpdefOverlayApplier.ResolveOverlayPaths(outputDir, result.Diagnostics);
            TyhpdefOverlayApplier.Apply(finalApi, overlayPaths, result.Diagnostics);
            if (result.Diagnostics.HasErrors)
            {
                return;
            }

            var goldenApi = TyhpdefApiIndex.FromIr(golden);
            Compare(goldenApi, finalApi, result.Diagnostics);

            if (!result.Diagnostics.HasErrors)
            {
                Message.Display("CLI_TyhpdefVerifyPassed", goldenApi.Symbols.Count, overlayPaths.Count);
            }
        }

        internal static void Compare(TyhpdefApiIndex golden, TyhpdefApiIndex final, DiagnosticBag diagnostics)
        {
            foreach (var symbol in golden.Symbols.Values.OrderBy(s => s.Fqn, StringComparer.OrdinalIgnoreCase))
            {
                if (final.Omitted.Contains(symbol.Fqn) || golden.Omitted.Contains(symbol.Fqn))
                {
                    continue;
                }

                var found = final.Get(symbol.Fqn);
                if (found is null)
                {
                    diagnostics.AddError(
                        MessageCode.TyhpdefVerifyIncompatible,
                        "generate_tyhpdef",
                        0,
                        0,
                        symbol.Fqn);
                    continue;
                }

                if (!KindsCompatible(symbol.Kind, found.Kind))
                {
                    diagnostics.AddError(
                        MessageCode.TyhpdefVerifyIncompatible,
                        "generate_tyhpdef",
                        0,
                        0,
                        symbol.Fqn);
                    continue;
                }

                if (IsCallable(symbol.Kind) && !TyhpdefApiIndex.CallablesCover(symbol, found))
                {
                    diagnostics.AddError(
                        MessageCode.TyhpdefVerifyIncompatible,
                        "generate_tyhpdef",
                        0,
                        0,
                        symbol.Fqn);
                }
            }
        }

        private static bool KindsCompatible(string golden, string final)
        {
            if (string.Equals(golden, final, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsCallable(golden) && IsCallable(final))
            {
                return true;
            }

            return false;
        }

        private static bool IsCallable(string kind)
            => kind is "function" or "method" or "fn" or "operator";
    }
}
