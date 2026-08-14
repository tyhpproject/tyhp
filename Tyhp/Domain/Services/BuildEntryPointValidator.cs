using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Validates that library projects do not contain executable entrypoint files.
    /// </summary>
    public static class BuildEntryPointValidator
    {
        /// <summary>
        /// Reports compile errors when a library project contains root-level executable code.
        /// </summary>
        public static void ValidateLibraryProject(
            Project project,
            IReadOnlyList<SrcFileAst> parsedFiles,
            DiagnosticBag diagnostics)
        {
            if (project.Type != ProjectType.Library || parsedFiles.Count == 0)
            {
                return;
            }

            var context = EmitContext.Create(globalScope: null, diagnostics, project);

            foreach (var srcFile in parsedFiles)
            {
                if (!ContainsEntryPoint(srcFile, context))
                {
                    continue;
                }

                diagnostics.AddError(
                    MessageCode.TyhpdefLibraryEntrypointDetected,
                    srcFile.FileName,
                    0,
                    0,
                    srcFile.FileName);
            }
        }

        private static bool ContainsEntryPoint(SrcFileAst srcFile, EmitContext context)
        {
            // A single malformed file must never crash the whole build; treat any failure to build
            // the emit tree as "no entrypoint found" rather than propagating the exception.
            try
            {
                return PHPOutputFile.FromAstTree(srcFile, context).Any(outputFile => outputFile.IsEntryPoint);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
