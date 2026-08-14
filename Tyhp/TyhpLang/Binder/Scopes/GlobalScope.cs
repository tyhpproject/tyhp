using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace Tyhp.TyhpLang.Binder.Scopes {

    public class GlobalScope :
        BaseScope<
            IBaseScope,
            NoSymbol,
            IGlobalScopeChild,
            IGlobalScopeSymbol,
            GlobalScope
        >
    {
        private readonly Dictionary<string, NamespaceScope> _namespaceScopesByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileScope> _fileScopesByFileName = new(StringComparer.OrdinalIgnoreCase);

        public override IBaseScope? Parent {
            get => null;
            set { }
        }

        public GlobalScope() : base(null, new NoSymbol())
        {
        }

        /// <summary>
        /// Gets the number of file scopes registered in this global scope.
        /// </summary>
        public int FileScopeCount => this._fileScopesByFileName.Count;

        /// <summary>
        /// Gets the number of namespace scopes registered in this global scope.
        /// </summary>
        public int NamespaceScopeCount => this._namespaceScopesByName.Count;

        /// <summary>
        /// Counts the total number of symbols across all scopes recursively.
        /// </summary>
        public int TotalSymbolCount => CountSymbolsAndScopes(this).symbols;

        /// <summary>
        /// Counts the total number of scopes (including this one) recursively.
        /// </summary>
        public int TotalScopeCount => CountSymbolsAndScopes(this).scopes;

        /// <summary>
        /// Returns the total symbol count and scope count from a single tree traversal.
        /// </summary>
        public (int SymbolCount, int ScopeCount) GetCounts()
        {
            return CountSymbolsAndScopes(this);
        }

        private static (int symbols, int scopes) CountSymbolsAndScopes(IBaseScope root)
        {
            int symbolCount = 0;
            int scopeCount = 0;
            var stack = new Stack<IBaseScope>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                scopeCount++;
                symbolCount += current.GetAllChildSymbols().Count();

                foreach (var child in current.GetAllChildScopes())
                    stack.Push(child);
            }

            return (symbolCount, scopeCount);
        }

        public FileScope AddFileScope(string fileName, string fileHash, string? sourceFile = null)
        {
            return this.AddFileScope(new FileSymbol(fileName, fileHash, sourceFile));
        }

        public FileScope AddFileScope(FileSymbol fileSymbol)
        {
            System.ArgumentNullException.ThrowIfNull(fileSymbol, nameof(fileSymbol));

            var fileName = fileSymbol.FileName;
            if (this._fileScopesByFileName.TryGetValue(fileName, out var existingFileScope))
            {
                return existingFileScope;
            }

            var scope = new FileScope(this, fileSymbol);
            this._fileScopesByFileName[fileName] = scope;
            this.AddChildScope(scope);
            return scope;
        }

        /// <remarks>This method only suppresses <see cref="System.ArgumentException"/>. Other exception types may propagate to the caller.</remarks>
        public bool TryAddFileScope(
            string fileName,
            string fileHash,
            string? sourceFile,
            out FileScope? fileScope,
            out string? diagnosticMessage
        )
        {
            try
            {
                fileScope = this.AddFileScope(fileName, fileHash, sourceFile);
                diagnosticMessage = null;
                return true;
            }
            catch (System.ArgumentException exception)
            {
                fileScope = null;
                diagnosticMessage = exception.Message;
                return false;
            }
        }

        /// <remarks>This method only suppresses <see cref="System.ArgumentException"/>. Other exception types may propagate to the caller.</remarks>
        public bool TryAddFileScope(
            FileSymbol fileSymbol,
            out FileScope? fileScope,
            out string? diagnosticMessage
        )
        {
            if (fileSymbol == null)
            {
                fileScope = null;
                diagnosticMessage = "fileSymbol cannot be null.";
                return false;
            }

            try
            {
                fileScope = this.AddFileScope(fileSymbol);
                diagnosticMessage = null;
                return true;
            }
            catch (System.ArgumentException exception)
            {
                fileScope = null;
                diagnosticMessage = exception.Message;
                return false;
            }
        }

        /// <remarks>This method only suppresses <see cref="System.ArgumentException"/>. Other exception types may propagate to the caller.</remarks>
        public bool TryAddFileScope(
            string fileName,
            string fileHash,
            string? sourceFile,
            out FileScope? fileScope,
            DiagnosticBag diagnostics,
            string phase = "Bind"
        )
        {
            try
            {
                fileScope = this.AddFileScope(fileName, fileHash, sourceFile);
                return true;
            }
            catch (System.ArgumentException exception)
            {
                fileScope = null;
                ReportFileScopeMetadataDiagnostic(
                    GetFileMetadataFilePath(fileName, sourceFile),
                    diagnostics,
                    phase,
                    exception
                );
                return false;
            }
        }

        /// <remarks>This method only suppresses <see cref="System.ArgumentException"/>. Other exception types may propagate to the caller.</remarks>
        public bool TryAddFileScope(
            FileSymbol fileSymbol,
            out FileScope? fileScope,
            DiagnosticBag diagnostics,
            string phase = "Bind"
        )
        {
            if (fileSymbol == null)
            {
                fileScope = null;
                ReportFileScopeMetadataDiagnostic(
                    "<unknown>",
                    diagnostics,
                    phase,
                    new System.ArgumentException("fileSymbol cannot be null.", nameof(fileSymbol))
                );
                return false;
            }

            try
            {
                fileScope = this.AddFileScope(fileSymbol);
                return true;
            }
            catch (System.ArgumentException exception)
            {
                fileScope = null;
                ReportFileScopeMetadataDiagnostic(
                    GetFileMetadataFilePath(fileSymbol.SourceFile, fileSymbol.Name),
                    diagnostics,
                    phase,
                    exception
                );
                return false;
            }
        }

        private static string GetFileMetadataFilePath(string? sourceFileName, string? fileName)
        {
            if (!string.IsNullOrWhiteSpace(sourceFileName))
            {
                return sourceFileName;
            }

            return string.IsNullOrWhiteSpace(fileName) ? "<unknown>" : fileName;
        }

        private static void ReportFileScopeMetadataDiagnostic(
            string filePath,
            DiagnosticBag? diagnostics,
            string phase,
            System.ArgumentException exception
        )
        {
            if (diagnostics == null)
            {
                return;
            }

            var normalizedFilePath = string.IsNullOrWhiteSpace(filePath) ? "<unknown>" : filePath;
            var normalizedPhase = string.IsNullOrWhiteSpace(phase) ? "Bind" : phase;
            diagnostics.AddError(
                MessageCode.BinderInvalidFileScopeArgument,
                normalizedFilePath,
                0,
                0,
                $"[{normalizedPhase}] Invalid file metadata for \"{normalizedFilePath}\": {exception.Message}"
            );
        }

        public NamespaceScope AddNamespaceScope(string namespaceName)
        {
            return this.AddNamespaceScope(new NamespaceSymbol(GlobalScope.NormalizeNamespaceName(namespaceName)));
        }

        public NamespaceScope AddNamespaceScope(NamespaceSymbol namespaceSymbol)
        {
            System.ArgumentNullException.ThrowIfNull(namespaceSymbol, nameof(namespaceSymbol));

            var namespaceName = GlobalScope.NormalizeNamespaceName(namespaceSymbol.Name);
            if (this._namespaceScopesByName.TryGetValue(namespaceName, out var existingNamespaceScope))
            {
                return existingNamespaceScope;
            }

            namespaceSymbol.Name = namespaceName;
            var scope = new NamespaceScope(this, namespaceSymbol);
            this._namespaceScopesByName[namespaceName] = scope;
            this.AddChildScope(scope);
            return scope;
        }

        public bool TryAddNamespaceScope(
            string namespaceName,
            out NamespaceScope? namespaceScope,
            out string? diagnosticMessage
        )
        {
            try
            {
                namespaceScope = this.AddNamespaceScope(namespaceName);
                diagnosticMessage = null;
                return true;
            }
            catch (System.ArgumentException exception)
            {
                namespaceScope = null;
                diagnosticMessage = exception.Message;
                return false;
            }
        }

        public bool TryGetNamespaceScope(
            string namespaceName,
            out NamespaceScope? namespaceScope
        )
        {
            var normalizedName = GlobalScope.NormalizeNamespaceName(namespaceName);
            return this._namespaceScopesByName.TryGetValue(normalizedName, out namespaceScope);
        }

        public NamespaceScope? FindNamespaceScope(string namespaceName)
        {
            var normalizedName = GlobalScope.NormalizeNamespaceName(namespaceName);
            this._namespaceScopesByName.TryGetValue(normalizedName, out var namespaceScope);
            return namespaceScope;
        }

        private static string NormalizeNamespaceName(string namespaceName)
        {
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                return string.Empty;
            }

            var token = namespaceName.Trim();
            if (token == "\\")
            {
                return string.Empty;
            }

            if (token.StartsWith("\\", System.StringComparison.Ordinal))
            {
                token = token.TrimStart('\\');
            }

            if (token.Length == 0)
            {
                return string.Empty;
            }

            var segments = token.Split('\\');
            if (segments.Length == 0)
            {
                return string.Empty;
            }

            foreach (var segment in segments)
            {
                if (segment.Length == 0 || !IsValidIdentifierSegment(segment))
                {
                    throw new System.ArgumentException(
                        "namespaceName must contain one or more namespace segments using [A-Za-z_][A-Za-z0-9_]*.",
                        nameof(namespaceName)
                    );
                }
            }

            return string.Join("\\", segments);
        }

        private static bool IsValidIdentifierSegment(string segment)
        {
            var start = segment[0];
            if (!(start == '_' || (start >= 'A' && start <= 'Z') || (start >= 'a' && start <= 'z')))
            {
                return false;
            }

            for (var segmentIndex = 1; segmentIndex < segment.Length; segmentIndex += 1)
            {
                var currentCharacter = segment[segmentIndex];
                if (!(currentCharacter == '_' ||
                    (currentCharacter >= 'A' && currentCharacter <= 'Z') ||
                    (currentCharacter >= 'a' && currentCharacter <= 'z') ||
                    (currentCharacter >= '0' && currentCharacter <= '9')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}