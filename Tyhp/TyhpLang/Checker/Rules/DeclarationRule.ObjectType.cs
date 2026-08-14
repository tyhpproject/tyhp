using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
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
            if (objectType.BoundSymbol is not ObjectDeclarationSymbol objectSymbol)
            {
                return;
            }

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

            CheckObjectBody(objectType, objectSymbol, objectState, context, diagnostics);
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
            PhpObjectTypeDeclAst objectType,
            ObjectDeclarationSymbol objectSymbol,
            CheckerState objectState,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (objectType.Body is null)
            {
                return;
            }

            ValidateOperatorOverloadSet(objectType, objectState, diagnostics);

            // Prop-init #7: analyze the constructor before other instance methods so
            // MayBeUninitializedAfterConstruction is set before method-body reads are checked.
            var methods = new List<PhpMethodDeclAst>();
            PhpMethodDeclAst? constructor = null;
            foreach (var member in objectType.Body.GetAllNotNull())
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
            PhpObjectTypeDeclAst objectType,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (objectType.Body is null)
            {
                return;
            }

            var operators = new List<TyhpOperatorOverloadAst>();
            // PHP method names are case-insensitive; reserve generated names the same way.
            var methodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in objectType.Body.GetAllNotNull())
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

                if (methodNames.Contains(name))
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
                            CheckerHelpers.ReportError(
                                diagnostics, state, forms[j], MessageCode.CheckerMagicMethodSignature,
                                forms[j].Op?.ValueString ?? "operator",
                                "operator overload forms are ambiguous; operand types must be mutually distinguishable");
                        }
                    }
                }
            }
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

            var backingName = objectType.BackingType.Identifier?.ToLowerInvariant();
            if (backingName is not ("int" or "string"))
            {
                return;
            }

            var seenValues = new HashSet<string>(StringComparer.Ordinal);
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
            context.CheckAttributes(enumCase, state);

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
            HashSet<string> seenValues,
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
                if (valueKey is not null && !seenValues.Add(valueKey))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, enumCase, MessageCode.CheckerEnumCaseDuplicateValue, valueKey);
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
            context.CheckAttributes(constList, state);

            foreach (var constant in constList.GetAllNotNull())
            {
                if (constant.Value is not null && !CheckerHelpers.IsConstantExpression(constant.Value, state))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, constant, MessageCode.CheckerNonConstantExpression);
                }

                context.CheckNode(constant, state);
            }
        }
    }
}
