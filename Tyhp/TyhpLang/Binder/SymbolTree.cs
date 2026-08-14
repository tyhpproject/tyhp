using System.Collections.Generic;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.TyhpLang.Binder {
    /// <summary>
    /// Represents the symbol tree containing the global scope and extension method index.
    /// This class is not thread-safe. External synchronization is required for concurrent access.
    /// </summary>
    public class SymbolTree
    {
        public GlobalScope GlobalScope { get; }
        public SymbolIdentifier Identifier { get; }

        /// <summary>
        /// Index of extension methods keyed by method name (case-insensitive) for O(1) lookup.
        /// </summary>
        private Dictionary<string, List<ObjectMethodSymbol>>? _extensionMethodIndex;

        /// <summary>
        /// Gets or builds the extension method index.
        /// </summary>
        public Dictionary<string, List<ObjectMethodSymbol>> ExtensionMethodIndex
        {
            get
            {
                var index = _extensionMethodIndex;
                if (index == null)
                {
                    BuildExtensionMethodIndex();
                    index = _extensionMethodIndex;
                }
                return index!;
            }
        }

        public SymbolTree(SymbolIdentifier identifier)
        {
            this.Identifier = identifier;
            this.GlobalScope = new GlobalScope();
        }

        /// <summary>
        /// Wraps an existing bound <see cref="GlobalScope"/> for name resolution after binding.
        /// </summary>
        public SymbolTree(GlobalScope globalScope, SymbolIdentifier? identifier = null)
        {
            this.GlobalScope = globalScope ?? throw new ArgumentNullException(nameof(globalScope));
            this.Identifier = identifier ?? new SymbolIdentifier([]);
        }

        private void BuildExtensionMethodIndex()
        {
            _extensionMethodIndex = new Dictionary<string, List<ObjectMethodSymbol>>(StringComparer.OrdinalIgnoreCase);
            IndexExtensionsInScope(GlobalScope);
        }

        private void IndexExtensionsInScope(IBaseScope scope, int depth = 0)
        {
            if (depth > 500) return;

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is ObjectDeclarationSymbol objSymbol && objSymbol.IsExtension)
                {
                    foreach (var member in objSymbol.Members.Values)
                    {
                        if (member is ObjectMethodSymbol method)
                        {
                            if (!_extensionMethodIndex!.TryGetValue(method.Name, out var list))
                            {
                                list = new List<ObjectMethodSymbol>();
                                _extensionMethodIndex[method.Name] = list;
                            }
                            list.Add(method);
                        }
                    }
                }
            }

            foreach (var childScope in scope.GetAllChildScopes())
            {
                IndexExtensionsInScope(childScope, depth + 1);
            }
        }

        /// <summary>
        /// Invalidates the extension method index (call when new extensions are added after initial binding).
        /// </summary>
        public void InvalidateExtensionMethodIndex()
        {
            _extensionMethodIndex = null;
        }

        public FileScope AddFileScope(string fileName, string fileHash, string? sourceFile = null)
        {
            return this.GlobalScope.AddFileScope(fileName, fileHash, sourceFile);
        }

        public bool TryAddFileScope(
            string fileName,
            string fileHash,
            string? sourceFile,
            out FileScope? fileScope,
            out string? diagnosticMessage
        )
        {
            return this.GlobalScope.TryAddFileScope(fileName, fileHash, sourceFile, out fileScope, out diagnosticMessage);
        }

        public bool TryAddFileScope(
            string fileName,
            string fileHash,
            string? sourceFile,
            out FileScope? fileScope,
            DiagnosticBag diagnostics,
            string phase = "Bind"
        )
        {
            return this.GlobalScope.TryAddFileScope(
                fileName,
                fileHash,
                sourceFile,
                out fileScope,
                diagnostics,
                phase
            );
        }

        public bool TryAddFileScope(
            FileSymbol fileSymbol,
            out FileScope? fileScope,
            out string? diagnosticMessage
        )
        {
            return this.GlobalScope.TryAddFileScope(fileSymbol, out fileScope, out diagnosticMessage);
        }

        public bool TryAddFileScope(
            FileSymbol fileSymbol,
            out FileScope? fileScope,
            DiagnosticBag diagnostics,
            string phase = "Bind"
        )
        {
            return this.GlobalScope.TryAddFileScope(fileSymbol, out fileScope, diagnostics, phase);
        }

        public NamespaceScope AddNamespaceScope(string namespaceName)
        {
            return this.GlobalScope.AddNamespaceScope(namespaceName);
        }

        public NamespaceScope AddNamespaceScope(NamespaceSymbol namespaceSymbol)
        {
            return this.GlobalScope.AddNamespaceScope(namespaceSymbol);
        }

        public bool TryAddNamespaceScope(
            string namespaceName,
            out NamespaceScope? namespaceScope,
            out string? diagnosticMessage
        )
        {
            return this.GlobalScope.TryAddNamespaceScope(namespaceName, out namespaceScope, out diagnosticMessage);
        }

        public bool TryGetNamespaceScope(
            string namespaceName,
            out NamespaceScope? namespaceScope
        )
        {
            return this.GlobalScope.TryGetNamespaceScope(namespaceName, out namespaceScope);
        }

        public NamespaceScope? FindNamespaceScope(string namespaceName)
        {
            return this.GlobalScope.FindNamespaceScope(namespaceName);
        }

        /// <summary>
        /// Creates a reusable NameResolver instance backed by this SymbolTree's index.
        /// Prefer this over the ephemeral convenience methods when performing batch resolution.
        /// </summary>
        /// <param name="diagnostics">Diagnostic bag for reporting resolution errors.</param>
        /// <returns>A new NameResolver instance.</returns>
        public NameResolver CreateNameResolver(DiagnosticBag diagnostics)
        {
            return new NameResolver(this, diagnostics);
        }

        /// <summary>
        /// Resolves a simple name by walking up the scope chain from the given scope.
        /// Creates an ephemeral NameResolver per call; resolution results are not accumulated.
        /// For batch resolution, use <see cref="CreateNameResolver"/> and reuse the returned instance.
        /// </summary>
        /// <param name="name">The name to resolve.</param>
        /// <param name="fromScope">The scope to start resolution from.</param>
        /// <param name="diagnostics">Diagnostic bag for reporting resolution errors.</param>
        /// <returns>The resolved symbol, or null if not found.</returns>
        public IBaseSymbol? ResolveSymbol(string name, IBaseScope fromScope, DiagnosticBag diagnostics)
        {
            var resolver = new NameResolver(this, diagnostics);
            return resolver.ResolveSymbol(name, fromScope);
        }

        /// <summary>
        /// Resolves a fully-qualified name from global scope.
        /// Creates an ephemeral NameResolver per call; resolution results are not accumulated.
        /// For batch resolution, use <see cref="CreateNameResolver"/> and reuse the returned instance.
        /// </summary>
        /// <param name="segments">The segments of the fully-qualified name to resolve.</param>
        /// <param name="fromScope">The scope to start resolution from.</param>
        /// <param name="diagnostics">Diagnostic bag for reporting resolution errors.</param>
        /// <returns>The resolved symbol, or null if not found.</returns>
        public IBaseSymbol? ResolveQualifiedName(string[] segments, IBaseScope fromScope, DiagnosticBag diagnostics)
        {
            var resolver = new NameResolver(this, diagnostics);
            return resolver.ResolveQualifiedName(segments);
        }

        /// <summary>
        /// Resolves a name relative to the current namespace of <paramref name="fromScope"/>, falling
        /// back to global resolution. Unlike <see cref="ResolveSymbol"/>, this finds symbols declared
        /// in the same namespace but in a different source file.
        /// </summary>
        public IBaseSymbol? ResolveRelativeName(string[] segments, IBaseScope fromScope, DiagnosticBag diagnostics)
        {
            var resolver = new NameResolver(this, diagnostics);
            return resolver.ResolveRelativeName(segments, fromScope);
        }

        /// <summary>
        /// Resolves a type expression to its declaring symbol.
        /// Creates an ephemeral NameResolver per call; resolution results are not accumulated.
        /// For batch resolution, use <see cref="CreateNameResolver"/> and reuse the returned instance.
        /// </summary>
        /// <param name="typeAst">The type expression AST node to resolve.</param>
        /// <param name="fromScope">The scope to start resolution from.</param>
        /// <param name="diagnostics">Diagnostic bag for reporting resolution errors.</param>
        /// <returns>The resolved symbol, or null if not found.</returns>
        public IBaseSymbol? ResolveType(ITypeExpression typeAst, IBaseScope fromScope, DiagnosticBag diagnostics)
        {
            var resolver = new NameResolver(this, diagnostics);
            return resolver.ResolveType(typeAst, fromScope);
        }

        /// <summary>
        /// Resolves an instance member on an object declaration, including inherited members.
        /// Creates an ephemeral NameResolver per call; resolution results are not accumulated.
        /// For batch resolution, use <see cref="CreateNameResolver"/> and reuse the returned instance.
        /// </summary>
        /// <param name="memberName">The member name to resolve.</param>
        /// <param name="onObject">The object declaration to search for the member on.</param>
        /// <param name="diagnostics">Diagnostic bag for reporting resolution errors.</param>
        /// <returns>The resolved symbol, or null if not found.</returns>
        public IBaseSymbol? ResolveMember(string memberName, ObjectDeclarationSymbol onObject, DiagnosticBag diagnostics)
        {
            var resolver = new NameResolver(this, diagnostics);
            return resolver.ResolveMember(memberName, onObject);
        }

        /// <summary>
        /// Resolves a static member or constant on a class declaration, including inherited members.
        /// Creates an ephemeral NameResolver per call; resolution results are not accumulated.
        /// For batch resolution, use <see cref="CreateNameResolver"/> and reuse the returned instance.
        /// </summary>
        /// <param name="memberName">The static member name to resolve.</param>
        /// <param name="onClass">The class declaration to search for the static member on.</param>
        /// <param name="diagnostics">Diagnostic bag for reporting resolution errors.</param>
        /// <returns>The resolved symbol, or null if not found.</returns>
        public IBaseSymbol? ResolveStaticMember(string memberName, ObjectDeclarationSymbol onClass, DiagnosticBag diagnostics)
        {
            var resolver = new NameResolver(this, diagnostics);
            return resolver.ResolveStaticMember(memberName, onClass);
        }

        /// <summary>
        /// Resolves a class constant or enum case by exact name, including inherited constants.
        /// Creates an ephemeral NameResolver per call; resolution results are not accumulated.
        /// </summary>
        public IBaseSymbol? ResolveConstant(string constantName, ObjectDeclarationSymbol onClass, DiagnosticBag diagnostics)
        {
            var resolver = new NameResolver(this, diagnostics);
            return resolver.ResolveConstant(constantName, onClass);
        }
    }
}