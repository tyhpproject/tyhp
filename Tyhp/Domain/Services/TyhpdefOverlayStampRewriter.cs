using System.Text;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Writes or updates <c>// @overlay-against:</c> comments in overlay tyhpdefs from Layer 1 stamps.
    /// </summary>
    internal static class TyhpdefOverlayStampRewriter
    {
        public static int Apply(
            string filePath,
            IReadOnlyDictionary<string, string> layer1Stamps,
            string? fqnFilter,
            string? targetPhpVersion = null)
        {
            if (!File.Exists(filePath))
            {
                return 0;
            }

            var text = File.ReadAllText(filePath);
            var diagnostics = new Diagnostics.DiagnosticBag();
            var ast = Tyhp.TyhpLang.Binder.BuiltIn.Tyhpdef.ParseContent(
                text,
                filePath,
                ParseMode.Tyhpdef,
                diagnostics);
            if (ast == null)
            {
                return 0;
            }

            var edits = new List<(int InsertAt, int DeleteLength, string Text)>();
            var filter = string.IsNullOrWhiteSpace(fqnFilter)
                ? null
                : TyhpdefOverlayStamp.NormalizeFqn(fqnFilter);

            var declareConstraints = TyhpdefOverlayPhpGate.CollectFileLevelConstraints(ast);
            Walk(ast, layer1Stamps, filter, edits, declareConstraints, targetPhpVersion);
            if (edits.Count == 0)
            {
                return 0;
            }

            var updated = ApplyEdits(text, edits);
            if (!string.Equals(updated, text, StringComparison.Ordinal))
            {
                File.WriteAllText(filePath, updated);
            }

            return edits.Count;
        }

        private static void Walk(
            IBase2Ast node,
            IReadOnlyDictionary<string, string> layer1Stamps,
            string? filter,
            List<(int InsertAt, int DeleteLength, string Text)> edits,
            List<string> declareConstraints,
            string? targetPhpVersion,
            string currentNamespace = "")
        {
            if (node is PhpDeclareAst declare)
            {
                WalkDeclare(
                    declare,
                    layer1Stamps,
                    filter,
                    edits,
                    declareConstraints,
                    targetPhpVersion,
                    currentNamespace);
                return;
            }

            if (node is PhpBlockNamespaceDeclAst or PhpNamespaceDeclAst)
            {
                currentNamespace = node.Identifier ?? "";
            }

            switch (node)
            {
                case TyhpdefImportFunctionDeclAst function:
                    if (!TyhpdefOverlayPhpGate.IsIncluded(function, declareConstraints, targetPhpVersion))
                    {
                        break;
                    }

                    var functionFqn = QualifyFqn(GetDeclFqn(function.NameOrAlias), currentNamespace);
                    if (function.IsPartial)
                    {
                        QueueNameOnlyFunctionStamp(function, functionFqn, layer1Stamps, filter, edits);
                    }
                    else
                    {
                        QueueStamp(
                            function,
                            functionFqn,
                            TyhpdefOverlayStamp.KindFunction,
                            layer1Stamps,
                            filter,
                            edits);
                    }

                    break;
                case TyhpdefImportObjectDeclAst type:
                    // Stamping an `extern` is a no-op: there is no Layer 1 member list.
                    if (type.IsExtern)
                    {
                        break;
                    }

                    var typeFqn = QualifyFqn(GetDeclFqn(type.NameOrAlias), currentNamespace);
                    if (TyhpdefOverlayPhpGate.IsIncluded(type, declareConstraints, targetPhpVersion))
                    {
                        QueueStamp(
                            type,
                            typeFqn,
                            TyhpdefOverlayStamp.KindType,
                            layer1Stamps,
                            filter,
                            edits);
                        if (type.Body != null)
                        {
                            foreach (var member in type.Body.GetAllNotNull())
                            {
                                if (!TyhpdefOverlayPhpGate.IsIncluded(member, declareConstraints, targetPhpVersion))
                                {
                                    continue;
                                }

                                QueueMemberStamp(member, typeFqn, layer1Stamps, filter, edits);
                            }
                        }
                    }

                    break;
                case TyhpdefImportConstAst constant:
                    if (!TyhpdefOverlayPhpGate.IsIncluded(constant, declareConstraints, targetPhpVersion))
                    {
                        break;
                    }

                    QueueStamp(
                        constant,
                        QualifyFqn(GetDeclFqn(constant.NameOrAlias), currentNamespace),
                        TyhpdefOverlayStamp.KindConst,
                        layer1Stamps,
                        filter,
                        edits);
                    break;
                case TyhpdefImportVariableAst variable:
                    if (!TyhpdefOverlayPhpGate.IsIncluded(variable, declareConstraints, targetPhpVersion))
                    {
                        break;
                    }

                    QueueStamp(
                        variable,
                        QualifyFqn(variable.VariableName ?? "", currentNamespace),
                        TyhpdefOverlayStamp.KindVariable,
                        layer1Stamps,
                        filter,
                        edits);
                    break;
            }

            foreach (var child in node.AstChildren)
            {
                if (child != null && child is not PhpClassBodyAst)
                {
                    Walk(child, layer1Stamps, filter, edits, declareConstraints, targetPhpVersion, currentNamespace);

                    // Semicolon-form `namespace X;` has no body of its own — the declarations
                    // that follow it are its siblings in the same statement list, not its
                    // children — so it applies to every subsequent sibling until the next
                    // namespace declaration, same as PHP's own scoping rule. Block-form
                    // `namespace X { }` never reaches here with a null body, so this only
                    // widens the semicolon case.
                    if (child is PhpNamespaceDeclAst { TopStatements: null })
                    {
                        currentNamespace = child.Identifier ?? "";
                    }
                }
            }
        }

        /// <summary>
        /// Block-form <c>declare(php=…)</c> applies only inside the block. Semicolon form is
        /// already in <paramref name="declareConstraints"/> via the file-level scan.
        /// </summary>
        private static void WalkDeclare(
            PhpDeclareAst declare,
            IReadOnlyDictionary<string, string> layer1Stamps,
            string? filter,
            List<(int InsertAt, int DeleteLength, string Text)> edits,
            List<string> declareConstraints,
            string? targetPhpVersion,
            string currentNamespace)
        {
            var body = declare.Body as IBase2Ast;
            if (!TyhpdefOverlayPhpGate.IsDeclareBlock(declare) || body == null)
            {
                return;
            }

            var pushed = TyhpdefOverlayPhpGate.TryReadSolePhpConstraint(declare, out var constraint);
            if (pushed)
            {
                declareConstraints.Add(constraint);
            }

            try
            {
                Walk(body, layer1Stamps, filter, edits, declareConstraints, targetPhpVersion, currentNamespace);
            }
            finally
            {
                if (pushed && declareConstraints.Count > 0)
                {
                    declareConstraints.RemoveAt(declareConstraints.Count - 1);
                }
            }
        }

        /// <summary>
        /// Prefixes a name declared inside a tyhpdef <c>namespace X { }</c> block with that
        /// namespace so it matches the fully-qualified keys in <c>layer1Stamps</c>. Already
        /// fully-qualified names (leading <c>\</c>) are left as-is.
        /// </summary>
        private static string QualifyFqn(string name, string currentNamespace)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith('\\') || string.IsNullOrEmpty(currentNamespace))
            {
                return name;
            }

            return currentNamespace.TrimEnd('\\') + "\\" + name;
        }

        private static void QueueMemberStamp(
            IBase2Ast member,
            string typeFqn,
            IReadOnlyDictionary<string, string> layer1Stamps,
            string? filter,
            List<(int InsertAt, int DeleteLength, string Text)> edits)
        {
            if (filter != null
                && !string.Equals(TyhpdefOverlayStamp.NormalizeFqn(typeFqn), filter, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string? memberName = member switch
            {
                PhpMethodDeclAst method => method.Identifier,
                PhpPropertyDeclAst prop => prop.Properties?.GetAllNotNull().FirstOrDefault()?.Identifier,
                _ => null,
            };

            if (string.IsNullOrEmpty(memberName))
            {
                return;
            }

            var key = TyhpdefOverlayStamp.MemberKey(
                typeFqn,
                memberName,
                TyhpdefOverlayStamp.MemberKindOfDeclaration(member));
            if (!layer1Stamps.TryGetValue(key, out var stamp) || string.IsNullOrEmpty(stamp))
            {
                return;
            }

            if (member is PhpMethodDeclAst { IsPartial: true })
            {
                QueueStampAt(member, "function " + memberName.TrimStart('\\'), edits);
                return;
            }

            if (IsAlreadyStampedAgainstOneOfSeveralOverloads(member, stamp))
            {
                return;
            }

            QueueStampAt(member, TyhpdefOverlayStamp.PrimaryStamp(stamp), edits);
        }

        /// <summary>
        /// Layer 1 may record several overloads under one member/function key (see
        /// <see cref="TyhpdefOverlayStamp.Record"/>). This rewriter cannot tell, from a bare
        /// (unbound) re-parse, which overload a generically-typed overlay declaration
        /// (e.g. <c>DatePeriod::__construct(TDate $start, ...)</c>) actually matches — that
        /// needs the checker's bound-symbol comparison. Re-running <c>tyhp overlay stamp</c>
        /// must not clobber an already-correct per-overload <c>@overlay-against:</c> comment
        /// with the primary (first) overload's text just because the key is ambiguous.
        /// </summary>
        private static bool IsAlreadyStampedAgainstOneOfSeveralOverloads(IBase2Ast node, string recordedStamp)
        {
            if (recordedStamp.IndexOf(TyhpdefOverlayStamp.StampListSeparator) < 0)
            {
                return false;
            }

            var authored = TyhpdefOverlayStamp.TryGetAuthoredStamp(node);
            return !string.IsNullOrEmpty(authored)
                && TyhpdefOverlayStamp.MatchesAnyRecordedStamp(authored, recordedStamp);
        }

        /// <summary>
        /// Overlay <c>partial function</c> stamps are name-only. Write
        /// <c>function {name}</c> only when Layer 1 still has that key.
        /// </summary>
        private static void QueueNameOnlyFunctionStamp(
            IBase2Ast node,
            string fqn,
            IReadOnlyDictionary<string, string> layer1Stamps,
            string? filter,
            List<(int InsertAt, int DeleteLength, string Text)> edits)
        {
            var normalized = TyhpdefOverlayStamp.NormalizeFqn(fqn);
            if (filter != null
                && !string.Equals(normalized, filter, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!layer1Stamps.ContainsKey(TyhpdefOverlayStamp.SymbolKey(fqn, TyhpdefOverlayStamp.KindFunction)))
            {
                return;
            }

            var name = fqn.TrimStart('\\');
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            QueueStampAt(node, "function " + name, edits);
        }

        private static void QueueStamp(
            IBase2Ast node,
            string fqn,
            string kind,
            IReadOnlyDictionary<string, string> layer1Stamps,
            string? filter,
            List<(int InsertAt, int DeleteLength, string Text)> edits)
        {
            var normalized = TyhpdefOverlayStamp.NormalizeFqn(fqn);
            if (filter != null
                && !string.Equals(normalized, filter, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!layer1Stamps.TryGetValue(TyhpdefOverlayStamp.SymbolKey(fqn, kind), out var stamp)
                || string.IsNullOrEmpty(stamp))
            {
                return;
            }

            if (IsAlreadyStampedAgainstOneOfSeveralOverloads(node, stamp))
            {
                return;
            }

            QueueStampAt(node, TyhpdefOverlayStamp.PrimaryStamp(stamp), edits);
        }

        private static void QueueStampAt(
            IBase2Ast node,
            string stamp,
            List<(int InsertAt, int DeleteLength, string Text)> edits)
        {
            if (node.StartIndex < 0)
            {
                return;
            }

            edits.Add((node.StartIndex, 0, "// @overlay-against: " + stamp + Environment.NewLine));
        }

        private static string GetDeclFqn(IBase2Ast? nameOrAlias)
        {
            if (nameOrAlias == null)
            {
                return "";
            }

            // `class php_name as tyhpName` stores the Tyhp name on the node and the PHP
            // original on `aliasOf`. Layer 1 stamps are keyed by the PHP name.
            if (nameOrAlias.AstGrammarAddons.TryGetValue("aliasOf", out var aliasOf))
            {
                var original = aliasOf.ValueString ?? aliasOf.Identifier;
                if (!string.IsNullOrEmpty(original))
                {
                    return original;
                }
            }

            // `function php_name as tyhpName` stores php_name on the node (`ValueString`)
            // and the Tyhp alias on `aliasedAs`. Do not look up Layer 1 by the alias.
            return nameOrAlias.ValueString ?? nameOrAlias.Identifier ?? "";
        }

        private static string ApplyEdits(string text, List<(int InsertAt, int DeleteLength, string Text)> edits)
        {
            var ordered = edits.OrderByDescending(e => e.InsertAt).ToList();
            var builder = new StringBuilder(text);
            foreach (var edit in ordered)
            {
                var located = LocateExistingStamp(text, edit.InsertAt);
                var line = IndentStamp(text, located.Start, edit.Text);
                if (located.Length > 0)
                {
                    builder.Remove(located.Start, located.Length);
                    builder.Insert(located.Start, line);
                }
                else
                {
                    builder.Insert(located.Start, line);
                }

                // Clean up stray duplicate stamp lines left over from a previous run (e.g. one
                // written before the attribute and another, canonical one after it — the exact
                // shape of the bug this rewriter is fixing). Removed highest-offset-first so
                // earlier positions in `text` stay valid across removals.
                foreach (var extra in located.ExtraDuplicates.OrderByDescending(r => r.Start))
                {
                    builder.Remove(extra.Start, extra.Length);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Finds an existing <c>// @overlay-against:</c> to replace, or the insert
        /// point immediately before the declaration keyword.
        /// </summary>
        /// <remarks>
        /// Member <c>StartIndex</c> often points at a leading <c>#[…]</c> attribute.
        /// Scan from the previous line through decorations/stamps up to the keyword,
        /// replace the last stamp found in that window (covers stamp-after-attribute
        /// and stamp-before-attribute), and report any earlier stamp lines in the same
        /// window as stray duplicates to delete outright. Otherwise insert at the keyword.
        /// </remarks>
        private static (int Start, int Length, string Indent, List<(int Start, int Length)> ExtraDuplicates) LocateExistingStamp(string text, int declStart)
        {
            var startLine = LineStart(text, declStart);
            var keywordLine = FindDeclarationKeywordLine(text, startLine);
            var scanAt = PreviousLineStart(text, startLine);

            var stampLines = new List<(int Start, int Length)>();
            while (scanAt < keywordLine)
            {
                var next = NextLineStart(text, scanAt);
                var lineEnd = Math.Min(next, text.Length);
                var line = text[scanAt..lineEnd];
                if (PhpParserAstVisitor.ExtractOverlayAgainstStamp(line.Trim()) != null)
                {
                    var stampEnd = next > scanAt ? next : text.Length;
                    stampLines.Add((scanAt, stampEnd - scanAt));
                }

                if (next <= scanAt)
                {
                    break;
                }

                scanAt = next;
            }

            if (stampLines.Count > 0)
            {
                var last = stampLines[^1];
                var extraDuplicates = stampLines.Count > 1 ? stampLines.GetRange(0, stampLines.Count - 1) : new List<(int Start, int Length)>();
                return (last.Start, last.Length, "", extraDuplicates);
            }

            return (keywordLine, 0, "", new List<(int Start, int Length)>());
        }

        /// <summary>
        /// First line at or after <paramref name="startLine"/> that is not blank,
        /// an attribute, a docblock, or an overlay stamp — the declaration keyword.
        /// </summary>
        private static int FindDeclarationKeywordLine(string text, int startLine)
        {
            var at = startLine;
            while (at < text.Length)
            {
                var next = NextLineStart(text, at);
                var line = text[at..Math.Min(next, text.Length)];
                var trimmed = line.Trim();
                if (trimmed.Length == 0
                    || IsAttributeLine(trimmed)
                    || IsDocBlockLine(trimmed)
                    || PhpParserAstVisitor.ExtractOverlayAgainstStamp(trimmed) != null)
                {
                    if (next <= at)
                    {
                        return at;
                    }

                    at = next;
                    continue;
                }

                return at;
            }

            return startLine;
        }

        private static bool IsAttributeLine(string trimmed)
            => trimmed.StartsWith("#[", StringComparison.Ordinal);

        private static bool IsDocBlockLine(string trimmed)
            => trimmed.StartsWith("/**", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal)
                || trimmed.StartsWith("*", StringComparison.Ordinal);

        private static int LineStart(string text, int index)
        {
            var lineStart = Math.Clamp(index, 0, text.Length);
            while (lineStart > 0 && text[lineStart - 1] != '\n')
            {
                lineStart--;
            }

            return lineStart;
        }

        private static int PreviousLineStart(string text, int lineStart)
        {
            if (lineStart <= 0)
            {
                return 0;
            }

            return LineStart(text, lineStart - 1);
        }

        private static int NextLineStart(string text, int lineStart)
        {
            var newline = text.IndexOf('\n', lineStart);
            if (newline < 0)
            {
                return text.Length;
            }

            return newline + 1;
        }

        private static string IndentStamp(string text, int insertAt, string stampLine)
        {
            var lineStart = insertAt;
            while (lineStart < text.Length && lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r')
            {
                // insertAt is already at line start for new stamps
                break;
            }

            var indent = new StringBuilder();
            for (var i = insertAt; i < text.Length && (text[i] == ' ' || text[i] == '\t'); i++)
            {
                indent.Append(text[i]);
            }

            if (stampLine.StartsWith("//", StringComparison.Ordinal))
            {
                return indent + stampLine.TrimStart() + (stampLine.EndsWith('\n') ? "" : Environment.NewLine);
            }

            return stampLine;
        }
    }
}
