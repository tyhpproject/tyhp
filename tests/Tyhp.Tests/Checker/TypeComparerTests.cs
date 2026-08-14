using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.Binder;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class TypeComparerTests
{
    private readonly SymbolTree _symbolTree;
    private readonly GlobalScope _globalScope;

    public TypeComparerTests()
    {
        _symbolTree = new SymbolTree(new SymbolIdentifier([]));
        _globalScope = _symbolTree.GlobalScope;
        Types.PopulateGlobal(_globalScope);
    }

    private ICheckedType BuiltIn(string name) =>
        CheckedTypes.FromSymbol(TypeComparer.ResolveBuiltIn(name, _globalScope)!);

    [Fact]
    public void IsAssignableTo_SameScalarTypes_ReturnsTrue()
    {
        TypeComparer.IsAssignableTo(CheckedTypes.Int, CheckedTypes.Int, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_IntToFloat_ReturnsTrue()
    {
        TypeComparer.IsAssignableTo(CheckedTypes.Int, CheckedTypes.Float, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_StringToInt_ReturnsFalse()
    {
        TypeComparer.IsAssignableTo(CheckedTypes.String, CheckedTypes.Int, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_NullToNullableString_ReturnsTrue()
    {
        var nullableString = new NullableCheckedType(CheckedTypes.String);
        TypeComparer.IsAssignableTo(CheckedTypes.Null, nullableString, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_AnyTypeToMixed_ReturnsTrue()
    {
        TypeComparer.IsAssignableTo(CheckedTypes.Int, CheckedTypes.Mixed, _symbolTree, _globalScope)
            .Should().BeTrue();
        TypeComparer.IsAssignableTo(CheckedTypes.String, CheckedTypes.Mixed, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_NeverToAnyType_ReturnsTrue()
    {
        TypeComparer.IsAssignableTo(CheckedTypes.Never, CheckedTypes.String, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_MixedToString_ReturnsFalse()
    {
        TypeComparer.IsAssignableTo(CheckedTypes.Mixed, CheckedTypes.String, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_UnionTarget_IntAssignable()
    {
        var union = new UnionCheckedType([CheckedTypes.Int, CheckedTypes.String]);
        TypeComparer.IsAssignableTo(CheckedTypes.Int, union, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_UnionSource_ToInt_ReturnsFalse()
    {
        var union = new UnionCheckedType([CheckedTypes.Int, CheckedTypes.String]);
        TypeComparer.IsAssignableTo(union, CheckedTypes.Int, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_IntersectionSource_ToMember_ReturnsTrue()
    {
        var childClass = new ObjectDeclarationSymbol("Child");
        var intersection = new IntersectionCheckedType([CheckedTypes.FromSymbol(childClass), CheckedTypes.String]);
        TypeComparer.IsAssignableTo(intersection, CheckedTypes.FromSymbol(childClass), _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_ArrayToIterable_ReturnsTrue()
    {
        var array = BuiltIn("array");
        var iterable = BuiltIn("iterable");
        TypeComparer.IsAssignableTo(array, iterable, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_IterableToArray_ReturnsFalse()
    {
        var array = BuiltIn("array");
        var iterable = BuiltIn("iterable");
        TypeComparer.IsAssignableTo(iterable, array, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_GenericArrayToGenericIterable_ReturnsTrue()
    {
        var arrayBase = BuiltIn("array");
        var iterableBase = BuiltIn("iterable");
        var array = new GenericCheckedType(arrayBase, [CheckedTypes.Int, CheckedTypes.String]);
        var iterable = new GenericCheckedType(iterableBase, [CheckedTypes.Int, CheckedTypes.String]);
        TypeComparer.IsAssignableTo(array, iterable, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_SameGenericDeclaration_IncompatibleTypeArgs_ReturnsFalse()
    {
        // FOUND generic structs 2026-08-05 §1 / CHECKER_GAPS P0 #1: same declaration must not
        // fall through to declaration-only ImplementsOrExtends and accept Box<string> as Box<int>.
        var box = new ObjectDeclarationSymbol("Box");
        box.GenericParameters.Add(
            new GenericTypeParameterSymbol("T", SymbolType.ClassGenericTypeParameter));
        var boxBase = CheckedTypes.FromSymbol(box);
        var boxOfString = new GenericCheckedType(boxBase, [CheckedTypes.String]);
        var boxOfInt = new GenericCheckedType(boxBase, [CheckedTypes.Int]);

        TypeComparer.IsAssignableTo(boxOfString, boxOfInt, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_SameGenericDeclaration_MatchingTypeArgs_ReturnsTrue()
    {
        var box = new ObjectDeclarationSymbol("Box");
        box.GenericParameters.Add(
            new GenericTypeParameterSymbol("T", SymbolType.ClassGenericTypeParameter));
        var boxBase = CheckedTypes.FromSymbol(box);
        var boxOfInt = new GenericCheckedType(boxBase, [CheckedTypes.Int]);
        var alsoBoxOfInt = new GenericCheckedType(boxBase, [CheckedTypes.Int]);

        TypeComparer.IsAssignableTo(boxOfInt, alsoBoxOfInt, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_SameGenericDeclaration_ToMixed_ReturnsTrue()
    {
        // P0 #1 follow-up: G<T> → G<mixed> for heterogeneous bags (e.g. PropertyAccessor).
        var box = new ObjectDeclarationSymbol("Box");
        box.GenericParameters.Add(
            new GenericTypeParameterSymbol("T", SymbolType.ClassGenericTypeParameter));
        var boxBase = CheckedTypes.FromSymbol(box);
        var boxOfString = new GenericCheckedType(boxBase, [CheckedTypes.String]);
        var boxOfMixed = new GenericCheckedType(boxBase, [CheckedTypes.Mixed]);

        TypeComparer.IsAssignableTo(boxOfString, boxOfMixed, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_SameGenericDeclaration_FromMixed_ReturnsFalse()
    {
        var box = new ObjectDeclarationSymbol("Box");
        box.GenericParameters.Add(
            new GenericTypeParameterSymbol("T", SymbolType.ClassGenericTypeParameter));
        var boxBase = CheckedTypes.FromSymbol(box);
        var boxOfMixed = new GenericCheckedType(boxBase, [CheckedTypes.Mixed]);
        var boxOfString = new GenericCheckedType(boxBase, [CheckedTypes.String]);

        TypeComparer.IsAssignableTo(boxOfMixed, boxOfString, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_SameGenericDeclaration_VoidToMixed_ReturnsFalse()
    {
        var box = new ObjectDeclarationSymbol("Box");
        box.GenericParameters.Add(
            new GenericTypeParameterSymbol("T", SymbolType.ClassGenericTypeParameter));
        var boxBase = CheckedTypes.FromSymbol(box);
        var boxOfVoid = new GenericCheckedType(boxBase, [CheckedTypes.Void]);
        var boxOfMixed = new GenericCheckedType(boxBase, [CheckedTypes.Mixed]);

        TypeComparer.IsAssignableTo(boxOfVoid, boxOfMixed, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_SameGenericDeclaration_NeverToMixed_ReturnsFalse()
    {
        var box = new ObjectDeclarationSymbol("Box");
        box.GenericParameters.Add(
            new GenericTypeParameterSymbol("T", SymbolType.ClassGenericTypeParameter));
        var boxBase = CheckedTypes.FromSymbol(box);
        var boxOfNever = new GenericCheckedType(boxBase, [CheckedTypes.Never]);
        var boxOfMixed = new GenericCheckedType(boxBase, [CheckedTypes.Mixed]);

        TypeComparer.IsAssignableTo(boxOfNever, boxOfMixed, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_TypedArray_CovariantValueArgs_ReturnsTrue()
    {
        // User generics are invariant; array/iterable remain covariant for assignability.
        var arrayBase = BuiltIn("array");
        var source = new GenericCheckedType(arrayBase, [CheckedTypes.Int, CheckedTypes.Int]);
        var target = new GenericCheckedType(
            arrayBase,
            [CheckedTypes.Int, new UnionCheckedType([CheckedTypes.Int, CheckedTypes.String])]);

        TypeComparer.IsAssignableTo(source, target, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_ClosureToCallable_ReturnsTrue()
    {
        var closure = new ObjectDeclarationSymbol("Closure");
        var callable = BuiltIn("callable");
        TypeComparer.IsAssignableTo(CheckedTypes.FromSymbol(closure), callable, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_StringToCallable_ReturnsFalse()
    {
        var callable = BuiltIn("callable");
        TypeComparer.IsAssignableTo(CheckedTypes.String, callable, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_ArrayToCallable_ReturnsFalse()
    {
        var array = BuiltIn("array");
        var callable = BuiltIn("callable");
        TypeComparer.IsAssignableTo(array, callable, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_TypedCallableToUntypedCallable_ReturnsTrue()
    {
        var untyped = BuiltIn("callable");
        var typed = new CallableCheckedType([CheckedTypes.String], CheckedTypes.Int);
        TypeComparer.IsAssignableTo(typed, untyped, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_ClassToObject_ReturnsTrue()
    {
        var child = new ObjectDeclarationSymbol("Child");
        var objectType = BuiltIn("object");
        TypeComparer.IsAssignableTo(CheckedTypes.FromSymbol(child), objectType, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_ObjectToClass_ReturnsFalse()
    {
        var child = new ObjectDeclarationSymbol("Child");
        var objectType = BuiltIn("object");
        TypeComparer.IsAssignableTo(objectType, CheckedTypes.FromSymbol(child), _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_TrueLiteralToBool_ReturnsTrue()
    {
        var trueLiteral = new LiteralCheckedType(true, new SimpleCheckedType(new BuiltInTypeSymbol("true")));
        TypeComparer.IsAssignableTo(trueLiteral, CheckedTypes.Bool, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_BoolToTrueLiteral_ReturnsFalse()
    {
        var trueLiteral = new LiteralCheckedType(true, new SimpleCheckedType(new BuiltInTypeSymbol("true")));
        TypeComparer.IsAssignableTo(CheckedTypes.Bool, trueLiteral, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_StructToCompatibleArray_ReturnsTrue()
    {
        var structType = StructCheckedType.FromMutableProperties(new Dictionary<string, ICheckedType>
        {
            ["name"] = CheckedTypes.String,
            ["age"] = CheckedTypes.Int,
        });
        var arrayBase = BuiltIn("array");
        var target = new GenericCheckedType(arrayBase, [CheckedTypes.String, new UnionCheckedType([CheckedTypes.String, CheckedTypes.Int])]);

        TypeComparer.IsAssignableTo(structType, target, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_StructShapeToBareArray_ReturnsTrue()
    {
        var structType = StructCheckedType.FromMutableProperties(new Dictionary<string, ICheckedType>
        {
            ["name"] = CheckedTypes.String,
        });

        TypeComparer.IsAssignableTo(structType, BuiltIn("array"), _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_NamedStructToBareArray_ReturnsTrue()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/binder/struct_inheritance.tyhp");
        var (global, diagnostics) = BinderTestHelper.BindFile(path);

        diagnostics.HasErrors.Should().BeFalse();
        global.Should().NotBeNull();

        var declarations = EnumerateScopes((IBaseScope)global!)
            .SelectMany(scope => scope.GetAllChildSymbols())
            .OfType<ObjectDeclarationSymbol>()
            .ToList();

        var pointLike = declarations.First(symbol => symbol.Name == "SerializedExpression");
        var symbolTree = new SymbolTree(global!);
        var array = CheckedTypes.FromSymbol(TypeComparer.ResolveBuiltIn("array", global!)!);

        TypeComparer.IsAssignableTo(CheckedTypes.FromSymbol(pointLike), array, symbolTree, global!)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_GenericArrayToBareArray_ReturnsTrue()
    {
        var arrayBase = BuiltIn("array");
        var typed = new GenericCheckedType(arrayBase, [CheckedTypes.String, CheckedTypes.Int]);

        TypeComparer.IsAssignableTo(typed, BuiltIn("array"), _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_BareArrayToGenericArray_ReturnsTrue()
    {
        var arrayBase = BuiltIn("array");
        var typed = new GenericCheckedType(arrayBase, [CheckedTypes.Int, CheckedTypes.String]);

        TypeComparer.IsAssignableTo(BuiltIn("array"), typed, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_StructToIncompatibleArrayValue_ReturnsFalse()
    {
        var structType = StructCheckedType.FromMutableProperties(new Dictionary<string, ICheckedType>
        {
            ["name"] = CheckedTypes.String,
            ["age"] = CheckedTypes.Int,
        });
        var arrayBase = BuiltIn("array");
        var target = new GenericCheckedType(arrayBase, [CheckedTypes.String, CheckedTypes.String]);

        TypeComparer.IsAssignableTo(structType, target, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_StructToIntKeyedArray_ReturnsFalse()
    {
        var structType = StructCheckedType.FromMutableProperties(new Dictionary<string, ICheckedType>
        {
            ["name"] = CheckedTypes.String,
        });
        var arrayBase = BuiltIn("array");
        var target = new GenericCheckedType(arrayBase, [CheckedTypes.Int, CheckedTypes.Mixed]);

        TypeComparer.IsAssignableTo(structType, target, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_PositionalStructToIntKeyedArray_ReturnsTrue()
    {
        var target = new GenericCheckedType(BuiltIn("array"), [CheckedTypes.Int, CheckedTypes.Mixed]);

        TypeComparer.IsAssignableTo(PositionalStruct(), target, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_PositionalStructToStringKeyedArray_ReturnsFalse()
    {
        var target = new GenericCheckedType(BuiltIn("array"), [CheckedTypes.String, CheckedTypes.Mixed]);

        TypeComparer.IsAssignableTo(PositionalStruct(), target, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_PositionalStructToArrayShorthand_ReturnsTrue()
    {
        // `array<V>` normalizes to an `int|string` key, which admits positional and named keys.
        var target = new GenericCheckedType(
            BuiltIn("array"),
            [new UnionCheckedType([CheckedTypes.Int, CheckedTypes.String]), CheckedTypes.Mixed]);

        TypeComparer.IsAssignableTo(PositionalStruct(), target, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    /// <summary>
    /// A <c>CallableArgs*</c> / <c>__CallableParametersTuple</c> shape: properties carry int PHP
    /// array keys rather than their <c>$_N</c> names.
    /// </summary>
    private static StructCheckedType PositionalStruct() =>
        new(new Dictionary<string, StructPropertyInfo>
        {
            ["$_1"] = new(CheckedTypes.String, IntegerKeyAlias: 0),
            ["$_2"] = new(CheckedTypes.Int, IntegerKeyAlias: 1),
        });

    [Fact]
    public void IsAssignableTo_SourceMissingOptionalStructKey_ReturnsTrue()
    {
        var source = new StructCheckedType(new Dictionary<string, StructPropertyInfo>
        {
            ["$name"] = new(CheckedTypes.String),
        });
        var target = new StructCheckedType(new Dictionary<string, StructPropertyInfo>
        {
            ["$name"] = new(CheckedTypes.String),
            ["$age"] = new(CheckedTypes.Int, IsOptional: true),
        });

        TypeComparer.IsAssignableTo(source, target, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAssignableTo_SourceMissingRequiredStructKey_ReturnsFalse()
    {
        var source = new StructCheckedType(new Dictionary<string, StructPropertyInfo>
        {
            ["$age"] = new(CheckedTypes.Int, IsOptional: true),
        });
        var target = new StructCheckedType(new Dictionary<string, StructPropertyInfo>
        {
            ["$name"] = new(CheckedTypes.String),
            ["$age"] = new(CheckedTypes.Int, IsOptional: true),
        });

        TypeComparer.IsAssignableTo(source, target, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_OptionalSourceProperty_DoesNotSatisfyRequiredTarget()
    {
        var source = new StructCheckedType(new Dictionary<string, StructPropertyInfo>
        {
            ["$name"] = new(CheckedTypes.String),
            ["$age"] = new(CheckedTypes.Int, IsOptional: true),
        });
        var target = new StructCheckedType(new Dictionary<string, StructPropertyInfo>
        {
            ["$name"] = new(CheckedTypes.String),
            ["$age"] = new(CheckedTypes.Int),
        });

        TypeComparer.IsAssignableTo(source, target, _symbolTree, _globalScope)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAssignableTo_RequiredSourceProperty_SatisfiesOptionalTarget()
    {
        var source = new StructCheckedType(new Dictionary<string, StructPropertyInfo>
        {
            ["$name"] = new(CheckedTypes.String),
            ["$age"] = new(CheckedTypes.Int),
        });
        var target = new StructCheckedType(new Dictionary<string, StructPropertyInfo>
        {
            ["$name"] = new(CheckedTypes.String),
            ["$age"] = new(CheckedTypes.Int, IsOptional: true),
        });

        TypeComparer.IsAssignableTo(source, target, _symbolTree, _globalScope)
            .Should().BeTrue();
    }

    [Fact]
    public void AreTypesEqual_OptionalFlagDistinguishesStructShapes()
    {
        var required = new StructCheckedType(new Dictionary<string, StructPropertyInfo>
        {
            ["$age"] = new(CheckedTypes.Int),
        });
        var optional = new StructCheckedType(new Dictionary<string, StructPropertyInfo>
        {
            ["$age"] = new(CheckedTypes.Int, IsOptional: true),
        });

        TypeComparer.AreTypesEqual(required, optional).Should().BeFalse();
        TypeComparer.AreTypesEqual(optional, optional).Should().BeTrue();
    }

    [Fact]
    public void IsSubtypeOf_ChildStructToParent_ReturnsTrue()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/binder/struct_inheritance.tyhp");
        var (global, diagnostics) = BinderTestHelper.BindFile(path);

        diagnostics.HasErrors.Should().BeFalse();
        global.Should().NotBeNull();

        var declarations = EnumerateScopes((IBaseScope)global!)
            .SelectMany(scope => scope.GetAllChildSymbols())
            .OfType<ObjectDeclarationSymbol>()
            .ToList();

        var parent = declarations.First(symbol => symbol.Name == "SerializedExpression");
        var child = declarations.First(symbol => symbol.Name == "SerializedParameterExpression");

        var symbolTree = new SymbolTree(global!);
        TypeComparer.IsSubtypeOf(CheckedTypes.FromSymbol(child), CheckedTypes.FromSymbol(parent), symbolTree, global!)
            .Should().BeTrue();
        TypeComparer.IsAssignableTo(CheckedTypes.FromSymbol(child), CheckedTypes.FromSymbol(parent), symbolTree, global!)
            .Should().BeTrue();
    }

    [Fact]
    public void IsSubtypeOf_ChildClassToParent_ReturnsTrue()
    {
        var path = Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp/binder/class_inheritance.tyhp");
        var (global, diagnostics) = BinderTestHelper.BindFile(path);

        diagnostics.HasErrors.Should().BeFalse();
        global.Should().NotBeNull();

        var declarations = EnumerateScopes((IBaseScope)global!)
            .SelectMany(scope => scope.GetAllChildSymbols())
            .OfType<ObjectDeclarationSymbol>()
            .ToList();

        var parent = declarations.First(symbol => symbol.Name == "Animal");
        var child = declarations.First(symbol => symbol.Name == "Dog");

        var symbolTree = new SymbolTree(global!);
        TypeComparer.IsSubtypeOf(CheckedTypes.FromSymbol(child), CheckedTypes.FromSymbol(parent), symbolTree, global!)
            .Should().BeTrue();
    }

    private static IEnumerable<IBaseScope> EnumerateScopes(IBaseScope root)
    {
        yield return root;
        foreach (var child in root.GetAllChildScopes())
        {
            foreach (var descendant in EnumerateScopes(child))
            {
                yield return descendant;
            }
        }
    }

    [Fact]
    public void UnionTypes_TrueAndFalse_SimplifiesToBool()
    {
        var trueType = new LiteralCheckedType(true, new SimpleCheckedType(new BuiltInTypeSymbol("true")));
        var falseType = new LiteralCheckedType(false, new SimpleCheckedType(new BuiltInTypeSymbol("false")));
        var union = TypeComparer.UnionTypes(trueType, falseType, _symbolTree, _globalScope);
        union.Should().Be(CheckedTypes.Bool);
    }

    [Fact]
    public void UnionTypes_TrueFalseAndOtherType_KeepsOtherMember()
    {
        var trueType = new LiteralCheckedType(true, new SimpleCheckedType(new BuiltInTypeSymbol("true")));
        var falseType = new LiteralCheckedType(false, new SimpleCheckedType(new BuiltInTypeSymbol("false")));
        var union = TypeComparer.UnionTypes(
            new[] { trueType, falseType, CheckedTypes.Int },
            _symbolTree,
            _globalScope);

        union.Should().BeOfType<UnionCheckedType>();
        var members = ((UnionCheckedType)union).Members;
        members.Should().Contain(member => TypeComparer.AreTypesEqual(member, CheckedTypes.Bool));
        members.Should().Contain(member => TypeComparer.AreTypesEqual(member, CheckedTypes.Int));
        members.Count.Should().Be(2);
    }

    [Fact]
    public void NarrowType_UnionContainingNarrowType_ExtractsMember()
    {
        var union = new UnionCheckedType([CheckedTypes.Int, CheckedTypes.String]);
        var narrowed = TypeComparer.NarrowType(union, CheckedTypes.String, _symbolTree, _globalScope);
        narrowed.Should().Be(CheckedTypes.String);
    }

    [Fact]
    public void NarrowType_NullableToNonNull_RemovesNull()
    {
        var nullable = new NullableCheckedType(CheckedTypes.String);
        var narrowed = TypeComparer.NarrowType(nullable, CheckedTypes.String, _symbolTree, _globalScope);
        narrowed.Should().Be(CheckedTypes.String);
    }

    [Fact]
    public void ResolveGenericType_SubstitutesTypeParameter()
    {
        var typeParam = new GenericTypeParameterSymbol("T", SymbolType.ClassGenericTypeParameter);
        var generic = new SimpleCheckedType(typeParam);
        var resolved = TypeComparer.ResolveGenericType(
            generic,
            new Dictionary<string, ICheckedType> { ["T"] = CheckedTypes.Int },
            _symbolTree,
            _globalScope);

        resolved.Should().Be(CheckedTypes.Int);
    }

    [Fact]
    public void AreTypesEqual_UnionOrderIndependent()
    {
        var left = new UnionCheckedType([CheckedTypes.Int, CheckedTypes.String]);
        var right = new UnionCheckedType([CheckedTypes.String, CheckedTypes.Int]);
        TypeComparer.AreTypesEqual(left, right).Should().BeTrue();
    }

    [Fact]
    public void TyhpChecker_CheckAssignment_ReportsTypeMismatch()
    {
        var diagnostics = new DiagnosticBag();
        var checker = new TyhpChecker(diagnostics, _symbolTree, _globalScope);
        var node = ErrorAst.Create("x", MessageCode.VisitorUnknownError, 3, 4);

        checker.CheckAssignment(node, CheckedTypes.Int, CheckedTypes.String, "assignment");

        diagnostics.Errors.Should().ContainSingle(e => e.Code == MessageCode.CheckerTypeMismatch);
    }
}
