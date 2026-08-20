using System.Collections.Generic;
using Tyhp.TyhpLang.Emitter.SourceMap;

namespace Tyhp.TyhpLang.Emitter
{
    public partial class EmitItem
    {
        public Enum.EmitType EmitType { get; set; }
        public IList<string> StartContent { get; set; }
        public IList<string> EndContent { get; set; }
        public string? ExtraData1 { get; set; }
        public string? ExtraData2 { get; set; }
        public string? ExtraData3 { get; set; }
        public string? ExtraData4 { get; set; }
        public EmitItem? Parent { get; set; }
        public List<EmitItem> Children { get; set; }

        public Ast.Interfaces.IBase2Ast Provider {get; set;}

        public EmitItem(
            Ast.Interfaces.IBase2Ast provider,
            Enum.EmitType emitType,
            IList<string> startContent,
            IList<string> endContent,
            EmitItem? parent = null
        )
        {
            this.Provider = provider;
            this.EmitType = emitType;
            this.StartContent = startContent;
            this.EndContent = endContent;
            this.Parent = parent;
            this.Children = new List<EmitItem>();
        }

        public IOrderedEnumerable<(EmitItem value, int index)> SortedChildren()
        {
            return Children
                .Select((value,index)=>(value,index))
                .OrderBy(x => (int)x.value.EmitType)
                .ThenBy(x => x.index);
        }

        // public void AddIndent(int amount, char indentChar = ' ')
        // {
        //     string indent = new(indentChar, amount);
            
        //     if (!string.IsNullOrWhiteSpace(StartContent)) {
        //         StartContent = indent + StartContent.Replace("\n", "\n" + indent);
        //         if (StartContent.StartsWith(indent + "\n")) {
        //             StartContent = StartContent.TrimStart(indentChar);
        //         }
        //     }

        //     EndContent = EndContent.Replace("\n", "\n" + indent);
        //     if (EndContent.EndsWith("\n" + indent)) {
        //         EndContent = EndContent.TrimEnd(indentChar);
        //     }

        //     foreach (var child in Children) {
        //         child.AddIndent(amount, indentChar);
        //     }
        // }

        public string emit(int indentLevel = 0)
        {
            string indent = "";
            for (int i = 0; i < indentLevel; i++) {
                indent += "    ";
            }

            // Each piece (the opening line(s), every child, and the closing line(s)) lives on its
            // own line. Collecting non-empty pieces and joining them with newlines guarantees a
            // separator between sibling children; concatenating them directly (the previous
            // behaviour) glued adjacent statements together, e.g. `}    public function` or
            // `$x = 1;    foreach (...)`.
            //
            // Content strings may themselves contain newlines (closures, switch, etc.). Each
            // embedded line is indented to this item's level so PSR-12 multiline forms stay
            // aligned when emitted as a single statement line.
            var segments = new List<string>();

            AppendIndentedContent(segments, StartContent, indent);

            Enum.EmitType? lastEmittedChildType = null;
            foreach (var child in this.SortedChildren()) {
                string childText = child.value.emit(indentLevel + 1);
                if (!String.IsNullOrWhiteSpace(childText)) {
                    // PSR-12 §4.2: blank line after the trait-use group when more class members follow.
                    if (segments.Count > 0
                        && lastEmittedChildType == Enum.EmitType.ObjectTraitUse
                        && child.value.EmitType != Enum.EmitType.ObjectTraitUse)
                    {
                        segments.Add("");
                    }

                    segments.Add(childText);
                    lastEmittedChildType = child.value.EmitType;
                }
            }

            AppendIndentedContent(segments, EndContent, indent);

            return String.Join("\n", segments);
        }

        private static void AppendIndentedContent(List<string> segments, IList<string> content, string indent)
        {
            foreach (var piece in content)
            {
                if (piece is null)
                {
                    continue;
                }

                var normalized = piece.Replace("\r\n", "\n").Replace('\r', '\n');
                if (normalized.Length == 0)
                {
                    continue;
                }

                foreach (var line in normalized.Split('\n'))
                {
                    // Preserve intentional blank lines inside a piece, but never emit trailing
                    // whitespace on an otherwise empty line (PSR-12 §2.3).
                    segments.Add(line.Length == 0 ? "" : indent + line);
                }
            }
        }

        /// <summary>
        /// Render this item the same way as <see cref="emit(int)"/>, while reporting each fragment
        /// to <paramref name="collector"/> so Source Map v3 mappings can be recorded.
        /// </summary>
        /// <remarks>
        /// Indent whitespace and the newlines that join segments have no source mapping
        /// (<see langword="null"/> provider). Content lines are reported with
        /// <see cref="Provider"/>. Skipped whitespace-only children are never reported, so the
        /// collector's generated position stays aligned with the returned string.
        /// </remarks>
        public string emit(int indentLevel, SourceMapCollector collector)
        {
            ArgumentNullException.ThrowIfNull(collector);

            string indent = "";
            for (int i = 0; i < indentLevel; i++) {
                indent += "    ";
            }

            var segments = new List<string>();

            AppendTrackedContent(segments, collector, StartContent, indent, this.Provider);

            Enum.EmitType? lastEmittedChildType = null;
            foreach (var child in this.SortedChildren()) {
                // Peek with the non-tracking path so children that emit() would drop never touch
                // the collector (their mappings / column advances would desync from the string).
                string childText = child.value.emit(indentLevel + 1);
                if (!String.IsNullOrWhiteSpace(childText)) {
                    // PSR-12 §4.2: blank line after the trait-use group when more class members follow.
                    if (segments.Count > 0
                        && lastEmittedChildType == Enum.EmitType.ObjectTraitUse
                        && child.value.EmitType != Enum.EmitType.ObjectTraitUse)
                    {
                        AppendTrackedSegment(segments, collector, indent: "", line: "", provider: null);
                    }

                    if (segments.Count > 0)
                    {
                        collector.AddContent("\n", null);
                    }

                    string tracked = child.value.emit(indentLevel + 1, collector);
                    segments.Add(tracked);
                    lastEmittedChildType = child.value.EmitType;
                }
            }

            AppendTrackedContent(segments, collector, EndContent, indent, this.Provider);

            return String.Join("\n", segments);
        }

        private static void AppendTrackedContent(
            List<string> segments,
            SourceMapCollector collector,
            IList<string> content,
            string indent,
            Ast.Interfaces.IBase2Ast provider)
        {
            foreach (var piece in content)
            {
                if (piece is null)
                {
                    continue;
                }

                var normalized = piece.Replace("\r\n", "\n").Replace('\r', '\n');
                if (normalized.Length == 0)
                {
                    continue;
                }

                foreach (var line in normalized.Split('\n'))
                {
                    AppendTrackedSegment(segments, collector, indent, line, provider);
                }
            }
        }

        private static void AppendTrackedSegment(
            List<string> segments,
            SourceMapCollector collector,
            string indent,
            string line,
            Ast.Interfaces.IBase2Ast? provider)
        {
            string segment = line.Length == 0 ? "" : indent + line;

            if (segments.Count > 0)
            {
                collector.AddContent("\n", null);
            }

            if (line.Length > 0)
            {
                if (indent.Length > 0)
                {
                    collector.AddContent(indent, null);
                }

                collector.AddContent(line, provider);
            }

            segments.Add(segment);
        }

        /// <summary>
        /// Prepends a docblock to an item's own opening content and returns that same item.
        /// </summary>
        /// <remarks>
        /// The item must be returned unchanged in identity: callers hold the result and keep adding
        /// members to it (a class block collects its methods after the docblock is attached), and
        /// the <see cref="Line"/>/<see cref="Block"/> factories have already registered the item
        /// with its parent. Attaching to a separate wrapper would strand both the docblock and
        /// everything added afterwards, and would also re-sort the declaration by
        /// <see cref="SortedChildren"/>.
        /// </remarks>
        public static EmitItem AttachDocComment(string docComment, EmitItem emit)
        {
            var lines = NormalizeDocCommentLines(docComment).ToList();
            if (lines.Count == 0) {
                return emit;
            }

            emit.StartContent = [.. lines, .. emit.StartContent];
            return emit;
        }

        /// <summary>
        /// Re-indents a captured docblock so its continuation lines align under the opening
        /// <c>/**</c> rather than keeping the column they happened to sit at in the source.
        /// </summary>
        private static IEnumerable<string> NormalizeDocCommentLines(string docComment)
        {
            var lines = docComment.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++) {
                // Everything after the leading `*` is left alone, so indented examples inside the
                // docblock survive.
                var line = i == 0 ? lines[i].Trim() : (" " + lines[i].TrimStart()).TrimEnd();
                if (i == 0 && line.Length == 0) {
                    continue;
                }

                yield return line;
            }
        }

        public EmitItem CreateCopy(EmitItem? newParent = null)
        {
            var copyEmit = new EmitItem(this.Provider, this.EmitType, this.StartContent.ToList(), this.EndContent.ToList(), newParent) {
                ExtraData1 = this.ExtraData1,
                ExtraData2 = this.ExtraData2,
                ExtraData3 = this.ExtraData3,
                ExtraData4 = this.ExtraData4,
            };

            this.Children.ForEach(x => copyEmit.Children.Add(x.CreateCopy(copyEmit)));

            return copyEmit;
        }

        public static EmitItem Line(
            Ast.Interfaces.IBase2Ast provider,
            Enum.EmitType type,
            string content,
            EmitItem? parent = null)
        {
            var item = new EmitItem(provider, type, [content], [], parent);
            parent?.Children.Add(item);
            return item;
        }

        public static EmitItem Block(
            Ast.Interfaces.IBase2Ast provider,
            Enum.EmitType type,
            string openLine,
            string closeLine,
            EmitItem? parent = null)
        {
            var item = new EmitItem(provider, type, [openLine], [closeLine], parent);
            parent?.Children.Add(item);
            return item;
        }

        /// <summary>
        /// Like <see cref="Block"/>, but places the opening <c>{</c> on the line after
        /// <paramref name="signature"/> (PSR-12 class / method / function brace style).
        /// </summary>
        public static EmitItem BlockBraceNextLine(
            Ast.Interfaces.IBase2Ast provider,
            Enum.EmitType type,
            string signature,
            string closeLine = "}",
            EmitItem? parent = null)
        {
            var item = new EmitItem(provider, type, [signature, "{"], [closeLine], parent);
            parent?.Children.Add(item);
            return item;
        }

        public static EmitItem Empty(
            Ast.Interfaces.IBase2Ast provider,
            Enum.EmitType type,
            EmitItem? parent = null)
        {
            var item = new EmitItem(provider, type, [], [], parent);
            parent?.Children.Add(item);
            return item;
        }

        public static EmitItem MultiLine(
            Ast.Interfaces.IBase2Ast provider,
            Enum.EmitType type,
            IEnumerable<string> lines,
            EmitItem? parent = null)
        {
            var item = new EmitItem(provider, type, lines.ToList(), [], parent);
            parent?.Children.Add(item);
            return item;
        }
    }
}