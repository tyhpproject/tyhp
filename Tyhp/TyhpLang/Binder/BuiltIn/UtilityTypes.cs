using System.Linq;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    /// <summary>
    /// Registers built-in checker utility types in the <c>\Tyhp</c> namespace.
    /// </summary>
    public static class UtilityTypes
    {
        private const string TyhpNamespace = "Tyhp";

        public static void PopulateGlobal(GlobalScope globalScope)
        {
            var tyhpBlock = GetOrCreateTyhpNamespaceBlock(globalScope);

            Register(tyhpBlock, "Readonly", UtilityBehavior.Readonly,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.ClassInterfaceOrStruct));
            Register(tyhpBlock, "Partial", UtilityBehavior.Partial,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.ClassInterfaceOrStruct));
            Register(tyhpBlock, "Required", UtilityBehavior.Required,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.ClassInterfaceOrStruct));
            Register(tyhpBlock, "Pick", UtilityBehavior.Pick,
                GenericParameterRequirements.Pair("T", BuiltInGenericParameterConstraint.ClassOrStruct, "K", BuiltInGenericParameterConstraint.StringLiteralUnion));
            Register(tyhpBlock, "Omit", UtilityBehavior.Omit,
                GenericParameterRequirements.Pair("T", BuiltInGenericParameterConstraint.ClassOrStruct, "K", BuiltInGenericParameterConstraint.StringLiteralUnion));
            Register(tyhpBlock, "Record", UtilityBehavior.Record,
                GenericParameterRequirements.Pair("K", BuiltInGenericParameterConstraint.KeyIntOrString, "V", BuiltInGenericParameterConstraint.AnyType));
            Register(tyhpBlock, "Exclude", UtilityBehavior.Exclude,
                GenericParameterRequirements.Pair("T", BuiltInGenericParameterConstraint.UnionType, "U", BuiltInGenericParameterConstraint.AnyType));
            Register(tyhpBlock, "Extract", UtilityBehavior.Extract,
                GenericParameterRequirements.Pair("T", BuiltInGenericParameterConstraint.UnionType, "U", BuiltInGenericParameterConstraint.AnyType));
            Register(tyhpBlock, "NonNullable", UtilityBehavior.NonNullable,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.AnyType));
            Register(tyhpBlock, "Nullable", UtilityBehavior.Nullable,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.AnyType));
            Register(tyhpBlock, "ReturnType", UtilityBehavior.ReturnType,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.Callable));
            Register(tyhpBlock, "Parameters", UtilityBehavior.Parameters,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.Callable));
            Register(tyhpBlock, "Awaited", UtilityBehavior.Awaited,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.AnyType));
        }

        private static void Register(
            NamespaceBlockScope tyhpBlock,
            string name,
            UtilityBehavior behavior,
            GenericParameterRequirements requirements
        )
        {
            tyhpBlock.AddChildSymbol(new BuiltInUtilityTypeSymbol(name, behavior, requirements));
        }

        private static NamespaceBlockScope GetOrCreateTyhpNamespaceBlock(GlobalScope globalScope)
        {
            var nsScope = globalScope.AddNamespaceScope(TyhpNamespace);
            var existing = nsScope.ChildScopes.OfType<NamespaceBlockScope>().FirstOrDefault();
            if (existing != null)
            {
                return existing;
            }

            var blockSymbol = new NamespaceBlockSymbol(TyhpNamespace);
            var blockScope = new NamespaceBlockScope(nsScope, blockSymbol);
            nsScope.AddChildScope(blockScope);
            return blockScope;
        }
    }
}
