using System.Text;
using System.Text.Json.Nodes;
using Antlr4.Runtime;
using Tyhp.CLI.Support;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.CLI
{
    /// <summary>
    /// Implements the <c>tokenize</c> action: lexes one or more source files and dumps the full
    /// token list as JSON. Intended for debugging lexer issues without stepping through C#.
    /// The token list includes every channel (default, hidden whitespace, comments, etc.).
    /// </summary>
    public class TokenizeAction : ActionRunnerBase
    {
        private readonly Tyhp.Config.Project _project;
        private readonly string? _outputPath;
        private readonly string? _modeOverride;

        /// <summary>
        /// Initializes a new instance of the <see cref="TokenizeAction"/> class.
        /// </summary>
        /// <param name="project">Project configuration (source paths, tagless mode).</param>
        /// <param name="outputPath">Optional <c>--out</c> file path; when null, JSON is written to stdout.</param>
        /// <param name="modeOverride">Optional <c>--mode</c> override (php|tyhp|tyhpdef).</param>
        public TokenizeAction(Tyhp.Config.Project project, string? outputPath, string? modeOverride)
        {
            this._project = project;
            this._outputPath = outputPath;
            this._modeOverride = modeOverride;
        }

        /// <inheritdoc/>
        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            DebugCommandSupport.Status("CLI_StartingTokenize");

            var discoveryDiagnostics = new DiagnosticBag();
            var sourceFiles = DebugCommandSupport.ResolveInputFiles(this._project, discoveryDiagnostics);

            var root = new JsonObject
            {
                ["command"] = "tokenize",
            };

            if (sourceFiles.Count == 0)
            {
                DebugCommandSupport.Status("CLI_DebugNoSourceFiles");
                root["files"] = new JsonArray();
                root["diagnostics"] = DebugJson.SerializeDiagnostics(discoveryDiagnostics);
                DebugCommandSupport.WriteJson(root, this._outputPath);
                return null;
            }

            var filesArray = new JsonArray();

            foreach (var filePath in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                filesArray.Add(this.TokenizeFile(filePath));
            }

            root["files"] = filesArray;
            DebugCommandSupport.WriteJson(root, this._outputPath);

            DebugCommandSupport.Status("CLI_TokenizeCompleted", sourceFiles.Count);
            return null;
        }

        private JsonObject TokenizeFile(string filePath)
        {
            var fileObject = new JsonObject
            {
                ["file"] = filePath,
            };

            var diagnostics = new DiagnosticBag();

            string content;
            try
            {
                content = File.ReadAllText(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DebugCommandSupport.Status("CLI_DebugReadError", filePath, ex.Message);
                fileObject["error"] = ex.Message;
                return fileObject;
            }

            var mode = DebugCommandSupport.ResolveParseMode(filePath, this._modeOverride);
            var taglessEnabled = this._project.Tagless && mode != ParseMode.Php;

            fileObject["parseMode"] = mode.ToString();
            fileObject["tagless"] = taglessEnabled;

            var contentBytes = Encoding.UTF8.GetBytes(content);
            var inputStream = new AntlrInputStream(new MemoryStream(contentBytes));
            var lexer = new TyhpLexer(inputStream);
            lexer.RemoveErrorListeners();

            using var lexerErrorListener = new TyhpAntlrErrorListener<int>(diagnostics);
            lexerErrorListener.SetFileName(filePath);
            lexer.AddErrorListener(lexerErrorListener);

            var taglessLanguageMode = mode switch
            {
                ParseMode.Tyhpdef => "tyhpdef",
                ParseMode.Tyhp => "tyhp",
                _ => string.Empty,
            };
            lexer.ConfigureTagless(taglessEnabled, taglessLanguageMode, diagnostics, filePath);

            var tokenStream = new CommonTokenStream(lexer);
            tokenStream.Fill();

            var tokens = DebugJson.SerializeTokens(tokenStream.GetTokens());
            fileObject["tokenCount"] = tokens.Count;
            fileObject["tokens"] = tokens;
            fileObject["diagnostics"] = DebugJson.SerializeDiagnostics(diagnostics);

            DebugCommandSupport.Status("CLI_TokenizedFile", filePath, tokens.Count);

            return fileObject;
        }
    }
}
