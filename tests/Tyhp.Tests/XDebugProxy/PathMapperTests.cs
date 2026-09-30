using Tyhp.XDebugProxy.SourceMap;
using Tyhp.XDebugProxy.Translation;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class PathMapperTests
{
    private static PathMapper Mapper() => new("/project/src", "/project/build");

    [Fact]
    public void ToSourceMapLine_AndToDbgpLine_AreInversesForClassicOffByOne()
    {
        PathMapper.ToSourceMapLine(42).Should().Be(41);
        PathMapper.ToDbgpLine(41).Should().Be(42);
        PathMapper.ToSourceMapLine(67).Should().Be(66);
        PathMapper.ToDbgpLine(66).Should().Be(67);
        PathMapper.ToDbgpLine(PathMapper.ToSourceMapLine(42)).Should().Be(42);
    }

    [Fact]
    public void ToFileSystemPath_UnixFileUri_StripsScheme()
    {
        Mapper().ToFileSystemPath("file:///project/src/App.tyhp")
            .Should().Be("/project/src/App.tyhp");
    }

    [Fact]
    public void ToFileSystemPath_WindowsDriveLetterUri_DoesNotBreakDrive()
    {
        Mapper().ToFileSystemPath("file:///C:/project/src/App.tyhp")
            .Should().Be("C:/project/src/App.tyhp");
    }

    [Fact]
    public void ToFileSystemPath_UncUri_KeepsServerShare()
    {
        Mapper().ToFileSystemPath("file://fileserver/share/App.tyhp")
            .Should().Be("//fileserver/share/App.tyhp");
    }

    [Fact]
    public void ToFileSystemPath_LocalhostUri_IsLocalPath()
    {
        Mapper().ToFileSystemPath("file://localhost/project/src/App.tyhp")
            .Should().Be("/project/src/App.tyhp");
    }

    [Fact]
    public void ToFileSystemPath_PercentEncodedSpaces_Unescapes()
    {
        Mapper().ToFileSystemPath("file:///project/src/My%20File.tyhp")
            .Should().Be("/project/src/My File.tyhp");
    }

    [Fact]
    public void ToFileSystemPath_Backslashes_NormalizeToForwardSlashes()
    {
        Mapper().ToFileSystemPath(@"C:\project\src\App.tyhp")
            .Should().Be("C:/project/src/App.tyhp");
    }

    [Fact]
    public void ToFileUri_UnixAbsolutePath_UsesThreeSlashes()
    {
        Mapper().ToFileUri("/project/build/App.php")
            .Should().Be("file:///project/build/App.php");
    }

    [Fact]
    public void ToFileUri_WindowsDriveLetter_UsesFileSlashSlashSlash()
    {
        Mapper().ToFileUri("C:/project/build/App.php")
            .Should().Be("file:///C:/project/build/App.php");
    }

    [Fact]
    public void PreserveScheme_FileUriStaysUri_PlainPathStaysPath()
    {
        PathMapper mapper = Mapper();

        mapper.PreserveScheme("file:///project/src/App.tyhp", "/project/build/App.php")
            .Should().Be("file:///project/build/App.php");
        mapper.PreserveScheme("/project/src/App.tyhp", "/project/build/App.php")
            .Should().Be("/project/build/App.php");
    }

    [Fact]
    public void IsTyhpFile_AndIsPhpFile_UseExtensionAfterStrippingUri()
    {
        PathMapper mapper = Mapper();

        mapper.IsTyhpFile("file:///project/src/App.tyhp").Should().BeTrue();
        mapper.IsPhpFile("file:///project/build/App.php").Should().BeTrue();
        mapper.IsTyhpFile("file:///project/build/App.php").Should().BeFalse();
        mapper.IsDbgpUri("dbgp://1").Should().BeTrue();
        mapper.IsTyhpFile("dbgp://1").Should().BeFalse();
    }

    [Fact]
    public void Combine_DoesNotBreakUncOrDriveLetters()
    {
        PathMapper mapper = Mapper();

        mapper.Combine("/project/build", "App.php").Should().Be("/project/build/App.php");
        mapper.Combine("C:/build", "App.php").Should().Be("C:/build/App.php");
        mapper.Combine("/project", "//server/share/App.php").Should().Be("//server/share/App.php");
    }

    [Fact]
    public void ResolveGeneratedPhpPath_PrefersMapFilePathThenOutputRoot()
    {
        PathMapper mapper = Mapper();
        SourceMapFile fromDisk = SourceMapFile.Parse(
            """{"version":3,"file":"App.php","sources":["App.tyhp"],"mappings":"AAAA"}""",
            mapFilePath: "/project/build/App.php.map");

        mapper.ResolveGeneratedPhpPath(fromDisk).Should().Be("/project/build/App.php");

        SourceMapFile filenameOnly = SourceMapFile.Parse(
            """{"version":3,"file":"App.php","sources":["App.tyhp"],"mappings":"AAAA"}""");
        mapper.ResolveGeneratedPhpPath(filenameOnly).Should().Be("/project/build/App.php");
    }
}
