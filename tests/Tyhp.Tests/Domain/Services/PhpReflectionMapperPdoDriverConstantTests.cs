using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class PhpReflectionMapperPdoDriverConstantTests
{
    [Theory]
    [InlineData("MYSQL_ATTR_COMPRESS", "pdo_mysql")]
    [InlineData("SQLITE_ATTR_OPEN_FLAGS", "pdo_sqlite")]
    [InlineData("PGSQL_ATTR_DISABLE_PREPARES", "pdo_pgsql")]
    [InlineData("ODBC_ATTR_USE_CURSOR_LIBRARY", "pdo_odbc")]
    [InlineData("DBLIB_ATTR_QUERY_TIMEOUT", "pdo_dblib")]
    [InlineData("FB_ATTR_TIMESTAMP_FORMAT", "pdo_firebird")]
    [InlineData("OCI_ATTR_ACTION", "pdo_oci")]
    [InlineData("SQL_ATTR_USE_TRUSTED_CONTEXT", "pdo_ibm")]
    [InlineData("CUBRID_ATTR_ISOLATION_LEVEL", "pdo_cubrid")]
    [InlineData("SQLSRV_ATTR_QUERY_TIMEOUT", "pdo_sqlsrv")]
    public void TryGetOwnerExtension_MapsDriverPrefixes(string constant, string owner)
    {
        PdoDriverClassConstants.TryGetOwnerExtension(constant).Should().Be(owner);
    }

    [Theory]
    [InlineData("ATTR_ERRMODE")]
    [InlineData("ATTR_ORACLE_NULLS")]
    [InlineData("FETCH_ASSOC")]
    [InlineData("PARAM_STR")]
    public void TryGetOwnerExtension_CorePdoConstants_HaveNoDriverOwner(string constant)
    {
        PdoDriverClassConstants.TryGetOwnerExtension(constant).Should().BeNull();
    }

    /// <summary>
    /// No shipped PDO driver ever registers these prefixes: <c>pdo_firebird</c> only registers
    /// <c>FB_*</c> (never <c>FIREBIRD_*</c>), <c>pdo_dblib</c> only registers <c>DBLIB_*</c>
    /// (never <c>MSSQL_*</c>), and <c>pdo_informix</c> registers no driver-specific class
    /// constants at all. A constant with one of these made-up prefixes must fall back to core
    /// (no owner), not silently disappear into a nonexistent driver bucket.
    /// </summary>
    [Theory]
    [InlineData("FIREBIRD_ATTR_DATE_FORMAT")]
    [InlineData("MSSQL_ATTR_QUERY_TIMEOUT")]
    [InlineData("INFORMIX_ATTR_SOMETHING")]
    public void TryGetOwnerExtension_UnshippedPrefixes_HaveNoDriverOwner(string constant)
    {
        PdoDriverClassConstants.TryGetOwnerExtension(constant).Should().BeNull();
    }

    [Fact]
    public void Map_PdoExtension_OmitsDriverSpecificClassConstants()
    {
        var text = MapToTyhpdef("PDO", PdoDumpJson);

        text.Should().Contain("class PDO");
        text.Should().NotContain("partial class PDO");
        text.Should().Contain("public const int ATTR_ERRMODE ?? 3;");
        text.Should().Contain("public const int ATTR_ORACLE_NULLS ?? 11;");
        text.Should().Contain("public function beginTransaction(): bool;");
        text.Should().NotContain("MYSQL_ATTR_COMPRESS");
        text.Should().NotContain("SQLITE_ATTR_OPEN_FLAGS");
        text.Should().NotContain("PGSQL_ATTR_DISABLE_PREPARES");
        AssertParses(text);
    }

    [Fact]
    public void Map_PdoMysqlExtension_EmitsDriverConstantsOnPartialPdoOnly()
    {
        var text = MapToTyhpdef("pdo_mysql", PdoDumpJson);

        text.Should().Contain("partial class PDO {");
        text.Should().Contain("public const int MYSQL_ATTR_COMPRESS ?? 1003;");
        text.Should().NotContain("ATTR_ERRMODE");
        text.Should().NotContain("SQLITE_ATTR_OPEN_FLAGS");
        text.Should().NotContain("beginTransaction");
        text.Should().Contain("class Mysql extends \\PDO {");
        text.Should().Contain("public const int ATTR_COMPRESS ?? 1003;");
        AssertParses(text);
    }

    [Fact]
    public void Map_PdoSqliteExtension_KeepsSqliteConstantsOmitsMysql()
    {
        var text = MapToTyhpdef("pdo_sqlite", PdoDumpJson);

        text.Should().Contain("partial class PDO {");
        text.Should().Contain("public const int SQLITE_ATTR_OPEN_FLAGS ?? 1000;");
        text.Should().NotContain("MYSQL_ATTR_COMPRESS");
        text.Should().NotContain("ATTR_ERRMODE");
        AssertParses(text);
    }

    [Fact]
    public void Map_PdoMysqlExtension_OmitsEmptyHostWhenDumpHasNoDriverConstants()
    {
        var text = MapToTyhpdef("pdo_mysql", CorePdoOnlyDumpJson);

        text.Should().NotContain("class PDO");
        text.Should().NotContain("MYSQL_ATTR");
        text.Should().Contain("class Mysql extends \\PDO {");
        AssertParses(text);
    }

    [Fact]
    public void Map_FallsBackToDumpExtensionNameWhenOptionsOmitIt()
    {
        var dump = PhpReflectionJson.Deserialize(PdoDumpJson);
        var runtime = new PhpRuntimeInfo
        {
            Path = "/tmp/php",
            Version = "8.3.11",
            IsManaged = true,
            LoadedExtensions = ["PDO"],
        };
        var options = new TyhpdefGenerationOptions
        {
            Mode = TyhpdefGenerationMode.PhpExtension,
            IncludeDocComments = false,
            IncludeDeprecated = true,
        };

        var file = PhpReflectionMapper.Map(dump, options, runtime);
        var text = TyhpdefOutputWriter.Write(file, includeDocComments: false);

        text.Should().Contain("public const int ATTR_ERRMODE ?? 3;");
        text.Should().NotContain("MYSQL_ATTR_COMPRESS");
        AssertParses(text);
    }

    private static string MapToTyhpdef(string extensionName, string json)
    {
        var runtime = new PhpRuntimeInfo
        {
            Path = "/tmp/php",
            Version = "8.3.11",
            IsManaged = true,
            LoadedExtensions = [extensionName],
        };
        var options = new TyhpdefGenerationOptions
        {
            Mode = TyhpdefGenerationMode.PhpExtension,
            ExtensionName = extensionName,
            IncludeDocComments = false,
            IncludeDeprecated = true,
        };

        var file = PhpReflectionMapper.Map(json, options, runtime);
        return TyhpdefOutputWriter.Write(file, includeDocComments: false);
    }

    private static void AssertParses(string tyhpdef)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(tyhpdef);
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)) + "\n" + tyhpdef);
    }

    private const string PdoDumpJson = """
    {
      "schemaVersion": 1,
      "phpVersion": "8.3.11",
      "extension": "PDO",
      "extensionVersion": "8.3.11",
      "constants": [],
      "functions": [],
      "classes": [
        {
          "kind": "class",
          "name": "PDO",
          "fqn": "\\PDO",
          "modifiers": [],
          "extends": null,
          "implements": [],
          "uses": [],
          "isAnonymous": false,
          "backingType": null,
          "docComment": null,
          "deprecated": false,
          "attributes": [],
          "constants": [
            {
              "name": "ATTR_ERRMODE",
              "value": { "kind": "int", "value": 3, "constName": null },
              "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] },
              "modifiers": ["public"],
              "deprecated": false,
              "docComment": null
            },
            {
              "name": "ATTR_ORACLE_NULLS",
              "value": { "kind": "int", "value": 11, "constName": null },
              "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] },
              "modifiers": ["public"],
              "deprecated": false,
              "docComment": null
            },
            {
              "name": "MYSQL_ATTR_COMPRESS",
              "value": { "kind": "int", "value": 1003, "constName": null },
              "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] },
              "modifiers": ["public"],
              "deprecated": false,
              "docComment": null
            },
            {
              "name": "SQLITE_ATTR_OPEN_FLAGS",
              "value": { "kind": "int", "value": 1000, "constName": null },
              "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] },
              "modifiers": ["public"],
              "deprecated": false,
              "docComment": null
            },
            {
              "name": "PGSQL_ATTR_DISABLE_PREPARES",
              "value": { "kind": "int", "value": 1000, "constName": null },
              "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] },
              "modifiers": ["public"],
              "deprecated": false,
              "docComment": null
            }
          ],
          "properties": [],
          "methods": [
            {
              "name": "beginTransaction",
              "params": [],
              "returnType": { "kind": "named", "text": "bool", "name": "bool", "builtin": true, "nullable": false, "types": [] },
              "returnByRef": false,
              "tentativeReturn": false,
              "deprecated": false,
              "docComment": null,
              "attributes": [],
              "modifiers": ["public"]
            }
          ],
          "enumCases": []
        },
        {
          "kind": "class",
          "name": "Mysql",
          "fqn": "\\Pdo\\Mysql",
          "modifiers": [],
          "extends": "\\PDO",
          "implements": [],
          "uses": [],
          "isAnonymous": false,
          "backingType": null,
          "docComment": null,
          "deprecated": false,
          "attributes": [],
          "constants": [
            {
              "name": "ATTR_COMPRESS",
              "value": { "kind": "int", "value": 1003, "constName": null },
              "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] },
              "modifiers": ["public"],
              "deprecated": false,
              "docComment": null
            }
          ],
          "properties": [],
          "methods": [],
          "enumCases": []
        }
      ]
    }
    """;

    private const string CorePdoOnlyDumpJson = """
    {
      "schemaVersion": 1,
      "phpVersion": "8.3.11",
      "extension": "pdo_mysql",
      "extensionVersion": "8.3.11",
      "constants": [],
      "functions": [],
      "classes": [
        {
          "kind": "class",
          "name": "PDO",
          "fqn": "\\PDO",
          "modifiers": [],
          "extends": null,
          "implements": [],
          "uses": [],
          "isAnonymous": false,
          "backingType": null,
          "docComment": null,
          "deprecated": false,
          "attributes": [],
          "constants": [
            {
              "name": "ATTR_ERRMODE",
              "value": { "kind": "int", "value": 3, "constName": null },
              "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] },
              "modifiers": ["public"],
              "deprecated": false,
              "docComment": null
            }
          ],
          "properties": [],
          "methods": [],
          "enumCases": []
        },
        {
          "kind": "class",
          "name": "Mysql",
          "fqn": "\\Pdo\\Mysql",
          "modifiers": [],
          "extends": "\\PDO",
          "implements": [],
          "uses": [],
          "isAnonymous": false,
          "backingType": null,
          "docComment": null,
          "deprecated": false,
          "attributes": [],
          "constants": [
            {
              "name": "ATTR_COMPRESS",
              "value": { "kind": "int", "value": 1003, "constName": null },
              "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] },
              "modifiers": ["public"],
              "deprecated": false,
              "docComment": null
            }
          ],
          "properties": [],
          "methods": [],
          "enumCases": []
        }
      ]
    }
    """;
}
