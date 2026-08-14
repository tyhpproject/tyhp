using System.Collections.Generic;
using System;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Symbols
{
    public class FileSymbol :
        BaseSymbol
    {
        public string FileName { get; protected set; }

        public string FileHash { get; protected set; }

        private Dictionary<string, string> _fileDeclareDirectives;
        public IReadOnlyDictionary<string, string> FileDeclareDirectives => this._fileDeclareDirectives;

        /// <summary>
        /// Creates a file declaration symbol.
        /// <para>
        /// Enforces strict invariants: <paramref name="fileName"/> and
        /// <paramref name="fileHash"/> must be non-empty values.
        /// </para>
        /// </summary>
        public FileSymbol(
            string fileName,
            string fileHash,
            string? sourceFile = null
        )
            : base(fileName, SymbolType.File, sourceFile: string.IsNullOrWhiteSpace(sourceFile) ? fileName : sourceFile)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new System.ArgumentException("fileName must not be null or whitespace.", nameof(fileName));
            }

            if (string.IsNullOrWhiteSpace(fileHash))
            {
                throw new System.ArgumentException("fileHash must not be null or whitespace.", nameof(fileHash));
            }

            this.FileName = fileName;
            this.FileHash = fileHash;
            this._fileDeclareDirectives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Attempts to add or update a file declare directive.
        /// Returns <c>false</c> with a validation message if the key or value is invalid.
        /// </summary>
        public bool TryAddFileDeclareDirective(
            string key,
            string value,
            out string? validationMessage
        )
        {
            if (!TryNormalizeDirectiveKey(key, out var normalizedKey, out var keyError))
            {
                validationMessage = keyError;
                return false;
            }

            if (!TryNormalizeDirectiveValue(value, out var normalizedValue, out var valueError))
            {
                validationMessage = valueError;
                return false;
            }

            this._fileDeclareDirectives[normalizedKey] = normalizedValue;
            validationMessage = null;
            return true;
        }

        public bool TryGetFileDeclareDirective(
            string key,
            out string? value
        )
        {
            value = null;

            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var normalizedKey = NormalizeDirectiveKey(key, nameof(key));
            return this._fileDeclareDirectives.TryGetValue(normalizedKey, out value);
        }

        public bool HasFileDeclareDirective(string key)
        {
            return this.TryGetFileDeclareDirective(key, out _);
        }

        /// <summary>
        /// Adds or updates a file declare directive.
        /// Throws <see cref="ArgumentException"/> if the key or value is invalid.
        /// </summary>
        public FileSymbol AddFileDeclareDirective(string key, string value)
        {
            if (!this.TryAddFileDeclareDirective(key, value, out var error))
            {
                throw new ArgumentException(error);
            }

            return this;
        }

        private static bool TryNormalizeDirectiveKey(string key, out string normalizedKey, out string? error)
        {
            normalizedKey = string.Empty;
            error = null;

            if (string.IsNullOrWhiteSpace(key))
            {
                error = "Directive key must not be null or whitespace.";
                return false;
            }

            var trimmed = key.Trim();
            if (!IsValidDirectiveKeyToken(trimmed))
            {
                error = $"Not a valid directive key token: {key}";
                return false;
            }

            normalizedKey = trimmed.ToLowerInvariant();
            return true;
        }

        private static bool TryNormalizeDirectiveValue(string value, out string normalizedValue, out string? error)
        {
            normalizedValue = string.Empty;
            error = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Directive value must not be null or whitespace.";
                return false;
            }

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                error = "Directive value must not be empty.";
                return false;
            }

            normalizedValue = trimmed;
            return true;
        }

        private static string NormalizeDirectiveKey(string key, string parameterName)
        {
            if (!TryNormalizeDirectiveKey(key, out var normalizedKey, out var error))
            {
                throw new ArgumentException(error, parameterName);
            }

            return normalizedKey;
        }

        private static string NormalizeDirectiveValue(string value, string parameterName)
        {
            if (!TryNormalizeDirectiveValue(value, out var normalizedValue, out var error))
            {
                throw new ArgumentException(error, parameterName);
            }

            return normalizedValue;
        }

        private static bool IsValidDirectiveKeyToken(string token)
        {
            if (token.Length == 0)
            {
                return false;
            }

            var start = token[0];
            if (!(start == '_' || (start >= 'A' && start <= 'Z') || (start >= 'a' && start <= 'z')))
            {
                return false;
            }

            for (var tokenIndex = 1; tokenIndex < token.Length; tokenIndex += 1)
            {
                var current = token[tokenIndex];
                if (!(current == '_' ||
                    current == '-' ||
                    current == '.' ||
                    (current >= 'A' && current <= 'Z') ||
                    (current >= 'a' && current <= 'z') ||
                    (current >= '0' && current <= '9')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
