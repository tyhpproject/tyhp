using Tyhp.TyhpLang.Binder;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Tyhp")]
public class PhpReservedFunctionNamesTests
{
    // PHP's "other reserved words" (self, parent, int, string, float, bool, true, false, null,
    // void, iterable, object, mixed, never, readonly, enum, …) only restrict class/interface/
    // trait/enum names — they are all valid PHP *function* names. Only names below were verified
    // against a real PHP CLI (`php -l`) to fail parsing as `function <name>() {}`.
    [Theory]
    [InlineData("if")]
    [InlineData("match")]
    [InlineData("enddeclare")]
    [InlineData("list")]
    [InlineData("array")]
    [InlineData("callable")]
    [InlineData("fn")]
    [InlineData("yield")]
    [InlineData("empty")]
    [InlineData("isset")]
    [InlineData("unset")]
    [InlineData("eval")]
    [InlineData("die")]
    [InlineData("exit")]
    [InlineData("static")]
    [InlineData("__halt_compiler")]
    [InlineData("IF")] // case-insensitive
    public void CannotBePhpFunction_TrueForRealPhpKeywords(string name)
    {
        PhpReservedFunctionNames.CannotBePhpFunction(name).Should().BeTrue(
            $"'{name}' cannot be declared as a PHP function");
    }

    // These are PHP's "other reserved words" (class/interface/trait/enum-only restrictions),
    // Tyhp-only keywords (async, using, operator, struct), or not reserved at all — every one
    // parses and runs as `function <name>() {}` in PHP 8.5.
    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("float")]
    [InlineData("bool")]
    [InlineData("mixed")]
    [InlineData("void")]
    [InlineData("never")]
    [InlineData("iterable")]
    [InlineData("object")]
    [InlineData("self")]
    [InlineData("parent")]
    [InlineData("readonly")]
    [InlineData("enum")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("async")]
    [InlineData("await")]
    [InlineData("struct")]
    [InlineData("using")]
    [InlineData("operator")]
    [InlineData("decimal")]
    [InlineData("this")]
    public void CannotBePhpFunction_FalseForNamesUsablePhpFunctionNames(string name)
    {
        PhpReservedFunctionNames.CannotBePhpFunction(name).Should().BeFalse(
            $"'{name}' is a valid PHP function name");
    }
}
