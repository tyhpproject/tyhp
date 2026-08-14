using System.Text.Json.Nodes;
using Antlr4.Runtime;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.CLI.Support
{
    /// <summary>
    /// Builds machine-readable JSON for the <c>tokenize</c> and <c>dump-ast</c> debug commands.
    /// These commands exist so tooling (and AI agents) can inspect lexer/parser behaviour
    /// without reading C# internals, so the shapes here favour completeness and stability.
    /// </summary>
    public static class DebugJson
    {
        /// <summary>
        /// Guards against pathological/cyclic trees. Real ASTs are far shallower than this.
        /// </summary>
        private const int MaxAstDepth = 1024;

        /// <summary>
        /// Serializes every token in the stream (all channels, including hidden whitespace and
        /// comments) into a JSON array. Each entry carries the symbolic token name, numeric type,
        /// channel, source position, and raw text.
        /// </summary>
        public static JsonArray SerializeTokens(IEnumerable<IToken> tokens)
        {
            var array = new JsonArray();
            var vocabulary = TyhpLexer.DefaultVocabulary;

            foreach (var token in tokens)
            {
                var obj = new JsonObject
                {
                    ["index"] = token.TokenIndex,
                    ["type"] = ResolveTokenTypeName(vocabulary, token.Type),
                    ["typeId"] = token.Type,
                    ["channel"] = ResolveChannelName(token.Channel),
                    ["channelId"] = token.Channel,
                    ["line"] = token.Line,
                    ["column"] = token.Column,
                    ["startIndex"] = token.StartIndex,
                    ["stopIndex"] = token.StopIndex,
                    ["text"] = token.Text,
                };
                array.Add(obj);
            }

            return array;
        }

        /// <summary>
        /// Serializes an AST node and its full subtree into JSON. Null-valued fields are omitted
        /// to keep the output compact and readable.
        /// </summary>
        public static JsonNode? SerializeAst(IBase2Ast? node)
            => SerializeAst(node, 0);

        private static JsonNode? SerializeAst(IBase2Ast? node, int depth)
        {
            if (node is null)
            {
                return null;
            }

            var obj = new JsonObject
            {
                ["node"] = node.GetType().Name,
            };

            if (node is Base2Ast concrete)
            {
                obj["nodeType"] = concrete.NodeType;
            }

            if (!string.IsNullOrEmpty(node.LanguageMode))
            {
                obj["languageMode"] = node.LanguageMode;
            }

            obj["line"] = node.Line;
            obj["column"] = node.Column;
            obj["startIndex"] = node.StartIndex;
            if (node.EndLine >= 0)
            {
                obj["endLine"] = node.EndLine;
            }

            if (node.EndColumn >= 0)
            {
                obj["endColumn"] = node.EndColumn;
            }

            if (node.EndIndex >= 0)
            {
                obj["endIndex"] = node.EndIndex;
            }

            if (!string.IsNullOrEmpty(node.Identifier))
            {
                obj["identifier"] = node.Identifier;
            }

            if (node.ValueString is not null)
            {
                obj["valueString"] = node.ValueString;
            }

            if (node.ValueInt64 is not null)
            {
                obj["valueInt64"] = node.ValueInt64.Value;
            }

            if (node.ValueDecimal is not null)
            {
                obj["valueDecimal"] = node.ValueDecimal.Value;
            }

            if (node.ValueBoolean is not null)
            {
                obj["valueBoolean"] = node.ValueBoolean.Value;
            }

            if (!string.IsNullOrEmpty(node.DocComment))
            {
                obj["docComment"] = node.DocComment;
            }

            if (depth >= MaxAstDepth)
            {
                obj["truncated"] = true;
                return obj;
            }

            if (node.AstAttributes.Count > 0)
            {
                var attributes = new JsonArray();
                foreach (var attribute in node.AstAttributes)
                {
                    attributes.Add(SerializeAst(attribute, depth + 1));
                }
                obj["attributes"] = attributes;
            }

            if (node.AstGrammarAddons.Count > 0)
            {
                var addons = new JsonObject();
                foreach (var addon in node.AstGrammarAddons)
                {
                    addons[addon.Key] = SerializeAst(addon.Value, depth + 1);
                }
                obj["grammarAddons"] = addons;
            }

            if (node.AstChildren.Count > 0)
            {
                var children = new JsonArray();
                foreach (var child in node.AstChildren)
                {
                    children.Add(SerializeAst(child, depth + 1));
                }
                obj["children"] = children;
            }

            return obj;
        }

        /// <summary>
        /// Serializes all diagnostics in the bag into a JSON array.
        /// </summary>
        public static JsonArray SerializeDiagnostics(DiagnosticBag diagnostics)
        {
            var array = new JsonArray();

            foreach (var diagnostic in diagnostics.All)
            {
                var obj = new JsonObject
                {
                    ["severity"] = diagnostic.Severity.ToString(),
                    ["code"] = (int)diagnostic.Code,
                    ["codeName"] = diagnostic.Code.ToString(),
                    ["file"] = diagnostic.FileName,
                    ["line"] = diagnostic.Line,
                    ["column"] = diagnostic.Column,
                    ["message"] = diagnostic.Message,
                };
                if (diagnostic.EndLine is int endLine)
                {
                    obj["endLine"] = endLine;
                }

                if (diagnostic.EndColumn is int endColumn)
                {
                    obj["endColumn"] = endColumn;
                }

                array.Add(obj);
            }

            return array;
        }

        private static string ResolveTokenTypeName(IVocabulary vocabulary, int tokenType)
        {
            if (tokenType == TokenConstants.EOF)
            {
                return "EOF";
            }

            return vocabulary.GetSymbolicName(tokenType)
                ?? vocabulary.GetDisplayName(tokenType)
                ?? tokenType.ToString();
        }

        private static string ResolveChannelName(int channel)
        {
            var names = TyhpLexer.channelNames;
            if (channel >= 0 && channel < names.Length)
            {
                return names[channel];
            }

            return channel.ToString();
        }
    }
}
