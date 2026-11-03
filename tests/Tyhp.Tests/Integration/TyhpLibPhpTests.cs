namespace Tyhp.Tests.Integration;

[Trait("Category", "PHP")]
[Trait("Category", "Integration")]
public class TyhpLibPhpTests
{
    [Fact(Skip = "C# tests do not run runtime-package PHPUnit. Run PHPUnit per package under tyhp-runtime-src/packages/tyhp/ (FOUND_BUGS.md item on PhpUnit_RuntimePackages_AllPass).")]
    public void PhpUnit_RuntimePackages_AllPass()
    {
    }
}
