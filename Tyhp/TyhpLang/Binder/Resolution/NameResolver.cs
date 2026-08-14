using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Maximum inheritance chain depth. Cycles are blocked by the visited set;
        /// this cap guards against pathologically deep linear chains.
        /// </summary>
        private const int MaxInheritanceDepth = 100;

        private readonly GlobalScope _globalScope;
        private readonly SymbolTree? _symbolTree;
        private readonly DiagnosticBag _diagnostics;
        private readonly Dictionary<IBase2Ast, IBaseSymbol> _resolvedSymbols = new();

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
        {
            if (string.IsNullOrEmpty(name)) return null;

            var scope = fromScope;
            while (scope != null)
            {
                var found = scope.FindChildSymbolByName(name);
                if (found != null && found is not UseIncludeSymbol)
                {
                    return found;
                }

                var useAliasResolved = ResolveUseAlias(name, scope);
                if (useAliasResolved != null)
                {
                    return useAliasResolved;
                }

                // When in a namespace block, also check the owning FileScope for file-level symbols and use aliases.
                // NamespaceBlockScope is the only scope type whose DeclarationSymbol is NamespaceBlockSymbol,
                // so matching on the scope type alone is sufficient.
                if (scope is NamespaceBlockScope)
                {
                    var fileScope = GetOwningFileScope(scope);
                    if (fileScope != null && !ReferenceEquals(fileScope, scope))
                    {
                        var fileFound = ((IBaseScope)fileScope).FindChildSymbolByName(name);
                        if (fileFound is UseIncludeSymbol fileUseInclude)
                        {
                            var segments = fileUseInclude.ImportedNameSegments;
                            if (segments != null && segments.Length > 0)
                            {
                                var resolved = ResolveQualifiedName(segments);
                                if (resolved != null) return resolved;
                            }
                        }
                        else if (fileFound != null)
                        {
                            return fileFound;
                        }

                        var fileUseResolved = ResolveUseAlias(name, fileScope);
                        if (fileUseResolved != null)
                        {
                            return fileUseResolved;
                        }
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
                return ((IBaseScope)_globalScope).FindChildSymbolByName(name);

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

            return null;
        }

        /// <summary>
        /// Resolves an instance member (property, method) on an object declaration,
        /// walking the inheritance chain: own members → parent → traits → interfaces.
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
        /// walking the inheritance chain. Constants are matched case-sensitively in their own
        /// namespace before case-insensitive static method/property lookup.
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
                    var typeName = builtinType.Identifier;
                    if (string.IsNullOrEmpty(typeName)) return null;

                    // The late-static-binding type `static` (and `self`/`parent`) is parsed as a
                    // builtin type but resolves to the enclosing class context, not a global
                    // symbol — those keywords live in the object scope, not the global scope.
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
                    return ResolveNamedType(namedType, fromScope);
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

                case PhpTypeExpressionAst typeExpr:
                {
                    if (typeExpr.Types == null) return null;

                    IBaseSymbol? firstResolved = null;
                    foreach (var childType in typeExpr.Types.GetAllNotNull())
                    {
                        var resolved = ResolveType(childType, fromScope);
                        firstResolved ??= resolved;
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
        /// Searches for extension methods applicable to a type.
        /// Uses indexed lookup when created from a SymbolTree; otherwise scans reachable scopes.
        /// </summary>
        public IBaseSymbol? ResolveExtensionMethod(string methodName, IBaseSymbol? onType)
        {
            if (string.IsNullOrEmpty(methodName) || onType == null) return null;

            var fromScope = onType is ObjectDeclarationSymbol ods && ods.ContainingScope != null
                ? ods.ContainingScope
                : _globalScope;

            var effectiveMethodName = methodName;
            (string? ExtName, string OriginalMethod)? aliasInfo = null;
            if (onType is ObjectDeclarationSymbol receiverForAlias &&
                receiverForAlias.ExtensionUseMethodAliases != null &&
                receiverForAlias.ExtensionUseMethodAliases.TryGetValue(methodName, out var aliasTuple))
            {
                effectiveMethodName = aliasTuple.OriginalMethod;
                aliasInfo = aliasTuple;
            }

            if (onType is ObjectDeclarationSymbol receiver &&
                receiver.TyhpdefAutoActivatedExtensions is { Count: > 0 } allowedExt)
            {
                foreach (var extDecl in allowedExt)
                {
                    if (receiver.ExtensionUseMethodPrecedence != null &&
                        receiver.ExtensionUseMethodPrecedence.TryGetValue(methodName, out var preferredExtName) &&
                        !ExtensionDeclarationMatchesName(extDecl, preferredExtName))
                    {
                        continue;
                    }

                    if (aliasInfo is { ExtName: { } extFilter } &&
                        !ExtensionDeclarationMatchesName(extDecl, extFilter))
                    {
                        continue;
                    }

                    if (!extDecl.Members.TryGetValue(effectiveMethodName, out var member) ||
                        member is not ObjectMethodSymbol extensionMethod)
                    {
                        continue;
                    }

                    if (extensionMethod.Parameters == null || extensionMethod.Parameters.Count == 0)
                        continue;

                    var firstParamType = extensionMethod.Parameters[0].DeclaredType;
                    if (firstParamType == null)
                        continue;

                    if (ExtensionMethodFirstParamMatches(onType, firstParamType, fromScope))
                        return extensionMethod;
                }

                return null;
            }

            if (_symbolTree != null)
            {
                if (_symbolTree.ExtensionMethodIndex.TryGetValue(methodName, out var candidates))
                {
                    foreach (var extensionMethod in candidates)
                    {
                        if (extensionMethod.Parameters == null || extensionMethod.Parameters.Count == 0)
                            continue;

                        var firstParamType = extensionMethod.Parameters[0].DeclaredType;
                        if (firstParamType == null)
                            continue;

                        if (ExtensionMethodFirstParamMatches(onType, firstParamType, _globalScope))
                            return extensionMethod;
                    }
                }

                return null;
            }

            foreach (var childScope in _globalScope.ChildScopes)
            {
                if (childScope is NamespaceScope nsScope)
                {
                    foreach (var blockScope in nsScope.ChildScopes)
                    {
                        var result = SearchExtensionsInScope(methodName, onType, blockScope);
                        if (result != null) return result;
                    }
                }
                else if (childScope is FileScope fileScope)
                {
                    var result = SearchExtensionsInScope(methodName, onType, fileScope);
                    if (result != null) return result;
                }
            }

            return null;
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

        private IBaseSymbol? ResolveNamedType(PhpNamedTypeAst namedType, IBaseScope fromScope)
        {
            var nameExpr = namedType.Name;
            if (nameExpr == null) return null;

            var typeName = GetExpressionName(nameExpr);
            if (string.IsNullOrEmpty(typeName)) return null;

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
                var resolved = ResolveQualifiedName(segments);
                if (resolved != null)
                {
                    RecordResolution(namedType, resolved);
                }
                return resolved;
            }

            if (typeName.Contains('\\'))
            {
                var segments = typeName.Split('\\');
                var resolved = ResolveRelativeName(segments, fromScope);
                if (resolved != null)
                {
                    RecordResolution(namedType, resolved);
                }
                return resolved;
            }

            var simpleResult = ResolveSymbol(typeName, fromScope);

            // An unqualified type name that is not lexically in scope (e.g. a class declared in
            // another file of the same namespace, or a global type) still resolves per PHP name
            // resolution: relative to the current namespace, then falling back to the global
            // namespace. ResolveSymbol only walks lexical/use scopes, so fall back here.
            simpleResult ??= ResolveRelativeName(new[] { typeName }, fromScope);

            if (simpleResult != null)
            {
                RecordResolution(namedType, simpleResult);
            }
            return simpleResult;
        }

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

            var parentObj = ResolveParentObject(obj);
            if (parentObj != null)
            {
                var parentResult = ResolveInheritedMember(
                    memberName, parentObj, staticOnly, includeConstants, visited, depth + 1);
                if (parentResult != null) return parentResult;
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
                                memberName, resolvedImpl, staticOnly, includeConstants, visited, depth + 1);
                            if (result != null) return result;
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
                                aliasInfo.OriginalMethod, resolvedImpl, staticOnly, includeConstants, visited, depth + 1);
                            if (result != null) return result;
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
                    if (result != null)
                    {
                        traitMatchCount++;
                        firstTraitMatch ??= result;
                    }
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

            // Search interfaces
            foreach (var ifaceObj in pendingInterfaces)
            {
                var result = ResolveInheritedMember(
                    memberName, ifaceObj, staticOnly, includeConstants, visited, depth + 1);
                if (result != null) return result;
            }

            // Magic method fallback: __call / __callStatic for unresolved method names.
            // __get only for property-keyed lookups (`$name`) — bare names are methods, and
            // returning __get made `$this->missingMethod()` look like a zero-arg `__get` call
            // (TYHP4142 on `$name`) when nested `use Trait` members were invisible.
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

            return null;
        }

        /// <summary>
        /// Trait names in a <c>use</c> clause are raw <see cref="IClassName"/> nodes, and
        /// <c>BindTraitUseBlock</c> only records those that also happen to be
        /// <see cref="ITypeExpression"/> — so <see cref="ObjectDeclarationSymbol.ImplementsTypes"/>
        /// is incomplete for ordinary <c>use BootsTraits;</c>. Merge the AST list (same idea as
        /// <c>TypeComparer.ResolveUsedTraits</c> / <see cref="ResolveParentObject"/>).
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

            if (obj.ContainingScope is not { } scope)
            {
                return result;
            }

            foreach (var className in GetAstUsedTraitClassNames(obj))
            {
                var resolved = ResolveClassNameToObject(className, scope);
                if (resolved is null || !seen.Add(resolved))
                {
                    continue;
                }

                result.Add((null, resolved));
            }

            return result;
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

            foreach (var implType in obj.ImplementsTypes)
            {
                var resolved = ResolveTypeToObject(implType, obj);
                if (resolved == null) continue;

                if (resolved.ObjectKind is PhpTypeDeclType.Trait or PhpTypeDeclType.Interface)
                {
                    var result = ResolveInheritedConstant(constantName, resolved, visited, depth + 1);
                    if (result != null) return result;
                }
            }

            return null;
        }

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

            var extendsName = obj.DeclaringAstNode switch
            {
                PhpObjectTypeDeclAst { Extends: { } className } => className,
                TyhpStructDeclAst { Extends: { } className } => className,
                TyhpdefImportObjectDeclAst { Extends: IClassName className } => className,
                _ => null,
            };

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
                    if (objSymbol.Members.TryGetValue(methodName, out var member))
                    {
                        if (member is not ObjectMethodSymbol extensionMethod)
                            continue;
                        if (extensionMethod.Parameters == null || extensionMethod.Parameters.Count == 0)
                            continue;

                        var firstParamType = extensionMethod.Parameters[0].DeclaredType;
                        if (firstParamType == null)
                            continue;

                        if (ExtensionMethodFirstParamMatches(onType, firstParamType, scope))
                            return member;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Checks whether an extension method's first parameter type matches the target type.
        /// Uses symbol resolution and canonicalized FQN comparison instead of fragile ToString().
        /// </summary>
        private bool ExtensionMethodFirstParamMatches(IBaseSymbol onType, ITypeExpression firstParamType, IBaseScope scope)
        {
            var resolvedParamType = ResolveType(firstParamType, scope);
            if (resolvedParamType != null)
            {
                if (ReferenceEquals(resolvedParamType, onType))
                    return true;

                var resolvedFqn = string.IsNullOrEmpty(resolvedParamType.FullyQualifiedName)
                    ? resolvedParamType.Name : resolvedParamType.FullyQualifiedName;
                var onTypeFqn = string.IsNullOrEmpty(onType.FullyQualifiedName)
                    ? onType.Name : onType.FullyQualifiedName;
                return string.Equals(resolvedFqn, onTypeFqn, StringComparison.OrdinalIgnoreCase);
            }

            var paramTypeName = firstParamType.ToString();
            var onTypeName = onType is ObjectDeclarationSymbol ods
                ? (string.IsNullOrEmpty(ods.FullyQualifiedName) ? ods.Name : ods.FullyQualifiedName)
                : onType.Name;
            return string.Equals(paramTypeName, onTypeName, StringComparison.OrdinalIgnoreCase);
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

        private ObjectDeclarationScope? FindEnclosingObjectScope(IBaseScope fromScope)
        {
            var scope = fromScope;
            while (scope != null)
            {
                if (scope is ObjectDeclarationScope objScope)
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
