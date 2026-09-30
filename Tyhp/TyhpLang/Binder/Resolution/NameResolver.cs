using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.Resolution
{
    /// <summary>
    /// Provides name resolution for the Tyhp binder. After the declaration pass registers
    /// all symbols, the NameResolver resolves name references to their declaring symbols
    /// by walking the scope chain, handling use/import aliases, qualified names, member
    /// access, and type resolution.
    /// </summary>
    public class NameResolver
    {
        /// <summary>
        /// Maximum inheritance chain depth. Cycles are blocked by the visited set and the
        /// in-flight lookup stack; this cap guards against pathologically deep linear chains.
        /// </summary>
        private const int MaxInheritanceDepth = 100;

        /// <summary>
        /// Member / constant lookups currently on this resolver's call stack.
        /// Per-walk <c>visited</c> HashSets are allocated at each public
        /// <see cref="ResolveMember"/> / <see cref="TryResolveObjectTypeAlias"/> entry, so they
        /// cannot see re-entry that happens while resolving a parent or implements type
        /// (PHP class/namespace FQN collisions such as <c>\Dompdf\Renderer</c> vs
        /// <c>\Dompdf\Renderer\AbstractRenderer</c>). This set is shared across those nested
        /// walks. Member names compare case-insensitively (matching PHP method/property lookup
        /// and <see cref="ObjectDeclarationSymbol.Members"/>'s own comparer) so a re-entrant
        /// lookup is still recognized even if intermediate resolution re-derives the name with
        /// different casing; being conservative here only widens the guard, it never hides a
        /// legitimate distinct lookup because the object and kind must still match exactly.
        /// </summary>
        private readonly HashSet<(ObjectDeclarationSymbol Object, string Member, int Kind)> _inheritedLookupInFlight =
            new(InheritedLookupKeyComparer.Instance);

        private const int InheritedLookupKindInstance = 0;
        private const int InheritedLookupKindStatic = 1;
        private const int InheritedLookupKindConstant = 2;

        private sealed class InheritedLookupKeyComparer
            : IEqualityComparer<(ObjectDeclarationSymbol Object, string Member, int Kind)>
        {
            public static readonly InheritedLookupKeyComparer Instance = new();

            public bool Equals(
                (ObjectDeclarationSymbol Object, string Member, int Kind) x,
                (ObjectDeclarationSymbol Object, string Member, int Kind) y)
                => ReferenceEquals(x.Object, y.Object)
                    && x.Kind == y.Kind
                    && string.Equals(x.Member, y.Member, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((ObjectDeclarationSymbol Object, string Member, int Kind) key)
                => HashCode.Combine(
                    RuntimeHelpers.GetHashCode(key.Object),
                    key.Kind,
                    StringComparer.OrdinalIgnoreCase.GetHashCode(key.Member));
        }

        private readonly GlobalScope _globalScope;
        private readonly SymbolTree? _symbolTree;
        private readonly DiagnosticBag _diagnostics;
        private readonly Dictionary<IBase2Ast, IBaseSymbol> _resolvedSymbols = new();

        /// <summary>
        /// When resolving a type-alias body or its generic constraints, <c>T</c> in
        /// <c>type Alias&lt;T&gt; = …</c> (file-level or class-level) is this alias's
        /// parameter list, not a class in the file scope.
        /// </summary>
        internal IReadOnlyList<GenericTypeParameterSymbol>? GenericAliasParameters { get; set; }

        /// <summary>
        /// Resolved symbol map: AST node → its resolved symbol.
        /// </summary>
        public IReadOnlyDictionary<IBase2Ast, IBaseSymbol> ResolvedSymbols => _resolvedSymbols;

        public NameResolver(GlobalScope globalScope, DiagnosticBag diagnostics)
        {
            _globalScope = globalScope ?? throw new ArgumentNullException(nameof(globalScope));
            _symbolTree = null;
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public NameResolver(SymbolTree symbolTree, DiagnosticBag diagnostics)
        {
            _globalScope = (symbolTree ?? throw new ArgumentNullException(nameof(symbolTree))).GlobalScope;
            _symbolTree = symbolTree;
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>
        /// Records a resolution result, mapping an AST node to its resolved symbol.
        /// </summary>
        public void RecordResolution(IBase2Ast astNode, IBaseSymbol symbol)
        {
            if (astNode != null && symbol != null)
            {
                _resolvedSymbols[astNode] = symbol;
                astNode.BoundSymbol = symbol;
            }
        }

        /// <summary>
        /// Resolves an attribute's class-name expression (e.g. <c>MyAttr</c>, <c>\Attribute</c>)
        /// against <paramref name="fromScope"/> and records the result on the name AST so the
        /// checker can read <see cref="IBase2Ast.BoundSymbol"/>. Returns null when the name does
        /// not name a declared type — callers must not treat that as a hard error for built-ins
        /// that may be missing from the ExtCore stub (notably <c>\Override</c>).
        /// </summary>
        public ObjectDeclarationSymbol? ResolveAttributeClassName(IExpression? nameExpr, IBaseScope fromScope)
        {
            if (nameExpr is not PhpNameAst nameAst)
            {
                return null;
            }

            var typeName = GetExpressionName(nameAst);
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            IBaseSymbol? resolved;
            if (typeName.StartsWith("\\", StringComparison.Ordinal))
            {
                resolved = ResolveQualifiedName(typeName.TrimStart('\\').Split('\\'));
            }
            else if (typeName.Contains('\\'))
            {
                resolved = ResolveRelativeName(typeName.Split('\\'), fromScope);
            }
            else
            {
                // Same fallback as ResolveNamedType: lexical/use walk, then current namespace /
                // global. An attribute names a class, so a same-named member of the enclosing
                // declaration (`#[Marker]` in a class that also declares `marker()` or
                // `const Marker`) or a same-named function must not end the lexical walk — PHP
                // keeps those in their own symbol tables.
                resolved = ResolveSymbol(typeName, fromScope) as ObjectDeclarationSymbol
                    ?? ResolveRelativeName([typeName], fromScope);
            }

            if (resolved is not ObjectDeclarationSymbol attributeClass)
            {
                return null;
            }

            RecordResolution(nameAst, attributeClass);
            return attributeClass;
        }

        /// <summary>
        /// Resolves a simple name by walking up the scope chain from <paramref name="fromScope"/>.
        /// Checks each scope's child symbols for a match, then checks use/import aliases,
        /// then walks to the parent scope. The walk includes FileScope when traversing
        /// from a NamespaceBlockScope.
        /// </summary>
        public IBaseSymbol? ResolveSymbol(string name, IBaseScope fromScope)
            => ResolveSymbol(name, fromScope, accept: null);

        /// <summary>
        /// Like <see cref="ResolveSymbol(string, IBaseScope)"/>, but skips hits that fail
        /// <paramref name="accept"/> and keeps walking. Type-position lookup uses this so a
        /// class method such as <c>Type::bool()</c> does not steal the <c>bool</c> builtin.
        /// </summary>
        public IBaseSymbol? ResolveSymbol(
            string name,
            IBaseScope fromScope,
            Func<IBaseSymbol, bool>? accept)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var scope = fromScope;
            while (scope != null)
            {
                if (TryAcceptChildByName(scope, name, accept, out var accepted))
                {
                    return accepted;
                }

                // When in a namespace block, also check the owning FileScope for file-level symbols and use aliases.
                // NamespaceBlockScope is the only scope type whose DeclarationSymbol is NamespaceBlockSymbol,
                // so matching on the scope type alone is sufficient.
                if (scope is NamespaceBlockScope)
                {
                    var fileScope = GetOwningFileScope(scope);
                    if (fileScope != null
                        && !ReferenceEquals(fileScope, scope)
                        && TryAcceptChildByName(fileScope, name, accept, out var fileAccepted))
                    {
                        return fileAccepted;
                    }
                }

                // Variables cannot cross function scope boundaries (PHP scoping rules).
                // Only superglobals (resolved from GlobalScope) pass through.
                if (name.StartsWith("$") &&
                    (scope is FunctionDeclarationScope ||
                     scope is InstanceMethodDeclarationScope ||
                     scope is StaticMethodDeclarationScope ||
                     scope is AnonymousFunctionScope))
                {
                    break;
                }

                scope = scope.ParentScope;
            }

            if (name.StartsWith("$"))
            {
                return AcceptIf(((IBaseScope)_globalScope).FindChildSymbolByName(name), accept);
            }

            foreach (var global in _globalScope.GlobalImports)
            {
                if (!string.Equals(global.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var segments = global.ImportedNameSegments;
                if (segments == null || segments.Length == 0)
                {
                    continue;
                }

                var resolvedGlobal = AcceptIf(ResolveQualifiedName(segments), accept);
                if (resolvedGlobal != null)
                {
                    return resolvedGlobal;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves a fully-qualified name (e.g., \App\Models\User) by starting from the GlobalScope
        /// and walking through namespace scopes matching each segment.
        /// </summary>
        public IBaseSymbol? ResolveQualifiedName(string[] segments)
        {
            if (segments == null || segments.Length == 0) return null;

            if (segments.Length == 1)
            {
                return SearchGlobalNamespace(segments[0]);
            }

            var namespacePath = string.Join("\\", segments, 0, segments.Length - 1);
            var symbolName = segments[segments.Length - 1];

            var namespaceScope = _globalScope.FindNamespaceScope(namespacePath);
            if (namespaceScope == null) return null;

            foreach (var childScope in namespaceScope.ChildScopes)
            {
                var found = childScope.FindChildSymbolByName(symbolName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves a top-level (global namespace) symbol by name. The global namespace is not a
        /// single scope: built-in types live directly on the <see cref="GlobalScope"/>, while
        /// user- and tyhpdef-declared global types live in the <see cref="FileScope"/>s (for files
        /// without a namespace) or empty-named namespace blocks beneath the global scope. This
        /// searches all of those so a name like <c>\Closure</c> resolves regardless of which file
        /// declared it.
        /// </summary>
        private IBaseSymbol? SearchGlobalNamespace(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var direct = ((IBaseScope)_globalScope).FindChildSymbolByName(name);
            if (direct != null && direct is not UseIncludeSymbol)
            {
                return direct;
            }

            foreach (var childScope in ((IBaseScope)_globalScope).GetAllChildScopes())
            {
                if (childScope is FileScope fileScope)
                {
                    var found = ((IBaseScope)fileScope).FindChildSymbolByName(name);
                    if (found != null && found is not UseIncludeSymbol)
                    {
                        return found;
                    }
                }
                else if (childScope is NamespaceScope nsScope &&
                         string.IsNullOrEmpty(nsScope.DeclarationSymbol?.Name))
                {
                    foreach (var blockScope in nsScope.ChildScopes)
                    {
                        var found = blockScope.FindChildSymbolByName(name);
                        if (found != null && found is not UseIncludeSymbol)
                        {
                            return found;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves a name relative to the current namespace context of <paramref name="fromScope"/>.
        /// First expands a class <c>use</c> alias on the leading segment (PHP qualified-name
        /// rules), then prepends the enclosing namespace, then falls back to global resolution.
        /// </summary>
        public IBaseSymbol? ResolveRelativeName(string[] segments, IBaseScope fromScope)
        {
            if (segments == null || segments.Length == 0) return null;

            // PHP: `use Lib\Inner;` + `Inner\Deep` → `\Lib\Inner\Deep`. The alias replaces the
            // first segment only; remaining segments are appended. Do not fall through to the
            // enclosing-namespace path when an alias matched — that would ignore the import.
            if (TryExpandClassUseAliasPrefix(segments, fromScope, out var aliasedSegments))
            {
                return ResolveQualifiedName(aliasedSegments);
            }

            var currentNamespace = FindEnclosingNamespaceName(fromScope);
            if (string.IsNullOrEmpty(currentNamespace))
            {
                return ResolveQualifiedName(segments);
            }

            var nsSegments = currentNamespace.Split('\\');
            var fullSegments = new string[nsSegments.Length + segments.Length];
            Array.Copy(nsSegments, fullSegments, nsSegments.Length);
            Array.Copy(segments, 0, fullSegments, nsSegments.Length, segments.Length);

            var result = ResolveQualifiedName(fullSegments);
            if (result != null) return result;

            return ResolveQualifiedName(segments);
        }

        /// <summary>
        /// When <paramref name="segments"/>[0] matches a class <c>use</c> alias in scope,
        /// replaces that segment with the imported path and appends any remaining segments.
        /// </summary>
        private bool TryExpandClassUseAliasPrefix(
            string[] segments,
            IBaseScope fromScope,
            out string[] expandedSegments)
        {
            expandedSegments = segments;
            if (segments.Length == 0)
            {
                return false;
            }

            var useInclude = FindClassUseInclude(segments[0], fromScope);
            if (useInclude?.ImportedNameSegments is not { Length: > 0 } imported)
            {
                return false;
            }

            if (segments.Length == 1)
            {
                expandedSegments = imported;
                return true;
            }

            expandedSegments = new string[imported.Length + segments.Length - 1];
            Array.Copy(imported, expandedSegments, imported.Length);
            Array.Copy(segments, 1, expandedSegments, imported.Length, segments.Length - 1);
            return true;
        }

        private UseIncludeSymbol? FindClassUseInclude(string aliasName, IBaseScope fromScope)
        {
            var scope = fromScope;
            while (scope != null)
            {
                if (scope.FindChildSymbolByName(aliasName) is UseIncludeSymbol use
                    && use.UseType == PhpUseType.Class)
                {
                    return use;
                }

                if (scope is NamespaceBlockScope)
                {
                    var fileScope = GetOwningFileScope(scope);
                    if (fileScope != null
                        && !ReferenceEquals(fileScope, scope)
                        && ((IBaseScope)fileScope).FindChildSymbolByName(aliasName) is UseIncludeSymbol fileUse
                        && fileUse.UseType == PhpUseType.Class)
                    {
                        return fileUse;
                    }
                }

                scope = scope.ParentScope;
            }

            foreach (var global in _globalScope.GlobalImports)
            {
                if (global.UseType == PhpUseType.Class
                    && string.Equals(global.Name, aliasName, StringComparison.OrdinalIgnoreCase))
                {
                    return global;
                }
            }

            return null;
        }

        /// <summary>
        /// True when the leading segment of <paramref name="typeName"/> is a class
        /// <c>use</c> import visible from <paramref name="fromScope"/>. A fully-qualified
        /// name (leading <c>\</c>) is not an import alias.
        /// </summary>
        public bool HasClassUseImport(string? typeName, IBaseScope fromScope)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return false;
            }

            var trimmed = typeName.Trim();
            if (trimmed.StartsWith('\\') || trimmed.StartsWith('?'))
            {
                return false;
            }

            var first = trimmed.Split('\\', 2)[0];
            return FindClassUseInclude(first, fromScope) is not null;
        }

        /// <summary>
        /// Resolves an instance member (property, method) on an object declaration,
        /// walking the inheritance chain: own members → traits → parent methods →
        /// interfaces → own <c>__call</c> → trait <c>__call</c> → inherited <c>__call</c>.
        /// A used trait's method wins over an inherited instance method of the same name.
        /// An inherited magic <c>__call</c> is only a fallback after used traits and
        /// interfaces on this class are searched. A trait <c>__call</c> is that same
        /// kind of fallback: it does not count as the trait defining the looked-up name.
        /// Class constants are not considered (see <see cref="ResolveConstant"/>).
        /// </summary>
        public IBaseSymbol? ResolveMember(string memberName, ObjectDeclarationSymbol onObject)
        {
            if (string.IsNullOrEmpty(memberName) || onObject == null) return null;

            return ResolveInheritedMember(
                memberName, onObject, staticOnly: false, includeConstants: false,
                new HashSet<ObjectDeclarationSymbol>());
        }

        /// <summary>
        /// Resolves a static member or constant on a class declaration,
        /// walking the inheritance chain (used-trait methods before an inherited method
        /// of the same name, then interfaces; inherited <c>__callStatic</c> only after
        /// those). Constants are matched case-sensitively in their own namespace before
        /// case-insensitive static method/property lookup.
        /// </summary>
        public IBaseSymbol? ResolveStaticMember(string memberName, ObjectDeclarationSymbol onClass)
        {
            if (string.IsNullOrEmpty(memberName) || onClass == null) return null;

            return ResolveInheritedMember(
                memberName, onClass, staticOnly: true, includeConstants: true,
                new HashSet<ObjectDeclarationSymbol>());
        }

        /// <summary>
        /// Resolves a class constant or enum case by exact (case-sensitive) name, walking the
        /// inheritance / trait / interface chain. Does not consult the method/property namespace.
        /// </summary>
        public IBaseSymbol? ResolveConstant(string constantName, ObjectDeclarationSymbol onClass)
        {
            if (string.IsNullOrEmpty(constantName) || onClass == null) return null;

            return ResolveInheritedConstant(constantName, onClass, new HashSet<ObjectDeclarationSymbol>());
        }

        /// <summary>
        /// Resolves an <see cref="ITypeExpression"/> AST node to its corresponding symbol.
        /// Handles built-in types, named types (qualified and unqualified),
        /// nullable types, union types, intersection types, and generic type instantiations.
        /// For union/intersection types (<see cref="PhpTypeExpressionAst"/>), all component types
        /// are resolved and recorded via <see cref="RecordResolution"/>, but only the first
        /// non-null resolved component is returned. Downstream consumers must use
        /// <see cref="ResolvedSymbols"/> for complete composite type information, not this return value.
        /// </summary>
        public IBaseSymbol? ResolveType(ITypeExpression? typeAst, IBaseScope fromScope)
        {
            if (typeAst == null) return null;

            switch (typeAst)
            {
                case PhpBuiltinTypeAst builtinType:
                {
                    // `array<TKey, TValue>` stores its arguments on the typeName addon, not as
                    // children. Bind them in this scope so a nested `extends<T>` group (and a
                    // method generic) is visible inside the instantiation.
                    ResolveAppliedTypeArguments(builtinType, fromScope);

                    var typeName = builtinType.Identifier;
                    if (string.IsNullOrEmpty(typeName)) return null;

                    // The late-static-binding type `static` (and `self`/`parent`) is parsed as a
                    // builtin type but resolves to the enclosing class context, not a global
                    // symbol — those keywords live in the object scope, not the global scope.
                    if (string.Equals(typeName, "self", StringComparison.OrdinalIgnoreCase))
                    {
                        var blockTarget = TryGetExtensionBlockTarget(fromScope);
                        if (blockTarget != null)
                        {
                            RecordResolution(builtinType, blockTarget);
                            return blockTarget;
                        }
                    }

                    if (string.Equals(typeName, "static", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(typeName, "self", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(typeName, "parent", StringComparison.OrdinalIgnoreCase))
                    {
                        var selfResult = ResolveSelfStaticParent(typeName, fromScope);
                        if (selfResult != null)
                        {
                            RecordResolution(builtinType, selfResult);
                        }
                        return selfResult;
                    }

                    var resolved = ((IBaseScope)_globalScope).FindChildSymbolByName(typeName);
                    if (resolved != null)
                    {
                        RecordResolution(builtinType, resolved);
                        return resolved;
                    }

                    // Static-value (literal) types — `'red'`, `42`, `3.14`, … — are not symbols.
                    // Bind them to their underlying scalar builtin so parameter/return resolution
                    // does not report 3019/3020 when every union member is a literal.
                    if (StaticValueTypeHelper.TryGetUnderlyingBuiltinName(typeName, out var underlyingName))
                    {
                        var underlying = ((IBaseScope)_globalScope).FindChildSymbolByName(underlyingName);
                        if (underlying != null)
                        {
                            RecordResolution(builtinType, underlying);
                        }
                        return underlying;
                    }

                    return null;
                }

                case PhpNamedTypeAst namedType:
                {
                    var resolved = ResolveNamedType(namedType, fromScope);
                    ResolveAppliedTypeArguments(namedType, fromScope);
                    return resolved;
                }

                case TyhpReturnTypeGuardAst guardType:
                {
                    // A type-guard return annotation (`$value is Foo`) declares a `bool`-returning
                    // predicate; the meaningful symbol to resolve is the guarded type expression.
                    // Resolving it here keeps the guarded type bound (so the checker can narrow on
                    // it) and prevents a spurious "unresolved return type" error.
                    return guardType.TypeExpression is null
                        ? null
                        : ResolveType(guardType.TypeExpression, fromScope);
                }

                case TyhpTemplateStringTypeAst templateType:
                {
                    // A template-string type (`"prefix-${T}-suffix"`) is not a symbol reference —
                    // the checker resolves its precise pattern via `TemplateStringCheckedType`
                    // (see TypeInferrer.TemplateStrings.cs), entirely independent of this binder
                    // pass. Bind it to the `string` builtin here purely so a bare template-string
                    // parameter/return type does not report a spurious 3019/3020 "unresolved type".
                    var stringSymbol = ((IBaseScope)_globalScope).FindChildSymbolByName("string");
                    if (stringSymbol != null)
                    {
                        RecordResolution(templateType, stringSymbol);
                    }

                    return stringSymbol;
                }

                case TyhpObjectShapeAst shape:
                {
                    ResolveTypesInAst(shape.Members, fromScope, 0);
                    return null;
                }

                case TyhpCallableShapeAst callableShape:
                {
                    // Inline `callable(…): R` is a refinement of the `callable` builtin, not a
                    // named symbol. Walk parameter/return types so nested names bind, then attach
                    // the builtin so Pass 2 does not report 3019/3020/3021 on the shape itself.
                    ResolveTypesInAst(callableShape.Parameters, fromScope, 0);
                    if (callableShape.ReturnType != null)
                    {
                        ResolveType(callableShape.ReturnType, fromScope);
                    }

                    var callableSymbol = ((IBaseScope)_globalScope).FindChildSymbolByName("callable");
                    if (callableSymbol != null)
                    {
                        RecordResolution(callableShape, callableSymbol);
                    }

                    return callableSymbol;
                }

                case TyhpStructShapeAst structShape:
                {
                    // Inline / alias-RHS `struct { … }` is a refinement of the `struct` builtin.
                    // Walk field types and the optional `extends` name so nested names bind.
                    if (structShape.Extends is PhpNameAst extendsName)
                    {
                        var parentName = extendsName.ValueString ?? extendsName.Identifier;
                        if (!string.IsNullOrEmpty(parentName))
                        {
                            var parent = parentName.StartsWith('\\')
                                ? ResolveQualifiedName(parentName.TrimStart('\\').Split('\\'))
                                : ResolveSymbol(parentName, fromScope)
                                    ?? ResolveRelativeName(parentName.Split('\\'), fromScope);
                            if (parent != null)
                            {
                                RecordResolution(extendsName, parent);
                            }
                        }
                    }

                    ResolveTypesInAst(structShape.PropertyList, fromScope, 0);
                    var structSymbol = ((IBaseScope)_globalScope).FindChildSymbolByName("struct");
                    if (structSymbol != null
                        && structShape.BoundSymbol is not ObjectDeclarationSymbol { IsStruct: true })
                    {
                        RecordResolution(structShape, structSymbol);
                    }

                    return structShape.BoundSymbol ?? structSymbol;
                }

                case PhpTypeExpressionAst typeExpr:
                {
                    if (typeExpr.Types == null) return null;

                    IBaseSymbol? firstResolved = null;
                    var childCount = 0;
                    foreach (var childType in typeExpr.Types.GetAllNotNull())
                    {
                        childCount++;
                        var resolved = ResolveType(childType, fromScope);
                        firstResolved ??= resolved;
                    }

                    // A single non-null type is stored as this wrapper (`Money`, `self`, `T`).
                    // Record the symbol on the wrapper so parameter and return nodes that point
                    // here are bound, not only the inner name.
                    if (typeExpr.TypeKind == PhpTypeKind.Simple
                        && !typeExpr.IsNullable
                        && childCount == 1
                        && firstResolved != null)
                    {
                        RecordResolution(typeExpr, firstResolved);
                    }

                    // For Simple type kind with a single child, return the resolved symbol directly.
                    // For union/intersection, we resolve each component but return the first;
                    // actual type compatibility is checked in the checker phase.
                    return firstResolved;
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Resolves type annotations nested in an object-shape member list (parameter/return
        /// types, properties, consts). Recursive self-references such as
        /// <c>type Node = object { public function parent(): ?Node; }</c> bind because the
        /// alias symbol is already registered.
        /// </summary>
        private void ResolveTypesInAst(IBase2Ast? node, IBaseScope fromScope, int depth)
        {
            if (node is null || depth > 500)
            {
                return;
            }

            if (node is ITypeExpression typeExpr
                && node is not TyhpObjectShapeAst
                && node is not TyhpCallableShapeAst
                && node is not TyhpStructShapeAst)
            {
                ResolveType(typeExpr, fromScope);
                foreach (var addon in node.AstGrammarAddons.Values)
                {
                    ResolveTypesInAst(addon, fromScope, depth + 1);
                }

                return;
            }

            foreach (var child in node.AstChildren)
            {
                ResolveTypesInAst(child, fromScope, depth + 1);
            }

            foreach (var addon in node.AstGrammarAddons.Values)
            {
                ResolveTypesInAst(addon, fromScope, depth + 1);
            }
        }

        /// <summary>
        /// Searches for extension methods applicable to a type.
        /// Uses indexed lookup when created from a SymbolTree; otherwise scans reachable scopes.
        /// </summary>
        /// <param name="callSiteScope">
        /// Scope of the call (function, class, or file). File-local <c>use extension</c>
        /// <c>hide</c> / <c>insteadof</c> / method <c>as</c> live on that file's
        /// <see cref="FileScope"/>, not on built-in receiver symbols (whose
        /// <see cref="IBaseSymbol.ContainingScope"/> is global).
        /// </param>
        public IBaseSymbol? ResolveExtensionMethod(
            string methodName,
            IBaseSymbol? onType,
            IBaseScope? callSiteScope = null)
        {
            if (string.IsNullOrEmpty(methodName) || onType == null) return null;

            var fromScope = callSiteScope
                ?? (onType is ObjectDeclarationSymbol ods && ods.ContainingScope != null
                    ? ods.ContainingScope
                    : _globalScope);

            var effectiveMethodName = methodName;
            (string? ExtName, string OriginalMethod)? aliasInfo = null;
            if (TryGetExtensionMethodAlias(onType, fromScope, methodName, out var aliasTuple))
            {
                effectiveMethodName = aliasTuple.OriginalMethod;
                aliasInfo = aliasTuple;
            }

            if (IsExtensionMemberHidden(onType, fromScope, methodName)
                || IsExtensionMemberHidden(onType, fromScope, effectiveMethodName))
            {
                return null;
            }

            // Class-body tyhpdef thin mappings are auto-active on the enclosing type (no
            // `use extension`). They live on the synthetic scope, not the receiver's Members.
            if (onType is ObjectDeclarationSymbol inlineOwner
                && inlineOwner.FindSyntheticInlineMember(effectiveMethodName) is { } inlineMember)
            {
                return inlineMember;
            }

            if (onType is ObjectDeclarationSymbol receiver &&
                receiver.TyhpdefAutoActivatedExtensions is { Count: > 0 } allowedExt)
            {
                foreach (var extDecl in allowedExt)
                {
                    if (ShouldSkipExtensionForPrecedence(
                            extDecl, onType, fromScope, methodName, effectiveMethodName))
                    {
                        continue;
                    }

                    if (aliasInfo is { ExtName: { } extFilter } &&
                        !ExtensionDeclarationMatchesName(extDecl, extFilter))
                    {
                        continue;
                    }

                    foreach (var extensionMethod in EnumerateExtensionMethods(extDecl, effectiveMethodName))
                    {
                        if (ExtensionMethodAppliesToReceiver(extensionMethod, onType, fromScope))
                            return extensionMethod;
                    }
                }

                return null;
            }

            if (_symbolTree != null)
            {
                if (_symbolTree.ExtensionMethodIndex.TryGetValue(effectiveMethodName, out var candidates))
                {
                    ObjectMethodSymbol? best = null;
                    var bestRank = int.MinValue;
                    foreach (var extensionMethod in candidates)
                    {
                        if (!IsExtensionMethodActivated(extensionMethod, fromScope))
                            continue;

                        var owner = FindDeclaringExtension(extensionMethod);
                        if (owner != null
                            && ShouldSkipExtensionForPrecedence(
                                owner, onType, fromScope, methodName, effectiveMethodName))
                        {
                            continue;
                        }

                        if (aliasInfo is { ExtName: { } extFilter }
                            && (owner == null || !ExtensionDeclarationMatchesName(owner, extFilter)))
                        {
                            continue;
                        }

                        if (!ExtensionMethodAppliesToReceiver(extensionMethod, onType, _globalScope))
                            continue;

                        // File-local `use extension` must beat a globally-used stub with the
                        // same member (`MinimalString::length` vs `\Tyhp\StringExtensions::length`).
                        var rank = RankActivatedExtension(owner, fromScope);
                        if (rank > bestRank)
                        {
                            bestRank = rank;
                            best = extensionMethod;
                        }
                    }

                    return best;
                }

                return null;
            }

            foreach (var childScope in _globalScope.ChildScopes)
            {
                if (childScope is NamespaceScope nsScope)
                {
                    foreach (var blockScope in nsScope.ChildScopes)
                    {
                        var result = SearchExtensionsInScope(effectiveMethodName, onType, blockScope);
                        if (result != null) return result;
                    }
                }
                else if (childScope is FileScope fileScope)
                {
                    var result = SearchExtensionsInScope(effectiveMethodName, onType, fileScope);
                    if (result != null) return result;
                }
            }

            return null;
        }

        /// <summary>
        /// Scope that owns a call site for file-local <c>use extension</c> adaptations
        /// and unqualified free-function lookup.
        /// Prefers a nested lexical scope (function / method / class) when provided.
        /// <see cref="FileScope"/> and <see cref="GlobalScope"/> are not enough for
        /// namespaced top-level statements — those need the file's
        /// <see cref="NamespaceBlockScope"/> so current-namespace function lookup
        /// matches PHP. <paramref name="currentNamespaceName"/> selects that block.
        /// Falls back to the file scope, then global.
        /// </summary>
        public IBaseScope FindCallSiteScope(IBase2Ast? node, IBaseScope? lexicalScope)
            => FindCallSiteScope(node, lexicalScope, currentNamespaceName: null);

        /// <inheritdoc cref="FindCallSiteScope(IBase2Ast?, IBaseScope?)"/>
        public IBaseScope FindCallSiteScope(
            IBase2Ast? node,
            IBaseScope? lexicalScope,
            string? currentNamespaceName)
        {
            if (lexicalScope is FileScope fileLexical)
            {
                return PreferNamespaceBlockForFile(fileLexical, currentNamespaceName);
            }

            if (lexicalScope is not null and not GlobalScope)
            {
                return lexicalScope;
            }

            var fileScope = FindFileScopeForNode(node);
            if (fileScope != null)
            {
                return PreferNamespaceBlockForFile(fileScope, currentNamespaceName);
            }

            return PreferNamespaceScope(currentNamespaceName) ?? _globalScope;
        }

        private FileScope? FindFileScopeForNode(IBase2Ast? node)
        {
            var owningFile = node as SrcFileAst ?? node?.OwningFile;
            if (owningFile == null)
            {
                return null;
            }

            var fileName = owningFile.FileName;
            var absolutePath = owningFile.Identifier;
            foreach (var child in ((IBaseScope)_globalScope).GetAllChildScopes())
            {
                if (child is FileScope fileScope
                    && FileScopeMatchesOwningFile(fileScope, fileName, absolutePath))
                {
                    return fileScope;
                }
            }

            return null;
        }

        /// <summary>
        /// PHP current-namespace lookup for unqualified names lives on
        /// <see cref="NamespaceBlockScope"/>, not on <see cref="FileScope"/>
        /// (namespace blocks are siblings of the file under global). When
        /// <paramref name="namespaceName"/> is set, prefer this file's block
        /// in that namespace so top-level <c>twice()</c> in <c>namespace App</c>
        /// binds <c>App\twice</c>. Sibling files in the same namespace are
        /// still found via <see cref="ResolveRelativeName"/>.
        /// </summary>
        private IBaseScope PreferNamespaceBlockForFile(FileScope fileScope, string? namespaceName)
        {
            if (string.IsNullOrEmpty(namespaceName))
            {
                return fileScope;
            }

            var nsScope = _globalScope.FindNamespaceScope(namespaceName);
            if (nsScope == null)
            {
                return fileScope;
            }

            foreach (var child in nsScope.ChildScopes)
            {
                if (child is NamespaceBlockScope block
                    && ReferenceEquals(block.DeclarationSymbol.OwningFileScope, fileScope))
                {
                    return block;
                }
            }

            return nsScope;
        }

        private IBaseScope? PreferNamespaceScope(string? namespaceName)
        {
            if (string.IsNullOrEmpty(namespaceName))
            {
                return null;
            }

            return _globalScope.FindNamespaceScope(namespaceName);
        }

        private bool IsExtensionMemberHidden(IBaseSymbol onType, IBaseScope fromScope, string memberName)
        {
            if (string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            if (onType is ObjectDeclarationSymbol receiver
                && receiver.ExtensionUseHiddenMembers != null
                && receiver.ExtensionUseHiddenMembers.Contains(memberName))
            {
                return true;
            }

            var file = GetOwningFileScope(fromScope);
            if (file?.ExtensionUseHiddenMembers != null
                && file.ExtensionUseHiddenMembers.Contains(memberName))
            {
                return true;
            }

            return _globalScope.ExtensionUseHiddenMembers != null
                && _globalScope.ExtensionUseHiddenMembers.Contains(memberName);
        }

        private bool TryGetExtensionMethodAlias(
            IBaseSymbol onType,
            IBaseScope fromScope,
            string methodName,
            out (string? ExtName, string OriginalMethod) alias)
        {
            alias = default;
            if (string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            if (onType is ObjectDeclarationSymbol receiver
                && receiver.ExtensionUseMethodAliases != null
                && receiver.ExtensionUseMethodAliases.TryGetValue(methodName, out var fromReceiver))
            {
                alias = fromReceiver;
                return true;
            }

            var file = GetOwningFileScope(fromScope);
            if (file?.ExtensionUseMethodAliases != null
                && file.ExtensionUseMethodAliases.TryGetValue(methodName, out var fromFile))
            {
                alias = fromFile;
                return true;
            }

            if (_globalScope.ExtensionUseMethodAliases != null
                && _globalScope.ExtensionUseMethodAliases.TryGetValue(methodName, out var fromGlobal))
            {
                alias = fromGlobal;
                return true;
            }

            return false;
        }

        private bool TryGetPreferredExtensionName(
            IBaseSymbol onType,
            IBaseScope fromScope,
            string methodName,
            out string preferred)
        {
            preferred = "";
            if (string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            if (onType is ObjectDeclarationSymbol receiver
                && receiver.ExtensionUseMethodPrecedence != null
                && receiver.ExtensionUseMethodPrecedence.TryGetValue(methodName, out var fromReceiver))
            {
                preferred = fromReceiver;
                return true;
            }

            var file = GetOwningFileScope(fromScope);
            if (file?.ExtensionUseMethodPrecedence != null
                && file.ExtensionUseMethodPrecedence.TryGetValue(methodName, out var fromFile))
            {
                preferred = fromFile;
                return true;
            }

            if (_globalScope.ExtensionUseMethodPrecedence != null
                && _globalScope.ExtensionUseMethodPrecedence.TryGetValue(methodName, out var fromGlobal))
            {
                preferred = fromGlobal;
                return true;
            }

            return false;
        }

        private bool ShouldSkipExtensionForPrecedence(
            ObjectDeclarationSymbol extDecl,
            IBaseSymbol onType,
            IBaseScope fromScope,
            string callName,
            string effectiveName)
        {
            if (TryGetPreferredExtensionName(onType, fromScope, callName, out var preferred)
                || (!string.Equals(callName, effectiveName, StringComparison.OrdinalIgnoreCase)
                    && TryGetPreferredExtensionName(onType, fromScope, effectiveName, out preferred)))
            {
                return !ExtensionDeclarationMatchesName(extDecl, preferred);
            }

            return false;
        }

        private static bool FileScopeMatchesOwningFile(
            FileScope fileScope,
            string? relativeFileName,
            string? absolutePath)
        {
            if (!string.IsNullOrEmpty(relativeFileName)
                && (string.Equals(fileScope.FileName, relativeFileName, StringComparison.Ordinal)
                    || string.Equals(fileScope.SourceFile, relativeFileName, StringComparison.Ordinal)))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(absolutePath)
                && (string.Equals(fileScope.SourceFile, absolutePath, StringComparison.Ordinal)
                    || string.Equals(fileScope.FileName, absolutePath, StringComparison.Ordinal)))
            {
                return true;
            }

            return false;
        }

        private static ObjectDeclarationSymbol? FindDeclaringExtension(ObjectMethodSymbol method)
        {
            for (IBaseScope? scope = method.ContainingScope; scope != null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj
                    && obj.IsExtension
                    && !obj.IsExtensionTargetGroup)
                {
                    return obj;
                }
            }

            return null;
        }

        /// <summary>
        /// <c>self</c> inside an extension member is the header or group target, not the
        /// extension class. <c>static</c> and <c>parent</c> keep the enclosing-object path.
        /// Stops at the <em>nearest</em> enclosing <see cref="ObjectDeclarationSymbol"/> scope —
        /// an anonymous class (or other nested object declaration) declared inside an extension
        /// member's body is itself an <see cref="ObjectDeclarationScope"/>, and its own <c>self</c>
        /// must mean that anonymous class, not the extension's block target. Only when the
        /// nearest such scope is the block/group itself (whose <see cref="ObjectDeclarationSymbol.ExtensionBlockTargetSymbol"/>
        /// is set) does this return the target; otherwise it returns null so the caller falls
        /// back to normal self/static/parent handling for that nearer object.
        /// </summary>
        private static IBaseSymbol? TryGetExtensionBlockTarget(IBaseScope fromScope)
        {
            for (var scope = fromScope; scope != null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol obj)
                {
                    return obj.ExtensionBlockTargetSymbol;
                }
            }

            return null;
        }

        private bool IsExtensionMethodActivated(ObjectMethodSymbol extensionMethod, IBaseScope fromScope)
        {
            var owner = FindDeclaringExtension(extensionMethod);
            if (owner == null || !owner.IsExtension)
            {
                return true;
            }

            if (owner.IsCompilerGenerated)
            {
                return true;
            }

            var source = owner.SourceFile ?? "";
            if (source.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase)
                && !source.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var file = GetOwningFileScope(fromScope);
            if (file != null
                && (file.DeclaredExtensions.Contains(owner) || file.ImportedExtensions.Contains(owner)))
            {
                return true;
            }

            return _globalScope.GloballyActivatedExtensions.Contains(owner);
        }

        /// <summary>
        /// Among several activated extensions that all match a receiver, prefer this file's
        /// own <c>extension</c> declarations, then file-local <c>use extension</c>, then
        /// <c>global use extension</c>. Ties keep index order (first match).
        /// </summary>
        private int RankActivatedExtension(ObjectDeclarationSymbol? owner, IBaseScope fromScope)
        {
            if (owner is null)
            {
                return 0;
            }

            var file = GetOwningFileScope(fromScope);
            if (file != null)
            {
                if (file.DeclaredExtensions.Contains(owner))
                {
                    return 300;
                }

                if (file.ImportedExtensions.Contains(owner))
                {
                    return 200;
                }
            }

            if (_globalScope.GloballyActivatedExtensions.Contains(owner)
                || owner.IsCompilerGenerated)
            {
                return 100;
            }

            return 50;
        }

        private static bool ExtensionDeclarationMatchesName(ObjectDeclarationSymbol extDecl, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;

            var fqn = string.IsNullOrEmpty(extDecl.FullyQualifiedName) ? extDecl.Name : extDecl.FullyQualifiedName;
            var lastSegment = extDecl.FullyQualifiedName?.Split('\\').LastOrDefault() ?? extDecl.Name;
            return string.Equals(fqn, pattern, StringComparison.OrdinalIgnoreCase)
                || string.Equals(extDecl.Name, pattern, StringComparison.OrdinalIgnoreCase)
                || string.Equals(lastSegment, pattern, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves self, static, or parent keywords to the enclosing class's symbol.
        /// </summary>
        /// <param name="keyword">One of "self", "static", or "parent".</param>
        /// <param name="fromScope">The scope from which the keyword is used.</param>
        /// <returns>The resolved ObjectDeclarationSymbol, or null if not in a class context.</returns>
        public ObjectDeclarationSymbol? ResolveSelfStaticParent(string keyword, IBaseScope fromScope)
        {
            if (string.IsNullOrEmpty(keyword)) return null;

            var objScope = FindEnclosingObjectScope(fromScope);
            if (objScope == null) return null;

            var objSymbol = objScope.DeclarationSymbol as ObjectDeclarationSymbol;
            if (objSymbol == null) return null;

            if (string.Equals(keyword, "self", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(keyword, "static", StringComparison.OrdinalIgnoreCase))
            {
                if (objSymbol.IsCompilerGenerated && objSymbol.IsExtension &&
                    objSymbol.InlineExtensionReceiverClass != null)
                {
                    return objSymbol.InlineExtensionReceiverClass;
                }

                return objSymbol;
            }

            if (string.Equals(keyword, "parent", StringComparison.OrdinalIgnoreCase))
            {
                if (objSymbol.ExtendsType != null)
                {
                    var parentSymbol = ResolveType(objSymbol.ExtendsType, objScope);
                    if (parentSymbol is ObjectDeclarationSymbol parentObj)
                    {
                        return parentObj;
                    }
                }
                return null;
            }

            return null;
        }

        /// <summary>
        /// Resolves a generic type parameter by name within the given scope.
        /// Searches the enclosing method and class for matching generic parameters.
        /// Method generic parameters shadow class generic parameters.
        /// </summary>
        public GenericTypeParameterSymbol? ResolveGenericTypeParameter(string name, IBaseScope fromScope)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (GenericAliasParameters != null)
            {
                var aliasParam = GenericAliasParameters.FirstOrDefault(
                    gp => string.Equals(gp.Name, name, StringComparison.OrdinalIgnoreCase));
                if (aliasParam != null)
                {
                    return aliasParam;
                }
            }

            var scope = fromScope;
            while (scope != null)
            {
                if (scope.DeclarationSymbol is FunctionDeclarationSymbol funcSymbol)
                {
                    var methodParam = funcSymbol.GenericParameters?.FirstOrDefault(
                        gp => string.Equals(gp.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (methodParam != null) return methodParam;
                }
                else if (scope.DeclarationSymbol is ObjectMethodSymbol methodSymbol)
                {
                    var methodParam = methodSymbol.GenericParameters?.FirstOrDefault(
                        gp => string.Equals(gp.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (methodParam != null) return methodParam;
                }

                if (scope.DeclarationSymbol is ObjectDeclarationSymbol objSymbol)
                {
                    var classParam = objSymbol.GenericParameters?.FirstOrDefault(
                        gp => string.Equals(gp.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (classParam != null) return classParam;
                }

                scope = scope.ParentScope;
            }

            return null;
        }

        private IBaseSymbol? ResolveUseAlias(string name, IBaseScope scope)
        {
            var useSymbol = scope.FindChildSymbolByName(name);
            if (useSymbol is UseIncludeSymbol useInclude)
            {
                var segments = useInclude.ImportedNameSegments;
                if (segments == null || segments.Length == 0) return null;

                return ResolveQualifiedName(segments);
            }

            return null;
        }

        /// <summary>
        /// Symbols that may bind a type-position name. Methods, properties, constants, and
        /// functions are not types even when they share a spelling with a builtin or alias.
        /// </summary>
        public static bool IsTypePositionSymbol(IBaseSymbol? symbol) =>
            symbol is BuiltInTypeSymbol
                or BuiltInUtilityTypeSymbol
                or ObjectDeclarationSymbol
                or TypeAliasSymbol
                or ObjectTypeAliasSymbol
                or GenericTypeParameterSymbol
                or AnonymousObjectDeclarationSymbol;

        private static IBaseSymbol? AcceptIf(IBaseSymbol? symbol, Func<IBaseSymbol, bool>? accept)
        {
            if (symbol is null)
            {
                return null;
            }

            return accept is null || accept(symbol) ? symbol : null;
        }

        private bool TryAcceptChildByName(
            IBaseScope scope,
            string name,
            Func<IBaseSymbol, bool>? accept,
            out IBaseSymbol? accepted)
        {
            accepted = null;
            var found = scope.FindChildSymbolByName(name);
            if (found is UseIncludeSymbol useInclude)
            {
                var segments = useInclude.ImportedNameSegments;
                if (segments is { Length: > 0 })
                {
                    accepted = AcceptIf(ResolveQualifiedName(segments), accept);
                    return accepted != null;
                }

                return false;
            }

            if (found != null)
            {
                accepted = AcceptIf(found, accept);
                return accepted != null;
            }

            accepted = AcceptIf(ResolveUseAlias(name, scope), accept);
            return accepted != null;
        }

        private IBaseSymbol? RecordTypePositionResolution(PhpNamedTypeAst namedType, IBaseSymbol? resolved)
        {
            var accepted = AcceptIf(resolved, IsTypePositionSymbol);
            if (accepted != null)
            {
                RecordResolution(namedType, accepted);
            }

            return accepted;
        }

        /// <summary>
        /// Resolves type arguments applied to a builtin or named type
        /// (<c>array&lt;TKey, TValue&gt;</c>, <c>\Closure&lt;…&gt;</c>). Those arguments live on
        /// the <c>typeName</c> grammar addon. The enclosing method or nested
        /// <c>extends&lt;T&gt;</c> group scope is what makes <c>T</c> resolve.
        /// </summary>
        private void ResolveAppliedTypeArguments(IBase2Ast typeNode, IBaseScope fromScope)
        {
            if (typeNode.AstGrammarAddons.TryGetValue("typeName", out var typeName))
            {
                ResolveTypesInAst(typeName, fromScope, 0);
            }

            if (typeNode is PhpNamedTypeAst { Name: TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst nameArgs } })
            {
                ResolveTypesInAst(nameArgs, fromScope, 0);
            }
        }

        private IBaseSymbol? ResolveNamedType(PhpNamedTypeAst namedType, IBaseScope fromScope)
        {
            // Pass 2 binds a group or method type parameter from the declaring scope. A later
            // resolve (checker, from a scope that does not own that parameter) must keep that
            // binding. Re-resolving here would miss `extends<TKey, TValue>` and report TYHP3003.
            if (namedType.BoundSymbol is GenericTypeParameterSymbol boundParameter)
            {
                return boundParameter;
            }

            var nameExpr = namedType.Name;
            if (nameExpr == null) return null;

            var typeName = GetExpressionName(nameExpr);
            if (string.IsNullOrEmpty(typeName)) return null;

            if (string.Equals(typeName, "self", StringComparison.OrdinalIgnoreCase))
            {
                var blockTarget = TryGetExtensionBlockTarget(fromScope);
                if (blockTarget != null)
                {
                    RecordResolution(namedType, blockTarget);
                    return blockTarget;
                }
            }

            if (string.Equals(typeName, "self", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "static", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "parent", StringComparison.OrdinalIgnoreCase))
            {
                var selfResult = ResolveSelfStaticParent(typeName, fromScope);
                if (selfResult != null)
                {
                    RecordResolution(namedType, selfResult);
                }
                return selfResult;
            }

            var genericParam = ResolveGenericTypeParameter(typeName, fromScope);
            if (genericParam != null)
            {
                RecordResolution(namedType, genericParam);
                return genericParam;
            }

            if (typeName.StartsWith("\\", StringComparison.Ordinal))
            {
                var segments = typeName.TrimStart('\\').Split('\\');
                var objectAlias = TryResolveObjectTypeAlias(segments, fromScope, isFullyQualified: true);
                if (objectAlias != null)
                {
                    RecordResolution(namedType, objectAlias);
                    return objectAlias;
                }

                return RecordTypePositionResolution(namedType, ResolveQualifiedName(segments));
            }

            if (typeName.Contains('\\'))
            {
                var segments = typeName.Split('\\');
                var objectAlias = TryResolveObjectTypeAlias(segments, fromScope, isFullyQualified: false);
                if (objectAlias != null)
                {
                    RecordResolution(namedType, objectAlias);
                    return objectAlias;
                }

                return RecordTypePositionResolution(
                    namedType,
                    ResolveRelativeName(segments, fromScope));
            }

            var simpleResult = ResolveSymbol(typeName, fromScope, IsTypePositionSymbol);

            // An unqualified type name that is not lexically in scope (e.g. a class declared in
            // another file of the same namespace, or a global type) still resolves per PHP name
            // resolution: relative to the current namespace, then falling back to the global
            // namespace. ResolveSymbol only walks lexical/use scopes, so fall back here.
            simpleResult ??= AcceptIf(
                ResolveRelativeName(new[] { typeName }, fromScope),
                IsTypePositionSymbol);

            if (simpleResult != null)
            {
                RecordResolution(namedType, simpleResult);
            }
            return simpleResult;
        }

        /// <summary>
        /// Resolves <c>self\Alias</c>, <c>static\Alias</c>, <c>parent\Alias</c>, and
        /// <c>ClassName\Alias</c> to a class-level type member: an
        /// <see cref="ObjectTypeAliasSymbol"/> or a nested named struct
        /// (<see cref="ObjectDeclarationSymbol"/> with <c>IsStruct</c>).
        /// Used for type annotations, <c>new</c>, and <c>is</c> / <c>instanceof</c> RHS names.
        /// </summary>
        public IBaseSymbol? TryResolveObjectTypeAlias(
            string[] segments,
            IBaseScope fromScope,
            bool isFullyQualified)
        {
            if (segments == null || segments.Length < 2)
            {
                return null;
            }

            var aliasName = segments[^1];
            var ownerSegments = segments[..^1];
            if (string.IsNullOrEmpty(aliasName) || ownerSegments.Length == 0)
            {
                return null;
            }

            ObjectDeclarationSymbol? owner = null;
            if (ownerSegments.Length == 1
                && (string.Equals(ownerSegments[0], "self", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ownerSegments[0], "static", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ownerSegments[0], "parent", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.Equals(ownerSegments[0], "self", StringComparison.OrdinalIgnoreCase)
                    && TryGetExtensionBlockTarget(fromScope) is ObjectDeclarationSymbol blockOwner)
                {
                    owner = blockOwner;
                }
                else if (string.Equals(ownerSegments[0], "self", StringComparison.OrdinalIgnoreCase)
                    && TryGetExtensionBlockTarget(fromScope) != null)
                {
                    return null;
                }
                else
                {
                    owner = ResolveSelfStaticParent(ownerSegments[0], fromScope);
                }
            }
            else
            {
                var resolvedOwner = isFullyQualified
                    ? ResolveQualifiedName(ownerSegments)
                    : ResolveRelativeName(ownerSegments, fromScope)
                        ?? ResolveQualifiedName(ownerSegments);
                owner = resolvedOwner as ObjectDeclarationSymbol;
            }

            if (owner == null)
            {
                return null;
            }

            if (owner.Members.TryGetValue(aliasName, out var member)
                && IsNestedTypeMember(member))
            {
                return member;
            }

            var inherited = ResolveInheritedMember(
                aliasName,
                owner,
                staticOnly: false,
                includeConstants: false,
                new HashSet<ObjectDeclarationSymbol>());
            return IsNestedTypeMember(inherited) ? inherited : null;
        }

        private static bool IsNestedTypeMember(IBaseSymbol? member) =>
            member is ObjectTypeAliasSymbol
            || member is ObjectDeclarationSymbol { IsStruct: true };

        private IBaseSymbol? ResolveInheritedMember(
            string memberName,
            ObjectDeclarationSymbol obj,
            bool staticOnly,
            bool includeConstants,
            HashSet<ObjectDeclarationSymbol> visited,
            int depth = 0)
        {
            if (depth > MaxInheritanceDepth)
            {
                _diagnostics.AddError(MessageCode.BinderUnknownError, obj.SourceFile ?? "", obj.Line, obj.Column,
                    "Maximum inheritance depth exceeded during member resolution");
                return null;
            }

            var lookupKind = staticOnly || includeConstants
                ? InheritedLookupKindStatic
                : InheritedLookupKindInstance;
            if (!TryPushInheritedLookup(obj, memberName, lookupKind))
            {
                return null;
            }

            try
            {
                return ResolveInheritedMemberBody(
                    memberName, obj, staticOnly, includeConstants, visited, depth);
            }
            finally
            {
                PopInheritedLookup(obj, memberName, lookupKind);
            }
        }

        private IBaseSymbol? ResolveInheritedMemberBody(
            string memberName,
            ObjectDeclarationSymbol obj,
            bool staticOnly,
            bool includeConstants,
            HashSet<ObjectDeclarationSymbol> visited,
            int depth)
        {
            if (!visited.Add(obj)) return null;

            // Class constants are case-sensitive and live in their own map so they can share a
            // spelling with a method (`const TAG` + `tag()`). Only consult them for static /
            // constant resolution — instance `->` lookup stays in the method/property namespace.
            if (includeConstants && obj.TryGetConstant(memberName, out var ownConstant))
            {
                return ownConstant;
            }

            if (obj.Members.TryGetValue(memberName, out var ownMember))
            {
                if (!staticOnly || IsStaticOrConstant(ownMember))
                {
                    return ownMember;
                }
            }

            // A parent (or ancestor) `__call` / `__callStatic` is only a dispatch fallback.
            // Soft-defer it so this class's `use Trait` methods and interfaces are searched
            // first — otherwise Filament-style `extends BaseWithCall { use HasKeyBindings; }`
            // typechecks `keyBindings([...])` against `__call(string $method, array $parameters)`
            // instead of the trait overlay.
            // A concrete inherited method is also deferred until after this class's traits:
            // PHP inserts trait methods ahead of inherited instance methods of the same name.
            // Properties and constants still return here so that walk stays parent-first.
            // A trait's own magic hook is also a fallback for the looked-up name: it must not
            // count as that trait defining the name, and this class's `__call` / `__callStatic`
            // still wins over a trait hook, which wins over the deferred ancestor hook.
            IBaseSymbol? deferredAncestorMagic = null;
            IBaseSymbol? deferredTraitMagic = null;
            IBaseSymbol? deferredParentMethod = null;
            var parentObj = ResolveParentObject(obj);
            if (parentObj != null)
            {
                var parentResult = ResolveInheritedMember(
                    memberName, parentObj, staticOnly, includeConstants, visited, depth + 1);
                if (parentResult != null)
                {
                    if (IsMagicCallFallback(parentResult, memberName))
                    {
                        deferredAncestorMagic = parentResult;
                    }
                    else if (IsMethodMember(parentResult))
                    {
                        deferredParentMethod = parentResult;
                    }
                    else
                    {
                        return parentResult;
                    }
                }
            }

            var resolvedImpls = CollectResolvedImplementsAndUsedTraits(obj);

            // Check trait adaptation rules (insteadof / as)
            if (obj.TraitMethodPrecedence != null &&
                obj.TraitMethodPrecedence.TryGetValue(memberName, out var preferredTrait))
            {
                foreach (var (implType, resolvedImpl) in resolvedImpls)
                {
                    if (resolvedImpl != null && resolvedImpl.ObjectKind == PhpTypeDeclType.Trait)
                    {
                        var implFqn = string.IsNullOrEmpty(resolvedImpl.FullyQualifiedName) ? resolvedImpl.Name : resolvedImpl.FullyQualifiedName;
                        var lastSegment = resolvedImpl.FullyQualifiedName?.Split('\\').LastOrDefault() ?? resolvedImpl.Name;
                        if (string.Equals(implFqn, preferredTrait, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(lastSegment, preferredTrait, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(resolvedImpl.Name, preferredTrait, StringComparison.OrdinalIgnoreCase))
                        {
                            var result = ResolveInheritedMember(
                                memberName, resolvedImpl, staticOnly, includeConstants,
                                NewAdaptationVisited(obj), depth + 1);
                            if (result != null && !IsMagicCallFallback(result, memberName))
                            {
                                return result;
                            }
                        }
                    }
                }
            }

            // Check trait aliases (as rule)
            if (obj.TraitMethodAliases != null &&
                obj.TraitMethodAliases.TryGetValue(memberName, out var aliasInfo))
            {
                foreach (var (implType, resolvedImpl) in resolvedImpls)
                {
                    if (resolvedImpl != null && resolvedImpl.ObjectKind == PhpTypeDeclType.Trait)
                    {
                        var aliasImplFqn = string.IsNullOrEmpty(resolvedImpl.FullyQualifiedName) ? resolvedImpl.Name : resolvedImpl.FullyQualifiedName;
                        var aliasLastSegment = resolvedImpl.FullyQualifiedName?.Split('\\').LastOrDefault() ?? resolvedImpl.Name;
                        if (aliasInfo.TraitName == null ||
                            string.Equals(aliasImplFqn, aliasInfo.TraitName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(aliasLastSegment, aliasInfo.TraitName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(resolvedImpl.Name, aliasInfo.TraitName, StringComparison.OrdinalIgnoreCase))
                        {
                            var result = ResolveInheritedMember(
                                aliasInfo.OriginalMethod, resolvedImpl, staticOnly, includeConstants,
                                NewAdaptationVisited(obj), depth + 1);
                            if (result != null && !IsMagicCallFallback(result, aliasInfo.OriginalMethod))
                            {
                                return result;
                            }
                        }
                    }
                }
            }

            // Search traits/interfaces from ImplementsTypes + AST trait uses
            IBaseSymbol? firstTraitMatch = null;
            int traitMatchCount = 0;
            var pendingInterfaces = new List<ObjectDeclarationSymbol>();

            foreach (var (implType, resolved) in resolvedImpls)
            {
                if (resolved == null) continue;

                if (resolved.ObjectKind == PhpTypeDeclType.Trait)
                {
                    var result = ResolveInheritedMember(
                        memberName, resolved, staticOnly, includeConstants, visited, depth + 1);
                    if (result == null)
                    {
                        continue;
                    }

                    if (IsMagicCallFallback(result, memberName))
                    {
                        deferredTraitMagic ??= result;
                        continue;
                    }

                    traitMatchCount++;
                    firstTraitMatch ??= result;
                }
                else if (resolved.ObjectKind == PhpTypeDeclType.Interface)
                {
                    pendingInterfaces.Add(resolved);
                }
            }

            if (traitMatchCount == 1)
            {
                return firstTraitMatch;
            }
            else if (traitMatchCount > 1)
            {
                _diagnostics.AddError(MessageCode.BinderTraitConflict, obj.SourceFile ?? "", obj.Line, obj.Column,
                    $"Multiple traits define method '{memberName}' without insteadof resolution");
                return firstTraitMatch;
            }

            // Trait methods were absent. The inherited method is the call target;
            // interfaces and magic hooks stay behind it.
            if (deferredParentMethod != null)
            {
                return deferredParentMethod;
            }

            // Search interfaces
            foreach (var ifaceObj in pendingInterfaces)
            {
                var result = ResolveInheritedMember(
                    memberName, ifaceObj, staticOnly, includeConstants, visited, depth + 1);
                if (result != null && !IsMagicCallFallback(result, memberName))
                {
                    return result;
                }
            }

            // Magic method fallback: __call / __callStatic for unresolved method names.
            // __get only for property-keyed lookups (`$name`) — bare names are methods, and
            // returning __get made `$this->missingMethod()` look like a zero-arg `__get` call
            // (TYHP4142 on `$name`) when nested `use Trait` members were invisible.
            // Prefer this class's own magic, then a used trait's magic, then a deferred
            // ancestor magic. All three lose to any concrete trait / interface match above.
            if (!staticOnly)
            {
                if (obj.Members.TryGetValue("__call", out var magicCall))
                    return magicCall;
                if (memberName.StartsWith("$", StringComparison.Ordinal)
                    && obj.Members.TryGetValue("__get", out var magicGet))
                    return magicGet;
            }
            else
            {
                if (obj.Members.TryGetValue("__callStatic", out var magicCallStatic))
                    return magicCallStatic;
            }

            return deferredTraitMagic ?? deferredAncestorMagic;
        }

        /// <summary>
        /// <c>insteadof</c> / <c>as</c> start a new walk. The parent walk may already
        /// have entered the same trait; that must not hide the child's adaptation.
        /// The class being resolved stays visited so the adaptation cannot re-enter it.
        /// </summary>
        private static HashSet<ObjectDeclarationSymbol> NewAdaptationVisited(ObjectDeclarationSymbol obj) =>
            new() { obj };

        /// <summary>
        /// A concrete instance or static method. <see cref="IsMagicCallFallback"/> already
        /// split off an ancestor <c>__call</c> / <c>__callStatic</c> used as a dispatch hook
        /// for a different name.
        /// </summary>
        private static bool IsMethodMember(IBaseSymbol symbol) =>
            SymbolTypeHelper.IsInstanceMethodDeclarationScope(symbol.SymbolType)
            || SymbolTypeHelper.IsStaticMethodDeclarationScope(symbol.SymbolType);

        /// <summary>
        /// Inherited <c>__call</c> / <c>__callStatic</c> are magic dispatch hooks, not
        /// concrete members for the looked-up name. Callers must keep searching used
        /// traits and interfaces before accepting them as the call target.
        /// </summary>
        private static bool IsMagicCallFallback(IBaseSymbol symbol, string memberName)
        {
            // Looking up `__call` / `__callStatic` by name finds that method itself.
            // The same symbol is a fallback only when some other name resolved to it.
            if (string.Equals(symbol.Name, memberName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return symbol.SymbolType is SymbolType.ObjectMagicCallMethod
                or SymbolType.ObjectMagicCallStaticMethod;
        }

        /// <summary>
        /// Trait names in a <c>use</c> clause are raw <see cref="IClassName"/> nodes, and
        /// <c>BindTraitUseBlock</c> only records those that also happen to be
        /// <see cref="ITypeExpression"/> — so <see cref="ObjectDeclarationSymbol.ImplementsTypes"/>
        /// is incomplete for ordinary <c>use BootsTraits;</c>. Merge the AST list (same idea as
        /// <c>TypeComparer.ResolveUsedTraits</c> / <see cref="ResolveParentObject"/>).
        /// Engine <c>\UnitEnum</c> / <c>\BackedEnum</c> are auto-implemented on enumerations
        /// (PHP); they are not written on the AST, so member lookup injects them here —
        /// the same walker, not a second heritage walk.
        /// </summary>
        private List<(ITypeExpression? TypeExpr, ObjectDeclarationSymbol? Symbol)> CollectResolvedImplementsAndUsedTraits(
            ObjectDeclarationSymbol obj)
        {
            var result = new List<(ITypeExpression? TypeExpr, ObjectDeclarationSymbol? Symbol)>();
            var seen = new HashSet<ObjectDeclarationSymbol>();

            foreach (var typeExpr in obj.ImplementsTypes)
            {
                var resolved = ResolveTypeToObject(typeExpr, obj);
                if (resolved is not null && !seen.Add(resolved))
                {
                    continue;
                }

                result.Add((typeExpr, resolved));
            }

            if (obj.ContainingScope is { } scope)
            {
                foreach (var className in GetAstUsedTraitClassNames(obj))
                {
                    var resolved = ResolveClassNameToObject(className, scope);
                    if (resolved is null || !seen.Add(resolved))
                    {
                        continue;
                    }

                    result.Add((null, resolved));
                }
            }

            AppendEngineEnumAutoImplements(obj, result, seen);

            // Tyhpdef `interface Child extends Parent` stores the base list on `Extends`
            // (`PhpClassNameListAst`), not `Implements`. The binder only copies `Implements`
            // into `ImplementsTypes`, so member lookup would otherwise stop at the child's
            // own methods and a call such as `FilesystemOperator::write` would skip argument
            // checking. Source interfaces park that same list in `Implements`, which is
            // already walked above.
            if (obj.ObjectKind == PhpTypeDeclType.Interface
                && obj.DeclaringAstNode is TyhpdefImportObjectDeclAst { Extends: PhpClassNameListAst extendsList }
                && obj.ContainingScope is { } interfaceScope)
            {
                foreach (var className in extendsList.GetAllNotNull())
                {
                    var resolved = ResolveClassNameToObject(className, interfaceScope);
                    if (resolved is not { ObjectKind: PhpTypeDeclType.Interface }
                        || !seen.Add(resolved))
                    {
                        continue;
                    }

                    result.Add((null, resolved));
                }
            }

            return result;
        }

        /// <summary>
        /// PHP applies <c>\UnitEnum</c> to every enumeration and <c>\BackedEnum</c> to
        /// backed enumerations. Inject those global engine interfaces into the existing
        /// implements list so <c>cases</c> / <c>from</c> / <c>tryFrom</c> resolve without
        /// a written <c>implements</c>. Does not mutate <see cref="ObjectDeclarationSymbol.ImplementsTypes"/>,
        /// so emit stays native (no written implements) and interface-body checking does
        /// not require the user to declare those engine methods.
        /// </summary>
        private void AppendEngineEnumAutoImplements(
            ObjectDeclarationSymbol obj,
            List<(ITypeExpression? TypeExpr, ObjectDeclarationSymbol? Symbol)> result,
            HashSet<ObjectDeclarationSymbol> seen)
        {
            if (obj.ObjectKind != PhpTypeDeclType.Enum)
            {
                return;
            }

            TryAddGlobalEngineInterface("UnitEnum", result, seen);
            if (obj.BackingType is not null)
            {
                TryAddGlobalEngineInterface("BackedEnum", result, seen);
            }
        }

        private void TryAddGlobalEngineInterface(
            string name,
            List<(ITypeExpression? TypeExpr, ObjectDeclarationSymbol? Symbol)> result,
            HashSet<ObjectDeclarationSymbol> seen)
        {
            if (ResolveQualifiedName([name]) is not ObjectDeclarationSymbol symbol
                || symbol.ObjectKind != PhpTypeDeclType.Interface)
            {
                return;
            }

            var fqn = string.IsNullOrEmpty(symbol.FullyQualifiedName)
                ? symbol.Name
                : symbol.FullyQualifiedName;
            if (!string.Equals(fqn.TrimStart('\\'), name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!seen.Add(symbol))
            {
                return;
            }

            result.Add((null, symbol));
        }

        private static IEnumerable<IClassName> GetAstUsedTraitClassNames(ObjectDeclarationSymbol declaration)
        {
            var body = declaration.DeclaringAstNode switch
            {
                PhpObjectTypeDeclAst { Body: { } classBody } => classBody,
                TyhpdefImportObjectDeclAst { Body: { } tyhpdefBody } => tyhpdefBody,
                _ => null,
            };

            if (body is null)
            {
                yield break;
            }

            foreach (var member in body.GetAllNotNull())
            {
                if (member is not PhpTraitUseAst { TraitNames: { } traitNames })
                {
                    continue;
                }

                foreach (var className in traitNames.GetAllNotNull())
                {
                    yield return className;
                }
            }
        }

        private ObjectDeclarationSymbol? ResolveClassNameToObject(IClassName className, IBaseScope scope)
        {
            var name = className switch
            {
                PhpNameAst named => named.ValueString,
                TokenValueAst token => token.ValueString,
                _ => className.Identifier,
            };

            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (name.StartsWith("\\", StringComparison.Ordinal))
            {
                return ResolveQualifiedName(name.TrimStart('\\').Split('\\')) as ObjectDeclarationSymbol;
            }

            if (name.Contains('\\'))
            {
                return ResolveRelativeName(name.Split('\\'), scope) as ObjectDeclarationSymbol;
            }

            return ResolveSymbol(name, scope) as ObjectDeclarationSymbol
                ?? ResolveRelativeName([name], scope) as ObjectDeclarationSymbol;
        }

        private IBaseSymbol? ResolveInheritedConstant(
            string constantName,
            ObjectDeclarationSymbol obj,
            HashSet<ObjectDeclarationSymbol> visited,
            int depth = 0)
        {
            if (depth > MaxInheritanceDepth)
            {
                _diagnostics.AddError(MessageCode.BinderUnknownError, obj.SourceFile ?? "", obj.Line, obj.Column,
                    "Maximum inheritance depth exceeded during constant resolution");
                return null;
            }

            if (!TryPushInheritedLookup(obj, constantName, InheritedLookupKindConstant))
            {
                return null;
            }

            try
            {
                return ResolveInheritedConstantBody(constantName, obj, visited, depth);
            }
            finally
            {
                PopInheritedLookup(obj, constantName, InheritedLookupKindConstant);
            }
        }

        private IBaseSymbol? ResolveInheritedConstantBody(
            string constantName,
            ObjectDeclarationSymbol obj,
            HashSet<ObjectDeclarationSymbol> visited,
            int depth)
        {
            if (!visited.Add(obj)) return null;

            if (obj.TryGetConstant(constantName, out var ownConstant))
            {
                return ownConstant;
            }

            var parentObj = ResolveParentObject(obj);
            if (parentObj != null)
            {
                var parentResult = ResolveInheritedConstant(constantName, parentObj, visited, depth + 1);
                if (parentResult != null) return parentResult;
            }

            // Same resolved bases as method lookup: ImplementsTypes, trait `use`, engine enum
            // interfaces, and a tyhpdef interface `extends` list. That list is not copied into
            // ImplementsTypes, so a constant on the parent interface is otherwise invisible.
            foreach (var (_, resolved) in CollectResolvedImplementsAndUsedTraits(obj))
            {
                if (resolved == null) continue;

                if (resolved.ObjectKind is PhpTypeDeclType.Trait or PhpTypeDeclType.Interface)
                {
                    var result = ResolveInheritedConstant(constantName, resolved, visited, depth + 1);
                    if (result != null) return result;
                }
            }

            return null;
        }

        private bool TryPushInheritedLookup(ObjectDeclarationSymbol obj, string memberName, int kind)
            => _inheritedLookupInFlight.Add((obj, memberName, kind));

        private void PopInheritedLookup(ObjectDeclarationSymbol obj, string memberName, int kind)
            => _inheritedLookupInFlight.Remove((obj, memberName, kind));

        /// <summary>
        /// The base class of <paramref name="obj"/>. <see cref="ObjectDeclarationSymbol.ExtendsType"/>
        /// is only populated when the base was written as a type expression; a Tyhp <c>extends</c>
        /// clause parses as a raw <see cref="IClassName"/> and leaves it null, so the declaring AST is
        /// the authoritative source and member resolution would otherwise stop at the class's own
        /// members.
        /// </summary>
        private ObjectDeclarationSymbol? ResolveParentObject(ObjectDeclarationSymbol obj)
        {
            if (obj.ExtendsType != null && ResolveTypeToObject(obj.ExtendsType, obj) is { } fromTypeExpression)
            {
                return fromTypeExpression;
            }

            var extendsName = StructExtends.FromDeclaringNode(obj.DeclaringAstNode);

            if (extendsName is null || obj.ContainingScope is not { } scope)
            {
                return null;
            }

            var name = extendsName switch
            {
                PhpNameAst named => named.ValueString,
                TokenValueAst token => token.ValueString,
                _ => extendsName.Identifier,
            };

            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // Fully-qualified (`\Foo\Bar`) resolves from the global root. A relative qualified name
            // (`Exceptions\Base`, no leading `\`) must resolve against the enclosing namespace / a
            // leading `use` alias (Prop-init #17), not the global root directly — otherwise member
            // resolution through the inheritance chain silently stops at this class.
            if (name.StartsWith("\\", StringComparison.Ordinal))
            {
                return ResolveQualifiedName(name.TrimStart('\\').Split('\\')) as ObjectDeclarationSymbol;
            }

            if (name.Contains('\\'))
            {
                return ResolveRelativeName(name.Split('\\'), scope) as ObjectDeclarationSymbol;
            }

            // A bare `extends` name refers to a type in the enclosing namespace, which may be declared
            // in a different file; relative resolution searches every file contributing to it.
            return ResolveSymbol(name, scope) as ObjectDeclarationSymbol
                ?? ResolveRelativeName([name], scope) as ObjectDeclarationSymbol;
        }

        private ObjectDeclarationSymbol? ResolveTypeToObject(ITypeExpression typeExpr, ObjectDeclarationSymbol context)
        {
            var scope = context.ContainingScope;
            if (scope == null) return null;

            var resolved = ResolveType(typeExpr, scope);
            return resolved as ObjectDeclarationSymbol;
        }

        private static bool IsStaticOrConstant(IBaseSymbol symbol)
        {
            return symbol.SymbolType is
                SymbolType.StaticObjectMethod or
                SymbolType.StaticObjectProperty or
                SymbolType.ObjectConstant or
                SymbolType.Constant or
                SymbolType.ObjectMagicCallStaticMethod;
        }

        private IBaseSymbol? SearchExtensionsInScope(string methodName, IBaseSymbol onType, IBaseScope scope)
        {
            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is ObjectDeclarationSymbol objSymbol && objSymbol.IsExtension)
                {
                    foreach (var extensionMethod in EnumerateExtensionMethods(objSymbol, methodName))
                    {
                        if (ExtensionMethodAppliesToReceiver(extensionMethod, onType, scope))
                            return extensionMethod;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// True when <paramref name="extensionMethod"/> is an extension of <paramref name="onType"/>.
        /// The indexed search resolves the first parameter from <see cref="GlobalScope"/>, which
        /// cannot see a namespaced relative target (<c>namespace App; extension E extends Box</c>).
        /// A block-target method's synthesized <c>$this</c> reuses that target AST, so when the
        /// global re-resolution misses, the block's already-bound
        /// <see cref="ObjectDeclarationSymbol.ExtensionBlockTargetSymbol"/> is the receiver.
        /// </summary>
        private bool ExtensionMethodAppliesToReceiver(
            ObjectMethodSymbol extensionMethod,
            IBaseSymbol onType,
            IBaseScope typeScope)
        {
            if (extensionMethod.Parameters is not { Count: > 0 } parameters
                || parameters[0].DeclaredType is not { } firstParamType)
            {
                return false;
            }

            if (ExtensionMethodFirstParamMatches(onType, firstParamType, typeScope))
            {
                return true;
            }

            return IsSynthesizedBlockTargetReceiver(extensionMethod, parameters[0])
                && ExtensionBlockTargetMatchesReceiver(extensionMethod, onType);
        }

        /// <summary>
        /// The implicit <c>$this</c> inserted for <c>extension E extends T</c> stores
        /// <c>T</c>'s AST as its declared type. An authored <c>$this</c> parameter uses a
        /// different type node and stays on the first-parameter match above.
        /// </summary>
        private static bool IsSynthesizedBlockTargetReceiver(
            ObjectMethodSymbol method,
            ParameterInfo first)
        {
            if (first.DeclaredType is null
                || (!string.Equals(first.Name, "$this", StringComparison.Ordinal)
                    && !string.Equals(first.Name, "this", StringComparison.Ordinal)))
            {
                return false;
            }

            for (var scope = method.ContainingScope; scope != null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol block
                    && ReferenceEquals(block.PendingExtensionBlockTarget, first.DeclaredType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Nearest header or nested-group target. A mismatch stops the walk: a nested
        /// group's target is not replaced by the enclosing extension's target.
        /// </summary>
        private static bool ExtensionBlockTargetMatchesReceiver(
            ObjectMethodSymbol method,
            IBaseSymbol receiverType)
        {
            for (var scope = method.ContainingScope; scope != null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is not ObjectDeclarationSymbol block
                    || block.ExtensionBlockTargetSymbol is not { } target)
                {
                    continue;
                }

                if (ReferenceEquals(target, receiverType))
                {
                    return true;
                }

                if (target is BuiltInTypeSymbol builtin
                    && receiverType is BuiltInTypeSymbol receiverBuiltin
                    && string.Equals(builtin.Name, receiverBuiltin.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(target.FullyQualifiedName)
                    && string.Equals(
                        target.FullyQualifiedName.TrimStart('\\'),
                        receiverType.FullyQualifiedName?.TrimStart('\\'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }

            return false;
        }

        /// <summary>
        /// Checks whether an extension method's first parameter type matches the target type.
        /// Compares resolved symbols by identity, builtin name, and FQN with a leading
        /// <c>\</c> stripped so <see cref="Checker.CheckedTypes"/> singletons (FQN <c>int</c>)
        /// match the global builtin (FQN <c>\int</c>), and so a fresh
        /// <c>BuiltInTypeSymbol("string")</c> under a string literal matches <c>extends string</c>.
        /// </summary>
        private bool ExtensionMethodFirstParamMatches(IBaseSymbol onType, ITypeExpression firstParamType, IBaseScope scope)
        {
            var resolvedParamType = ResolveType(firstParamType, scope);
            if (resolvedParamType != null
                && ExtensionReceiverSymbolsMatch(resolvedParamType, onType))
            {
                return true;
            }

            var paramTypeName = NormalizeExtensionReceiverName(firstParamType.ToString());
            var onTypeName = NormalizeExtensionReceiverName(
                onType is ObjectDeclarationSymbol ods
                    ? (string.IsNullOrEmpty(ods.FullyQualifiedName) ? ods.Name : ods.FullyQualifiedName)
                    : onType.Name);
            return string.Equals(paramTypeName, onTypeName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ExtensionReceiverSymbolsMatch(IBaseSymbol left, IBaseSymbol right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is BuiltInTypeSymbol && right is BuiltInTypeSymbol
                && string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(
                NormalizeExtensionReceiverName(left.FullyQualifiedName, left.Name),
                NormalizeExtensionReceiverName(right.FullyQualifiedName, right.Name),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeExtensionReceiverName(string? fqn, string? name = null)
        {
            var value = !string.IsNullOrEmpty(fqn) ? fqn : (name ?? string.Empty);
            return value.TrimStart('\\');
        }

        private FileScope? GetOwningFileScope(IBaseScope scope)
        {
            var current = scope;
            while (current != null)
            {
                if (current is FileScope fileScope)
                {
                    return fileScope;
                }

                if (current.DeclarationSymbol is NamespaceBlockSymbol nsBlockSym && nsBlockSym.OwningFileScope != null)
                {
                    return nsBlockSym.OwningFileScope;
                }

                current = current.ParentScope;
            }

            return null;
        }

        private static IEnumerable<ObjectMethodSymbol> EnumerateExtensionMethods(
            ObjectDeclarationSymbol extension,
            string methodName)
        {
            if (!extension.Members.TryGetValue(methodName, out var member)
                || member is not ObjectMethodSymbol primary
                || primary is ObjectOperatorOverloadMethodSymbol)
            {
                yield break;
            }

            yield return primary;
            foreach (var overload in primary.Overloads)
            {
                if (overload is not ObjectOperatorOverloadMethodSymbol)
                {
                    yield return overload;
                }
            }
        }

        private ObjectDeclarationScope? FindEnclosingObjectScope(IBaseScope fromScope)
        {
            var scope = fromScope;
            while (scope != null)
            {
                if (scope is ObjectDeclarationScope objScope
                    && objScope.DeclarationSymbol is not ObjectDeclarationSymbol { IsExtensionTargetGroup: true })
                {
                    return objScope;
                }

                scope = scope.ParentScope;
            }

            return null;
        }

        private string? FindEnclosingNamespaceName(IBaseScope fromScope)
        {
            var scope = fromScope;
            while (scope != null)
            {
                if (scope.DeclarationSymbol is NamespaceSymbol nsSymbol)
                {
                    return nsSymbol.Name?.Trim('\\');
                }

                if (scope.DeclarationSymbol is NamespaceBlockSymbol)
                {
                    var parentScope = scope.ParentScope;
                    if (parentScope?.DeclarationSymbol is NamespaceSymbol parentNsSymbol)
                    {
                        return parentNsSymbol.Name?.Trim('\\');
                    }
                }

                scope = scope.ParentScope;
            }

            return null;
        }

        private static string? GetExpressionName(IBase2Ast? expression)
        {
            if (expression == null)
            {
                return null;
            }

            return !string.IsNullOrEmpty(expression.Identifier)
                ? expression.Identifier
                : expression.ValueString;
        }
    }
}
