using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker.Rules
{
    public sealed partial class TypeCompatibilityRule
    {
        private enum UndeclaredMemberKind
        {
            Property,
            InstanceMethod,
            StaticMethod,
        }

        /// <summary>
        /// True when <paramref name="receiverType"/> is a class / interface (or bare <c>object</c>)
        /// whose complete hierarchy is known and the member exists nowhere on it, and the type has
        /// no magic accessor (<c>__get</c> / <c>__call</c> / <c>__callStatic</c>) that could serve
        /// the access. Receivers that cannot be judged — unresolved ancestry, unresolved traits,
        /// structs and scalars (handled by the closed-receiver rules), enums' built-in members,
        /// the engine <c>\stdClass</c> — return false, so an unknown type never produces a false
        /// "member not found".
        /// </summary>
        private static bool IsUndeclaredMemberOnKnownType(
            ICheckedType receiverType,
            string memberName,
            UndeclaredMemberKind kind,
            IDereferenceableBase? receiverBase,
            CheckerState state,
            CheckerRuleContext context)
        {
            // `\property_exists($v, 'name')` / `\method_exists($v, 'name')` proves the member for
            // the guarded branch.
            if (kind != UndeclaredMemberKind.StaticMethod
                && IsMemberProvenToExist(receiverBase, memberName, kind, state))
            {
                return false;
            }

            var type = CheckerHelpers.UnwrapMemberAccessReceiver(receiverType);

            if (TypeComparer.IsBuiltInName(type, "object"))
            {
                // Bare `object` declares no members at all; narrow to a class first.
                return kind != UndeclaredMemberKind.StaticMethod;
            }

            var objectDecl = CheckerHelpers.TryGetObjectDeclaration(type);
            if (objectDecl is null
                || objectDecl.IsStruct
                || objectDecl.IsExtension
                || objectDecl.IsCompilerGenerated
                || GeneratedNames.EndsWithExtensionBackerSuffix(objectDecl.Name)
                || GeneratedNames.EndsWithExtensionBackerSuffix(objectDecl.FullyQualifiedName)
                || objectDecl.ObjectKind is not (PhpTypeDeclType.Class or PhpTypeDeclType.Interface))
            {
                return false;
            }

            if (kind == UndeclaredMemberKind.Property && IsEngineStdClass(objectDecl))
            {
                return false;
            }

            if (!HasFullyResolvedAncestry(objectDecl, context))
            {
                return false;
            }

            var magicName = kind switch
            {
                UndeclaredMemberKind.Property => "__get",
                UndeclaredMemberKind.InstanceMethod => "__call",
                _ => "__callStatic",
            };

            return !TryResolveMethod(type, magicName, staticOnly: kind == UndeclaredMemberKind.StaticMethod, state, context, out _);
        }

        /// <summary>
        /// True when the member in <paramref name="suffix"/> is spelled as a plain identifier
        /// (<c>-&gt;name</c>, <c>::name</c>); <c>-&gt;{$expr}</c> / <c>-&gt;$name</c> are dynamic and
        /// cannot be judged.
        /// </summary>
        private static bool IsStaticMemberNameNode(IBase2Ast? suffix) =>
            suffix switch
            {
                PhpInstanceMemberAccessAst access => access.MemberName is PhpNameAst or TokenValueAst,
                PhpStaticMemberAccessAst access => access.Member is PhpNameAst or TokenValueAst,
                PhpClassConstantAccessAst access => access.Member is PhpNameAst or TokenValueAst,
                _ => false,
            };

        private static bool IsMemberProvenToExist(
            IDereferenceableBase? receiverBase,
            string memberName,
            UndeclaredMemberKind kind,
            CheckerState state) =>
            receiverBase is PhpVariableAst variable
            && TypeNarrowingRule.BuildProvenMemberKey(
                variable, memberName, isMethod: kind == UndeclaredMemberKind.InstanceMethod) is { } key
            && state.LookupMemberAccess(key) is not null;

        private static bool IsEngineStdClass(ObjectDeclarationSymbol objectDecl)
        {
            var fqn = string.IsNullOrEmpty(objectDecl.FullyQualifiedName)
                ? objectDecl.Name
                : objectDecl.FullyQualifiedName;
            return string.Equals(fqn.TrimStart('\\'), "stdClass", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Every <c>extends</c> / <c>implements</c> / <c>use</c> reference reachable from the
        /// declaration resolves to a declaration; otherwise a member could live on the part of the
        /// hierarchy the checker cannot see.
        /// </summary>
        private static bool HasFullyResolvedAncestry(ObjectDeclarationSymbol objectDecl, CheckerRuleContext context)
        {
            var visited = new HashSet<ObjectDeclarationSymbol>();
            var pending = new Stack<ObjectDeclarationSymbol>();
            pending.Push(objectDecl);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                var parent = TypeComparer.TryGetParentDeclaration(current, context.SymbolTree, context.GlobalScope);
                if (parent is null)
                {
                    if (StructExtends.ExtendsAstNode(current.DeclaringAstNode) is not null)
                    {
                        return false;
                    }
                }
                else
                {
                    pending.Push(parent);
                }

                var scope = current.ContainingScope ?? context.GlobalScope;
                foreach (var className in TypeComparer.GetAstImplementsClassNames(current))
                {
                    var implemented = TypeComparer.ResolveClassNameSymbol(className, scope, context.SymbolTree);
                    if (implemented is null)
                    {
                        return false;
                    }

                    pending.Push(implemented);
                }

                var traits = TypeComparer.ResolveUsedTraits(
                    current, context.SymbolTree, context.GlobalScope, out var hasUnresolvedTrait);
                if (hasUnresolvedTrait)
                {
                    return false;
                }

                foreach (var trait in traits)
                {
                    pending.Push(trait);
                }
            }

            return true;
        }
    }
}
