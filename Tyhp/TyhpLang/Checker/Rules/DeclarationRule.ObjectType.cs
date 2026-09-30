using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    public sealed partial class DeclarationRule
    {
        private void CheckObjectType(
            PhpObjectTypeDeclAst objectType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // DeclarationRule suppresses child traversal on the whole object shell, so written
            // extends/implements names are never CheckNode'd. Mark them for TYHP4130 the same
            // way operator operands and method parameter types are marked. Do this before the
            // BoundSymbol early-return so anonymous classes still count heritage imports.
            MarkHeritageImportNames(objectType, state, context);

            if (objectType.BoundSymbol is not ObjectDeclarationSymbol objectSymbol)
            {
                return;
            }

            RejectReservedExtensionBackerName(objectType.Identifier, objectType, state, diagnostics);

            var declKind = objectSymbol.ObjectKind;
            var modifiers = CheckerHelpers.ToMemberModifiers(objectType.Modifiers);

            if (declKind == PhpTypeDeclType.Class)
            {
                ValidateClassModifiers(objectType, state, modifiers, diagnostics);
            }

            if ((modifiers & MemberModifier.Static) != 0 && declKind != PhpTypeDeclType.Trait)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, objectType, MessageCode.CheckerNotAllowedMemberModifier, "static");
            }

            // Extends/implements are usually raw IClassName nodes (ExtendsType / ImplementsTypes stay
            // empty), so the binder's 3017/3018 path never fires. Diagnose unresolved targets and
            // inheritance cycles here once; silent resolvers elsewhere keep SilentDiagnostics.
            CheckInheritanceTargets(objectType, objectSymbol, state, context, diagnostics);
            CheckTraversableListing(objectType, objectSymbol, state, context, diagnostics);
            CheckEngineEnumListing(objectType, objectSymbol, state, context, diagnostics);

            if (declKind == PhpTypeDeclType.Class && objectType.Extends is not null)
            {
                CheckExtendsNotFinal(objectType, objectSymbol, state, context, diagnostics);
            }

            var objectState = state.Split(ScopeType.ObjectTypeDeclaration);
            objectState.EnclosingObject = objectSymbol;
            objectState.ObjectGenerics = objectSymbol.GenericParameters;
            objectState.EnclosingObjectType = CheckedTypes.FromSymbol(objectSymbol);
            objectState.Modifiers = modifiers;
            GenericConstraintResolver.ResolveAll(objectSymbol.GenericParameters, objectState, context);
            GenericTypeArgumentValidator.ValidateGenericParameterDefaults(
                objectSymbol.GenericParameters,
                objectType,
                objectState,
                context.SymbolTree,
                context.GlobalScope,
                diagnostics,
                (typeExpr, s, isReturn, isUser) =>
                    context.ResolveTypeAnnotation(typeExpr, s, isReturn, isUser));

            if (declKind is PhpTypeDeclType.Class or PhpTypeDeclType.Enum)
            {
                CheckInheritedAbstractMethods(objectType, objectSymbol, objectState, context, diagnostics);
                CheckInterfaceImplementation(objectType, objectSymbol, objectState, context, diagnostics);
                CheckTraitRequirements(objectType, objectSymbol, objectState, context, diagnostics);
            }

            if (declKind == PhpTypeDeclType.Enum)
            {
                CheckEnumDeclaration(objectType, objectSymbol, objectState, context, diagnostics);
            }

            if (declKind == PhpTypeDeclType.Interface)
            {
                objectState.Modifiers |= MemberModifier.Abstract;
            }

            CheckObjectBody(objectType.Body, objectSymbol, objectState, context, diagnostics);
            MarkTraitFlattenedGenericPropertyTracking(objectSymbol, objectState, context);
        }

        /// <summary>
        /// Tyhpdef class/trait/interface/enum shells are <see cref="TyhpdefImportObjectDeclAst"/>,
        /// not <see cref="PhpObjectTypeDeclAst"/>. Without this entry point the default walk visits
        /// members under a file-scope state with a null <see cref="CheckerState.EnclosingObject"/>,
        /// so bodyless signatures report missing returns and <c>self</c> on operators reports
        /// <c>CheckerRelativeTypeOutsideClass</c>.
        /// </summary>
        private void CheckTyhpdefImportObject(
            TyhpdefImportObjectDeclAst objectType,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (objectType.BoundSymbol is not ObjectDeclarationSymbol objectSymbol)
            {
                return;
            }

            var modifiers = CheckerHelpers.ToMemberModifiers(objectType.Modifiers);
            var objectState = state.Split(ScopeType.ObjectTypeDeclaration);
            objectState.EnclosingObject = objectSymbol;
            objectState.ObjectGenerics = objectSymbol.GenericParameters;
            objectState.EnclosingObjectType = CheckedTypes.FromSymbol(objectSymbol);
            objectState.Modifiers = modifiers;
            GenericConstraintResolver.ResolveAll(objectSymbol.GenericParameters, objectState, context);

            if (objectSymbol.ObjectKind == PhpTypeDeclType.Interface)
            {
                objectState.Modifiers |= MemberModifier.Abstract;
            }

            // SuppressChildTraversal owns the whole shell — still walk non-body children the way the
            // previous default traversal did (extends / implements / name / modifiers).
            // Traversable listing (TYHP4326) and engine-enum listing (TYHP4337) are user-.tyhp
            // rules (CheckTraversableListing / CheckEngineEnumListing, called only from
            // CheckObjectType); harvested .tyhpdef shells keep redundant
            // `implements \Iterator, \Traversable` and `implements \UnitEnum` / `\BackedEnum`
            // and must not be diagnosed here.
            foreach (var child in objectType.AstChildren)
            {
                if (child is null || ReferenceEquals(child, objectType.Body))
                {
                    continue;
                }

                context.CheckNode(child, objectState);
            }

            CheckObjectBody(objectType.Body, objectSymbol, objectState, context, diagnostics);
        }

        private static void MarkHeritageImportNames(
            PhpObjectTypeDeclAst objectType,
            CheckerState state,
            CheckerRuleContext context)
        {
            context.MarkImportNames(objectType.Extends, state);
            context.MarkImportNames(objectType.Implements, state);
        }

        private static void ValidateClassModifiers(
            PhpObjectTypeDeclAst objectType,
            CheckerState state,
            MemberModifier modifiers,
            DiagnosticBag diagnostics)
        {
            if (CheckerHelpers.CountVisibilityModifiers(modifiers) > 1)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    objectType,
                    MessageCode.CheckerMultipleVisibilities,
                    objectType.Identifier);
            }

            if ((modifiers & MemberModifier.Abstract) != 0 && (modifiers & MemberModifier.Final) != 0)
            {
                CheckerHelpers.ReportError(diagnostics, state, objectType, MessageCode.CheckerMemberModifierConflict, "abstract", "final");
            }
        }

        private static void CheckInheritanceTargets(
            PhpObjectTypeDeclAst objectType,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var declKind = objectSymbol.ObjectKind;
            var isInterface = declKind == PhpTypeDeclType.Interface;
            // Trait `extends`/`implements` are compile-time *requirements* (erased on emit), not
            // inheritance — CheckTraitRequirements owns satisfaction. Do not apply kind rules here.
            var checkTargetKinds = declKind != PhpTypeDeclType.Trait;

            // Classes / traits / enums: single-parent `extends` lives on Extends. Interfaces put their
            // base list in Implements (VisitInterfaceExtendsList).
            if (!isInterface && objectType.Extends is { } extendsName)
            {
                if (!TryReportObjectShapeHeritage(extendsName, objectSymbol, state, context, diagnostics))
                {
                    var parent = TypeComparer.TryGetParentDeclaration(
                        objectSymbol, context.SymbolTree, context.GlobalScope);
                    if (parent is null)
                    {
                        ReportUnresolvedTypeName(
                            extendsName,
                            MessageCode.BinderUnresolvedExtendsType,
                            state,
                            diagnostics);
                    }
                    else if (checkTargetKinds
                             && TryGetExpectedExtendsKind(declKind, out var expectedExtendsKind)
                             && parent.ObjectKind != expectedExtendsKind)
                    {
                        ReportWrongKindTypeName(
                            extendsName,
                            MessageCode.BinderInvalidExtendsTypeKind,
                            state,
                            diagnostics,
                            ObjectKindDisplayName(parent.ObjectKind),
                            ObjectKindDisplayName(expectedExtendsKind));
                    }
                    else if (declKind == PhpTypeDeclType.Class
                             && ClassInheritanceIsCircular(objectSymbol, context))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics,
                            state,
                            extendsName,
                            MessageCode.BinderCircularInheritance,
                            objectSymbol.Name);
                    }
                }
            }

            if (objectType.Implements is null)
            {
                return;
            }

            // Interface `extends` lists are stored on Implements; both that list and a class/enum
            // `implements` clause require each target to be an interface.
            var unresolvedCode = isInterface
                ? MessageCode.BinderUnresolvedExtendsType
                : MessageCode.BinderUnresolvedImplementsType;
            var wrongKindCode = isInterface
                ? MessageCode.BinderInvalidExtendsTypeKind
                : MessageCode.BinderInvalidImplementsTypeKind;

            foreach (var name in objectType.Implements.GetAllNotNull())
            {
                if (TryReportObjectShapeHeritage(name, objectSymbol, state, context, diagnostics))
                {
                    continue;
                }

                if (TypeComparer.TryResolveClassName(
                        name, objectSymbol, context.SymbolTree, context.GlobalScope)
                    is not { } resolved)
                {
                    ReportUnresolvedTypeName(name, unresolvedCode, state, diagnostics);
                }
                else if (checkTargetKinds && resolved.ObjectKind != PhpTypeDeclType.Interface)
                {
                    if (isInterface)
                    {
                        ReportWrongKindTypeName(
                            name,
                            wrongKindCode,
                            state,
                            diagnostics,
                            ObjectKindDisplayName(resolved.ObjectKind),
                            ObjectKindDisplayName(PhpTypeDeclType.Interface));
                    }
                    else
                    {
                        ReportWrongKindTypeName(
                            name,
                            wrongKindCode,
                            state,
                            diagnostics,
                            ObjectKindDisplayName(resolved.ObjectKind));
                    }
                }
            }

            if (isInterface && InterfaceInheritanceIsCircular(objectSymbol, context))
            {
                var reportAt = objectType.Implements.GetAllNotNull().FirstOrDefault()
                    ?? (IBase2Ast)objectType;
            CheckerHelpers.ReportError(
                diagnostics,
                state,
                reportAt,
                MessageCode.BinderCircularInheritance,
                objectSymbol.Name);
            }
        }

        private static bool TryReportObjectShapeHeritage(
            IClassName name,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var scope = objectSymbol.ContainingScope ?? context.GlobalScope;
            if (!ObjectShapeSupport.TryResolveObjectShapeAlias(
                    name,
                    scope,
                    context.SymbolTree,
                    out var alias))
            {
                return false;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                name,
                MessageCode.CheckerObjectShapeUsedAsClass,
                alias.Name);
            return true;
        }

        /// <summary>
        /// PHP: only <c>\Iterator</c> and <c>\IteratorAggregate</c> may extend <c>\Traversable</c>.
        /// User <c>.tyhp</c> classes, enums, interfaces, and traits must not list Traversable in
        /// <c>extends</c>/<c>implements</c> (including the PHP 8 duplicate
        /// <c>implements \Iterator, \Traversable</c>). Harvested <c>.tyhpdef</c> shells are a
        /// different AST (<see cref="Tyhp.TyhpLang.Ast.TyhpdefImportObjectDeclAst"/>) reached
        /// through <c>CheckTyhpdefImportObject</c>, not this method, so Layer 1/2 dumps stay honest
        /// without any attribute check. <c>#[\Tyhp\Php]</c> is an ordinary user-facing version gate
        /// (see <c>PhpVersionRule</c>) — it never marks a real <see cref="PhpObjectTypeDeclAst"/> as
        /// harvest output, so it must not exempt a genuine <c>.tyhp</c> declaration here.
        /// </summary>
        private static void CheckTraversableListing(
            PhpObjectTypeDeclAst objectType,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (string.Equals(objectType.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase)
                || IsEngineIteratorFamily(objectSymbol))
            {
                return;
            }

            foreach (var name in EnumerateWrittenHeritageNames(objectType, objectSymbol.ObjectKind))
            {
                if (TypeComparer.TryResolveClassName(
                        name, objectSymbol, context.SymbolTree, context.GlobalScope)
                    is not { } resolved
                    || !IsGlobalEngineInterface(resolved, "Traversable"))
                {
                    continue;
                }

                var display = objectType.IsAnonymousClass
                    ? "anonymous class"
                    : objectSymbol.Name;
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    name,
                    MessageCode.CheckerTraversableCannotBeListed,
                    display);
            }
        }

        /// <summary>
        /// PHP: userland types cannot implement or extend <c>\UnitEnum</c> / <c>\BackedEnum</c>.
        /// User <c>.tyhp</c> enums must not list them in <c>implements</c> (already implied).
        /// Only the engine <c>\BackedEnum</c> interface may extend <c>\UnitEnum</c>. Harvested
        /// <c>.tyhpdef</c> shells are a different AST
        /// (<see cref="Tyhp.TyhpLang.Ast.TyhpdefImportObjectDeclAst"/>) reached through
        /// <c>CheckTyhpdefImportObject</c>, not this method. <c>#[\Tyhp\Php]</c> never exempts a
        /// genuine <see cref="PhpObjectTypeDeclAst"/>.
        /// </summary>
        private static void CheckEngineEnumListing(
            PhpObjectTypeDeclAst objectType,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (string.Equals(objectType.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase)
                || IsEngineBackedEnumInterface(objectSymbol))
            {
                return;
            }

            foreach (var name in EnumerateWrittenHeritageNames(objectType, objectSymbol.ObjectKind))
            {
                if (TypeComparer.TryResolveClassName(
                        name, objectSymbol, context.SymbolTree, context.GlobalScope)
                    is not { } resolved
                    || !IsEngineEnumInterface(resolved))
                {
                    continue;
                }

                var display = objectType.IsAnonymousClass
                    ? "anonymous class"
                    : objectSymbol.Name;
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    name,
                    MessageCode.CheckerEngineEnumCannotBeListed,
                    display,
                    FormatGlobalEngineInterfaceName(resolved));
            }
        }

        private static IEnumerable<IClassName> EnumerateWrittenHeritageNames(
            PhpObjectTypeDeclAst objectType,
            PhpTypeDeclType declKind)
        {
            // Interfaces store `extends` in Implements. Trait `extends` is a class requirement
            // (kind rules skip traits), so a written Traversable parent still has to be rejected.
            if (declKind == PhpTypeDeclType.Trait && objectType.Extends is { } traitExtends)
            {
                yield return traitExtends;
            }

            if (objectType.Implements is null)
            {
                yield break;
            }

            foreach (var name in objectType.Implements.GetAllNotNull())
            {
                yield return name;
            }
        }

        private static bool IsEngineIteratorFamily(ObjectDeclarationSymbol symbol) =>
            IsGlobalEngineInterface(symbol, "Iterator")
            || IsGlobalEngineInterface(symbol, "IteratorAggregate");

        private static bool IsEngineEnumInterface(ObjectDeclarationSymbol symbol) =>
            IsGlobalEngineInterface(symbol, "UnitEnum")
            || IsGlobalEngineInterface(symbol, "BackedEnum");

        private static bool IsEngineBackedEnumInterface(ObjectDeclarationSymbol symbol) =>
            IsGlobalEngineInterface(symbol, "BackedEnum");

        private static string FormatGlobalEngineInterfaceName(ObjectDeclarationSymbol symbol)
        {
            var fqn = string.IsNullOrEmpty(symbol.FullyQualifiedName)
                ? symbol.Name
                : symbol.FullyQualifiedName;
            return "\\" + fqn.TrimStart('\\');
        }

        private static bool IsGlobalEngineInterface(ObjectDeclarationSymbol symbol, string name)
        {
            var fqn = string.IsNullOrEmpty(symbol.FullyQualifiedName)
                ? symbol.Name
                : symbol.FullyQualifiedName;
            return string.Equals(fqn.TrimStart('\\'), name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Real inheritance <c>extends</c> expects a matching kind. Classes extend classes;
        /// interfaces put their bases on <c>Implements</c>. Traits and enums do not use this path
        /// for kind checking (traits are requirements; enums have no parent class).
        /// </summary>
        private static bool TryGetExpectedExtendsKind(
            PhpTypeDeclType declarerKind,
            out PhpTypeDeclType expectedKind)
        {
            if (declarerKind == PhpTypeDeclType.Class)
            {
                expectedKind = PhpTypeDeclType.Class;
                return true;
            }

            expectedKind = default;
            return false;
        }

        private static string ObjectKindDisplayName(PhpTypeDeclType kind) =>
            kind switch
            {
                PhpTypeDeclType.Class => "class",
                PhpTypeDeclType.Interface => "interface",
                PhpTypeDeclType.Trait => "trait",
                PhpTypeDeclType.Enum => "enum",
                _ => kind.ToString().ToLowerInvariant(),
            };

        private static void ReportUnresolvedTypeName(
            IClassName name,
            MessageCode code,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var display = TypeComparer.GetClassNameText(name)
                ?? name.Identifier
                ?? "?";
            CheckerHelpers.ReportError(diagnostics, state, name, code, display);
        }

        private static void ReportWrongKindTypeName(
            IClassName name,
            MessageCode code,
            CheckerState state,
            DiagnosticBag diagnostics,
            params object[] kindArgs)
        {
            var display = TypeComparer.GetClassNameText(name)
                ?? name.Identifier
                ?? "?";
            var args = new object[kindArgs.Length + 1];
            args[0] = display;
            kindArgs.CopyTo(args, 1);
            CheckerHelpers.ReportError(diagnostics, state, name, code, args);
        }

        /// <summary>
        /// True when walking <paramref name="objectSymbol"/>'s single-parent chain re-enters a type
        /// already on the path (self-extends, two-class cycles, longer cycles).
        /// </summary>
        private static bool ClassInheritanceIsCircular(
            ObjectDeclarationSymbol objectSymbol,
            CheckerRuleContext context)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            for (var current = objectSymbol;
                 current is not null;
                 current = TypeComparer.TryGetParentDeclaration(
                     current, context.SymbolTree, context.GlobalScope))
            {
                if (!visited.Add(current))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when an interface's <c>extends</c> graph contains a cycle. Cycle detection needs a
        /// path set rather than a plain visited set, or diamond inheritance would read as a cycle.
        /// <paramref name="objectSymbol"/> alone re-entering an interface is not enough, though:
        /// re-walking every path through a diamond is exponential in its depth, so an interface whose
        /// whole reachable graph has been explored without finding a back edge is also recorded and
        /// never entered again (standard grey/black DFS).
        /// </summary>
        private static bool InterfaceInheritanceIsCircular(
            ObjectDeclarationSymbol objectSymbol,
            CheckerRuleContext context)
        {
            var path = new HashSet<ObjectDeclarationSymbol> { objectSymbol };
            var provenAcyclic = new HashSet<ObjectDeclarationSymbol>();
            return Walk(objectSymbol);

            bool Walk(ObjectDeclarationSymbol current)
            {
                foreach (var parent in TypeComparer.ResolveImplementedInterfaces(
                             current, context.SymbolTree, context.GlobalScope))
                {
                    if (parent.ObjectKind != PhpTypeDeclType.Interface
                        || provenAcyclic.Contains(parent))
                    {
                        continue;
                    }

                    if (!path.Add(parent))
                    {
                        return true;
                    }

                    if (Walk(parent))
                    {
                        return true;
                    }

                    path.Remove(parent);
                    provenAcyclic.Add(parent);
                }

                return false;
            }
        }

        private static void CheckExtendsNotFinal(
            PhpObjectTypeDeclAst objectType,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // ExtendsType is usually null for a raw `extends` class name; resolve via the AST fallback.
            if (TypeComparer.TryGetParentDeclaration(objectSymbol, context.SymbolTree, context.GlobalScope)
                is ObjectDeclarationSymbol parent
                && (parent.Visibility & MemberModifier.Final) != 0)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, objectType, MessageCode.CheckerFinalClassExtended, parent.Name);
            }
        }

        private static void CheckInheritedAbstractMethods(
            PhpObjectTypeDeclAst objectType,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if ((state.Modifiers & MemberModifier.Abstract) != 0)
            {
                return;
            }

            var operatorMethodNames = CollectGeneratedOperatorMethodNames(objectType);

            foreach (var abstractMethod in CollectAbstractMethods(objectSymbol, context))
            {
                if (!ImplementsMethod(objectSymbol, abstractMethod.Name, context)
                    && !operatorMethodNames.Contains(abstractMethod.Name))
                {
                    var declaringClass =
                        (abstractMethod.ContainingScope as ObjectDeclarationScope)?.DeclarationSymbol?.Name
                        ?? objectSymbol.Name;
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        objectType,
                        MessageCode.CheckerAbstractMethodNotImplemented,
                        objectSymbol.Name,
                        abstractMethod.Name,
                        declaringClass);
                }
            }
        }

        private static void CheckInterfaceImplementation(
            PhpObjectTypeDeclAst objectType,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Abstract classes may leave interface methods for a concrete descendant — same rule as
            // CheckInheritedAbstractMethods.
            if ((state.Modifiers & MemberModifier.Abstract) != 0)
            {
                return;
            }

            var operatorMethodNames = CollectGeneratedOperatorMethodNames(objectType);

            // A method name is only missing when *no* reachable interface supplies a default body for
            // it, so the whole interface set has to be collected before anything is reported. One
            // diagnostic per name even when several interfaces declare it; the first declaring
            // interface is named.
            var required = new List<(string Name, string DeclaringInterface)>();
            var seenRequired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var defaulted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var interfaceSymbol in CollectImplementedInterfaces(objectSymbol, context))
            {
                foreach (var method in interfaceSymbol.Members.Values.OfType<ObjectMethodSymbol>())
                {
                    if (method.Name is "__construct" or "__destruct")
                    {
                        continue;
                    }

                    // Private interface methods are shared helpers inside the interface; PHP does not
                    // require the implementing class to declare them, and they are not inherited, so
                    // they cannot satisfy another interface's requirement either.
                    if ((method.Visibility & MemberModifier.Private) != 0)
                    {
                        continue;
                    }

                    // A method with a body is a default implementation the class inherits.
                    if (InterfaceMethodHasDefaultBody(method))
                    {
                        defaulted.Add(method.Name);
                        continue;
                    }

                    if (seenRequired.Add(method.Name))
                    {
                        required.Add((method.Name, interfaceSymbol.Name));
                    }
                }
            }

            foreach (var (name, declaringInterface) in required)
            {
                if (defaulted.Contains(name)
                    || operatorMethodNames.Contains(name)
                    || ImplementsMethod(objectSymbol, name, context))
                {
                    continue;
                }

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    objectType,
                    MessageCode.CheckerInterfaceMethodNotImplemented,
                    objectSymbol.Name,
                    name,
                    declaringInterface);
            }
        }

        /// <summary>
        /// Every interface reachable from <paramref name="objectSymbol"/> — declared on it, inherited
        /// through a base class, or extended by another interface. <c>ImplementsTypes</c> alone is
        /// usually empty (raw <c>IClassName</c> nodes), so this walks
        /// <see cref="TypeComparer.EnumerateDirectAncestors"/>.
        /// </summary>
        private static IEnumerable<ObjectDeclarationSymbol> CollectImplementedInterfaces(
            ObjectDeclarationSymbol objectSymbol,
            CheckerRuleContext context)
        {
            var visited = new HashSet<ObjectDeclarationSymbol> { objectSymbol };
            var pending = new Queue<ObjectDeclarationSymbol>();
            pending.Enqueue(objectSymbol);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                foreach (var ancestor in TypeComparer.EnumerateDirectAncestors(
                             current, context.SymbolTree, context.GlobalScope))
                {
                    if (!visited.Add(ancestor))
                    {
                        continue;
                    }

                    // Trait names can appear in ImplementsTypes when a `use` clause happens to be an
                    // ITypeExpression. Their `implements` lists are *requirements* on the using class
                    // (checked by CheckTraitRequirements), not interfaces this type declares.
                    if (ancestor.ObjectKind == PhpTypeDeclType.Trait)
                    {
                        continue;
                    }

                    pending.Enqueue(ancestor);
                    if (ancestor.ObjectKind == PhpTypeDeclType.Interface)
                    {
                        yield return ancestor;
                    }
                }
            }
        }

        private static bool InterfaceMethodHasDefaultBody(ObjectMethodSymbol method) =>
            method.DeclaringAstNode is PhpMethodDeclAst { Body: not null };

        private static void CheckTraitRequirements(
            PhpObjectTypeDeclAst objectType,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Trait names live on `use` clauses as raw IClassName nodes — ImplementsTypes only records
            // the rare ITypeExpression case. ResolveUsedTraits is the same AST fallback used by
            // ImplementsMethod.
            var traits = TypeComparer.ResolveUsedTraits(
                objectSymbol, context.SymbolTree, context.GlobalScope, out _);
            var classType = CheckedTypes.FromSymbol(objectSymbol);

            foreach (var traitSymbol in traits)
            {
                if (TypeComparer.TryGetParentDeclaration(
                        traitSymbol, context.SymbolTree, context.GlobalScope)
                    is ObjectDeclarationSymbol requiredBaseSymbol)
                {
                    var requiredBase = CheckedTypes.FromSymbol(requiredBaseSymbol);
                    if (!TypeComparer.IsSubtypeOf(
                            classType, requiredBase, context.SymbolTree, context.GlobalScope))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics,
                            state,
                            objectType,
                            MessageCode.CheckerTraitRequirementNotMet,
                            traitSymbol.Name,
                            requiredBase.DisplayName,
                            objectSymbol.Name);
                    }
                }

                var seenInterfaces = new HashSet<ObjectDeclarationSymbol>();
                foreach (var requiredInterface in TypeComparer.ResolveImplementedInterfaces(
                             traitSymbol, context.SymbolTree, context.GlobalScope))
                {
                    if (requiredInterface.ObjectKind != PhpTypeDeclType.Interface
                        || !seenInterfaces.Add(requiredInterface))
                    {
                        continue;
                    }

                    var required = CheckedTypes.FromSymbol(requiredInterface);
                    if (!TypeComparer.IsSubtypeOf(
                            classType, required, context.SymbolTree, context.GlobalScope))
                    {
                        CheckerHelpers.ReportError(
                            diagnostics,
                            state,
                            objectType,
                            MessageCode.CheckerTraitRequirementImplNotMet,
                            traitSymbol.Name,
                            required.DisplayName,
                            objectSymbol.Name);
                    }
                }
            }
        }

        // Interface methods are deliberately not included: CheckInterfaceImplementation owns them, and
        // it has to see the whole interface set at once to honour default bodies.
        private static IEnumerable<ObjectMethodSymbol> CollectAbstractMethods(
            ObjectDeclarationSymbol objectSymbol,
            CheckerRuleContext context)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            for (var current = objectSymbol; current is not null; current = ResolveParent(current, context))
            {
                if (!visited.Add(current))
                {
                    break;
                }

                foreach (var member in current.Members.Values.OfType<ObjectMethodSymbol>())
                {
                    if (member.IsAbstract)
                    {
                        yield return member;
                    }
                }
            }
        }

        private static ObjectDeclarationSymbol? ResolveParent(
            ObjectDeclarationSymbol child,
            CheckerRuleContext context)
            => TypeComparer.TryGetParentDeclaration(child, context.SymbolTree, context.GlobalScope);

        /// <summary>
        /// True when <paramref name="objectSymbol"/>, any ancestor, or a trait either of them uses
        /// provides <paramref name="methodName"/> — as a concrete declaration, a trait alias, or an
        /// operator overload's generated method. Neither inherited nor trait-provided members are
        /// flattened into <see cref="ObjectDeclarationSymbol.Members"/>, so the base chain and each
        /// level's <c>use</c> clauses must both be walked.
        /// </summary>
        private static bool ImplementsMethod(
            ObjectDeclarationSymbol objectSymbol,
            string methodName,
            CheckerRuleContext context)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            for (var current = objectSymbol;
                 current is not null && visited.Add(current);
                 current = TypeComparer.TryGetParentDeclaration(current, context.SymbolTree, context.GlobalScope))
            {
                if (ProvidesMethod(current, methodName)
                    || current.TraitMethodAliases?.ContainsKey(methodName) == true)
                {
                    return true;
                }

                var traits = TypeComparer.ResolveUsedTraits(
                    current, context.SymbolTree, context.GlobalScope, out var hasUnresolvedTrait);

                // An unresolvable trait may well carry the method; reporting it missing would reject
                // valid code, whereas staying quiet only loses a diagnostic PHP itself still raises.
                if (hasUnresolvedTrait || traits.Any(trait => ProvidesMethod(trait, methodName)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ProvidesMethod(ObjectDeclarationSymbol declaration, string methodName) =>
            DeclaresConcreteMethod(declaration, methodName)
            || DeclaresGeneratedOperatorMethod(declaration, methodName);

        private static bool DeclaresConcreteMethod(ObjectDeclarationSymbol declaration, string methodName) =>
            declaration.Members.TryGetValue(methodName, out var member)
            && member is ObjectMethodSymbol { IsAbstract: false };

        // An operator overload's generated method (e.g. `operator convert(self): string` -> `__toString`)
        // is synthesized during emit and never becomes a member symbol, so an inherited or
        // trait-provided one is only visible on the declaring AST.
        private static bool DeclaresGeneratedOperatorMethod(
            ObjectDeclarationSymbol declaration,
            string methodName) =>
            declaration.DeclaringAstNode is PhpObjectTypeDeclAst declaringAst
            && CollectGeneratedOperatorMethodNames(declaringAst).Contains(methodName);

        private void CheckObjectBody(
            PhpClassBodyAst? body,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState objectState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (body is null)
            {
                return;
            }

            ValidateOperatorOverloadSet(body, objectState, diagnostics);

            // Prop-init #7: analyze the constructor before other instance methods so
            // MayBeUninitializedAfterConstruction is set before method-body reads are checked.
            var methods = new List<PhpMethodDeclAst>();
            PhpMethodDeclAst? constructor = null;
            foreach (var member in body.GetAllNotNull())
            {
                switch (member)
                {
                    case PhpMethodDeclAst method:
                        if (method.BoundSymbol is ObjectConstructorMethodSymbol)
                        {
                            constructor = method;
                        }
                        else
                        {
                            methods.Add(method);
                        }

                        break;
                    case PhpPropertyDeclAst property:
                        CheckProperty(property, objectState, context, diagnostics);
                        break;
                    case PhpEnumCaseAst enumCase:
                        CheckEnumCase(enumCase, objectSymbol, objectState, context, diagnostics);
                        break;
                    case PhpConstDeclListAst constList:
                        CheckClassConstants(constList, objectState, context, diagnostics);
                        break;
                    default:
                        context.CheckNode(member, objectState);
                        break;
                }
            }

            if (constructor is not null)
            {
                CheckMethod(constructor, objectState, context, diagnostics);
            }
            else
            {
                PropertyInitializationAnalysis.RecordPostConstructionState(
                    objectSymbol,
                    constructorFinalState: null,
                    context.SymbolTree,
                    context.GlobalScope);
            }

            foreach (var method in methods)
            {
                CheckMethod(method, objectState, context, diagnostics);
            }
        }

        // Story 11 §8 redesign: operator overloads generate deterministic static method names, so a
        // class may not also declare a real method with that name (reserved-name conflict), and all
        // forms of one operator must be mutually distinguishable by operand type.
        private static void ValidateOperatorOverloadSet(
            PhpClassBodyAst body,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var operators = new List<TyhpOperatorOverloadAst>();
            // PHP method names are case-insensitive; reserve generated names the same way.
            var methodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in body.GetAllNotNull())
            {
                switch (member)
                {
                    case TyhpOperatorOverloadAst op:
                        operators.Add(op);
                        break;
                    case PhpMethodDeclAst method when !string.IsNullOrEmpty(method.Identifier):
                        methodNames.Add(method.Identifier!);
                        break;
                }
            }

            if (operators.Count == 0)
            {
                return;
            }

            var byGeneratedName = new Dictionary<string, List<TyhpOperatorOverloadAst>>(StringComparer.Ordinal);
            foreach (var op in operators)
            {
                var name = GetGeneratedOperatorMethodName(op);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // `extension operator ... => expr` (tyhpdef inline extension mapping) is a thin,
                // always-erased mapping onto an already-declared real method (see
                // Checker/technical-guide.md "Tyhpdef class-body thin mappings ... are always
                // erased") — it never synthesizes a method, so reusing that method's name is the
                // intended idiom, not a collision. Only a native operator (which does synthesize a
                // backing method) reserves its generated name.
                if (!op.IsInlineExtension && methodNames.Contains(name))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, op, MessageCode.CheckerMagicMethodSignature, name,
                        "an operator overload reserves this method name; remove the conflicting method");
                }

                if (!byGeneratedName.TryGetValue(name, out var list))
                {
                    list = new List<TyhpOperatorOverloadAst>();
                    byGeneratedName[name] = list;
                }

                list.Add(op);
            }

            foreach (var forms in byGeneratedName.Values)
            {
                for (var i = 0; i < forms.Count; i++)
                {
                    for (var j = i + 1; j < forms.Count; j++)
                    {
                        if (OperatorFormsAmbiguous(forms[i], forms[j]))
                        {
                            ReportAmbiguousOperatorForm(diagnostics, state, forms[j]);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Convert-from forms in one extension share a single <c>__from</c>, including forms
        /// declared on different nested <c>extends</c> groups. Two whose source types overlap
        /// are the same TYHP4074 ambiguity as class-owned operator forms. Convert-to forms
        /// (<c>self</c> / <c>static</c> operand) are omitted: each group's <c>self</c> is a
        /// different target, so the generated <c>__to…</c> guards stay distinct.
        /// </summary>
        internal static void ReportAmbiguousConvertFromForms(
            IReadOnlyList<TyhpOperatorOverloadAst> operators,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var fromForms = new List<TyhpOperatorOverloadAst>();
            foreach (var op in operators)
            {
                if (IsConvertFromForm(op))
                {
                    fromForms.Add(op);
                }
            }

            for (var i = 0; i < fromForms.Count; i++)
            {
                for (var j = i + 1; j < fromForms.Count; j++)
                {
                    if (OperatorFormsAmbiguous(fromForms[i], fromForms[j]))
                    {
                        ReportAmbiguousOperatorForm(diagnostics, state, fromForms[j]);
                    }
                }
            }
        }

        private static void ReportAmbiguousOperatorForm(
            DiagnosticBag diagnostics,
            CheckerState state,
            TyhpOperatorOverloadAst form)
        {
            CheckerHelpers.ReportError(
                diagnostics, state, form, MessageCode.CheckerMagicMethodSignature,
                form.Op?.ValueString ?? "operator",
                "operator overload forms are ambiguous; operand types must be mutually distinguishable");
        }

        private static bool IsConvertFromForm(TyhpOperatorOverloadAst op)
        {
            var isUnary = op.RightParameter is null;
            var opEnum = OverloadableOperatorHelper.FromToken(
                (int)(op.Op?.ValueInt64 ?? -1), op.Op?.ValueString ?? string.Empty, isAlternateKind: isUnary);
            return opEnum == OverloadableOperator.Convert && !IsSelfTypeName(op.LeftParameter?.Type);
        }

        // Story 11 §8 redesign: an operator overload introduces a hidden generated method (e.g.
        // `operator convert(self): int` -> `__toInt`). That hidden method satisfies interface
        // conformance (IntConvertible) and abstract-method requirements even though no plain method
        // by that name is written, so conformance checks must consider these names.
        private static HashSet<string> CollectGeneratedOperatorMethodNames(PhpObjectTypeDeclAst objectType)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (objectType.Body is null)
            {
                return names;
            }

            foreach (var member in objectType.Body.GetAllNotNull())
            {
                if (member is TyhpOperatorOverloadAst op
                    && GetGeneratedOperatorMethodName(op) is { Length: > 0 } name)
                {
                    names.Add(name);
                }
            }

            return names;
        }

        private static string GetGeneratedOperatorMethodName(TyhpOperatorOverloadAst op)
        {
            var isUnary = op.RightParameter is null;
            var opEnum = OverloadableOperatorHelper.FromToken(
                (int)(op.Op?.ValueInt64 ?? -1), op.Op?.ValueString ?? string.Empty, isAlternateKind: isUnary);
            if (opEnum == OverloadableOperator.Invalid)
            {
                return string.Empty;
            }

            if (opEnum == OverloadableOperator.Convert)
            {
                return IsSelfTypeName(op.LeftParameter?.Type)
                    ? OperatorMethodNameGenerator.GetConvertToMethodName(GetOperatorTypeName(op.ReturnType))
                    : OperatorMethodNameGenerator.ConvertFromMethodName;
            }

            return OperatorMethodNameGenerator.GetMethodName(opEnum);
        }

        private static bool OperatorFormsAmbiguous(TyhpOperatorOverloadAst a, TyhpOperatorOverloadAst b)
        {
            var aUnary = a.RightParameter is null;
            var bUnary = b.RightParameter is null;
            var aEnum = OverloadableOperatorHelper.FromToken(
                (int)(a.Op?.ValueInt64 ?? -1), a.Op?.ValueString ?? string.Empty, isAlternateKind: aUnary);
            var bEnum = OverloadableOperatorHelper.FromToken(
                (int)(b.Op?.ValueInt64 ?? -1), b.Op?.ValueString ?? string.Empty, isAlternateKind: bUnary);
            if (aEnum != bEnum)
            {
                return false;
            }

            if (aEnum == OverloadableOperator.Convert)
            {
                var aTo = IsSelfTypeName(a.LeftParameter?.Type);
                var bTo = IsSelfTypeName(b.LeftParameter?.Type);
                if (aTo != bTo)
                {
                    return false;
                }

                // Two convert-to forms to the same target collapse to one method (already grouped);
                // two convert-from forms are ambiguous when their source types overlap.
                return aTo || OperatorTypesOverlap(a.LeftParameter?.Type, b.LeftParameter?.Type);
            }

            if (aUnary && bUnary)
            {
                return OperatorTypesOverlap(a.LeftParameter?.Type, b.LeftParameter?.Type);
            }

            return OperatorTypesOverlap(a.LeftParameter?.Type, b.LeftParameter?.Type)
                && OperatorTypesOverlap(a.RightParameter?.Type, b.RightParameter?.Type);
        }

        private static bool OperatorTypesOverlap(ITypeExpression? a, ITypeExpression? b)
        {
            var atomsA = CollectTypeAtoms(a).ToList();
            var atomsB = CollectTypeAtoms(b).ToList();
            if (atomsA.Count == 0 || atomsB.Count == 0)
            {
                return false;
            }

            if (atomsA.Contains("mixed") || atomsB.Contains("mixed"))
            {
                return true;
            }

            return atomsA.Any(x => atomsB.Contains(x, StringComparer.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> CollectTypeAtoms(ITypeExpression? type)
        {
            switch (type)
            {
                case null:
                    yield break;
                case PhpTypeExpressionAst composite:
                    foreach (var member in composite.Types?.GetAllNotNull() ?? [])
                    {
                        if (member is ITypeExpression inner)
                        {
                            foreach (var atom in CollectTypeAtoms(inner))
                            {
                                yield return atom;
                            }
                        }
                    }

                    yield break;
                default:
                    var name = GetOperatorTypeName(type);
                    if (!string.IsNullOrEmpty(name)
                        && !string.Equals(name, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return NormalizeAtomName(name!);
                    }

                    yield break;
            }
        }

        private static string NormalizeAtomName(string name)
        {
            var trimmed = name.Trim().TrimStart('\\');
            if (string.Equals(trimmed, "self", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "static", StringComparison.OrdinalIgnoreCase))
            {
                return "self";
            }

            return trimmed.Split('\\')[^1].ToLowerInvariant();
        }

        private static bool IsSelfTypeName(ITypeExpression? type)
        {
            var name = GetOperatorTypeName(type);
            return name is not null
                && (string.Equals(name, "self", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "static", StringComparison.OrdinalIgnoreCase));
        }

        private static string? GetOperatorTypeName(ITypeExpression? typeExpr) =>
            typeExpr switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNamedTypeAst named => named.Name?.ValueString ?? named.Name?.Identifier,
                PhpTypeExpressionAst composite =>
                    composite.Types?.GetAllNotNull().FirstOrDefault() is ITypeExpression inner
                        ? GetOperatorTypeName(inner)
                        : null,
                _ => null,
            };

        private static void CheckEnumDeclaration(
            PhpObjectTypeDeclAst objectType,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            _ = context;
            if (objectType.BackingType is null)
            {
                return;
            }

            var backingName = GetEnumBackingTypeName(objectType.BackingType);
            if (backingName is not ("int" or "string"))
            {
                return;
            }

            var seenValues = new Dictionary<string, PhpEnumCaseAst>(StringComparer.Ordinal);
            foreach (var member in objectSymbol.Constants.Values)
            {
                if (member is not ObjectConstantSymbol enumCase)
                {
                    continue;
                }

                if (enumCase.DeclaringAstNode is PhpEnumCaseAst caseAst)
                {
                    ValidateEnumCase(caseAst, objectSymbol, backingName, seenValues, state, context, diagnostics);
                }
            }
        }

        private static string? GetEnumBackingTypeName(ITypeExpression backing)
        {
            switch (backing)
            {
                case PhpBuiltinTypeAst builtin when !string.IsNullOrEmpty(builtin.Identifier):
                    return builtin.Identifier.ToLowerInvariant();
                case PhpNamedTypeAst named:
                {
                    var text = named.Name is PhpNameAst name
                        ? name.ValueString
                        : named.Name?.Identifier;
                    return string.IsNullOrEmpty(text) ? null : text.ToLowerInvariant();
                }
                case PhpTypeExpressionAst typeExpr:
                    foreach (var member in typeExpr.Types?.GetAllNotNull() ?? [])
                    {
                        var inner = GetEnumBackingTypeName(member);
                        if (inner is "int" or "string")
                        {
                            return inner;
                        }
                    }

                    break;
            }

            return string.IsNullOrEmpty(backing.Identifier)
                ? null
                : backing.Identifier.ToLowerInvariant();
        }

        private static void CheckEnumCase(
            PhpEnumCaseAst enumCase,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Same bypass as methods and properties: CheckObjectBody calls us instead of CheckNode,
            // so nothing else validates the case's attributes or walks them for ImportRule.
            AttributeRule.ValidateDeclarationAttributes(enumCase, state, context, diagnostics);
            context.ValidatePhpVersionMember(enumCase, state);
            context.CheckAttributes(enumCase, state);

            var caseModifierList = enumCase.AstGrammarAddons.TryGetValue("modifiers", out var caseModAddon)
                ? caseModAddon as PhpModifierListAst
                : null;
            var caseModifiers = CheckerHelpers.ToMemberModifiers(caseModifierList);
            if (CheckerHelpers.CountVisibilityModifiers(caseModifiers) > 1)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    enumCase,
                    MessageCode.CheckerMultipleVisibilities,
                    enumCase.Name?.ValueString ?? enumCase.Identifier);
            }

            var isBacked = objectSymbol.DeclaringAstNode is PhpObjectTypeDeclAst { BackingType: not null };
            if (isBacked && enumCase.Value is null)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, enumCase, MessageCode.CheckerEnumCaseMissingValue, enumCase.Name?.ValueString ?? "");
            }
            else if (!isBacked && enumCase.Value is not null)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    enumCase,
                    MessageCode.CheckerEnumCaseValueOnNonBacked,
                    enumCase.Name?.ValueString ?? "");
            }

            // Backed-enum case value validation (constant-ness, backing-type compatibility,
            // and duplicate detection) is performed once in CheckEnumDeclaration using a shared
            // value set; re-running it here would emit duplicate diagnostics.
        }

        private static void ValidateEnumCase(
            PhpEnumCaseAst enumCase,
            ObjectDeclarationSymbol objectSymbol,
            string backingName,
            Dictionary<string, PhpEnumCaseAst> seenValues,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (enumCase.Value is not null && !CheckerHelpers.IsConstantExpression(enumCase.Value, state))
            {
                CheckerHelpers.ReportError(diagnostics, state, enumCase, MessageCode.CheckerNonConstantExpression);
            }

            if (enumCase.Value is not null)
            {
                var valueType = context.ResolveExpressionType(enumCase.Value, state);
                var expected = backingName == "string" ? CheckedTypes.String : CheckedTypes.Int;
                if (!TypeComparer.IsAssignableTo(valueType, expected, context.SymbolTree, context.GlobalScope))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, enumCase, MessageCode.CheckerEnumCaseTypeMismatch,
                        valueType.DisplayName, backingName);
                }

                var valueKey = GetEnumCaseValueKey(enumCase.Value);
                if (valueKey is not null)
                {
                    if (seenValues.TryGetValue(valueKey, out var firstCase))
                    {
                        var fileName = CheckerHelpers.ResolveDiagnosticFileName(state, enumCase);
                        diagnostics.AddDuplicateFromAst(
                            MessageCode.CheckerEnumCaseDuplicateValue,
                            enumCase,
                            fileName,
                            firstCase,
                            fileName,
                            valueKey);
                    }
                    else
                    {
                        seenValues[valueKey] = enumCase;
                    }
                }
            }
        }

        private static string? GetEnumCaseValueKey(IExpression value) =>
            value switch
            {
                PhpScalarAst scalar => $"{scalar.ScalarType}:{scalar.ValueString ?? scalar.ValueInt64?.ToString()}",
                PhpNameAst name => $"name:{name.ValueString}",
                TokenValueAst token => $"token:{token.ValueString ?? token.ValueInt64?.ToString()}",
                _ => null,
            };

        private static void CheckClassConstants(
            PhpConstDeclListAst constList,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            // Attributes are attached to the `const` statement, not to each declared name, and
            // CheckNode below only sees the individual names.
            AttributeRule.ValidateDeclarationAttributes(constList, state, context, diagnostics);
            context.ValidatePhpVersionMember(constList, state);
            context.CheckAttributes(constList, state);

            var firstConst = constList.GetAllNotNull().FirstOrDefault();
            var constModifiers = CheckerHelpers.ToMemberModifiers(firstConst?.Modifiers);
            if (CheckerHelpers.CountVisibilityModifiers(constModifiers) > 1)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    constList,
                    MessageCode.CheckerMultipleVisibilities,
                    firstConst?.Identifier ?? constList.Identifier);
            }

            foreach (var constant in constList.GetAllNotNull())
            {
                if (constant.Value is not null && !CheckerHelpers.IsConstantExpression(constant.Value, state))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, constant, MessageCode.CheckerNonConstantExpression);
                }

                CheckClassConstantType(constant, state, context, diagnostics);
                context.CheckNode(constant, state);
            }
        }

        private static void CheckClassConstantType(
            PhpConstDeclAst constant,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (!string.Equals(constant.LanguageMode, "tyhp", StringComparison.Ordinal)
                && !string.Equals(constant.LanguageMode, "tyhpdef", StringComparison.Ordinal))
            {
                return;
            }

            var name = constant.Identifier ?? "";
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var inherited = TryFindInheritedConstant(name, state, context);
            ICheckedType? inheritedType = null;
            if (inherited?.DeclaredType is not null)
            {
                var receiverType = state.EnclosingObjectType
                    ?? (state.EnclosingObject is not null
                        ? CheckedTypes.FromSymbol(state.EnclosingObject)
                        : CheckedTypes.Unresolved);
                inheritedType = context.ResolveMemberDeclaredType(
                    inherited.DeclaredType, receiverType, state);
            }

            ICheckedType? declaredType = null;
            if (constant.Type is not null)
            {
                declaredType = context.ResolveTypeAnnotation(constant.Type, state);
            }

            if (declaredType is not null
                && inheritedType is not null
                && declaredType.Kind != CheckedTypeKind.Unresolved
                && inheritedType.Kind != CheckedTypeKind.Unresolved
                && !TypeComparer.AreTypesEqual(declaredType, inheritedType))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    (IBase2Ast?)constant.Type ?? constant,
                    MessageCode.CheckerClassConstTypeMismatch,
                    name,
                    declaredType.DisplayName,
                    inheritedType.DisplayName);
            }
            else if (declaredType is null && inheritedType is null)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    constant,
                    MessageCode.CheckerClassConstTypeRequired,
                    name);
            }
            else if (declaredType is null
                && inherited?.DeclaredType is not null
                && state.EnclosingObject is { } owner
                && owner.TryGetConstant(name, out var member)
                && member is ObjectConstantSymbol childSymbol)
            {
                childSymbol.DeclaredType = inherited.DeclaredType;
            }

            var effectiveType = declaredType ?? inheritedType;
            if (effectiveType is not null
                && constant.Value is not null
                && effectiveType.Kind != CheckedTypeKind.Unresolved)
            {
                var valueType = context.ResolveExpressionType(constant.Value, state);
                if (valueType.Kind != CheckedTypeKind.Unresolved
                    && !context.IsAssignable(valueType, effectiveType, state))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        constant,
                        MessageCode.CheckerTypeMismatch,
                        valueType.DisplayName,
                        effectiveType.DisplayName);
                }
            }
        }

        /// <summary>
        /// Nearest non-private ancestor (parent class, then interfaces, then traits) that declared
        /// the same constant with a type. Used to infer omitted child types and to enforce
        /// invariance.
        /// </summary>
        private static ObjectConstantSymbol? TryFindInheritedConstant(
            string name,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (state.EnclosingObject is not { } owner)
            {
                return null;
            }

            var visited = new HashSet<ObjectDeclarationSymbol> { owner };

            var parent = TypeComparer.TryGetParentDeclaration(
                owner, context.SymbolTree, context.GlobalScope);
            while (parent is not null && visited.Add(parent))
            {
                if (TryGetVisibleTypedConstant(parent, name) is { } fromParent)
                {
                    return fromParent;
                }

                parent = TypeComparer.TryGetParentDeclaration(
                    parent, context.SymbolTree, context.GlobalScope);
            }

            foreach (var iface in CollectImplementedInterfaces(owner, context))
            {
                if (!visited.Add(iface))
                {
                    continue;
                }

                if (TryGetVisibleTypedConstant(iface, name) is { } fromIface)
                {
                    return fromIface;
                }
            }

            foreach (var trait in TypeComparer.ResolveUsedTraits(
                         owner, context.SymbolTree, context.GlobalScope, out _))
            {
                if (!visited.Add(trait))
                {
                    continue;
                }

                if (TryGetVisibleTypedConstant(trait, name) is { } fromTrait)
                {
                    return fromTrait;
                }
            }

            return null;
        }

        private static ObjectConstantSymbol? TryGetVisibleTypedConstant(
            ObjectDeclarationSymbol type,
            string name)
        {
            if (!type.TryGetConstant(name, out var member)
                || member is not ObjectConstantSymbol constant
                || constant.IsEnumCase
                || (constant.Visibility & MemberModifier.Private) != 0
                || constant.DeclaredType is null)
            {
                return null;
            }

            return constant;
        }
    }
}
