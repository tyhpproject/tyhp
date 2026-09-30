using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Versioning;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Story 20.5 Phase 5 — PHP version-gate diagnostics (4300–4306).
    /// Binder already reports 4301/4303/4304/4305 in many cases; this rule fills 4300/4302
    /// and double-checks the rest. Identical binder+checker reports de-dupe in
    /// <see cref="DiagnosticBag"/> (same code, span, and format args).
    /// Inactive <c>declare(php=)</c> / <c>#[\Tyhp\Php]</c> alternate variants are not 4302;
    /// 4302 is only nested gates that cannot overlap the enclosing constraint.
    /// </summary>
    public sealed class PhpVersionRule : ICheckerRule
    {
        private const string PhpDeclareKey = "php";
        private const string TyhpPhpAttributeFqn = "\\Tyhp\\Php";
        private const string VersionArgumentName = "version";

        private readonly Dictionary<GatedDeclarationKey, List<GatedDeclarationRecord>> _gatedDeclarations = new();
        private string _targetPhpVersion = CompilationOptions.DefaultPhpVersionWhenUnset;

        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpDeclareAst),
            typeof(PhpFunctionDeclAst),
            typeof(TyhpdefImportFunctionDeclAst),
            typeof(PhpObjectTypeDeclAst),
            typeof(TyhpStructDeclAst),
            typeof(TyhpExtensionDeclAst),
            typeof(TyhpdefStandaloneExtensionDeclAst),
            typeof(PhpMethodDeclAst),
            typeof(PhpPropertyDeclAst),
            typeof(PhpEnumCaseAst),
            typeof(PhpConstDeclListAst),
        ];

        public bool SuppressChildTraversal(IBase2Ast node) => node is PhpDeclareAst;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            RememberTarget(context);
            switch (node)
            {
                case PhpDeclareAst declareAst:
                    CheckDeclare(declareAst, state, context, diagnostics, walkBodyWithCheckNode: true);
                    break;
                case PhpFunctionDeclAst function:
                    ValidateFunction(function, state, diagnostics);
                    if (function.BoundSymbol is null && function.Body is not null)
                    {
                        ScanUnboundRegion(function.Body, state, diagnostics);
                    }

                    break;
                case TyhpdefImportFunctionDeclAst tyhpdefFunction:
                    ValidateDeclaration(
                        tyhpdefFunction,
                        state,
                        diagnostics,
                        tyhpdefFunction.Identifier ?? "",
                        "function",
                        illegalAttributeTarget: false);
                    ScanParameters(tyhpdefFunction.Parameters, state, diagnostics);
                    break;
                case PhpObjectTypeDeclAst objectType:
                    ValidateDeclaration(
                        objectType,
                        state,
                        diagnostics,
                        objectType.Identifier ?? "",
                        "type",
                        illegalAttributeTarget: false);
                    if (objectType.BoundSymbol is null && objectType.Body is not null)
                    {
                        ScanUnboundRegion(objectType.Body, PushObjectContainer(state, objectType), diagnostics);
                    }

                    break;
                case TyhpStructDeclAst structDecl:
                    ValidateDeclaration(
                        structDecl,
                        state,
                        diagnostics,
                        structDecl.Identifier ?? "",
                        "type",
                        illegalAttributeTarget: true);
                    break;
                case TyhpExtensionDeclAst extension:
                    ValidateDeclaration(
                        extension,
                        state,
                        diagnostics,
                        extension.Identifier ?? "",
                        "type",
                        illegalAttributeTarget: true);
                    break;
                case TyhpdefStandaloneExtensionDeclAst tyhpdefExt:
                    ValidateDeclaration(
                        tyhpdefExt,
                        state,
                        diagnostics,
                        tyhpdefExt.Identifier ?? "",
                        "type",
                        illegalAttributeTarget: true);
                    break;
                case PhpMethodDeclAst method:
                    ValidateMethod(method, state, diagnostics);
                    break;
                case PhpPropertyDeclAst property:
                    ValidateProperty(property, state, diagnostics);
                    break;
                case PhpEnumCaseAst enumCase:
                    ValidateEnumCase(enumCase, state, diagnostics);
                    break;
                case PhpConstDeclListAst constList:
                    ValidateConstList(constList, state, diagnostics);
                    break;
            }
        }

        private void RememberTarget(CheckerRuleContext context)
        {
            var version = context.Options.PhpVersion;
            _targetPhpVersion = string.IsNullOrWhiteSpace(version)
                ? CompilationOptions.DefaultPhpVersionWhenUnset
                : version.Trim();
        }

        private void CheckDeclare(
            PhpDeclareAst declareAst,
            CheckerState state,
            CheckerRuleContext? context,
            DiagnosticBag diagnostics,
            bool walkBodyWithCheckNode)
        {
            var directives = ReadDeclareDirectives(declareAst);
            var hasPhp = directives.Exists(d => IsPhpDeclareKey(d.Key));
            var phpAlone = TryGetAlonePhpConstraint(directives, out var phpConstraint);
            if (hasPhp && !phpAlone)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, declareAst, MessageCode.CheckerPhpVersionDeclareNotAlone);
            }

            var isBlock = IsDeclareBlockForm(declareAst);
            var constraintIsValid = false;
            var constraintSatisfied = false;
            if (phpAlone)
            {
                var evaluated = PhpVersionConstraint.Evaluate(_targetPhpVersion, phpConstraint);
                constraintIsValid = evaluated.ConstraintIsValid;
                constraintSatisfied = evaluated.IsSatisfied;
                if (!constraintIsValid)
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        declareAst,
                        MessageCode.CheckerPhpVersionInvalidConstraint,
                        phpConstraint);
                }
                else if (isBlock
                    && state.PhpVersionConstraintStack.Count > 0
                    && !PhpVersionConstraint.AnyOverlap(state.PhpVersionConstraintStack, [phpConstraint]))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        declareAst,
                        MessageCode.CheckerPhpVersionUnreachable,
                        phpConstraint);
                }
            }

            if (!isBlock || declareAst.Body is null || declareAst.Body is PhpNopStatementAst)
            {
                return;
            }

            var bodyState = state.Split(Enum.ScopeType.DeclareBlock);
            if (phpAlone && constraintIsValid)
            {
                bodyState.PhpVersionConstraintStack.Add(phpConstraint);
            }

            // Binder omitted unsatisfied / invalid bodies. Full CheckNode would type-check
            // unbound extensions (`$this` → 4097, gated PHP APIs → 4182). Nested 4300/4302/4305
            // still need ScanUnboundRegion even when the outer gate is inactive for this target.
            var binderInactive = declareAst.BoundSymbol is DeclareBlockSymbol
            {
                IsPhpVersionGateInactive: true
            };
            var constraintInactive = phpAlone && (!constraintIsValid || !constraintSatisfied);
            var skipFullCheck = binderInactive || constraintInactive;

            if (walkBodyWithCheckNode && context is not null && !skipFullCheck)
            {
                context.CheckNode(declareAst.Body, bodyState);

                if (context.IsPairedGateBlock(declareAst) || !NeedsRuntimeCheck(declareAst))
                {
                    // Straight-line: the gate was fully decided at compile time, or the caller joins
                    // this block with its complement. What it assigns or returns holds afterwards.
                    state.AbsorbJoinedVariables(bodyState);
                    state.HasReturnedOnAllPaths = bodyState.HasReturnedOnAllPaths;
                }
                else
                {
                    // Emitted as `if (<runtime check>) { … }` with no else: an `if` without `else`.
                    var notEntered = state.Split(Enum.ScopeType.CodeBlock);
                    if (bodyState.HasReturnedOnAllPaths)
                    {
                        state.AbsorbJoinedVariables(notEntered);
                    }
                    else
                    {
                        bodyState.Merge(notEntered);
                        state.AbsorbJoinedVariables(bodyState);
                    }

                    state.HasReturnedOnAllPaths = false;
                }
            }
            else
            {
                ScanUnboundRegion(declareAst.Body, bodyState, diagnostics);
            }
        }

        private void ValidateFunction(PhpFunctionDeclAst function, CheckerState state, DiagnosticBag diagnostics)
        {
            ValidateDeclaration(
                function,
                state,
                diagnostics,
                function.Identifier ?? "",
                "function",
                illegalAttributeTarget: false);
            ScanParameters(function.Parameters, state, diagnostics);
        }

        /// <summary>
        /// PHP cannot declare a property, class constant, enum case, or interface method
        /// conditionally, so <c>#[\Tyhp\Php]</c> on one in Tyhp source has nothing to emit (4371).
        /// tyhpdef members keep the attribute: the binder gates them at compile time.
        /// </summary>
        private static void RejectTyhpPhpOnUnconditionalMember(
            IBase2Ast node,
            IBase2Ast? extra,
            CheckerState state,
            DiagnosticBag diagnostics,
            string memberName)
        {
            if (IsTyhpdefLanguage(node, extra ?? node, state))
            {
                return;
            }

            foreach (var attribute in CollectTyhpPhpAttributes(node, extra is null ? [] : [extra]))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    attribute,
                    MessageCode.CheckerPhpVersionAttributeInvalidMember,
                    memberName);
            }
        }

        private void ValidateMethod(PhpMethodDeclAst method, CheckerState state, DiagnosticBag diagnostics)
        {
            if (state.EnclosingObject is { ObjectKind: Enum.PhpTypeDeclType.Interface })
            {
                RejectTyhpPhpOnUnconditionalMember(method, null, state, diagnostics, method.Identifier ?? "");
            }

            ValidateDeclaration(
                method,
                state,
                diagnostics,
                method.Identifier ?? "",
                "method",
                illegalAttributeTarget: false);
            ScanParameters(method.Parameters, state, diagnostics);
            if (method.BoundSymbol is null && method.Body is not null)
            {
                ScanUnboundRegion(method.Body, state, diagnostics);
            }
        }

        private void ValidateProperty(PhpPropertyDeclAst property, CheckerState state, DiagnosticBag diagnostics)
        {
            foreach (var prop in property.Properties?.GetAllNotNull() ?? [])
            {
                RejectTyhpPhpOnUnconditionalMember(prop, property, state, diagnostics, prop.Identifier ?? "");
                ValidateDeclaration(
                    prop,
                    state,
                    diagnostics,
                    prop.Identifier ?? "",
                    "property",
                    illegalAttributeTarget: false,
                    additionalAttributeSources: property);
                RejectTyhpPhpOnPropertyHooks(prop, property, state, diagnostics);
            }
        }

        /// <summary>
        /// Version gates on tyhpdef hooked properties belong on the property or an enclosing
        /// <c>declare(php=…)</c>, not on individual <c>get</c>/<c>set</c> (TYHP8016). Hook-level
        /// <c>#[\Tyhp\Php]</c> in Tyhp source is left alone (library tyhpdef generation skips copying it).
        /// </summary>
        private static void RejectTyhpPhpOnPropertyHooks(
            PhpPropertyAst prop,
            PhpPropertyDeclAst property,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (!IsTyhpdefLanguage(prop, property, state))
            {
                return;
            }

            foreach (var hook in prop.Hooks?.GetAllNotNull() ?? [])
            {
                foreach (var attribute in CollectTyhpPhpAttributes(hook, []))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        attribute,
                        MessageCode.TyhpdefPhpVersionGateOnPropertyHook);
                }
            }
        }

        private static bool IsTyhpdefLanguage(IBase2Ast node, IBase2Ast extra, CheckerState state)
        {
            if (string.Equals(node.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extra.LanguageMode, "tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var fileName = state.CurrentFileName ?? string.Empty;
            return fileName.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase);
        }

        private void ValidateEnumCase(PhpEnumCaseAst enumCase, CheckerState state, DiagnosticBag diagnostics)
        {
            var name = !string.IsNullOrEmpty(enumCase.Name?.Identifier)
                ? enumCase.Name!.Identifier
                : enumCase.Name?.ValueString ?? "";
            RejectTyhpPhpOnUnconditionalMember(enumCase, null, state, diagnostics, name);
            ValidateDeclaration(
                enumCase,
                state,
                diagnostics,
                name,
                "object-constant",
                illegalAttributeTarget: false);
        }

        private void ValidateConstList(PhpConstDeclListAst constList, CheckerState state, DiagnosticBag diagnostics)
        {
            var kind = state.EnclosingObject is not null ? "object-constant" : "constant";
            foreach (var constant in constList.GetAllNotNull())
            {
                if (state.EnclosingObject is not null)
                {
                    RejectTyhpPhpOnUnconditionalMember(constant, constList, state, diagnostics, constant.Identifier ?? "");
                }

                ValidateDeclaration(
                    constant,
                    state,
                    diagnostics,
                    constant.Identifier ?? "",
                    kind,
                    illegalAttributeTarget: false,
                    additionalAttributeSources: constList);
            }
        }

        private void ScanParameters(
            PhpParameterListAst? parameters,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (parameters is null)
            {
                return;
            }

            foreach (var parameter in parameters.GetAllNotNull())
            {
                ValidateDeclaration(
                    parameter,
                    state,
                    diagnostics,
                    name: "",
                    kind: "",
                    illegalAttributeTarget: false,
                    recordOverlap: false);
            }
        }

        /// <summary>
        /// Walks a region the binder omitted so nested 4300/4302/4305 still fire without
        /// dispatching the rest of the checker on unbound declarations.
        /// </summary>
        private void ScanUnboundRegion(IBase2Ast node, CheckerState state, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpDeclareAst declareAst:
                    CheckDeclare(declareAst, state, context: null, diagnostics, walkBodyWithCheckNode: false);
                    return;
                case PhpFunctionDeclAst function:
                    ValidateFunction(function, state, diagnostics);
                    if (function.Body is not null)
                    {
                        ScanUnboundRegion(function.Body, state, diagnostics);
                    }

                    return;
                case TyhpdefImportFunctionDeclAst tyhpdefFunction:
                    ValidateDeclaration(
                        tyhpdefFunction,
                        state,
                        diagnostics,
                        tyhpdefFunction.Identifier ?? "",
                        "function",
                        illegalAttributeTarget: false);
                    return;
                case PhpObjectTypeDeclAst objectType:
                    ValidateDeclaration(
                        objectType,
                        state,
                        diagnostics,
                        objectType.Identifier ?? "",
                        "type",
                        illegalAttributeTarget: false);
                    if (objectType.Body is not null)
                    {
                        ScanUnboundRegion(objectType.Body, PushObjectContainer(state, objectType), diagnostics);
                    }

                    return;
                case TyhpStructDeclAst structDecl:
                    ValidateDeclaration(
                        structDecl, state, diagnostics, structDecl.Identifier ?? "", "type",
                        illegalAttributeTarget: true);
                    return;
                case TyhpExtensionDeclAst extension:
                    ValidateDeclaration(
                        extension, state, diagnostics, extension.Identifier ?? "", "type",
                        illegalAttributeTarget: true);
                    if (extension.FunctionList is not null)
                    {
                        ScanUnboundRegion(
                            extension.FunctionList,
                            PushExtensionContainer(
                                state,
                                extension.Identifier,
                                extension.BoundSymbol as ObjectDeclarationSymbol),
                            diagnostics);
                    }

                    return;
                case TyhpdefStandaloneExtensionDeclAst tyhpdefExt:
                    ValidateDeclaration(
                        tyhpdefExt, state, diagnostics, tyhpdefExt.Identifier ?? "", "type",
                        illegalAttributeTarget: true);
                    if (tyhpdefExt.FunctionList is not null)
                    {
                        ScanUnboundRegion(
                            tyhpdefExt.FunctionList,
                            PushExtensionContainer(
                                state,
                                tyhpdefExt.Identifier,
                                tyhpdefExt.BoundSymbol as ObjectDeclarationSymbol),
                            diagnostics);
                    }

                    return;
                case PhpMethodDeclAst method:
                    ValidateMethod(method, state, diagnostics);
                    return;
                case PhpPropertyDeclAst property:
                    ValidateProperty(property, state, diagnostics);
                    return;
                case PhpEnumCaseAst enumCase:
                    ValidateEnumCase(enumCase, state, diagnostics);
                    return;
                case PhpConstDeclListAst constList:
                    ValidateConstList(constList, state, diagnostics);
                    return;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    ScanUnboundRegion(child, state, diagnostics);
                }
            }
        }

        private void ValidateDeclaration(
            IBase2Ast node,
            CheckerState state,
            DiagnosticBag diagnostics,
            string name,
            string kind,
            bool illegalAttributeTarget,
            bool recordOverlap = true,
            params IBase2Ast?[] additionalAttributeSources)
        {
            var attributes = CollectTyhpPhpAttributes(node, additionalAttributeSources);
            if (attributes.Count == 0)
            {
                if (recordOverlap && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(kind))
                {
                    RecordOverlap(
                        state, diagnostics, node, name, kind, [.. state.PhpVersionConstraintStack]);
                }

                return;
            }

            if (illegalAttributeTarget)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    attributes[0],
                    MessageCode.CheckerPhpVersionAttributeInvalidTarget);
                if (recordOverlap && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(kind))
                {
                    RecordOverlap(
                        state, diagnostics, node, name, kind, [.. state.PhpVersionConstraintStack]);
                }

                return;
            }

            var collected = new List<string>(state.PhpVersionConstraintStack);
            var anyArgumentInvalid = false;
            foreach (var attribute in attributes)
            {
                if (!TryReadTyhpPhpVersionArgument(attribute, out var version))
                {
                    anyArgumentInvalid = true;
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        attribute,
                        MessageCode.CheckerPhpVersionAttributeInvalidArgument);
                    continue;
                }

                var evaluated = PhpVersionConstraint.Evaluate(_targetPhpVersion, version);
                if (!evaluated.ConstraintIsValid)
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        attribute,
                        MessageCode.CheckerPhpVersionInvalidConstraint,
                        version);
                    continue;
                }

                if (collected.Count > 0
                    && !PhpVersionConstraint.AnyOverlap(collected, [version]))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        attribute,
                        MessageCode.CheckerPhpVersionUnreachable,
                        version);
                }

                collected.Add(version);
            }

            if (anyArgumentInvalid)
            {
                collected = [.. state.PhpVersionConstraintStack];
            }

            if (recordOverlap && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(kind))
            {
                RecordOverlap(state, diagnostics, node, name, kind, collected);
            }
        }

        private void RecordOverlap(
            CheckerState state,
            DiagnosticBag diagnostics,
            IBase2Ast node,
            string name,
            string kind,
            IReadOnlyList<string> effectiveConstraints)
        {
            var signature = PhpVersionGatedCallableSignature.FromDeclaration(node);
            var key = new GatedDeclarationKey(GetContainerKey(state), GatedNameKey(name, kind), kind);
            if (!_gatedDeclarations.TryGetValue(key, out var records))
            {
                records = [];
                _gatedDeclarations[key] = records;
            }

            var overlapsExisting = false;
            var existingHadConstraints = false;
            IBase2Ast? firstOverlappingNode = null;
            foreach (var existing in records)
            {
                if (!PhpVersionConstraint.AnyOverlap(existing.Constraints, effectiveConstraints))
                {
                    continue;
                }

                if (PhpVersionGatedCallableSignature.AreCoexistingOverloads(
                        existing.Node, existing.Signature, node, signature))
                {
                    continue;
                }

                overlapsExisting = true;
                firstOverlappingNode ??= existing.Node;
                if (existing.Constraints.Count > 0)
                {
                    existingHadConstraints = true;
                }
            }

            var versionOverlap = overlapsExisting
                && (effectiveConstraints.Count > 0 || existingHadConstraints);
            if (versionOverlap)
            {
                var fileName = CheckerHelpers.ResolveDiagnosticFileName(state, node);
                var existingFileName = firstOverlappingNode is null
                    ? fileName
                    : CheckerHelpers.ResolveDiagnosticFileName(state, firstOverlappingNode);
                diagnostics.AddDuplicateFromAst(
                    MessageCode.CheckerPhpVersionDuplicateDeclaration,
                    node,
                    fileName,
                    firstOverlappingNode,
                    existingFileName,
                    name);
            }

            records.Add(new GatedDeclarationRecord(effectiveConstraints, signature, node));
        }

        private static CheckerState PushObjectContainer(CheckerState state, PhpObjectTypeDeclAst objectType)
        {
            var child = state.Split(Enum.ScopeType.ObjectTypeDeclaration);
            if (objectType.BoundSymbol is ObjectDeclarationSymbol bound)
            {
                child.EnclosingObject = bound;
            }

            return child;
        }

        /// <summary>
        /// Keys extension members by the extension type FQN, matching the binder's
        /// <c>ObjectDeclarationScope</c> container. Inactive <c>declare(php=…)</c> variants
        /// have no <see cref="ObjectDeclarationSymbol"/>; synthesize the same
        /// <c>obj:\Ns\Name</c> shape so they do not collide with a different extension's
        /// ungated members of the same name in the enclosing namespace.
        /// </summary>
        internal static CheckerState PushExtensionContainer(
            CheckerState state,
            string? identifier,
            ObjectDeclarationSymbol? bound)
        {
            var child = state.Split(Enum.ScopeType.ObjectTypeDeclaration);
            if (bound is not null)
            {
                child.EnclosingObject = bound;
                return child;
            }

            if (string.IsNullOrEmpty(identifier))
            {
                return child;
            }

            child.EnclosingObject = new ObjectDeclarationSymbol(identifier)
            {
                IsExtension = true,
                FullyQualifiedName = CombineNamespaceAndName(state.CurrentNamespaceName, identifier),
            };
            return child;
        }

        private static string CombineNamespaceAndName(string? ns, string name)
        {
            if (string.IsNullOrWhiteSpace(ns))
            {
                return "\\" + name;
            }

            var trimmed = ns.Trim();
            if (!trimmed.StartsWith('\\'))
            {
                trimmed = "\\" + trimmed;
            }

            return trimmed + "\\" + name;
        }

        private static string GetContainerKey(CheckerState state)
        {
            if (state.EnclosingObject is { } obj)
            {
                var fqn = obj.FullyQualifiedName;
                if (!string.IsNullOrEmpty(fqn))
                {
                    return "obj:" + fqn;
                }

                return "obj:" + obj.Name;
            }

            var ns = state.CurrentNamespaceName;
            if (string.IsNullOrWhiteSpace(ns))
            {
                return "global";
            }

            var trimmed = ns.Trim();
            return trimmed.StartsWith('\\') ? trimmed : "\\" + trimmed;
        }

        private static string GatedNameKey(string name, string kind) =>
            kind is "constant" or "object-constant" ? name : name.ToLowerInvariant();

        private static List<PhpAttributeAst> CollectTyhpPhpAttributes(
            IBase2Ast node,
            IBase2Ast?[] additionalAttributeSources)
        {
            var found = new List<PhpAttributeAst>();
            CollectTyhpPhpAttributesFrom(node, found);
            foreach (var extra in additionalAttributeSources)
            {
                if (extra is not null && !ReferenceEquals(extra, node))
                {
                    CollectTyhpPhpAttributesFrom(extra, found);
                }
            }

            return found;
        }

        private static void CollectTyhpPhpAttributesFrom(IBase2Ast node, List<PhpAttributeAst> found)
        {
            foreach (var attributeNode in node.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute && IsTyhpPhpAttribute(attribute))
                {
                    found.Add(attribute);
                }
            }
        }

        internal static bool IsTyhpPhpAttribute(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol bound }
                && IsTyhpPhpFullyQualifiedName(bound.FullyQualifiedName))
            {
                return true;
            }

            return IsTyhpPhpFullyQualifiedName(GetAttributeNameText(attribute.Name));
        }

        internal static bool IsTyhpPhpFullyQualifiedName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.Trim();
            return trimmed.Equals(TyhpPhpAttributeFqn, StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Tyhp\\Php", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith("\\Tyhp\\Php", StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetAttributeNameText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                IExpression expr => expr.Identifier,
                _ => null,
            };

        private static bool TryReadTyhpPhpVersionArgument(PhpAttributeAst attribute, out string version)
        {
            version = "";
            var arguments = attribute.Arguments?.GetAllNotNull();
            if (arguments is null)
            {
                return false;
            }

            PhpArgumentAst? chosen = null;
            PhpArgumentAst? firstPositional = null;
            foreach (var argument in arguments)
            {
                var argName = argument.Name?.ValueString;
                if (string.IsNullOrEmpty(argName))
                {
                    firstPositional ??= argument;
                    continue;
                }

                if (string.Equals(argName, VersionArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = argument;
                    break;
                }
            }

            chosen ??= firstPositional;
            if (chosen?.Expression is null)
            {
                return false;
            }

            return TryReadStringLiteral(chosen.Expression, out version);
        }

        private static bool TryReadStringLiteral(IExpression expression, out string value)
        {
            value = "";
            switch (expression)
            {
                case PhpScalarAst scalar:
                    if (scalar.ValueInt64.HasValue || scalar.ValueDecimal.HasValue || scalar.ValueBoolean.HasValue)
                    {
                        return false;
                    }

                    if (string.IsNullOrEmpty(scalar.ValueString))
                    {
                        return false;
                    }

                    value = UnquotePhpString(scalar.ValueString);
                    return !string.IsNullOrEmpty(value);

                case PhpEncapsStringAst encaps:
                    value = UnquotePhpString(encaps.ValueString ?? encaps.TokenValue?.ValueString);
                    return !string.IsNullOrEmpty(value);

                case PhpEncapsListAst list:
                    value = string.Concat(
                        list.GetAllNotNull().Select(part => UnquotePhpString(part.ValueString)));
                    return !string.IsNullOrEmpty(value);

                default:
                    return false;
            }
        }

        private static bool IsDeclareBlockForm(PhpDeclareAst declareAst)
            => declareAst.Body is not null and not PhpNopStatementAst;

        /// <summary>
        /// True when the emitter writes this block behind a runtime check: any <c>declare(ext=…)</c>,
        /// or a <c>declare(php=…)</c> that holds for only some versions at or above
        /// <c>output.phpVersion</c>.
        /// </summary>
        private bool NeedsRuntimeCheck(PhpDeclareAst declareAst)
        {
            if (declareAst.BoundSymbol is not DeclareBlockSymbol block)
            {
                return false;
            }

            return RuntimeGateEmission.GetBlockConditions(block, _targetPhpVersion).Count > 0;
        }

        /// <summary>
        /// True when <paramref name="first"/> is <c>declare(ext="x") { … }</c> and
        /// <paramref name="second"/> is <c>declare(ext="!x") { … }</c> (or the reverse): exactly one
        /// of them runs, so together they behave like <c>if … else …</c>.
        /// </summary>
        public static bool AreComplementaryExtBlocks(IBase2Ast first, IBase2Ast second)
        {
            return first is PhpDeclareAst { BoundSymbol: DeclareBlockSymbol a }
                && second is PhpDeclareAst { BoundSymbol: DeclareBlockSymbol b }
                && RuntimeGateEmission.AreComplementaryExtGates(a, b);
        }

        private static bool IsPhpDeclareKey(string? key)
            => string.Equals(key, PhpDeclareKey, StringComparison.OrdinalIgnoreCase);

        private static List<DeclareDirective> ReadDeclareDirectives(PhpDeclareAst declareAst)
        {
            var result = new List<DeclareDirective>();
            if (declareAst.Declarations == null)
            {
                return result;
            }

            foreach (var decl in declareAst.Declarations.GetAllNotNull())
            {
                var key = decl.Identifier ?? "";
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var forPhp = IsPhpDeclareKey(key);
                result.Add(new DeclareDirective(key, ExtractDeclareValue(decl.Value, forPhp)));
            }

            return result;
        }

        private static bool TryGetAlonePhpConstraint(
            List<DeclareDirective> directives,
            out string phpConstraint)
        {
            phpConstraint = "";
            string? found = null;
            var otherCount = 0;
            foreach (var directive in directives)
            {
                if (IsPhpDeclareKey(directive.Key))
                {
                    found = directive.Value;
                }
                else
                {
                    otherCount++;
                }
            }

            if (found == null || otherCount > 0)
            {
                return false;
            }

            phpConstraint = found;
            return true;
        }

        private static string ExtractDeclareValue(IExpression? valueExpr, bool forPhpConstraint)
        {
            if (valueExpr == null)
            {
                return forPhpConstraint ? "" : "1";
            }

            switch (valueExpr)
            {
                case PhpScalarAst scalar:
                    if (!string.IsNullOrEmpty(scalar.ValueString))
                    {
                        return UnquotePhpString(scalar.ValueString);
                    }

                    if (scalar.ValueInt64.HasValue)
                    {
                        return scalar.ValueInt64.Value.ToString();
                    }

                    if (scalar.ValueBoolean.HasValue)
                    {
                        return scalar.ValueBoolean.Value ? "true" : "false";
                    }

                    break;

                case PhpEncapsStringAst encaps:
                    return UnquotePhpString(encaps.ValueString ?? encaps.TokenValue?.ValueString);

                case PhpEncapsListAst list:
                    return string.Concat(
                        list.GetAllNotNull().Select(part => UnquotePhpString(part.ValueString)));
            }

            if (!string.IsNullOrEmpty(valueExpr.ValueString))
            {
                return UnquotePhpString(valueExpr.ValueString);
            }

            return forPhpConstraint ? "" : "1";
        }

        private static string UnquotePhpString(string? literal)
        {
            if (string.IsNullOrEmpty(literal))
            {
                return "";
            }

            if (literal.Length >= 2
                && ((literal[0] == '\'' && literal[^1] == '\'')
                    || (literal[0] == '"' && literal[^1] == '"')))
            {
                return literal[1..^1];
            }

            return literal;
        }

        private readonly record struct DeclareDirective(string Key, string Value);

        private readonly record struct GatedDeclarationKey(string Container, string Name, string Kind);

        private sealed record GatedDeclarationRecord(
            IReadOnlyList<string> Constraints,
            string? Signature,
            IBase2Ast Node);
    }
}
