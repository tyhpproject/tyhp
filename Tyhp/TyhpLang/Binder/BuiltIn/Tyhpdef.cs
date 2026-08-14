using System.Collections.ObjectModel;
using System.Text;
using Antlr4.Runtime;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    public static partial class Tyhpdef
    {
        /// <summary>
        /// Loads and parses all tyhpdef sources: embedded definitions, Composer
        /// <c>vendor/</c> package manifests, explicit <c>package.tyhp.json</c> / tyhpdef
        /// includes from <c>tyhp.json</c>, and user-configured tyhpdef paths.
        /// </summary>
        /// <param name="diagnostics">Diagnostic bag for reporting parse errors and missing files.</param>
        /// <param name="options">Compilation options (PHP version target, tyhpdef include/exclude paths, project path).</param>
        /// <returns>Parsed tyhpdef sources with package provenance metadata.</returns>
        public static IEnumerable<TyhpdefSourceFile> GetSourceFiles(DiagnosticBag diagnostics, CompilationOptions? options = null)
        {
            var results = new List<TyhpdefSourceFile>();
            var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var context = new TyhpdefLoadContext { Options = options };

            LoadEmbeddedTyhpdefs(results, diagnostics);
            LoadPackageTyhpdefs(results, diagnostics, context, loadedPaths);
            LoadUserTyhpdefs(results, diagnostics, context, loadedPaths);
            ApplyTyhpdefExcludes(results, options);
            ReportMissingTyhpdefPackages(context, diagnostics);

            return results;
        }

        /// <summary>
        /// Loads and parses all tyhpdef sources and returns only the AST instances.
        /// </summary>
        public static IEnumerable<SrcFileAst> Get(DiagnosticBag diagnostics, CompilationOptions? options = null)
            => GetSourceFiles(diagnostics, options).Select(static source => source.Ast);

        private static void LoadEmbeddedTyhpdefs(List<TyhpdefSourceFile> results, DiagnosticBag diagnostics)
        {
            ReadOnlyDictionary<string, string> allKeyed;
            try
            {
                allKeyed = TyhpBuiltIn.Tyhpdef.AllKeyed;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostics.AddError(MessageCode.TyhpdefParseError, "<tyhpdef:embedded>", 0, 0, ex.Message);
                return;
            }

            foreach (var kvp in allKeyed)
            {
                var fileName = $"<tyhpdef:embedded:{kvp.Key}>";
                try
                {
                    var ast = ParseContent(kvp.Value, fileName, ParseMode.Tyhpdef, diagnostics);
                    if (ast != null)
                    {
                        AddEmbeddedSource(results, ast, kvp.Key);
                    }
                    else
                    {
                        diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, fileName, 0, 0, fileName);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    diagnostics.AddError(MessageCode.TyhpdefParseError, fileName, 0, 0, ex.Message);
                }
            }
        }

        /// <summary>
        /// Lexes, parses, and visits content into a <see cref="SrcFileAst"/>.
        /// Error-free results are stored in <see cref="AstCacheService"/> and reused across compiles.
        /// Parses that produced diagnostics are never cached — the cache stores only the AST, so a
        /// later hit would otherwise drop the syntax errors. Cache hits deserialize a fresh tree each
        /// time so binder mutations (<c>BoundSymbol</c>, <c>OwningFile</c>) never leak between
        /// <c>Bind()</c> calls.
        /// </summary>
        /// <remarks>
        /// Tyhpdef caching is always on (independent of <see cref="CompilationOptions.EnableAstCache"/>).
        /// Unit tests commonly disable the user-file AST cache for isolation, but re-parsing ~1MB of
        /// PHP-extension / runtime package tyhpdefs on every compile is never useful.
        /// </remarks>
        internal static SrcFileAst? ParseContent(
            string content,
            string fileName,
            ParseMode mode,
            DiagnosticBag diagnostics,
            bool tagless = false)
        {
            // Match CompilationService hashing so the same path+bytes share one AstCache entry.
            var fileHash = AstCacheService.ComputeContentHash(content, tagless);

            try
            {
                var cached = AstCacheService.Get(fileName, fileHash);
                if (cached != null)
                {
                    return cached;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Cache read failed — fall through to a fresh parse.
                diagnostics.AddWarning(
                    MessageCode.ParserUnknownError,
                    fileName,
                    0,
                    0,
                    $"Failed to read AST cache for tyhpdef: {ex.Message}");
            }

            // Snapshot before lex/parse/visit so recoverable error trees are not cached.
            var errorsBeforeParse = diagnostics.CountErrorsForFile(fileName);

            var contentBytes = Encoding.UTF8.GetBytes(content);
            var inputStream = new AntlrInputStream(new System.IO.MemoryStream(contentBytes));
            var lexer = new TyhpLexer(inputStream);
            lexer.RemoveErrorListeners();

            using var lexerErrorListener = new TyhpAntlrErrorListener<int>(diagnostics, MessageCode.TyhpdefParseError);
            lexerErrorListener.SetFileName(fileName);
            lexer.AddErrorListener(lexerErrorListener);

            // Tagless applies only to the tyhpdef/tyhp parse modes (not raw PHP). When a package
            // is published in tagless mode, its files omit the open tag and forbid the closing tag,
            // so they must be lexed/parsed via the dedicated tagless start mode + entry rules.
            var taglessEnabled = tagless && mode != ParseMode.Php;
            var taglessLanguageMode = mode switch
            {
                ParseMode.Tyhpdef => "tyhpdef",
                ParseMode.Tyhp => "tyhp",
                _ => string.Empty,
            };
            lexer.ConfigureTagless(taglessEnabled, taglessLanguageMode, diagnostics, fileName);

            var tokenStream = new CommonTokenStream(lexer);
            var parser = new TyhpParser(tokenStream, TextWriter.Null, TextWriter.Null);
            parser.RemoveErrorListeners();

            using var parserErrorListener = new TyhpAntlrErrorListener<IToken>(diagnostics, MessageCode.TyhpdefParseError);
            parserErrorListener.SetFileName(fileName);
            parser.AddErrorListener(parserErrorListener);

            ParserRuleContext ctx = mode switch
            {
                ParseMode.Tyhpdef => taglessEnabled ? parser.tyhpdefTaglessSrcFile() : parser.tyhpdefSrcFile(),
                ParseMode.Tyhp => taglessEnabled ? parser.tyhpTaglessSrcFile() : parser.tyhpSrcFile(),
                _ => parser.phpSrcFile(),
            };

            if (taglessEnabled)
            {
                tokenStream.Fill();
            }

            var visitor = new TyhpParserAstVisitor(tokenStream, fileName, fileHash, diagnostics);
            SrcFileAst? ast;
            try
            {
                // Malformed input can leave null children after ANTLR recovery; visitors null-guard
                // those paths, but keep a safety net so callers never see a raw NRE.
                ast = visitor.Visit(ctx) as SrcFileAst;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                diagnostics.AddError(
                    MessageCode.ParserCompileAborted,
                    fileName,
                    0,
                    0,
                    $"Error ({ex.GetType().Name}): {ex.Message}");
                return null;
            }

            var parseProducedErrors = diagnostics.CountErrorsForFile(fileName) > errorsBeforeParse;
            if (ast != null && !parseProducedErrors)
            {
                try
                {
                    AstCacheService.AddOrUpdate(ast);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Cache write failed — parse result is still valid; continue without caching.
                    diagnostics.AddWarning(
                        MessageCode.ParserUnknownError,
                        fileName,
                        0,
                        0,
                        $"Failed to cache tyhpdef AST: {ex.Message}");
                }
            }

            return ast;
        }
    }
}
