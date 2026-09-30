using System;
using System.Collections.Generic;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Versioning;

namespace Tyhp.TyhpLang.Binder
{
    public partial class TyhpBinder
    {
        private const string TyhpPhpAttributeFqn = "\\Tyhp\\Php";
        private const string TyhpPhpVersionArgumentName = "version";

        private readonly Dictionary<GatedDeclarationKey, List<GatedDeclarationRecord>> _phpVersionGatedDeclarations = new();

        /// <summary>
        /// Function and type names declared inside <c>declare(php=…)</c> blocks that were not bound
        /// (constraint unsatisfied at the compile target), with each block's effective constraints.
        /// </summary>
        private readonly Dictionary<GatedDeclarationKey, List<IReadOnlyList<string>>> _uncompiledVersionVariants = new();
        private NameResolver? _declarationNameResolver;

        private NameResolver DeclarationNameResolver =>
            _declarationNameResolver ??= new NameResolver(_globalScope, _diagnostics);

        /// <summary>
        /// Overlay stamp / replace / partial / omit must not run when this declaration's
        /// <c>#[\Tyhp\Php]</c> (AND enclosing <c>declare(php=…)</c> stack) is unsatisfied —
        /// the same skip-before-bind as an inactive declare block. Does not record in
        /// <see cref="_phpVersionGatedDeclarations"/> (declare skip does not either).
        /// </summary>
        private bool ShouldSkipPhpVersionGatedOverlay(
            IBase2Ast node,
            IBaseScope parentScope,
            params IBase2Ast?[] additionalAttributeSources)
        {
            var evaluation = EvaluatePhpVersionGate(node, parentScope, additionalAttributeSources);
            return IsPhpVersionGateOmitted(evaluation);
        }

        /// <summary>
        /// Returns <see langword="false"/> when the declaration must not be registered
        /// (unsatisfied <c>#[\Tyhp\Php]</c> / invalid attribute constraint). Reports 4303/4304/4305
        /// as needed. Enclosing inactive <c>declare(php=…)</c> already skipped the region in Phase 3.
        /// </summary>
        private bool ShouldRegisterPhpVersionGatedDeclaration(
            IBase2Ast node,
            IBaseScope parentScope,
            string name,
            SymbolType symbolType,
            bool illegalAttributeTarget,
            out IReadOnlyList<string> effectiveConstraints,
            params IBase2Ast?[] additionalAttributeSources)
        {
            effectiveConstraints = Array.Empty<string>();
            if (string.IsNullOrEmpty(name) || node is null)
            {
                return true;
            }

            var evaluation = EvaluatePhpVersionGate(node, parentScope, additionalAttributeSources);
            var phpAttributes = evaluation.Attributes;
            if (illegalAttributeTarget && phpAttributes.Count > 0)
            {
                _diagnostics.AddErrorFromAst(
                    MessageCode.CheckerPhpVersionAttributeInvalidTarget,
                    phpAttributes[0],
                    _currentFileName);
                evaluation = new PhpVersionGateEvaluation(
                    Attributes: [],
                    EffectiveConstraints: _phpVersionConstraintStack.ToArray(),
                    AnyArgumentInvalid: false,
                    AttributeConstraintInvalid: false);
            }

            if (evaluation.AnyArgumentInvalid)
            {
                foreach (var attribute in evaluation.Attributes)
                {
                    if (!TryReadTyhpPhpVersionArgument(attribute, out _))
                    {
                        _diagnostics.AddErrorFromAst(
                            MessageCode.CheckerPhpVersionAttributeInvalidArgument,
                            attribute,
                            _currentFileName);
                    }
                }
            }

            effectiveConstraints = evaluation.EffectiveConstraints;
            var signature = PhpVersionGatedCallableSignature.FromDeclaration(node);

            var key = new GatedDeclarationKey(
                GetGatedContainerKey(parentScope),
                GatedNameKey(name, symbolType),
                NormalizeGatedKind(symbolType));
            if (!_phpVersionGatedDeclarations.TryGetValue(key, out var records))
            {
                records = [];
                _phpVersionGatedDeclarations[key] = records;
            }

            var overlapsExisting = false;
            var existingBoundOverlap = false;
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
                if (existing.Bound)
                {
                    existingBoundOverlap = true;
                }

                if (existing.Constraints.Count > 0)
                {
                    existingHadConstraints = true;
                }
            }

            // Empty vs empty is a regular duplicate (binder 3019 / tyhpdef 8002), not 4303.
            var versionOverlap = overlapsExisting
                && (effectiveConstraints.Count > 0 || existingHadConstraints);

            if (versionOverlap)
            {
                var existingFileName = firstOverlappingNode?.OwningFile?.FileName ?? _currentFileName;
                _diagnostics.AddDuplicateFromAst(
                    MessageCode.CheckerPhpVersionDuplicateDeclaration,
                    node,
                    _currentFileName,
                    firstOverlappingNode,
                    existingFileName,
                    name);
            }

            var omit = IsPhpVersionGateOmitted(evaluation);
            if (omit || (versionOverlap && existingBoundOverlap))
            {
                records.Add(new GatedDeclarationRecord(effectiveConstraints, Bound: false, node, signature));
                return false;
            }

            records.Add(new GatedDeclarationRecord(effectiveConstraints, Bound: true, node, signature));
            return true;
        }

        /// <summary>
        /// Overlay last-wins replace / omit: drop the Layer 1 gated-declaration record so a
        /// replacement with an overlapping <c>#[\Tyhp\Php]</c> gate does not report 4303.
        /// </summary>
        private void EvictPhpVersionGatedSymbol(IBaseScope parentScope, IBaseSymbol existing)
        {
            if (existing is ObjectDeclarationSymbol type)
            {
                foreach (var member in type.EnumerateMembersAndConstants().ToList())
                {
                    EvictPhpVersionGatedDeclaration(
                        member.ContainingScope ?? parentScope,
                        member.Name,
                        member.SymbolType);
                }
            }

            EvictPhpVersionGatedDeclaration(parentScope, existing.Name, existing.SymbolType);
        }

        private void EvictPhpVersionGatedDeclaration(IBaseScope parentScope, string name, SymbolType symbolType)
        {
            var caseSensitive = symbolType is SymbolType.Constant or SymbolType.ObjectConstant or SymbolType.MagicConstant;
            EvictPhpVersionGatedDeclaration(parentScope, name, NormalizeGatedKind(symbolType), caseSensitive);
        }

        /// <summary>
        /// True when Layer 1 declared <paramref name="name"/> under a <c>#[\Tyhp\Php]</c> /
        /// <c>declare(php=…)</c> gate that is unsatisfied for the compile target, so the
        /// symbol was never registered. Overlay <c>partial</c> of that name should stay
        /// silent (not TYHP8019) until the gate is satisfied.
        /// </summary>
        private bool HasInactiveGatedDeclaration(IBaseScope parentScope, string name, SymbolType symbolType)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var key = new GatedDeclarationKey(
                GetGatedContainerKey(parentScope),
                GatedNameKey(name, symbolType),
                NormalizeGatedKind(symbolType));
            if (!_phpVersionGatedDeclarations.TryGetValue(key, out var records) || records.Count == 0)
            {
                return false;
            }

            var anyBound = false;
            foreach (var record in records)
            {
                if (record.Bound)
                {
                    anyBound = true;
                    break;
                }
            }

            return !anyBound;
        }

        /// <summary>
        /// Evicts by the normalized gate <paramref name="kind"/> (<c>"method"</c>, <c>"property"</c>,
        /// <c>"function"</c>, <c>"object-constant"</c>, …) directly, for call sites that know the
        /// member's kind from AST shape but have no live symbol to evict — e.g. a Layer 1 member
        /// whose <c>#[\Tyhp\Php]</c> gate is unsatisfied for the compile target was never registered
        /// as a real symbol, yet still left a record behind in <see cref="_phpVersionGatedDeclarations"/>.
        /// </summary>
        private void EvictPhpVersionGatedDeclaration(IBaseScope parentScope, string name, string kind, bool caseSensitiveName)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var key = new GatedDeclarationKey(
                GetGatedContainerKey(parentScope),
                caseSensitiveName ? name : name.ToLowerInvariant(),
                kind);
            _phpVersionGatedDeclarations.Remove(key);
        }

        /// <summary>
        /// Flags each registered declaration whose name is also declared under a php gate that is
        /// not satisfied at the compile target but could hold on a newer PHP. Only the declaration
        /// that matches the target is compiled, so a runtime <c>\PHP_VERSION_ID</c> check around
        /// it would leave that name undeclared on the newer version; the emitter leaves such a
        /// declaration unwrapped instead.
        /// </summary>
        private void MarkDeclarationsWithUncompiledVersionVariants()
        {
            var target = GetTargetPhpVersion();
            foreach (var (key, records) in _phpVersionGatedDeclarations)
            {
                var hasUncompiledVariant = records.Any(r => !r.Bound
                    && PhpVersionConstraint.ClassifyAtOrAbove(r.Constraints, target).Kind != PhpRuntimeGateKind.Never);
                if (!hasUncompiledVariant
                    && _uncompiledVersionVariants.TryGetValue(key, out var skippedBlocks))
                {
                    hasUncompiledVariant = skippedBlocks.Any(constraints =>
                        PhpVersionConstraint.ClassifyAtOrAbove(constraints, target).Kind != PhpRuntimeGateKind.Never);
                }

                if (!hasUncompiledVariant)
                {
                    continue;
                }

                foreach (var record in records)
                {
                    if (record.Bound && record.Node.BoundSymbol is BaseSymbol symbol)
                    {
                        symbol.HasUncompiledVersionVariants = true;
                    }
                }
            }
        }

        private void StampPhpVersionConstraints(BaseSymbol symbol, IReadOnlyList<string> constraints)
        {
            symbol.EffectivePhpVersionConstraints = constraints;
            symbol.EffectiveExtGates = _extGateStack.Count == 0 ? [] : _extGateStack.ToArray();
        }

        /// <summary>
        /// Reads <c>#[\Tyhp\Php]</c> on <paramref name="node"/> and AND-combines those
        /// constraints with <see cref="_phpVersionConstraintStack"/>. Does not report
        /// diagnostics and does not touch <see cref="_phpVersionGatedDeclarations"/>.
        /// Invalid Composer strings omit the declaration (same as <c>declare(php=…)</c>);
        /// missing/non-string arguments do not apply the attribute as a gate.
        /// </summary>
        private PhpVersionGateEvaluation EvaluatePhpVersionGate(
            IBase2Ast node,
            IBaseScope parentScope,
            params IBase2Ast?[] additionalAttributeSources)
        {
            var phpAttributes = CollectTyhpPhpAttributes(parentScope, node, additionalAttributeSources);
            var constraints = new List<string>(_phpVersionConstraintStack);
            var attributeConstraintInvalid = false;
            var anyArgumentInvalid = false;
            if (phpAttributes.Count > 0)
            {
                foreach (var attribute in phpAttributes)
                {
                    if (!TryReadTyhpPhpVersionArgument(attribute, out var version))
                    {
                        anyArgumentInvalid = true;
                        continue;
                    }

                    var evaluated = PhpVersionConstraint.Evaluate(GetTargetPhpVersion(), version);
                    if (!evaluated.ConstraintIsValid)
                    {
                        // 4300 is checker-owned (Phase 5). Treat as inactive, same as declare.
                        attributeConstraintInvalid = true;
                        continue;
                    }

                    constraints.Add(version);
                }

                if (anyArgumentInvalid)
                {
                    // Missing/non-string version: do not apply the attribute as a gate.
                    constraints = [.. _phpVersionConstraintStack];
                    attributeConstraintInvalid = false;
                }
            }

            return new PhpVersionGateEvaluation(
                phpAttributes,
                constraints.ToArray(),
                anyArgumentInvalid,
                attributeConstraintInvalid);
        }

        private bool IsPhpVersionGateOmitted(PhpVersionGateEvaluation evaluation)
            => evaluation.AttributeConstraintInvalid
                || !IsEffectiveConstraintSetSatisfied(evaluation.EffectiveConstraints);

        private List<PhpAttributeAst> CollectTyhpPhpAttributes(
            IBaseScope fromScope,
            IBase2Ast node,
            IBase2Ast?[] additionalAttributeSources)
        {
            var found = new List<PhpAttributeAst>();
            CollectTyhpPhpAttributesFrom(node, fromScope, found);
            foreach (var extra in additionalAttributeSources)
            {
                if (extra is not null && !ReferenceEquals(extra, node))
                {
                    CollectTyhpPhpAttributesFrom(extra, fromScope, found);
                }
            }

            return found;
        }

        private void CollectTyhpPhpAttributesFrom(IBase2Ast node, IBaseScope fromScope, List<PhpAttributeAst> found)
        {
            foreach (var attributeNode in node.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute && IsTyhpPhpAttribute(attribute, fromScope))
                {
                    found.Add(attribute);
                }
            }
        }

        private bool IsTyhpPhpAttribute(PhpAttributeAst attribute, IBaseScope fromScope)
        {
            var written = GetAttributeNameText(attribute.Name);
            if (IsTyhpPhpFullyQualifiedName(written))
            {
                return true;
            }

            if (attribute.Name is not IExpression nameExpr)
            {
                return false;
            }

            var resolved = DeclarationNameResolver.ResolveAttributeClassName(nameExpr, fromScope);
            return IsTyhpPhpFullyQualifiedName(resolved?.FullyQualifiedName);
        }

        private static bool IsTyhpPhpFullyQualifiedName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.Trim();
            if (trimmed.Equals(TyhpPhpAttributeFqn, StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Tyhp\\Php", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string? GetAttributeNameText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                IExpression expr => expr.Identifier,
                _ => null,
            };

        /// <summary>
        /// Reads positional <c>#[\Tyhp\Php(">=8.4")]</c> or named <c>#[\Tyhp\Php(version: ">=8.4")]</c>.
        /// Complex/non-string arguments fail here so Phase 5 can still see 4305 on the same node.
        /// </summary>
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

                if (string.Equals(argName, TyhpPhpVersionArgumentName, StringComparison.OrdinalIgnoreCase))
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
                    return true;

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

        private bool IsEffectiveConstraintSetSatisfied(IReadOnlyList<string> constraints)
        {
            var target = GetTargetPhpVersion();
            foreach (var constraint in constraints)
            {
                var result = PhpVersionConstraint.Evaluate(target, constraint);
                if (!result.ConstraintIsValid || !result.IsSatisfied)
                {
                    return false;
                }
            }

            return true;
        }

        private static string GatedNameKey(string name, SymbolType symbolType) =>
            symbolType is SymbolType.Constant or SymbolType.ObjectConstant or SymbolType.MagicConstant
                ? name
                : name.ToLowerInvariant();

        private static string GetGatedContainerKey(IBaseScope scope)
        {
            if (scope is ObjectDeclarationScope objectScope)
            {
                var obj = objectScope.DeclarationSymbol;
                var fqn = obj?.FullyQualifiedName;
                if (!string.IsNullOrEmpty(fqn))
                {
                    return "obj:" + fqn;
                }

                return "obj:" + (obj?.Name ?? "");
            }

            var namespacePath = GetNamespacePathForGating(scope);
            return string.IsNullOrEmpty(namespacePath) ? "global" : namespacePath;
        }

        private static string GetNamespacePathForGating(IBaseScope? scope)
        {
            while (scope != null)
            {
                if (scope.DeclarationSymbol is NamespaceSymbol ns
                    && !string.IsNullOrWhiteSpace(ns.Name))
                {
                    var parent = GetNamespacePathForGating(scope.ParentScope);
                    var name = ns.Name.Trim('\\');
                    return string.IsNullOrEmpty(parent) ? "\\" + name : parent + "\\" + name;
                }

                scope = scope.ParentScope;
            }

            return "";
        }

        private static string NormalizeGatedKind(SymbolType symbolType) => symbolType switch
        {
            SymbolType.FunctionDeclaration or SymbolType.BuiltInFunction => "function",
            SymbolType.ObjectTypeDeclaration => "type",
            SymbolType.Constant or SymbolType.MagicConstant => "constant",
            SymbolType.ObjectConstant => "object-constant",
            SymbolType.InstanceObjectMethod
                or SymbolType.StaticObjectMethod
                or SymbolType.ObjectConstructor
                or SymbolType.ObjectDestructor => "method",
            SymbolType.InstanceObjectProperty or SymbolType.StaticObjectProperty => "property",
            _ => symbolType.ToString(),
        };

        private readonly record struct GatedDeclarationKey(string Container, string Name, string Kind);

        private readonly record struct PhpVersionGateEvaluation(
            IReadOnlyList<PhpAttributeAst> Attributes,
            IReadOnlyList<string> EffectiveConstraints,
            bool AnyArgumentInvalid,
            bool AttributeConstraintInvalid);

        private sealed record GatedDeclarationRecord(
            IReadOnlyList<string> Constraints,
            bool Bound,
            IBase2Ast Node,
            string? Signature);
    }
}
