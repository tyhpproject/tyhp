using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Emitter.SourceMap
{
    /// <summary>
    /// Accumulates Source Map v3 mapping segments while PHP text is emitted.
    /// </summary>
    /// <remarks>
    /// Tracks the current generated line/column as content is appended, and records a mapping
    /// whenever <see cref="AddContent"/> is given an AST provider with a known start position.
    /// The source file for each mapping comes from <see cref="IBase2Ast.OwningFile"/> when
    /// present, otherwise from the optional default file registered at construction (or via
    /// <see cref="RegisterSourceFile"/>). This type is the public sourcemap data surface on
    /// <c>PHPOutputFile</c>; it is not an algorithm helper and is therefore <see langword="public"/>.
    /// </remarks>
    public sealed class SourceMapCollector
    {
        private const int DefaultMappingCapacity = 256;

        private readonly string? _defaultSourceFileName;
        private readonly Dictionary<string, int> _sourceFileIndices;
        private readonly List<string> _sourceFiles;
        private readonly Dictionary<string, int> _nameIndices;
        private readonly List<string> _names;
        private readonly List<SourceMapping> _mappings;

        private int _currentGeneratedLine;
        private int _currentGeneratedColumn;

        /// <summary>
        /// Create a collector. When <paramref name="defaultSourceFileName"/> is non-empty it is
        /// registered immediately as source index 0 (the common one-output-file / one-<c>.tyhp</c>
        /// case). <paramref name="estimatedMappingCount"/> pre-sizes the mapping list.
        /// </summary>
        public SourceMapCollector(
            string? defaultSourceFileName = null,
            int estimatedMappingCount = DefaultMappingCapacity)
        {
            _defaultSourceFileName = defaultSourceFileName;
            _sourceFileIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            _sourceFiles = [];
            _nameIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            _names = [];
            _mappings = new List<SourceMapping>(Math.Max(0, estimatedMappingCount));

            if (!string.IsNullOrEmpty(_defaultSourceFileName))
            {
                RegisterSourceFile(_defaultSourceFileName);
            }
        }

        /// <summary>Current 0-based line in the generated PHP (starts at 0).</summary>
        public int CurrentGeneratedLine => _currentGeneratedLine;

        /// <summary>Current 0-based column in the generated PHP (starts at 0).</summary>
        public int CurrentGeneratedColumn => _currentGeneratedColumn;

        /// <summary>
        /// Record a mapping for <paramref name="content"/> (when <paramref name="provider"/> has a
        /// known start position) and advance the generated position through any newlines in
        /// <paramref name="content"/>.
        /// </summary>
        /// <param name="content">PHP text being appended. Newlines are <c>\n</c> only.</param>
        /// <param name="provider">
        /// Originating AST node, or <see langword="null"/> for generated boilerplate (indent,
        /// newlines between fragments, file preamble) that must not produce a mapping.
        /// </param>
        /// <param name="name">
        /// Optional symbol name for this segment. When non-empty it is registered in the
        /// <c>names</c> array and stored as <see cref="SourceMapping.NameIndex"/>.
        /// </param>
        /// <remarks>
        /// A mapping is recorded only when <paramref name="provider"/> has a known start
        /// position: <c>Line &gt; 0</c> (AST lines are 1-based; <c>-1</c> is the "unknown"
        /// sentinel, and <c>0</c> is never a real line) and <c>Column &gt;= 0</c> (already
        /// 0-based).
        /// </remarks>
        public void AddContent(string content, IBase2Ast? provider, string? name = null)
        {
            ArgumentNullException.ThrowIfNull(content);

            if (provider != null && provider.Line > 0 && provider.Column >= 0)
            {
                int sourceIndex = ResolveSourceIndex(provider);
                int? nameIndex = string.IsNullOrWhiteSpace(name) ? null : RegisterName(name);
                _mappings.Add(new SourceMapping(
                    _currentGeneratedLine,
                    _currentGeneratedColumn,
                    sourceIndex,
                    OriginalLine: provider.Line - 1,
                    OriginalColumn: provider.Column,
                    nameIndex));
            }

            AdvancePosition(content);
        }

        /// <summary>
        /// Advance to the next generated line without recording a mapping. Equivalent to appending
        /// a lone <c>\n</c> with a null provider.
        /// </summary>
        public void AddNewLine()
        {
            _currentGeneratedLine++;
            _currentGeneratedColumn = 0;
        }

        /// <summary>
        /// Force the current generated position without recording a mapping or touching mapping
        /// state. For callers that must discard already-tracked trailing content (e.g. a caller
        /// that trims trailing whitespace from text it already reported via <see cref="AddContent"/>
        /// before writing it out) and need the collector's cursor to match what was actually
        /// written.
        /// </summary>
        public void SetPosition(int generatedLine, int generatedColumn)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(generatedLine);
            ArgumentOutOfRangeException.ThrowIfNegative(generatedColumn);

            _currentGeneratedLine = generatedLine;
            _currentGeneratedColumn = generatedColumn;
        }

        /// <summary>
        /// Register a source file path and return its index in the <c>sources</c> array. Already
        /// registered paths return the existing index.
        /// </summary>
        public int RegisterSourceFile(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            if (_sourceFileIndices.TryGetValue(filePath, out int existing))
            {
                return existing;
            }

            int index = _sourceFiles.Count;
            _sourceFiles.Add(filePath);
            _sourceFileIndices[filePath] = index;
            return index;
        }

        /// <summary>
        /// Register a symbol name and return its index in the <c>names</c> array. Already
        /// registered names return the existing index.
        /// </summary>
        public int RegisterName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (_nameIndices.TryGetValue(name, out int existing))
            {
                return existing;
            }

            int index = _names.Count;
            _names.Add(name);
            _nameIndices[name] = index;
            return index;
        }

        /// <summary>
        /// All collected mappings, sorted by generated line then generated column. The returned
        /// list is a snapshot; later <see cref="AddContent"/> calls do not mutate it. Mappings
        /// that tie on generated line and column keep their original recording order (stable
        /// sort) — nested <see cref="Ast.Interfaces.IBase2Ast"/> fragments that start at the same
        /// generated position must not be reordered.
        /// </summary>
        public IReadOnlyList<SourceMapping> GetMappings()
        {
            return _mappings
                .OrderBy(m => m.GeneratedLine)
                .ThenBy(m => m.GeneratedColumn)
                .ToArray();
        }

        /// <summary>Ordered <c>sources</c> paths (registration order).</summary>
        public IReadOnlyList<string> GetSourceFiles() => _sourceFiles.ToArray();

        /// <summary>Ordered <c>names</c> entries (registration order).</summary>
        public IReadOnlyList<string> GetNames() => _names.ToArray();

        /// <summary>
        /// Clear mappings, names, source files, and generated position. If a default source file
        /// was supplied at construction, it is registered again as index 0.
        /// </summary>
        public void Reset()
        {
            _currentGeneratedLine = 0;
            _currentGeneratedColumn = 0;
            _mappings.Clear();
            _sourceFileIndices.Clear();
            _sourceFiles.Clear();
            _nameIndices.Clear();
            _names.Clear();

            if (!string.IsNullOrEmpty(_defaultSourceFileName))
            {
                RegisterSourceFile(_defaultSourceFileName);
            }
        }

        private int ResolveSourceIndex(IBase2Ast provider)
        {
            string? fileName = provider.OwningFile?.FileName;
            if (!string.IsNullOrEmpty(fileName))
            {
                return RegisterSourceFile(fileName);
            }

            // No OwningFile: use the default (index 0) when one was registered; otherwise still
            // emit SourceIndex 0 so position-only mappings remain valid for tests and preamble-free
            // collection. Phase 5 always registers the output file's source first.
            return 0;
        }

        private void AdvancePosition(string content)
        {
            int lastNewline = -1;
            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] == '\n')
                {
                    _currentGeneratedLine++;
                    _currentGeneratedColumn = 0;
                    lastNewline = i;
                }
            }

            if (lastNewline >= 0)
            {
                _currentGeneratedColumn = content.Length - lastNewline - 1;
            }
            else
            {
                _currentGeneratedColumn += content.Length;
            }
        }
    }
}
