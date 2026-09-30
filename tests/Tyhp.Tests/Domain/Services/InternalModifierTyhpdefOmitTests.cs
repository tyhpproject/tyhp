using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

/// <summary>
/// Public compiled-library tyhpdef omit for Tyhp <c>internal</c>. The generator skip is owned by
/// Phase 3; this asserts the Phase 4 <c>IsInternal</c> flag is honored when that skip is wired.
/// </summary>
[Trait("Category", "Tyhpdef")]
public class InternalModifierTyhpdefOmitTests
{
    [Fact]
    public void Library_PublicTyhpdef_OmitsInternalTypesAndMembers()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("api.tyhp", """
                <?tyhp
                namespace Lib;
                internal class Hidden {}
                class Visible {
                    internal function secret(): void {}
                    public function ok(): void {}
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var defPath = Path.Combine(project.ProjectDirectory, "package.tyhpdef");
        File.Exists(defPath).Should().BeTrue("compiled library tyhpdef should write the public package.tyhpdef");
        var def = File.ReadAllText(defPath);
        def.Should().NotContain("class Hidden");
        def.Should().Contain("class Visible");
        def.Should().NotContain("function secret(");
        def.Should().Contain("function ok(");

        File.Exists(Path.Combine(project.ProjectDirectory, "package.internal.tyhpdef")).Should().BeFalse();
        File.Exists(Path.Combine(project.ProjectDirectory, "build", "package.internal.tyhpdef")).Should().BeFalse();
    }
}
