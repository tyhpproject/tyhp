using System.IO;
using Antlr4.Runtime;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.TyhpLang.Parser
{
    /// <summary>
    /// ANTLR error listener that writes syntax errors to a DiagnosticBag.
    /// Thread-safe for concurrent parsing scenarios.
    /// </summary>
    /// <typeparam name="TType">The type of offending symbol (int for lexer, IToken for parser).</typeparam>
    public class TyhpAntlrErrorListener<TType> : IAntlrErrorListener<TType>, IDisposable
    {
        private readonly DiagnosticBag _diagnostics;
        private readonly ThreadLocal<string> _fileName;
        private readonly MessageCode? _overrideErrorCode;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the TyhpAntlrErrorListener class.
        /// </summary>
        /// <param name="diagnostics">The diagnostic bag to write errors to.</param>
        /// <param name="overrideErrorCode">Optional message code override. When set, all errors use this code
        /// instead of the default lexer/parser codes.</param>
        public TyhpAntlrErrorListener(DiagnosticBag diagnostics, MessageCode? overrideErrorCode = null)
        {
            this._diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            this._fileName = new ThreadLocal<string>(() => string.Empty, trackAllValues: false);
            this._overrideErrorCode = overrideErrorCode;
        }

        /// <summary>
        /// Sets the file name for the current thread's parsing context.
        /// Must be called before parsing each file in multi-threaded scenarios.
        /// </summary>
        /// <param name="fileName">The file name being parsed.</param>
        public void SetFileName(string fileName)
        {
            this._fileName.Value = fileName;
        }

        /// <inheritdoc/>
        public void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            TType offendingSymbol,
            int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            // Filter out certain ANTLR internal messages that aren't user-facing errors
            if (msg.Contains("reportAttemptingFullContext") ||
                msg.Contains("reportContextSensitivity") ||
                msg.Contains("failed predicate"))
            {
                return;
            }

            MessageCode messageCode;
            string tokenRepresentation;

            if (typeof(TType) == typeof(int))
            {
                messageCode = this._overrideErrorCode ?? MessageCode.ParserUnknownError;

                // For lexer errors, the offending symbol is a character code
                // Display it as a character if printable, otherwise as hex
                if (offendingSymbol is int charCode)
                {
                    if (charCode >= 32 && charCode <= 126)
                    {
                        tokenRepresentation = $"'{(char)charCode}'";
                    }
                    else if (charCode == -1)
                    {
                        tokenRepresentation = "<EOF>";
                    }
                    else
                    {
                        tokenRepresentation = $"0x{charCode:X}";
                    }
                }
                else
                {
                    tokenRepresentation = offendingSymbol?.ToString() ?? "<unknown>";
                }
            }
            else
            {
                messageCode = this._overrideErrorCode ?? MessageCode.ParserUnexpectedError;

                if (offendingSymbol is IToken token)
                {
                    // Use the vocabulary to get the display name for parser tokens
                    tokenRepresentation = recognizer.Vocabulary.GetDisplayName(token.Type);
                }
                else
                {
                    tokenRepresentation = offendingSymbol?.ToString() ?? "<unknown>";
                }
            }

            // Add the diagnostic to the bag
            // ERROR_TYHP1002 expects: "Unexpected token '{0}' at position {1}"
            // Parameter 0: token representation
            // Parameter 1: character position
            this._diagnostics.AddError(
                messageCode,
                this._fileName.Value ?? string.Empty,
                line,
                charPositionInLine,
                tokenRepresentation,
                charPositionInLine);
        }

        /// <summary>
        /// Releases all resources used by this error listener.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by this error listener and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this._disposed)
            {
                if (disposing)
                {
                    this._fileName?.Dispose();
                }
                this._disposed = true;
            }
        }
    }
}
