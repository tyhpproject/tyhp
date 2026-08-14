using System.Text.Json.Nodes;
using Tyhp.CLI.Support;
using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.CLI
{
    /// <summary>
    /// Implements the <c>dump-ast</c> action: parses one or more source files and dumps the AST
    /// as JSON, straight after parsing (no binding or checking). Intended for debugging parser
    /// and visitor issues without stepping through C#.
    /// </summary>
    public class DumpAstAction : ActionRunnerBase
    {
        private readonly Tyhp.Config.Project _project;
        private readonly string? _outputPath;
        private readonly string? _modeOverride;

        /// <summary>
        /// Initializes a new instance of the <see cref="DumpAstAction"/> class.
        /// </summary>
        /// <param name="project">Project configuration (source paths, tagless mode).</param>
        /// <param name="outputPath">Optional <c>--out</c> file path; when null, JSON is written to stdout.</param>
        /// <param name="modeOverride">Optional <c>--mode</c> override (php|tyhp|tyhpdef).</param>
        public DumpAstAction(Tyhp.Config.Project project, string? outputPath, string? modeOverride)
        {
            this._project = project;
            this._outputPath = outputPath;
            this._modeOverride = modeOverride;
        }

        /// <inheritdoc/>
        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            DebugCommandSupport.Status("CLI_StartingDumpAst");

            var discoveryDiagnostics = new DiagnosticBag();
            var sourceFiles = DebugCommandSupport.ResolveInputFiles(this._project, discoveryDiagnostics);

            var root = new JsonObject
            {
                ["command"] = "dump-ast",
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
                filesArray.Add(this.DumpFile(filePath));
            }

            root["files"] = filesArray;
            DebugCommandSupport.WriteJson(root, this._outputPath);

            DebugCommandSupport.Status("CLI_DumpAstCompleted", sourceFiles.Count);
            return null;
        }

        private JsonObject DumpFile(string filePath)
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

            SrcFileAst? ast;
            try
            {
                ast = Tyhpdef.ParseContent(content, filePath, mode, diagnostics, taglessEnabled);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                DebugCommandSupport.Status("CLI_DebugParseError", filePath, ex.Message);
                fileObject["error"] = ex.Message;
                fileObject["diagnostics"] = DebugJson.SerializeDiagnostics(diagnostics);
                return fileObject;
            }

            fileObject["ast"] = DebugJson.SerializeAst(ast);
            fileObject["diagnostics"] = DebugJson.SerializeDiagnostics(diagnostics);

            DebugCommandSupport.Status("CLI_DumpedAstFile", filePath, diagnostics.ErrorCount);

            return fileObject;
        }
    }
}
