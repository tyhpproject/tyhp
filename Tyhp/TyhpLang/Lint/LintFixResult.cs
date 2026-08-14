namespace Tyhp.TyhpLang.Lint
{
    /// <summary>
    /// A single text replacement within a source file (1-based line, 0-based column,
    /// matching the internal diagnostic coordinate contract).
    /// </summary>
    public readonly record struct TextEdit(
        int StartLine,
        int StartColumn,
        int EndLine,
        int EndColumn,
        string NewText);

    /// <summary>
    /// Result of applying an <see cref="ILintFix"/> to a source file.
    /// </summary>
    public sealed class LintFixResult
    {
        /// <summary>Whether the fix was applied successfully.</summary>
        public bool Success { get; init; }

        /// <summary>The modified source text, or <see langword="null"/> if the fix failed.</summary>
        public string? ModifiedSourceText { get; init; }

        /// <summary>Why the fix failed, or <see langword="null"/> on success.</summary>
        public string? FailureReason { get; init; }

        /// <summary>Individual text edits applied (empty when the fix failed).</summary>
        public IReadOnlyList<TextEdit> Edits { get; init; } = Array.Empty<TextEdit>();

        /// <summary>Creates a failed fix result.</summary>
        public static LintFixResult Failed(string reason)
            => new()
            {
                Success = false,
                FailureReason = reason,
                ModifiedSourceText = null,
                Edits = Array.Empty<TextEdit>(),
            };

        /// <summary>Creates a successful fix result.</summary>
        public static LintFixResult Succeeded(string modifiedSourceText, IReadOnlyList<TextEdit>? edits = null)
            => new()
            {
                Success = true,
                ModifiedSourceText = modifiedSourceText,
                FailureReason = null,
                Edits = edits ?? Array.Empty<TextEdit>(),
            };
    }
}
