using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// <c>tyhp generate_tyhpdef --validate=&lt;path&gt;</c>: parse-check existing <c>.tyhpdef</c> files.
    /// </summary>
    public sealed class TyhpdefValidateService
    {
        public void Validate(
            string path,
            TyhpdefGenerationResult result,
            bool quiet,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (string.IsNullOrWhiteSpace(path))
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    "generate_tyhpdef",
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefValidatePathNotFound", path ?? ""));
                Message.Error("CLI_TyhpdefValidatePathNotFound", path ?? "");
                return;
            }

            var fullPath = Path.GetFullPath(path.Trim());
            List<string> files;
            try
            {
                files = Discover(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                result.Diagnostics.AddError(
                    MessageCode.TyhpdefGenerationError,
                    fullPath,
                    0,
                    0,
                    Message.Localize("CLI_TyhpdefValidatePathNotFound", fullPath));
                Message.Error("CLI_TyhpdefValidatePathNotFound", fullPath);
                Message.Debug(ex.Message);
                return;
            }

            if (files.Count == 0)
            {
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                {
                    result.Diagnostics.AddError(
                        MessageCode.TyhpdefGenerationError,
                        fullPath,
                        0,
                        0,
                        Message.Localize("CLI_TyhpdefValidatePathNotFound", fullPath));
                    Message.Error("CLI_TyhpdefValidatePathNotFound", fullPath);
                    return;
                }

                Message.Display("CLI_TyhpdefValidateNoFiles", fullPath);
                Message.Display("CLI_TyhpdefValidateSummary", 0, 0, 0, 0);
                return;
            }

            var passed = 0;
            var failed = 0;
            var errorCount = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ValidateFile(file, result.Diagnostics, quiet, out var fileErrors))
                {
                    passed++;
                    continue;
                }

                failed++;
                errorCount += fileErrors;
            }

            result.ValidatedFileCount = files.Count;
            result.ValidatedPassedCount = passed;
            result.ValidatedFailedCount = failed;
            result.ValidatedErrorCount = errorCount;

            Message.Display("CLI_TyhpdefValidateSummary", files.Count, passed, failed, errorCount);
        }

        internal static List<string> Discover(string fullPath)
        {
            if (File.Exists(fullPath))
            {
                return fullPath.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                    ? [fullPath]
                    : [];
            }

            if (!Directory.Exists(fullPath))
            {
                return [];
            }

            return Directory.EnumerateFiles(fullPath, "*.tyhpdef", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool ValidateFile(string file, DiagnosticBag aggregate, bool quiet, out int errorCount)
        {
            errorCount = 0;
            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errorCount = 1;
                aggregate.AddError(MessageCode.TyhpdefGenerationError, file, 0, 0, ex.Message);
                Message.Error("CLI_TyhpdefValidateReadFailed", file, ex.Message);
                Message.Display("CLI_TyhpdefValidateFileFail", file, 1);
                return false;
            }

            var diagnostics = new DiagnosticBag();
            try
            {
                var ast = Tyhpdef.ParseContent(content, file, ParseMode.Tyhpdef, diagnostics);
                if (ast is not null && !diagnostics.HasErrors)
                {
                    if (!quiet)
                    {
                        Message.Display("CLI_TyhpdefValidateFilePass", file);
                    }

                    return true;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                diagnostics.AddError(MessageCode.TyhpdefParseError, file, 0, 0, ex.Message);
            }

            errorCount = diagnostics.Errors.Count;
            if (errorCount == 0)
            {
                diagnostics.AddError(MessageCode.TyhpdefParseError, file, 0, 0, file);
                errorCount = 1;
            }

            aggregate.AddRange(diagnostics.Errors);
            Message.Display("CLI_TyhpdefValidateFileFail", file, errorCount);
            return false;
        }
    }
}
