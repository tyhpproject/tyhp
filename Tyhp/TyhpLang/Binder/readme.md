# Binder

There are 2 types of symbols:
- Declarations
    - where a symbol is created
- References
    - referring to an existing symbol

Binder passes:
0. Scopes
    - traverse AST tree and define scopes
        - global
        - namespace
        - file
            - only has use imports as symbols
        - class/interface/enum object instance
            - scope owned by a variable's type
        - class/interface/enum object static
            - similar to namespaced functions and consts and other items
        - class constructor
            - object instance only
        - class destructor
            - object instance only
        - trait/parent obj
            - can be included object instance scopes
        - struct
            - may not be needed
        - extension wrapper
            - just a class with static methods
        - function
            - in namespace scope only
        - instance method
            - in object instance or trait scopes only
        - static method
            - in object static or extension wrapper scopes only
        - operator override
            - in object instance scope only
        - property accessor
            - in class or trait scopes only
        - long anon function (may only be part of variable scoping?)
            - belongs to a variable
            - if defined in object instance scope, it is a method instance scope
            - if defined in object static scope, it is a method static scope
            - scope can be redefined by using \Closure::bind() method
                - howto handle dynamic scope????
        - short anon function (may only be part of variable scoping?)
            - belongs to a variable
            - inherits variable scope, like an `if` block would
            - if defined in object instance scope, it is a method instance scope
            - if defined in object static scope, it is a method static scope
            - scope can be redefined by using \Closure::bind() method
                - howto handle dynamic scope????
1. Symbol declarations - find all symbol declarations, build global symbol list
    - error on duplicate symbol
    - not variables/parameters/properties
        - file (.tyhp file, useful for scoping)
        - class decl
        - interface decl
        - enum decl
        - trait decl
        - const decl
        - class const
        - type alias decl
        - class type alias decl
        - struct decl
        - extension function decl
        - extension wrapper decl
        - function decl
        - instance method decl
        - static method decl
        - operator override decl
        - property accessor decl
        - trait adaptation decl
        - enum case decl
        - class generic parameter decl
        - object type imports (use statements)
        - function imports (use statements)
        - const imports (use statements)
        - type alias import (use statement)
        - magic constant (__FILE__, __LINE__, etc.)
        - super globals
        - global decl (global keyword inside of function/method)
        - label
        - built in type
        - trait property adaptation (property accessor)
        - namespace (maybe not, since this can overlap anywhere)
    - can be scoped to parent decl symbol or file symbol, or global
    - how to define symbol list?????
        - single master list?
            - easy ot lookup items
        - multiple lists that are nested to define scopes?
            - easy to scope items and see hierarchy
            - *** I think this may be best!!!
    - links to declaring AST node
3. Symbol references = associate all symbol references to the appropriate declaration
    - error on undefined symbol
    - object type imports (use statements) (yes, this is both a symbol and a reference even if not aliased)
    - function imports (use statements) (yes, this is both a symbol and a reference even if not aliased)
    - const imports (use statements) (yes, this is both a symbol and a reference even if not aliased)
    - type alias import (use statement) (yes, this is both a symbol and a reference even if not aliased)
    - AST node the references symbol(s) holds link(s) to symbol(s)
    - AST node can refernce more than one symbol and it is up to the AST node to handle that
    - AS node should lookup symbols and track internally and report when symbol not found or symbol incompatible, etc.



Scope/Symbol tree layout:
    + ScopeType.Root - no symbol
        + ScopeType.Namespace[]
        + ScopeType.AnonymousObjectDeclaration[]
        + ScopeType.AnonymousFunction[]
        + SymbolType.MagicConstant[] - only on the root namespace
        + SymbolType.BuiltInType[] - only on the root namespace
        + SymbolType.SuperGlobal

    + ScopeType.Namespace - SymbolType.Namespace
        + ScopeType.NamespaceBlock[]

    - ScopeType.NamespaceBlock - SymbolType.NamespaceBlock
        + SymbolType.UseInclude[] - use (include) statements (restricted to use only in ns block)
        + SymbolType.TypeAlias[] - type aliases (can be used outside of ns block like any other type)
        + SymbolType.Constant[]
        + SymbolType.Variable[]
        + ScopeType.CodeBlock[]
            + ScopeType.ObjectDeclaration[] - only if in this scope path
            + ScopeType.FunctionDeclaration[] - only if in this scope path
        + ScopeType.DeclareBlock[]
        + ScopeType.Label[]
        + ScopeType.ObjectDeclaration[]
        + ScopeType.FunctionDeclaration[]

    + ScopeType.FunctionDeclaration - SymbolType.FunctionDeclaration
        + SymbolType.GenericTypeParameter[]
        + SymbolType.Variable[] - parameters
        + ScopeType.CodeBlock - single for the function body

    - ScopeType.ObjectDeclaration - SymbolType.ObjectDeclaration
        + SymbolType.GenericTypeParameter[]
        + SymbolType.ObjectConstant[] - can be direct constant, or ancestor constant
        + SymbolType.ObjectProperty[] - can be direct property, or accessor property, or trait adaptation
        + SymbolType.ObjectTypeAlias[] - can be direct type alias, or ancestor type alias
        - ScopeType.InstanceMethod[]
        - ScopeType.StaticMethod[]

    + ScopeType.InstanceMethod - SymbolType.ObjectAccessorMethod|SymbolType.ObjectMethod|SymbolType.ObjectConstructor|SymbolType.ObjectDestructor|SymbolType.ObjectMagicCallMethod|SymbolType.ObjectMagicGetMethod|SymbolType.ObjectMagicSetMethod|SymbolType.ObjectMagicIssetMethod|SymbolType.ObjectMagicUnsetMethod|SymbolType.ObjectMagicSleepMethod|SymbolType.ObjectMagicWakeupMethod|SymbolType.ObjectMagicSerializeMethod|SymbolType.ObjectMagicUnserializeMethod|SymbolType.ObjectMagicToStringMethod|SymbolType.ObjectMagicInvokeMethod|SymbolType.ObjectMagicCloneMethod|SymbolType.ObjectMagicDebugInfoMethod
        + SymbolType.GenericTypeParameter[]
        + SymbolType.Variable[] - parameters
        + ScopeType.CodeBlock - single for the method body

    - ScopeType.StaticMethod - SymbolType.ObjectAccessorMethod|SymbolType.ObjectMethod|SymbolType.ObjectOperatorOverload|SymbolType.ObjectMagicCallStaticMethod|SymbolType.ObjectMagicSetStateMethod
        + SymbolType.GenericTypeParameter[]
        + SymbolType.Variable[] - parameters
        - ScopeType.CodeBlock - single for the method body

    - ScopeType.CodeBlock - SymbolType.CodeBlock
        + SymbolType.Constant[]
        + SymbolType.Variable[]
        + ScopeType.CodeBlock[]
        + ScopeType.DeclareBlock[]
        + ScopeType.Label[]
        - ! additional sub scopes are possible based on scope path

    + ScopeType.AnonymousFunction - SymbolType.AnonymousFunction
        + SymbolType.GenericTypeParameter[]
        + SymbolType.Variable[] - parameters, and use() imports
        + ScopeType.CodeBlock - single for the function body

    + ScopeType.DeclareBlock - extends ScopeType.CodeBlock

    + ScopeType.Label - SymbolType.Label
        + ! no sub scopes, this is to allow for goto statements to jump to the label