using System.Text.Json;
using System.Text.Json.Nodes;
using Tyhp.CLI.Support;
using Tyhp.TyhpLang.Ast;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
public class DumpAstJsonDepthTests
{
    [Fact]
    public void WriteJson_AstDeeperThanSerializerDefault_WritesLeaf()
    {
        const int depth = 80;
        var root = new ChainAst("n0");
        var current = root;
        for (var i = 1; i < depth; i++)
        {
            var child = new ChainAst(i == depth - 1 ? "deep-leaf" : "n" + i);
            current.Nest(child);
            current = child;
        }

        var payload = new JsonObject
        {
            ["command"] = "dump-ast",
            ["files"] = new JsonArray
            {
                new JsonObject
                {
                    ["file"] = "deep.php",
                    ["ast"] = DebugJson.SerializeAst(root),
                },
            },
        };

        var path = Path.Combine(Path.GetTempPath(), "tyhp-dump-ast-depth-" + Guid.NewGuid().ToString("n") + ".json");
        try
        {
            DebugCommandSupport.WriteJson(payload, path);

            using var doc = JsonDocument.Parse(
                File.ReadAllText(path),
                new JsonDocumentOptions { MaxDepth = 8192 });
            var node = doc.RootElement.GetProperty("files")[0].GetProperty("ast");
            for (var i = 0; i < depth - 1; i++)
            {
                node = node.GetProperty("children")[0];
            }

            node.GetProperty("valueString").GetString().Should().Be("deep-leaf");
            node.TryGetProperty("truncated", out _).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class ChainAst : Base2Ast
    {
        public ChainAst(string name)
        {
            ValueString = name;
        }

        public void Nest(ChainAst child) => AddChild(child);
    }
}
