using System;
using System.Collections.Generic;

namespace Tyhp.TyhpLang.Enum {
    public enum SymbolType
    {
        Root,
        File,

        /// <summary>
        /// A named or anon namespace
        /// </summary>
        Namespace,

        /// <summary>
        /// A block of code that is part of a namespace
        /// </summary>
        NamespaceBlock,

        /// <summary>
        /// A type alias not in a class, or use statement on a namespace block
        /// </summary>
        TypeAlias, // a type alias not in a class

        // \MyApp\Helpers\MY_CONST_VALUE
        // unique item name (after \MyApp\Helpers\), case-insensitive
        // parent is namespace
        // can be a magic constant
        Constant, // just a regular constant

        // __LINE__
        // unique always, case-insensitive
        // no parent
        MagicConstant, // a constant like __LINE__, __FUNCTION__, etc.

        // \MyApp\Helpers\testFunction
        // unique item name (after \MyApp\Helpers\), case-insensitive
        // parent is namespace
        FunctionDeclaration, // function, anon-function, and extension function (outside of a wrapper)

        /// <summary>
        /// an anonymous function
        /// </summary>
        AnonymousFunctionDeclaration, // an anonymous function

        // in function
        // \MyApp\Helpers\testFunction#my_label
        // in file (outside of method or function)
        // !src/Helpers/helper_functions.php#my_label
        // unique item name (after \MyApp\Helpers\testFunction#), case-insensitive
        // parent is container (function, method, or file)
        Label, // a label or a switch-case item

        // always unique
        // no parent
        BuiltInType, // like int, string, float, array, void, null, bool, etc.

        /// <summary>
        /// Built-in checker utility type in the <c>\Tyhp</c> namespace (e.g. <c>\Tyhp\Partial&lt;T&gt;</c>).
        /// </summary>
        BuiltInUtilityType,

        /// <summary>
        /// Compile-time-only built-in function (e.g. <c>nameof()</c>, <c>typeof()</c>).
        /// </summary>
        BuiltInFunction,

        // same rules as class type alias
        // parent is object
        ClassGenericTypeParameter, // from a generic type in a declaration

        // same rules as type alias, but scoped to this function only
        // parent is function
        FunctionGenericTypeParameter, // from a generic type in a declaration

        // objects and object members

        /// <summary>
        /// an anonymous object declaration
        /// </summary>
        AnonymousObjectDeclaration,

        // \MyApp\Helpers\ObjectNameHelper
        // unique to namespace, case insensitive
        // parent is namespace
        // class, anon-class, enum, interface, trait, struct, extension_wrapper
        ObjectTypeDeclaration, // class/anon-class/enum/interface/trait/struct/extension_wrapper

        // \MyApp\Helpers\ObjectNameHelper::NAME_KEY
        // unique to object
        // parent is object
        // a constant or an enum case
        ObjectConstant, // a constant on a class

        // \MyApp\Helpers\ObjectNameHelper::$propertyName
        // unique to object
        // parent is object
        InstanceObjectProperty, // a class property

        StaticObjectProperty, // a static class property

        // \MyApp\Helpers\ObjectNameHelper::$cleanName
        // unique to object
        // parent is object
        InstanceObjectAccessorMethod, // a (get/set/unset/guard) method for a class property that uses accessors

        StaticObjectAccessorMethod, // a (get/set/unset/guard) method for a static class property that uses accessors

        // \MyApp\Helpers\ObjectNameHelper::nameTypes
        // unique to object
        // parent is object
        ObjectTypeAlias, // a type alias on a class

        // \MyApp\Helpers\ObjectNameHelper::isLastName
        // unique to object
        // parent is object
        InstanceObjectMethod, // instance methods

        StaticObjectMethod, // static methods and extension methods (in a wrapper, are actually just static methods)

        // \MyApp\Helpers\ObjectNameHelper::__construct
        // unique to object
        // parent is object
        ObjectConstructor, // the __construct method on a class

        // \MyApp\Helpers\ObjectNameHelper::__destruct
        // unique to object
        // parent is object
        ObjectDestructor, // the __destruct method on a class

        // \MyApp\Helpers\ObjectNameHelper::overload(+)
        // unique to object
        // parent is object
        ObjectOperatorOverload, // operator overload methods

        ObjectMagicCallMethod, // __call()
        ObjectMagicCallStaticMethod, // __callStatic()
        ObjectMagicGetMethod, // __get()
        ObjectMagicSetMethod, // __set()
        ObjectMagicIssetMethod, // __isset()
        ObjectMagicUnsetMethod, // __unset()
        ObjectMagicSleepMethod, // __sleep()
        ObjectMagicWakeupMethod, // __wakeup()
        ObjectMagicSerializeMethod, // __serialize()
        ObjectMagicUnserializeMethod, // __unserialize()
        ObjectMagicToStringMethod, // __toString()
        ObjectMagicInvokeMethod, // __invoke()
        ObjectMagicSetStateMethod, // __set_state()
        ObjectMagicCloneMethod, // __clone()
        ObjectMagicDebugInfoMethod, // __debugInfo()

        // variables

        // \MyApp\Helpers\testFunction$myVar
        // parent is function, method, or file
        // can also be a global variable (imported in a function/method)
        // can also be a super global variable (like $_SESSION, $_POST, etc.)
        // can be a parameter

        Variable, // a variable or a parameter

        // OTHER
        
        /// <summary>
        /// namespace- or file-level use/import declarations.
        /// </summary>
        UseInclude,

        /// <summary>
        /// the output_file declare can be nested in a namespace block
        /// cannot use the block syntax for the declare statement, this applies to the namespace block that contains it
        /// </summary>
        IncludeTag, // used with the output_file declare like so `declare(output_file="blah/myFile.php",include_tag="myFile") { /* ... */ }` so when in Tyhp, you do `include("myFile");` it will replace it with the output file name.

        CodeBlock,
        DeclareBlock,
        Statement,
    }

    public static class SymbolTypeHelper
    {
        private static readonly IReadOnlyList<SymbolType> NamespaceScopeTypes = new[] { SymbolType.Namespace };
        private static readonly IReadOnlyList<SymbolType> NamespaceBlockScopeTypes = new[] { SymbolType.NamespaceBlock };
        private static readonly IReadOnlyList<SymbolType> RootScopeTypes = new[] { SymbolType.Root };
        private static readonly IReadOnlyList<SymbolType> FileScopeTypes = new[] { SymbolType.File };
        private static readonly IReadOnlyList<SymbolType> FunctionDeclarationScopeTypes = new[] { SymbolType.FunctionDeclaration };
        private static readonly IReadOnlyList<SymbolType> ObjectDeclarationScopeTypes = new[] { SymbolType.ObjectTypeDeclaration };
        private static readonly IReadOnlyList<SymbolType> AnonymousFunctionScopeTypes = new[] { SymbolType.AnonymousFunctionDeclaration };
        private static readonly IReadOnlyList<SymbolType> AnonymousObjectDeclarationScopeTypes = new[] { SymbolType.AnonymousObjectDeclaration };
        private static readonly IReadOnlyList<SymbolType> CodeBlockScopeTypes = new[] { SymbolType.CodeBlock };
        private static readonly IReadOnlyList<SymbolType> DeclareBlockScopeTypes = new[] { SymbolType.DeclareBlock };
        private static readonly IReadOnlyList<SymbolType> LabelScopeTypes = new[] { SymbolType.Label };
        private static readonly IReadOnlyList<SymbolType> InstanceMethodDeclarationScopeTypes = new[]
        {
            SymbolType.InstanceObjectMethod,
            SymbolType.InstanceObjectAccessorMethod,
            SymbolType.ObjectConstructor,
            SymbolType.ObjectDestructor,
            SymbolType.ObjectMagicCallMethod,
            SymbolType.ObjectMagicGetMethod,
            SymbolType.ObjectMagicSetMethod,
            SymbolType.ObjectMagicIssetMethod,
            SymbolType.ObjectMagicUnsetMethod,
            SymbolType.ObjectMagicSleepMethod,
            SymbolType.ObjectMagicWakeupMethod,
            SymbolType.ObjectMagicSerializeMethod,
            SymbolType.ObjectMagicUnserializeMethod,
            SymbolType.ObjectMagicToStringMethod,
            SymbolType.ObjectMagicInvokeMethod,
            SymbolType.ObjectMagicCloneMethod,
            SymbolType.ObjectMagicDebugInfoMethod,
        };
        private static readonly IReadOnlyList<SymbolType> StaticMethodDeclarationScopeTypes = new[]
        {
            SymbolType.StaticObjectMethod,
            SymbolType.StaticObjectAccessorMethod,
            SymbolType.ObjectMagicCallStaticMethod,
            SymbolType.ObjectMagicSetStateMethod,
            SymbolType.ObjectOperatorOverload,
        };
        private static readonly IReadOnlyList<SymbolType> StatementScopeTypes = new[]
        {
            SymbolType.Statement,
            SymbolType.TypeAlias,
            SymbolType.Constant,
            SymbolType.Variable,
        };

        private static readonly HashSet<SymbolType> RootScopeTypeSet = new(RootScopeTypes);
        private static readonly HashSet<SymbolType> FileScopeTypeSet = new(FileScopeTypes);
        private static readonly HashSet<SymbolType> NamespaceScopeTypeSet = new(NamespaceScopeTypes);
        private static readonly HashSet<SymbolType> NamespaceBlockScopeTypeSet = new(NamespaceBlockScopeTypes);
        private static readonly HashSet<SymbolType> FunctionDeclarationScopeTypeSet = new(FunctionDeclarationScopeTypes);
        private static readonly HashSet<SymbolType> ObjectDeclarationScopeTypeSet = new(ObjectDeclarationScopeTypes);
        private static readonly HashSet<SymbolType> InstanceMethodDeclarationScopeTypeSet = new(InstanceMethodDeclarationScopeTypes);
        private static readonly HashSet<SymbolType> StaticMethodDeclarationScopeTypeSet = new(StaticMethodDeclarationScopeTypes);
        private static readonly HashSet<SymbolType> StatementScopeTypeSet = new(StatementScopeTypes);
        private static readonly HashSet<SymbolType> CodeBlockScopeTypeSet = new(CodeBlockScopeTypes);
        private static readonly HashSet<SymbolType> DeclareBlockScopeTypeSet = new(DeclareBlockScopeTypes);
        private static readonly HashSet<SymbolType> LabelScopeTypeSet = new(LabelScopeTypes);
        private static readonly HashSet<SymbolType> AnonymousFunctionScopeTypeSet = new(AnonymousFunctionScopeTypes);
        private static readonly HashSet<SymbolType> AnonymousObjectDeclarationScopeTypeSet = new(AnonymousObjectDeclarationScopeTypes);

        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> EmptyAllowedChildren = Array.Empty<(SymbolType SymbolType, bool AllowMultiple)>();
        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> RootAllowedChildren = new[]
        {
            (SymbolType.Namespace, true),
            (SymbolType.File, true),
            (SymbolType.AnonymousObjectDeclaration, true),
            (SymbolType.AnonymousFunctionDeclaration, true),
            (SymbolType.BuiltInType, true),
            (SymbolType.BuiltInUtilityType, true),
            (SymbolType.BuiltInFunction, true),
            (SymbolType.MagicConstant, true),
            (SymbolType.Variable, true),
        };
        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> FileAllowedChildren = new[]
        {
            (SymbolType.UseInclude, true),
            (SymbolType.TypeAlias, true),
            (SymbolType.Constant, true),
            (SymbolType.Variable, true),
            (SymbolType.CodeBlock, true),
            (SymbolType.DeclareBlock, true),
            (SymbolType.Label, true),
            (SymbolType.ObjectTypeDeclaration, true),
            (SymbolType.FunctionDeclaration, true),
        };
        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> NamespaceBlockAllowedChildren = new[]
        {
            (SymbolType.UseInclude, true),
            (SymbolType.IncludeTag, true),
            (SymbolType.TypeAlias, true),
            (SymbolType.Constant, true),
            (SymbolType.Variable, true),
            (SymbolType.CodeBlock, true),
            (SymbolType.Statement, true),
            (SymbolType.DeclareBlock, true),
            (SymbolType.Label, true),
            (SymbolType.ObjectTypeDeclaration, true),
            (SymbolType.FunctionDeclaration, true),
            (SymbolType.BuiltInUtilityType, true),
        };
        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> FunctionDeclarationAllowedChildren = new[]
        {
            (SymbolType.FunctionGenericTypeParameter, true),
            (SymbolType.Variable, true),
            (SymbolType.CodeBlock, false),
        };
        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> ObjectDeclarationAllowedChildren = new[]
        {
            (SymbolType.ClassGenericTypeParameter, true),
            (SymbolType.ObjectConstant, true),
            (SymbolType.InstanceObjectProperty, true),
            (SymbolType.StaticObjectProperty, true),
            (SymbolType.ObjectTypeAlias, true),
            (SymbolType.InstanceObjectMethod, true),
            (SymbolType.InstanceObjectAccessorMethod, true),
            (SymbolType.ObjectConstructor, true),
            (SymbolType.ObjectDestructor, true),
            (SymbolType.ObjectMagicCallMethod, true),
            (SymbolType.ObjectMagicGetMethod, true),
            (SymbolType.ObjectMagicSetMethod, true),
            (SymbolType.ObjectMagicIssetMethod, true),
            (SymbolType.ObjectMagicUnsetMethod, true),
            (SymbolType.ObjectMagicSleepMethod, true),
            (SymbolType.ObjectMagicWakeupMethod, true),
            (SymbolType.ObjectMagicSerializeMethod, true),
            (SymbolType.ObjectMagicUnserializeMethod, true),
            (SymbolType.ObjectMagicToStringMethod, true),
            (SymbolType.ObjectMagicInvokeMethod, true),
            (SymbolType.ObjectMagicCloneMethod, true),
            (SymbolType.ObjectMagicDebugInfoMethod, true),
            (SymbolType.StaticObjectMethod, true),
            (SymbolType.StaticObjectAccessorMethod, true),
            (SymbolType.ObjectMagicCallStaticMethod, true),
            (SymbolType.ObjectMagicSetStateMethod, true),
            (SymbolType.ObjectOperatorOverload, true),
        };
        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> MethodDeclarationAllowedChildren = new[]
        {
            (SymbolType.FunctionGenericTypeParameter, true),
            (SymbolType.Variable, true),
            (SymbolType.CodeBlock, false),
        };
        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> StatementAllowedChildren = new[]
        {
            (SymbolType.Variable, true),
        };
        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> CodeBlockAllowedChildren = new[]
        {
            (SymbolType.Variable, true),
            (SymbolType.CodeBlock, true),
            (SymbolType.Statement, true),
            (SymbolType.DeclareBlock, true),
            (SymbolType.Label, true),
            (SymbolType.ObjectTypeDeclaration, true),
            (SymbolType.FunctionDeclaration, true),
        };
        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> DeclareBlockAllowedChildren = new[]
        {
            (SymbolType.CodeBlock, false),
        };
        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> AnonymousFunctionAllowedChildren = new[]
        {
            (SymbolType.FunctionGenericTypeParameter, true),
            (SymbolType.Variable, true),
            (SymbolType.CodeBlock, false),
        };
        private static readonly IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> NamespaceAllowedChildren = new[]
        {
            (SymbolType.NamespaceBlock, true),
        };

        private static readonly IReadOnlyDictionary<SymbolType, IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)>> AllowedChildrenByScope =
            new Dictionary<SymbolType, IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)>>
            {
                [SymbolType.Root] = SymbolTypeHelper.RootAllowedChildren,
                [SymbolType.File] = SymbolTypeHelper.FileAllowedChildren,
                [SymbolType.Namespace] = SymbolTypeHelper.NamespaceAllowedChildren,
                [SymbolType.NamespaceBlock] = SymbolTypeHelper.NamespaceBlockAllowedChildren,
                [SymbolType.FunctionDeclaration] = SymbolTypeHelper.FunctionDeclarationAllowedChildren,
                [SymbolType.ObjectTypeDeclaration] = SymbolTypeHelper.ObjectDeclarationAllowedChildren,
                [SymbolType.CodeBlock] = SymbolTypeHelper.CodeBlockAllowedChildren,
                [SymbolType.DeclareBlock] = SymbolTypeHelper.DeclareBlockAllowedChildren,
                [SymbolType.Statement] = SymbolTypeHelper.StatementAllowedChildren,
                [SymbolType.AnonymousFunctionDeclaration] = SymbolTypeHelper.AnonymousFunctionAllowedChildren,
            };

        private static readonly Dictionary<SymbolType, ScopeType> _scopeTypeBySymbolType = new()
        {
            [SymbolType.Root] = ScopeType.Root,
            [SymbolType.File] = ScopeType.File,
            [SymbolType.Namespace] = ScopeType.Namespace,
            [SymbolType.NamespaceBlock] = ScopeType.NamespaceBlock,
            [SymbolType.FunctionDeclaration] = ScopeType.FunctionDeclaration,
            [SymbolType.AnonymousFunctionDeclaration] = ScopeType.AnonymousFunctionDeclaration,
            [SymbolType.ObjectTypeDeclaration] = ScopeType.ObjectTypeDeclaration,
            [SymbolType.InstanceObjectMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.InstanceObjectAccessorMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectConstructor] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectDestructor] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicCallMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicGetMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicSetMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicIssetMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicUnsetMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicSleepMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicWakeupMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicSerializeMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicUnserializeMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicToStringMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicInvokeMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicCloneMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.ObjectMagicDebugInfoMethod] = ScopeType.InstanceMethodDeclaration,
            [SymbolType.StaticObjectMethod] = ScopeType.StaticMethodDeclaration,
            [SymbolType.StaticObjectAccessorMethod] = ScopeType.StaticMethodDeclaration,
            [SymbolType.ObjectMagicCallStaticMethod] = ScopeType.StaticMethodDeclaration,
            [SymbolType.ObjectMagicSetStateMethod] = ScopeType.StaticMethodDeclaration,
            [SymbolType.ObjectOperatorOverload] = ScopeType.StaticMethodDeclaration,
            [SymbolType.Statement] = ScopeType.Statement,
            [SymbolType.TypeAlias] = ScopeType.Statement,
            [SymbolType.Constant] = ScopeType.Statement,
            [SymbolType.Variable] = ScopeType.Statement,
            [SymbolType.CodeBlock] = ScopeType.CodeBlock,
            [SymbolType.DeclareBlock] = ScopeType.DeclareBlock,
            [SymbolType.Label] = ScopeType.Label,
            [SymbolType.AnonymousObjectDeclaration] = ScopeType.AnonymousObjectDeclaration,
        };

        public static ScopeType GetScopeType(SymbolType symbolType)
        {
            return _scopeTypeBySymbolType.TryGetValue(symbolType, out var scopeType) ? scopeType : ScopeType.Unknown;
        }

        public static IReadOnlyList<SymbolType> GetRootScopeTypes()
        {
            return SymbolTypeHelper.RootScopeTypes;
        }

        public static IReadOnlyList<SymbolType> GetFileScopeTypes()
        {
            return SymbolTypeHelper.FileScopeTypes;
        }

        public static bool IsRootScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.RootScopeTypeSet.Contains(symbolType);
        }

        public static bool IsFileScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.FileScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<SymbolType> GetNamespaceScopeTypes()
        {
            return SymbolTypeHelper.NamespaceScopeTypes;
        }

        public static bool IsNamespaceScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.NamespaceScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<SymbolType> GetNamespaceBlockScopeTypes()
        {
            return SymbolTypeHelper.NamespaceBlockScopeTypes;
        }

        public static bool IsNamespaceBlockScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.NamespaceBlockScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<SymbolType> GetFunctionDeclarationScopeTypes()
        {
            return SymbolTypeHelper.FunctionDeclarationScopeTypes;
        }

        public static bool IsFunctionDeclarationScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.FunctionDeclarationScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<SymbolType> GetObjectDeclarationScopeTypes()
        {
            return SymbolTypeHelper.ObjectDeclarationScopeTypes;
        }

        public static bool IsObjectDeclarationScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.ObjectDeclarationScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<SymbolType> GetInstanceMethodDeclarationScopeTypes()
        {
            return SymbolTypeHelper.InstanceMethodDeclarationScopeTypes;
        }

        public static bool IsInstanceMethodDeclarationScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.InstanceMethodDeclarationScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<SymbolType> GetStaticMethodDeclarationScopeTypes()
        {
            return SymbolTypeHelper.StaticMethodDeclarationScopeTypes;
        }

        public static bool IsStaticMethodDeclarationScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.StaticMethodDeclarationScopeTypeSet.Contains(symbolType);
        }
        
        public static IReadOnlyList<SymbolType> GetStatementScopeTypes()
        {
            return SymbolTypeHelper.StatementScopeTypes;
        }

        public static bool IsStatementScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.StatementScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<SymbolType> GetCodeBlockScopeTypes()
        {
            return SymbolTypeHelper.CodeBlockScopeTypes;
        }

        public static bool IsCodeBlockScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.CodeBlockScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<SymbolType> GetDeclareBlockScopeTypes()
        {
            return SymbolTypeHelper.DeclareBlockScopeTypes;
        }

        public static bool IsDeclareBlockScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.DeclareBlockScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<SymbolType> GetLabelScopeTypes()
        {
            return SymbolTypeHelper.LabelScopeTypes;
        }

        public static bool IsLabelScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.LabelScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<SymbolType> GetAnonymousFunctionScopeTypes()
        {
            return SymbolTypeHelper.AnonymousFunctionScopeTypes;
        }

        public static bool IsAnonymousFunctionScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.AnonymousFunctionScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<SymbolType> GetAnonymousObjectDeclarationScopeTypes()
        {
            return SymbolTypeHelper.AnonymousObjectDeclarationScopeTypes;
        }

        public static bool IsAnonymousObjectDeclarationScope(SymbolType symbolType)
        {
            return SymbolTypeHelper.AnonymousObjectDeclarationScopeTypeSet.Contains(symbolType);
        }

        public static IReadOnlyList<(SymbolType SymbolType, bool AllowMultiple)> GetAllowedChildren(SymbolType symbolType)
        {
            if (SymbolTypeHelper.AllowedChildrenByScope.TryGetValue(symbolType, out var allowedChildren))
            {
                return allowedChildren;
            }

            if (SymbolTypeHelper.IsInstanceMethodDeclarationScope(symbolType))
            {
                return SymbolTypeHelper.MethodDeclarationAllowedChildren;
            }

            if (SymbolTypeHelper.IsStaticMethodDeclarationScope(symbolType))
            {
                return SymbolTypeHelper.MethodDeclarationAllowedChildren;
            }

            return SymbolTypeHelper.EmptyAllowedChildren;
        }
    }
}
