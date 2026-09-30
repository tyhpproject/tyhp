using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class RuntimePackagesRootDiscoveryTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public void Environment_PrefersPackagesChild()
    {
        var root = this.CreateTree(withPackagesChild: true, withSibling: false, withInTree: true);

        var resolved = ComposerJsonService.ResolveRuntimePackagesRoot(
            root,
            [Path.Combine(root, "nested")]);

        resolved.Should().Be(Path.GetFullPath(Path.Combine(root, "packages")));
    }

    [Fact]
    public void Environment_UsesDirectoryWhenItIsAlreadyThePackagesRoot()
    {
        var root = this.CreateTree(withPackagesChild: true, withSibling: false, withInTree: false);
        var packages = Path.Combine(root, "packages");

        var resolved = ComposerJsonService.ResolveRuntimePackagesRoot(packages, [root]);

        resolved.Should().Be(Path.GetFullPath(packages));
    }

    [Fact]
    public void SiblingCheckout_IsUsedWhenEnvironmentIsUnset()
    {
        var compiler = this.CreateTree(withPackagesChild: false, withSibling: true, withInTree: true);

        var resolved = ComposerJsonService.ResolveRuntimePackagesRoot(
            runtimeSrcEnvironment: null,
            [Path.Combine(compiler, "bin", "Debug")]);

        resolved.Should().Be(Path.GetFullPath(Path.Combine(compiler, "..", "tyhp-runtime-src", "packages")));
    }

    [Fact]
    public void InTreeRuntimePackages_IsNotRequired()
    {
        var compiler = this.CreateTree(withPackagesChild: false, withSibling: false, withInTree: true);

        var resolved = ComposerJsonService.ResolveRuntimePackagesRoot(
            runtimeSrcEnvironment: " ",
            [compiler]);

        resolved.Should().BeNull();
    }

    [Fact]
    public void MissingEnvironment_FallsThroughToSibling()
    {
        var compiler = this.CreateTree(withPackagesChild: false, withSibling: true, withInTree: false);

        var resolved = ComposerJsonService.ResolveRuntimePackagesRoot(
            "/this/path/does/not/exist",
            [compiler]);

        resolved.Should().Be(Path.GetFullPath(Path.Combine(compiler, "..", "tyhp-runtime-src", "packages")));
    }

    [Fact]
    public void RuntimePackagePaths_TreatResolvedRootAndInTreeAsSources()
    {
        var root = this.CreateTree(withPackagesChild: true, withSibling: false, withInTree: false);
        var packages = Path.GetFullPath(Path.Combine(root, "packages"));

        RuntimePackagePaths.IsRuntimePackageSource(
            Path.Combine(packages, "core", "tyhp_src", "StringHelper.tyhp"),
            packages).Should().BeTrue();
        RuntimePackagePaths.IsRuntimePackageSource(
            "/repo/runtime/packages/core/tyhp_src/StringHelper.tyhp",
            runtimePackagesRoot: null).Should().BeTrue();
        RuntimePackagePaths.IsRuntimePackageSource(
            "/work/app/src/Widget.tyhp",
            packages).Should().BeFalse();
        RuntimePackagePaths.IsEnginePhpPackageSource(
            Path.Combine(packages, "php", "_tyhpdef", "ExtCore.tyhpdef"),
            packages).Should().BeTrue();
        RuntimePackagePaths.IsEnginePhpPackageSource(
            "/opt/tyhp-runtime-src/packages/php/_tyhpdef/ExtCore.tyhpdef",
            runtimePackagesRoot: null).Should().BeTrue();
        RuntimePackagePaths.IsEnginePhpPackageSource(
            Path.Combine(packages, "core", "tyhp_src", "StringHelper.tyhp"),
            packages).Should().BeFalse();
        EmittedFqnHelper.IsExternalDeclarationSource(
            "/repo/tyhp-runtime-src/packages/async/tyhp_src/EventLoop.tyhp").Should().BeTrue();
    }

    public void Dispose()
    {
        foreach (var directory in this._tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private string CreateTree(bool withPackagesChild, bool withSibling, bool withInTree)
    {
        var parent = Path.Combine(Path.GetTempPath(), "tyhp-runtime-root", Guid.NewGuid().ToString("N"));
        var compiler = Path.Combine(parent, "tyhp");
        Directory.CreateDirectory(compiler);
        this._tempDirectories.Add(parent);

        if (withPackagesChild)
        {
            Directory.CreateDirectory(Path.Combine(compiler, "packages", "php"));
        }

        if (withSibling)
        {
            Directory.CreateDirectory(Path.Combine(parent, "tyhp-runtime-src", "packages", "php"));
        }

        if (withInTree)
        {
            Directory.CreateDirectory(Path.Combine(compiler, "runtime", "packages", "php"));
        }

        return compiler;
    }
}
