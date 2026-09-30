using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class TyhpEmitter
    {
        /// <summary>
        /// Method / function name whose signature is currently being spelled, so parameter
        /// inheritance does not leak into nested closures formatted during the body walk.
        /// </summary>
        private string? _phpTypeCallableName;

        private string SpellEmittedType(
            ITypeExpression? authoredType,
            IBase2Ast? localHost,
            string? inheritedHint)
        {
            if (PhpTypeAttributeSupport.TryGetHint(localHost, out var local))
            {
                return local;
            }

            if (!string.IsNullOrWhiteSpace(inheritedHint))
            {
                return inheritedHint;
            }

            if (authoredType is null)
            {
                return "";
            }

            return this.BuildTypeExpression(authoredType);
        }

        private string SpellMethodReturnPhpType(PhpMethodDeclAst method)
        {
            var omitReturnType = string.Equals(method.Identifier, "__construct", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method.Identifier, "__destruct", StringComparison.OrdinalIgnoreCase);
            if (omitReturnType)
            {
                return "";
            }

            if (this._emitNativeArrayAccessOffsetAsMixed
                && string.Equals(method.Identifier, "offsetGet", StringComparison.OrdinalIgnoreCase))
            {
                return ": mixed";
            }

            var inherited = this.FindInheritedMethodPhpTypeHint(method.Identifier, parameterName: null);
            var spelled = this.SpellEmittedType(method.ReturnType, method, inherited);
            return string.IsNullOrWhiteSpace(spelled) ? "" : ": " + spelled;
        }

        private string SpellFunctionReturnPhpType(PhpFunctionDeclAst function)
        {
            var spelled = this.SpellEmittedType(function.ReturnType, function, inheritedHint: null);
            return string.IsNullOrWhiteSpace(spelled) ? "" : ": " + spelled;
        }

        private string SpellInlineFunctionReturnPhpType(PhpInlineFunctionAst inlineFn, string authoredOrInferred)
        {
            if (PhpTypeAttributeSupport.TryGetHint(inlineFn, out var local))
            {
                return ": " + local;
            }

            return authoredOrInferred;
        }

        private string? FindInheritedMethodPhpTypeHint(string? methodName, string? parameterName)
        {
            if (string.IsNullOrEmpty(methodName)
                || this._currentObjectSymbol is null
                || string.Equals(methodName, "__construct", StringComparison.OrdinalIgnoreCase)
                || string.Equals(methodName, "__destruct", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var visited = new HashSet<ObjectDeclarationSymbol> { this._currentObjectSymbol };
            var pending = new Queue<ObjectDeclarationSymbol>();
            pending.Enqueue(this._currentObjectSymbol);
            var symbolTree = this._context.GetSymbolTree();
            var global = this._context.GlobalScope;

            while (pending.Count > 0)
            {
                foreach (var ancestor in TypeComparer.EnumerateDirectAncestors(
                             pending.Dequeue(), symbolTree, global))
                {
                    if (!visited.Add(ancestor))
                    {
                        continue;
                    }

                    pending.Enqueue(ancestor);

                    if (!TryGetNamedMember(ancestor, methodName, out var member)
                        || member is not ObjectMethodSymbol ancestorMethod
                        || (ancestorMethod.Visibility & MemberModifier.Private) != 0)
                    {
                        continue;
                    }

                    var host = ancestorMethod.DeclaringAstNode;
                    if (parameterName is null)
                    {
                        if (PhpTypeAttributeSupport.TryGetHint(host, out var returnHint))
                        {
                            return returnHint;
                        }

                        continue;
                    }

                    if (TryGetParameterHost(host, parameterName) is { } paramHost
                        && PhpTypeAttributeSupport.TryGetHint(paramHost, out var paramHint))
                    {
                        return paramHint;
                    }
                }
            }

            return null;
        }

        private string? FindInheritedPropertyPhpTypeHint(string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName) || this._currentObjectSymbol is null)
            {
                return null;
            }

            var visited = new HashSet<ObjectDeclarationSymbol> { this._currentObjectSymbol };
            var pending = new Queue<ObjectDeclarationSymbol>();
            pending.Enqueue(this._currentObjectSymbol);
            var symbolTree = this._context.GetSymbolTree();
            var global = this._context.GlobalScope;

            while (pending.Count > 0)
            {
                foreach (var ancestor in TypeComparer.EnumerateDirectAncestors(
                             pending.Dequeue(), symbolTree, global))
                {
                    if (!visited.Add(ancestor))
                    {
                        continue;
                    }

                    pending.Enqueue(ancestor);

                    if (!TryGetNamedMember(ancestor, propertyName, out var member)
                        || member is not ObjectPropertySymbol ancestorProperty
                        || (ancestorProperty.Visibility & MemberModifier.Private) != 0)
                    {
                        continue;
                    }

                    var host = FindPropertyAttributeHost(ancestorProperty);
                    if (PhpTypeAttributeSupport.TryGetHint(host, out var hint))
                    {
                        return hint;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// FOUND_BUGS #52: a class/interface/trait/enum constant that omits its own type but
        /// inherits one from a typed ancestor must still spell that type in the emitted PHP.
        /// PHP 8.3+ requires every override of a typed constant to redeclare a compatible type;
        /// omitting it is a fatal error at class-load time, not merely untyped. Unlike
        /// <see cref="FindInheritedPropertyPhpTypeHint"/> / <see cref="FindInheritedMethodPhpTypeHint"/>
        /// (which look for a <c>#[PhpType]</c> override on an ancestor), this reads the constant's
        /// own <see cref="ObjectConstantSymbol.DeclaredType"/> on the *current* symbol: the checker
        /// (<c>DeclarationRule.ObjectType.CheckClassConstantType</c>) already resolved inheritance
        /// and copied the ancestor's type expression onto this symbol when the declaration itself
        /// had none, so no separate ancestor walk is needed here.
        /// </summary>
        private string? FindInheritedConstantPhpTypeHint(string? constantName)
        {
            if (string.IsNullOrEmpty(constantName) || this._currentObjectSymbol is null)
            {
                return null;
            }

            // Class constants live in `Constants`, not `Members` (see
            // ObjectDeclarationSymbol.Constants) — TryGetNamedMember only searches `Members` and
            // would never find a constant here.
            if (!this._currentObjectSymbol.TryGetConstant(constantName, out var member)
                || member is not ObjectConstantSymbol constant
                || constant.DeclaredType is null)
            {
                return null;
            }

            return this.BuildTypeExpression(constant.DeclaredType);
        }

        private static IBase2Ast? FindPropertyAttributeHost(ObjectPropertySymbol property)
        {
            if (property.DeclaringAstNode is PhpPropertyDeclAst decl)
            {
                return decl;
            }

            var ownerAst = (property.ContainingScope?.DeclarationSymbol as ObjectDeclarationSymbol)
                ?.DeclaringAstNode;
            if (ownerAst is not null
                && FindPropertyDecl(ownerAst, property.Name) is { } found)
            {
                return found;
            }

            return property.DeclaringAstNode;
        }

        private static PhpPropertyDeclAst? FindPropertyDecl(IBase2Ast node, string name)
        {
            if (node is PhpPropertyDeclAst decl
                && decl.Properties?.GetAllNotNull()
                    .Any(p => string.Equals(p.Identifier, name, StringComparison.Ordinal)) == true)
            {
                return decl;
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null && FindPropertyDecl(child, name) is { } nested)
                {
                    return nested;
                }
            }

            return null;
        }

        private static bool TryGetNamedMember(
            ObjectDeclarationSymbol owner,
            string name,
            out IBaseSymbol member)
        {
            if (owner.Members.TryGetValue(name, out member!))
            {
                return true;
            }

            var trimmed = name.TrimStart('$');
            if (!string.Equals(trimmed, name, StringComparison.Ordinal)
                && owner.Members.TryGetValue(trimmed, out member!))
            {
                return true;
            }

            if (!name.StartsWith('$')
                && owner.Members.TryGetValue("$" + name, out member!))
            {
                return true;
            }

            member = null!;
            return false;
        }

        private static PhpParameterAst? TryGetParameterHost(IBase2Ast? callable, string parameterName)
        {
            var parameters = callable switch
            {
                PhpMethodDeclAst method => method.Parameters,
                PhpFunctionDeclAst function => function.Parameters,
                TyhpdefImportFunctionDeclAst tyhpdef => tyhpdef.Parameters,
                PhpInlineFunctionAst closure => closure.Parameters,
                _ => null,
            };

            if (parameters is null)
            {
                return null;
            }

            var needle = parameterName.TrimStart('$');
            foreach (var parameter in parameters.GetAllNotNull())
            {
                if (string.Equals(parameter.Name.TrimStart('$'), needle, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter;
                }
            }

            return null;
        }
    }
}
