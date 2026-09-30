using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Checked signatures for an <see cref="ObjectShapeCheckedType"/>, sourced from the bound
    /// <c>object { … }</c> AST (not a synthetic class). <c>__construct</c> stays in the map for
    /// later <c>__New</c> matching; instance assignability ignores it.
    /// </summary>
    public sealed class ObjectShapeMemberMap
    {
        public ObjectShapeMemberMap(
            IReadOnlyDictionary<string, ObjectShapeMethodMember> methods,
            IReadOnlyDictionary<string, ObjectShapePropertyMember> properties,
            IReadOnlyDictionary<string, ObjectShapeConstantMember> constants)
        {
            Methods = methods;
            Properties = properties;
            Constants = constants;
        }

        public IReadOnlyDictionary<string, ObjectShapeMethodMember> Methods { get; }

        public IReadOnlyDictionary<string, ObjectShapePropertyMember> Properties { get; }

        public IReadOnlyDictionary<string, ObjectShapeConstantMember> Constants { get; }

        public ObjectShapeMemberMap MapTypes(Func<ICheckedType, ICheckedType> map) =>
            new(
                Methods.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with { CallableType = map(pair.Value.CallableType) },
                    StringComparer.OrdinalIgnoreCase),
                Properties.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with { Type = map(pair.Value.Type) },
                    StringComparer.OrdinalIgnoreCase),
                Constants.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with { Type = map(pair.Value.Type) },
                    StringComparer.Ordinal));
    }

    public sealed record ObjectShapeMethodMember(
        string Name,
        ICheckedType CallableType,
        bool IsStatic);

    public sealed record ObjectShapePropertyMember(
        string Name,
        ICheckedType Type,
        bool IsReadonly,
        bool HasGetHook,
        bool HasSetHook)
    {
        public bool IsReadable => !HasSetHook || HasGetHook;

        public bool IsWritable =>
            !IsReadonly && (!HasGetHook || HasSetHook);
    }

    public sealed record ObjectShapeConstantMember(
        string Name,
        ICheckedType Type);

    /// <summary>
    /// Walks a bound object-shape AST and resolves member signatures into a
    /// <see cref="ObjectShapeMemberMap"/>.
    /// </summary>
    internal static class ObjectShapeMemberBuilder
    {
        public static ObjectShapeMemberMap Build(
            TyhpObjectShapeAst shape,
            Func<ITypeExpression, bool, ICheckedType> resolveType)
        {
            var methods = new Dictionary<string, ObjectShapeMethodMember>(
                StringComparer.OrdinalIgnoreCase);
            var properties = new Dictionary<string, ObjectShapePropertyMember>(
                StringComparer.OrdinalIgnoreCase);
            var constants = new Dictionary<string, ObjectShapeConstantMember>(
                StringComparer.Ordinal);

            var members = shape.Members?.GetAllNotNull() ?? [];
            foreach (var member in members)
            {
                switch (member)
                {
                    case PhpMethodDeclAst method:
                        AddMethod(methods, method, resolveType);
                        break;
                    case PhpPropertyDeclAst property:
                        AddProperties(properties, property, resolveType);
                        break;
                    case PhpConstDeclListAst constList:
                        AddConstants(constants, constList, resolveType);
                        break;
                    case TyhpdefImportConstDeclListAst tyhpdefConsts:
                        AddTyhpdefConstants(constants, tyhpdefConsts, resolveType);
                        break;
                }
            }

            return new ObjectShapeMemberMap(methods, properties, constants);
        }

        private static void AddMethod(
            Dictionary<string, ObjectShapeMethodMember> methods,
            PhpMethodDeclAst method,
            Func<ITypeExpression, bool, ICheckedType> resolveType)
        {
            var name = method.Identifier ?? string.Empty;
            if (string.IsNullOrEmpty(name) || methods.ContainsKey(name))
            {
                return;
            }

            var parameters = method.Parameters?.GetAllNotNull().ToList() ?? [];
            var parameterTypes = new List<ICheckedType>(parameters.Count);
            var flags = new List<(bool HasDefault, bool IsVariadic)>(parameters.Count);
            var names = new List<string?>(parameters.Count);
            foreach (var parameter in parameters)
            {
                parameterTypes.Add(
                    parameter.Type is null
                        ? CheckedTypes.Mixed
                        : resolveType(parameter.Type, false));
                flags.Add((parameter.DefaultValue is not null, parameter.IsVariadic));
                names.Add(CallableSignatureReflection.NormalizeParameterName(parameter.Name));
            }

            var returnType = method.ReturnType is null
                ? CheckedTypes.Mixed
                : resolveType(method.ReturnType, true);

            methods[name] = new ObjectShapeMethodMember(
                name,
                CallableArityFacetBuilder.Build(parameterTypes, flags, returnType, names),
                IsStatic(method.Modifiers));
        }

        private static void AddProperties(
            Dictionary<string, ObjectShapePropertyMember> properties,
            PhpPropertyDeclAst property,
            Func<ITypeExpression, bool, ICheckedType> resolveType)
        {
            var type = property.Type is null
                ? CheckedTypes.Mixed
                : resolveType(property.Type, false);
            var isReadonly = HasModifier(property.Modifiers, PhpModifier.Readonly);
            var declared = property.Properties?.GetAllNotNull() ?? [];
            foreach (var item in declared)
            {
                var name = NormalizePropertyName(item.Identifier);
                if (string.IsNullOrEmpty(name) || properties.ContainsKey(name))
                {
                    continue;
                }

                ReadHookFlags(item.Hooks, out var hasGet, out var hasSet);
                properties[name] = new ObjectShapePropertyMember(
                    name, type, isReadonly, hasGet, hasSet);
            }
        }

        private static void AddConstants(
            Dictionary<string, ObjectShapeConstantMember> constants,
            PhpConstDeclListAst constList,
            Func<ITypeExpression, bool, ICheckedType> resolveType)
        {
            foreach (var item in constList.GetAllNotNull())
            {
                var name = item.Identifier ?? string.Empty;
                if (string.IsNullOrEmpty(name) || constants.ContainsKey(name))
                {
                    continue;
                }

                var type = item.Type is null
                    ? CheckedTypes.Mixed
                    : resolveType(item.Type, false);
                constants[name] = new ObjectShapeConstantMember(name, type);
            }
        }

        private static void AddTyhpdefConstants(
            Dictionary<string, ObjectShapeConstantMember> constants,
            TyhpdefImportConstDeclListAst constList,
            Func<ITypeExpression, bool, ICheckedType> resolveType)
        {
            var typeAst = constList.AstGrammarAddons.TryGetValue("typeExpr", out var addon)
                ? addon as ITypeExpression
                : null;
            var type = typeAst is null
                ? CheckedTypes.Mixed
                : resolveType(typeAst, false);

            foreach (var item in constList.GetAllNotNull())
            {
                var name = item.Identifier ?? string.Empty;
                if (string.IsNullOrEmpty(name) || constants.ContainsKey(name))
                {
                    continue;
                }

                constants[name] = new ObjectShapeConstantMember(name, type);
            }
        }

        internal static string NormalizePropertyName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return name[0] == '$' ? name[1..] : name;
        }

        internal static bool IsConstructabilityName(string name) =>
            string.Equals(name, "__construct", StringComparison.OrdinalIgnoreCase);

        private static bool IsStatic(PhpModifierListAst? modifiers) =>
            HasModifier(modifiers, PhpModifier.Static);

        private static bool HasModifier(PhpModifierListAst? modifiers, PhpModifier wanted)
        {
            if (modifiers is null)
            {
                return false;
            }

            foreach (var modifier in modifiers.Modifiers)
            {
                if (modifier == wanted)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReadHookFlags(
            PhpPropertyHookListAst? hooks,
            out bool hasGet,
            out bool hasSet)
        {
            hasGet = false;
            hasSet = false;
            if (hooks is null)
            {
                return;
            }

            foreach (var hook in hooks.GetAllNotNull())
            {
                if (string.Equals(hook.Identifier, "get", StringComparison.OrdinalIgnoreCase))
                {
                    hasGet = true;
                }
                else if (string.Equals(hook.Identifier, "set", StringComparison.OrdinalIgnoreCase))
                {
                    hasSet = true;
                }
            }
        }
    }
}
