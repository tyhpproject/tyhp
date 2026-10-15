using System;
using System.Collections.Generic;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang
{
    /// <summary>
    /// Identifies compile-time-only overload <em>signatures</em>, which the binder skips and the
    /// emitter erases so that only the single implementation survives.
    ///
    /// Three forms are recognized:
    /// <list type="bullet">
    ///   <item>
    ///     Top-level functions written as bodyless <c>function name(...): T;</c> carry an
    ///     <c>isOverloadSignature</c> grammar addon. Named short functions
    ///     (<c>fn name(...) =&gt; expr;</c>) share the short-function grammar alt but are desugared
    ///     to a body at visit time and do <em>not</em> get that addon — they are implementations.
    ///   </item>
    ///   <item>
    ///     Class methods have no dedicated grammar, so overload signatures are detected
    ///     structurally: a bodyless, non-abstract method whose name matches an implementation (a
    ///     same-named method that has a body) in the same type body. Abstract and interface methods
    ///     are legitimately bodyless with no implementation sibling, so they are excluded.
    ///   </item>
    ///   <item>
    ///     Extension members are <see cref="PhpFunctionDeclAst"/> (not class methods). Bodyless
    ///     <c>function name(...): T;</c> in an <c>extension</c> body is an overload signature when
    ///     a same-named member with a body exists in that extension.
    ///   </item>
    /// </list>
    /// </summary>
    public static class OverloadSignatureHelper
    {
        private const string OverloadSignatureAddon = "isOverloadSignature";

        /// <summary>
        /// True when a top-level function declaration is an erasable overload signature: it carries
        /// the overload grammar addon and has no body. Named short-function implementations have a
        /// body (desugared <c>return expr;</c>) and therefore return false.
        /// </summary>
        public static bool IsErasableFunctionOverloadSignature(PhpFunctionDeclAst function)
            => function.Body == null
               && function.AstGrammarAddons.ContainsKey(OverloadSignatureAddon);

        /// <summary>
        /// Collects the names (case-insensitive) of all methods that provide an implementation
        /// (i.e. have a body) among the members of a type body.
        /// </summary>
        public static HashSet<string> CollectImplementedMethodNames(IEnumerable<IClassMember> members)
        {
            var implemented = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in members)
            {
                if (member is PhpMethodDeclAst method
                    && method.Body != null
                    && !string.IsNullOrEmpty(method.Identifier))
                {
                    implemented.Add(method.Identifier);
                }
            }

            return implemented;
        }

        /// <summary>
        /// True when a class method is an erasable overload signature: it is bodyless and
        /// non-abstract, and a same-named implementation exists in the same type body.
        /// </summary>
        public static bool IsClassMethodOverloadSignature(
            PhpMethodDeclAst method,
            ISet<string> implementedMethodNames)
        {
            if (method.Body != null || string.IsNullOrEmpty(method.Identifier))
            {
                return false;
            }

            if (method.Modifiers?.Modifiers.Contains(PhpModifier.Abstract) == true)
            {
                return false;
            }

            return implementedMethodNames.Contains(method.Identifier);
        }

        /// <summary>
        /// One <c>.tyhp</c> overload set found by <see cref="ScanMethods"/> / <see cref="ScanFunctions"/>.
        /// </summary>
        public sealed class OverloadSetPlan
        {
            /// <summary>The declaration the binder registers as the member (the implementation, or the last signature of an abstract/interface set).</summary>
            public required IBase2Ast Registered { get; init; }

            /// <summary>Overload signatures in declaration order, excluding <see cref="Registered"/>.</summary>
            public required List<IBase2Ast> Signatures { get; init; }

            /// <summary>True when <see cref="Registered"/> has a body, so its own signature is not part of the call surface.</summary>
            public required bool RegisteredIsImplementation { get; init; }
        }

        public enum OverloadStructureIssueKind
        {
            /// <summary>Signatures with no implementation directly after them.</summary>
            MissingImplementation,

            /// <summary>A signature that is not directly before its implementation.</summary>
            NotAdjacent,
        }

        public sealed record OverloadStructureIssue(IBase2Ast Node, OverloadStructureIssueKind Kind, string Name);

        /// <summary>Result of grouping the bodyless signatures of one declaration list.</summary>
        public sealed class OverloadScan
        {
            /// <summary>Sets keyed by their registered declaration.</summary>
            public Dictionary<IBase2Ast, OverloadSetPlan> Sets { get; } = new();

            /// <summary>Signature nodes that belong to a set (bound by the set, not on their own).</summary>
            public HashSet<IBase2Ast> ClaimedSignatures { get; } = new();

            /// <summary>Misplaced signatures: reported and erased, never bound.</summary>
            public HashSet<IBase2Ast> MisplacedSignatures { get; } = new();

            public List<OverloadStructureIssue> Issues { get; } = new();

            public bool IsSignatureNode(IBase2Ast node)
                => this.ClaimedSignatures.Contains(node) || this.MisplacedSignatures.Contains(node);

            public void Merge(OverloadScan other)
            {
                foreach (var pair in other.Sets) this.Sets[pair.Key] = pair.Value;
                this.ClaimedSignatures.UnionWith(other.ClaimedSignatures);
                this.MisplacedSignatures.UnionWith(other.MisplacedSignatures);
                this.Issues.AddRange(other.Issues);
            }
        }

        /// <summary>
        /// Structural fingerprint of a signature's parameter types (ignoring parameter names) and
        /// return type, used to spot two identical overload signatures.
        /// </summary>
        public static string Fingerprint(IBase2Ast declaration)
        {
            var (parameters, returnType) = declaration switch
            {
                PhpFunctionDeclAst function => (function.Parameters, function.ReturnType),
                PhpMethodDeclAst method => (method.Parameters, method.ReturnType),
                _ => ((PhpParameterListAst?)null, (ITypeExpression?)null),
            };

            var builder = new System.Text.StringBuilder("(");
            if (parameters is not null)
            {
                foreach (var parameter in parameters.GetAllNotNull())
                {
                    builder.Append(parameter.IsRef ? "&" : "").Append(parameter.IsVariadic ? "..." : "");
                    AppendNode(builder, parameter.Type);
                    builder.Append(parameter.DefaultValue is not null ? "=?" : "").Append(',');
                }
            }

            builder.Append("):");
            AppendNode(builder, returnType);
            return builder.ToString();
        }

        private static void AppendNode(System.Text.StringBuilder builder, IBase2Ast? node)
        {
            if (node is null)
            {
                builder.Append('_');
                return;
            }

            builder.Append(node.GetType().Name).Append(':').Append(node.ValueString ?? node.Identifier ?? "");
            builder.Append('[');
            foreach (var child in node.AstChildren)
            {
                AppendNode(builder, child);
                builder.Append(';');
            }

            builder.Append(']');
        }

        private static bool HasModifier(PhpMethodDeclAst method, PhpModifier modifier)
            => method.Modifiers?.Modifiers.Contains(modifier) == true;

        /// <summary>
        /// Groups the methods of one type body into overload sets. A set is a run of two or more
        /// directly adjacent same-named methods where the last has a body (concrete set: the
        /// bodyless ones before it are signatures and the last is the implementation), or, in an
        /// interface or with abstract methods, a run of bodyless same-named methods (the last is
        /// the registered declaration and every one of them is callable). A single bodyless
        /// non-abstract signature whose implementation is elsewhere in the body is misplaced.
        /// </summary>
        public static OverloadScan ScanMethods(IReadOnlyList<IClassMember> members, bool inInterface)
        {
            var scan = new OverloadScan();
            var implementedNames = CollectImplementedMethodNames(members);

            var index = 0;
            while (index < members.Count)
            {
                if (members[index] is not PhpMethodDeclAst first || string.IsNullOrEmpty(first.Identifier))
                {
                    index++;
                    continue;
                }

                var run = new List<PhpMethodDeclAst> { first };
                var next = index + 1;
                while (next < members.Count
                    && members[next] is PhpMethodDeclAst candidate
                    && string.Equals(candidate.Identifier, first.Identifier, StringComparison.OrdinalIgnoreCase))
                {
                    run.Add(candidate);
                    next++;
                }

                index = next;
                if (run.Count == 1)
                {
                    continue;
                }

                var pending = new List<PhpMethodDeclAst>();
                foreach (var method in run)
                {
                    if (method.Body == null)
                    {
                        pending.Add(method);
                        continue;
                    }

                    if (pending.Count > 0)
                    {
                        AddMethodSet(scan, pending, method, registeredIsImplementation: true);
                        pending = new List<PhpMethodDeclAst>();
                    }
                }

                if (pending.Count >= 2 && (inInterface || pending.Any(p => HasModifier(p, PhpModifier.Abstract))))
                {
                    var registered = pending[^1];
                    AddMethodSet(scan, pending.GetRange(0, pending.Count - 1), registered, registeredIsImplementation: false);
                }
                else if (pending.Count >= 2)
                {
                    if (implementedNames.Contains(first.Identifier))
                    {
                        // The implementation exists, but something sits between it and these signatures.
                        foreach (var orphan in pending)
                        {
                            scan.Issues.Add(new OverloadStructureIssue(
                                orphan, OverloadStructureIssueKind.NotAdjacent, first.Identifier));
                            scan.MisplacedSignatures.Add(orphan);
                        }
                    }
                    else
                    {
                        scan.Issues.Add(new OverloadStructureIssue(
                            pending[0], OverloadStructureIssueKind.MissingImplementation, first.Identifier));
                        foreach (var orphan in pending)
                        {
                            scan.MisplacedSignatures.Add(orphan);
                        }
                    }
                }
                else if (pending.Count == 1 && implementedNames.Contains(first.Identifier)
                    && !HasModifier(pending[0], PhpModifier.Abstract) && !inInterface)
                {
                    // A bodyless method trailing an implementation run of the same name.
                    scan.Issues.Add(new OverloadStructureIssue(
                        pending[0], OverloadStructureIssueKind.NotAdjacent, first.Identifier));
                    scan.MisplacedSignatures.Add(pending[0]);
                }
            }

            // Lone bodyless non-abstract signatures whose implementation sits elsewhere in the body.
            foreach (var member in members)
            {
                if (member is PhpMethodDeclAst method
                    && !scan.IsSignatureNode(method)
                    && IsClassMethodOverloadSignature(method, implementedNames)
                    && !inInterface)
                {
                    scan.Issues.Add(new OverloadStructureIssue(
                        method, OverloadStructureIssueKind.NotAdjacent, method.Identifier ?? ""));
                    scan.MisplacedSignatures.Add(method);
                }
            }

            return scan;
        }

        private static void AddMethodSet(
            OverloadScan scan,
            List<PhpMethodDeclAst> signatures,
            PhpMethodDeclAst registered,
            bool registeredIsImplementation)
        {
            scan.Sets[registered] = new OverloadSetPlan
            {
                Registered = registered,
                Signatures = signatures.Cast<IBase2Ast>().ToList(),
                RegisteredIsImplementation = registeredIsImplementation,
            };
            foreach (var signature in signatures)
            {
                scan.ClaimedSignatures.Add(signature);
            }
        }

        /// <summary>
        /// Groups <c>function name(...): T;</c> overload signatures among the siblings of one
        /// statement list with the implementation that directly follows them.
        /// </summary>
        public static OverloadScan ScanFunctions(IReadOnlyList<IBase2Ast?> siblings)
        {
            var scan = new OverloadScan();
            var implemented = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasSignature = false;
            foreach (var sibling in siblings)
            {
                if (sibling is PhpFunctionDeclAst function && !string.IsNullOrEmpty(function.Identifier))
                {
                    if (IsErasableFunctionOverloadSignature(function))
                    {
                        hasSignature = true;
                    }
                    else if (function.Body != null)
                    {
                        implemented.Add(function.Identifier);
                    }
                }
            }

            if (!hasSignature)
            {
                return scan;
            }

            var pending = new List<PhpFunctionDeclAst>();

            void Flush()
            {
                if (pending.Count == 0)
                {
                    return;
                }

                var name = pending[0].Identifier ?? "";
                if (implemented.Contains(name))
                {
                    foreach (var signature in pending)
                    {
                        scan.Issues.Add(new OverloadStructureIssue(
                            signature, OverloadStructureIssueKind.NotAdjacent, name));
                        scan.MisplacedSignatures.Add(signature);
                    }
                }
                else
                {
                    scan.Issues.Add(new OverloadStructureIssue(
                        pending[0], OverloadStructureIssueKind.MissingImplementation, name));
                    foreach (var signature in pending)
                    {
                        scan.MisplacedSignatures.Add(signature);
                    }
                }

                pending = new List<PhpFunctionDeclAst>();
            }

            foreach (var sibling in siblings)
            {
                if (sibling is PhpFunctionDeclAst function && !string.IsNullOrEmpty(function.Identifier))
                {
                    if (IsErasableFunctionOverloadSignature(function))
                    {
                        if (pending.Count > 0
                            && !string.Equals(pending[0].Identifier, function.Identifier, StringComparison.OrdinalIgnoreCase))
                        {
                            Flush();
                        }

                        pending.Add(function);
                        continue;
                    }

                    if (pending.Count > 0
                        && string.Equals(pending[0].Identifier, function.Identifier, StringComparison.OrdinalIgnoreCase)
                        && function.Body != null)
                    {
                        scan.Sets[function] = new OverloadSetPlan
                        {
                            Registered = function,
                            Signatures = pending.Cast<IBase2Ast>().ToList(),
                            RegisteredIsImplementation = true,
                        };
                        foreach (var signature in pending)
                        {
                            scan.ClaimedSignatures.Add(signature);
                        }

                        pending = new List<PhpFunctionDeclAst>();
                        continue;
                    }
                }

                Flush();
            }

            Flush();
            return scan;
        }

        /// <summary>
        /// Collects the names (case-insensitive) of extension functions that provide an
        /// implementation (a body, including desugared <c>fn … =&gt;</c>).
        /// </summary>
        public static HashSet<string> CollectImplementedExtensionFunctionNames(
            IEnumerable<IExtensionMemberAst> members)
        {
            var implemented = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in members)
            {
                if (member is PhpFunctionDeclAst function
                    && function.Body != null
                    && !string.IsNullOrEmpty(function.Identifier))
                {
                    implemented.Add(function.Identifier);
                }
            }

            return implemented;
        }

        /// <summary>
        /// True when an extension function is an erasable overload signature: it is bodyless
        /// and a same-named implementation exists in the same extension body.
        /// </summary>
        public static bool IsExtensionFunctionOverloadSignature(
            PhpFunctionDeclAst function,
            ISet<string> implementedFunctionNames)
        {
            if (function.Body != null || string.IsNullOrEmpty(function.Identifier))
            {
                return false;
            }

            return implementedFunctionNames.Contains(function.Identifier);
        }
    }
}
