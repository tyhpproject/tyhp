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
    /// Two forms are recognized:
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
    }
}
