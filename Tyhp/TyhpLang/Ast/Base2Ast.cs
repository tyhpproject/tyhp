using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Tyhp.TyhpLang.Attributes;
using System.Reflection;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Visitor;
using Antlr4.Runtime;

namespace Tyhp.TyhpLang.Ast {
    public abstract class Base2Ast: Interfaces.IBase2Ast
    {
        /// <summary>
        /// The symbol bound to this AST node by the binder.
        /// Not serialized — symbols are recreated each bind pass.
        /// </summary>
        public IBaseSymbol? BoundSymbol { get; set; }

        /// <summary>
        /// The source file AST that owns this node. Set during binding.
        /// Not serialized — reconstructed each bind pass.
        /// </summary>
        public SrcFileAst? OwningFile { get; set; }

        /// <summary>
        /// NodeType - type of the node, read only value determined by registry
        /// </summary>
        public virtual byte NodeType => AstNodeTypeRegistry.GetNodeTypeId(this.GetType());

        /// <summary>
        /// CustomNodeType - custom type of the node, read only value determined by registry for external types
        /// </summary>
        public virtual long CustomNodeType => 
            this.NodeType == AstNodeTypeRegistry.CustomNodeTypeByte 
                ? AstNodeTypeRegistry.GetCustomTypeHash(this.GetType()) 
                : 0L;

        /// <summary>
        /// LanguageMode - language mode of the node
        /// </summary>
        public virtual string? LanguageMode { get; protected set; } = null;

        /// <summary>
        /// Line - line number of the first line of the node
        /// </summary>
        public virtual int Line { get; protected set; } = -1;

        /// <summary>
        /// Column - column number of the first column of the node
        /// </summary>
        public virtual int Column { get; protected set; } = -1;

        /// <summary>
        /// StartIndex - index of the first character of the node
        /// </summary>
        public virtual int StartIndex { get; protected set; } = -1;

        /// <summary>
        /// EndLine - ending line of the node (1-indexed), or -1 when unknown.
        /// </summary>
        public virtual int EndLine { get; protected set; } = -1;

        /// <summary>
        /// EndColumn - exclusive ending column on <see cref="EndLine"/> (0-indexed), or -1 when
        /// unknown. Populated as one past the last character of the ANTLR stop token so it matches
        /// <c>IDiagnostic.EndColumn</c> for rich underlines.
        /// </summary>
        public virtual int EndColumn { get; protected set; } = -1;

        /// <summary>
        /// EndIndex - inclusive character index of the last character of the node, or -1 when unknown.
        /// </summary>
        public virtual int EndIndex { get; protected set; } = -1;

        /// <summary>
        /// DocComment - doc comment of the node
        /// </summary>
        public virtual string? DocComment { get; protected set; } = null;

        /// <summary>
        /// Children[] - array of child nodes
        /// </summary>
        protected List<Interfaces.IBase2Ast?> Children { get; set; } = [];

        public IReadOnlyList<Interfaces.IBase2Ast?> AstChildren => Children.AsReadOnly();

        /// <summary>
        /// Flags[] - array of flags
        /// </summary>
        protected List<short> Flags { get; set; } = [];

        /// <summary>
        /// ValueString - string value of the node
        /// </summary>
        public virtual string? ValueString { get; protected set; } = null;

        /// <summary>
        /// ValueInt64 - int64 value of the node
        /// </summary>
        public virtual long? ValueInt64 { get; protected set; } = null;

        /// <summary>
        /// ValueDecimal - decimal value of the node
        /// </summary>
        public virtual decimal? ValueDecimal { get; protected set; } = null;

        /// <summary>
        /// ValueBoolean - boolean value of the node
        /// </summary>
        public virtual bool? ValueBoolean { get; protected set; } = null;

        /// <summary>
        /// Identifier - identifier of the node
        /// </summary>
        public virtual string Identifier { get; protected set; } = "";

        /// <summary>
        /// Attributes[] - array of attribute nodes
        /// </summary>
        protected virtual IList<Interfaces.IBase2Ast> Attributes { get; set; } = [];
        public IReadOnlyList<Interfaces.IBase2Ast> AstAttributes => Attributes.AsReadOnly();

        protected virtual IDictionary<string, Interfaces.IBase2Ast> GrammarAddons { get; set; } = new Dictionary<string, Interfaces.IBase2Ast>();
        public IReadOnlyDictionary<string, Interfaces.IBase2Ast> AstGrammarAddons => GrammarAddons.AsReadOnly();

        protected Base2Ast() {
            this.Children = [];
            this.Flags = [];
            this.Attributes = [];
        }

        protected void SetContext(ParserRuleContext? context, string? languageMode = null)
        {
            this.LanguageMode = languageMode ?? this.LanguageMode ?? TyhpParserAstVisitor.GetCurrentLanguageMode(context);
            // ANTLR error-recovery trees can omit Start; leave default positions rather than NRE.
            if (context?.Start != null)
            {
                this.Line = context.Start.Line;
                this.Column = context.Start.Column;
                this.StartIndex = context.Start.StartIndex;
            }

            // Stop carries the last token of the rule. EndColumn is exclusive (one past the last
            // character) so diagnostics/LSP ranges underline the full span, not just the stop token's
            // start column. Leave End* at -1 when Stop is missing or has no usable indices.
            if (context?.Stop != null && context.Stop.StopIndex >= 0 && context.Stop.StartIndex >= 0)
            {
                this.EndLine = context.Stop.Line;
                var stopTokenLength = context.Stop.StopIndex - context.Stop.StartIndex + 1;
                this.EndColumn = context.Stop.Column + Math.Max(0, stopTokenLength);
                this.EndIndex = context.Stop.StopIndex;
            }
        }

        protected void SetContext(Base2Ast context)
        {
            this.LanguageMode = context.LanguageMode;
            this.Line = context.Line;
            this.Column = context.Column;
            this.StartIndex = context.StartIndex;
            this.EndLine = context.EndLine;
            this.EndColumn = context.EndColumn;
            this.EndIndex = context.EndIndex;
        }

        protected bool HasFlag(long flag)
            => HasFlag(Convert.ToInt16(flag));

        protected bool HasFlag(int flag)
            => HasFlag(Convert.ToInt16(flag));

        protected bool HasFlag(short flag)
            => Flags.Contains(flag);

        protected bool HasFlag<TEnum>(short flagOffset, TEnum flagValue)
            where TEnum : System.Enum
            => HasFlag(flagOffset + Convert.ToInt16(flagValue));

        protected IEnumerable<TEnum> GetEnumFlags<TEnum>(short flagOffset)
            where TEnum : System.Enum
            => Flags.Where(f => f >= flagOffset && f < flagOffset + 1000)
                .Select(f => (TEnum)System.Enum.ToObject(typeof(TEnum), f - flagOffset));

        protected IEnumerable<object> GetEnumFlags(short flagOffset, Type enumType)
            => Flags.Where(f => f >= flagOffset && f < flagOffset + 1000)
                .Select(f => System.Enum.ToObject(enumType, f - flagOffset));

        protected void SetFlag(long flag, bool value = true)
            => SetFlag(Convert.ToInt16(flag), value);

        protected void SetFlag(int flag, bool value = true)
            => SetFlag(Convert.ToInt16(flag), value);

        protected void SetFlag(short flag, bool value = true)
        {
            if (value && !Flags.Contains(flag)) {
                Flags.Add(flag);
            } else {
                Flags.RemoveAll(f => f == flag);
            }
        }

        protected void SetFlag(IEnumerable<short> flags, bool value = true)
            => flags.ToList().ForEach(f => SetFlag(f, value));

        protected void SetFlag(IEnumerable<long> flags, bool value = true)
            => flags.ToList().ForEach(f => SetFlag(f, value));

        protected void SetFlag(IEnumerable<int> flags, bool value = true)
            => flags.ToList().ForEach(f => SetFlag(f, value));

        protected void SetFlag<TEnum>(short flagOffset, TEnum flagValue, bool value = true)
            where TEnum : System.Enum
            => SetFlag(flagOffset + Convert.ToInt16(flagValue), value);

        protected void SetFlag<TEnum>(short flagOffset, IEnumerable<TEnum> flagValues, bool value = true)
            where TEnum : System.Enum
            => SetFlag(flagValues.Select(f => flagOffset + Convert.ToInt16(f)), value);

        public void AddAttributes(PhpAttributeListAst? attributes)
        {
            if (attributes != null) {
                foreach (var attribute in attributes.GetAllNotNull()) {
                    Attributes.Add(attribute);
                }
            }
        }

        public void AddGrammarAddon(string key, Interfaces.IBase2Ast? addon)
        {
            if (addon != null) {
                GrammarAddons[key] = addon;
            }
        }

        /// <summary>
        /// Returns true if the node is valid, useful as an integrity check after deserialization
        /// </summary>
        /// <returns>True if the node is valid, false otherwise</returns>
        public virtual bool IsValid() {
            return true;
        }

        // Binary block layout, per node (all integers little-endian, all strings UTF-8):
        //
        // 4 bytes - int32, block size (total bytes of THIS block, including this field)
        // 1 byte  - NodeType
        // 1 byte  - bit array
        //     bit 1 (0x01) - has Children
        //     bit 2 (0x02) - has Flags
        //     bit 3 (0x04) - has Attributes
        //     bit 4 (0x08) - has DocComment
        //     bit 5 (0x10) - has ValueString
        //     bit 6 (0x20) - has ValueInt64
        //     bit 7 (0x40) - has ValueDecimal
        //     bit 8 (0x80) - has ValueBoolean
        // 8 bytes - int64, custom node type
        // 2 bytes - reserved. reserved[0] == 0x01 flags a trailing GrammarAddons section
        //           (0x00 in older caches, preserving forward-compatibility of the layout).
        // 4 bytes - int32, LanguageMode UTF-8 byte length (0 => null)
        // N bytes - LanguageMode UTF-8 bytes
        // 4 bytes - int32, Identifier UTF-8 byte length
        // N bytes - Identifier UTF-8 bytes
        // 4 bytes - int32, Line
        // 4 bytes - int32, Column
        // 4 bytes - int32, StartIndex
        // 4 bytes - int32, EndLine
        // 4 bytes - int32, EndColumn (exclusive)
        // 4 bytes - int32, EndIndex
        // (if bit 4) 4 bytes int32 DocComment UTF-8 byte length + DocComment UTF-8 bytes
        // (if bit 5) 4 bytes int32 ValueString UTF-8 byte length + ValueString UTF-8 bytes
        // (if bit 6) 8 bytes int64 ValueInt64
        // (if bit 7) 16 bytes decimal ValueDecimal
        // (if bit 8) 1 byte bool ValueBoolean
        // (if bit 2) 4 bytes int32 Flags count + (2 bytes int16) * count
        // (if bit 3) 4 bytes int32 Attributes count + each attribute as a self-delimiting block
        // (if bit 1) 4 bytes int32 Children count + each child as a self-delimiting block
        //            (a null child is written as a 4-byte block size of 0)
        // (if reserved[0]==0x01) 4 bytes int32 addon count +
        //            per addon: 4 bytes int32 key UTF-8 byte length, key bytes, then value node block
        //
        // IMPORTANT (preserve): strings are length-prefixed by their UTF-8 *byte* count, so any
        // section can be skipped by advancing the offset without decoding, which is what the lean
        // metadata reader (TryReadSrcFileKey) and the skip-children fast path rely on. The bit-gated
        // sections (Flags/Attributes/Children/values) are written ONLY when their bit is set; writing
        // the length fields unconditionally would desynchronize the deserializer.

        /// <summary>
        /// Serialize - serialize the node and children to a byte array.
        /// </summary>
        /// <returns>Byte array</returns>
        public virtual byte[] Serialize()
        {
            // A single growable buffer is threaded through the entire recursion (SerializeInto), so
            // every byte is appended exactly once => linear total work. The previous implementation
            // returned a fresh byte[] per node and spread children into their parents, which recopied
            // each descendant's bytes once per ancestor level (~O(depth * size) copying plus heavy GC
            // churn). Do not revert to per-node array concatenation.
            var buffer = new List<byte>(256);
            this.SerializeInto(buffer);
            return [.. buffer];
        }

        /// <summary>
        /// Appends this node's serialized block to a shared buffer. See <see cref="Serialize"/> for
        /// why a single shared buffer is used instead of returning per-node arrays.
        /// </summary>
        protected void SerializeInto(List<byte> buffer)
        {
            if (this.NodeType == 0) {
                throw new Exception("Node of type " + this.GetType().Name + " cannot be serialized.");
            }

            byte bitArray = 0x00;
            if (Children.Count > 0) { bitArray |= 0x01; }
            if (Flags.Count > 0) { bitArray |= 0x02; }
            if (Attributes.Count > 0) { bitArray |= 0x04; }
            if (DocComment != null) { bitArray |= 0x08; }
            if (ValueString != null) { bitArray |= 0x10; }
            if (ValueInt64 != null) { bitArray |= 0x20; }
            if (ValueDecimal != null) { bitArray |= 0x40; }
            if (ValueBoolean != null) { bitArray |= 0x80; }

            int blockStart = buffer.Count;

            // Block size placeholder - back-patched once the whole block has been written.
            AppendInt32(buffer, 0);
            buffer.Add(this.NodeType);
            buffer.Add(bitArray);
            AppendInt64(buffer, this.CustomNodeType);
            // reserved[0]: GrammarAddons presence flag (see layout comment); reserved[1]: unused.
            buffer.Add(this.GrammarAddons.Count > 0 ? (byte)0x01 : (byte)0x00);
            buffer.Add(0x00);

            AppendString(buffer, this.LanguageMode);
            AppendString(buffer, this.Identifier);
            AppendInt32(buffer, this.Line);
            AppendInt32(buffer, this.Column);
            AppendInt32(buffer, this.StartIndex);
            AppendInt32(buffer, this.EndLine);
            AppendInt32(buffer, this.EndColumn);
            AppendInt32(buffer, this.EndIndex);

            if (DocComment != null) { AppendString(buffer, this.DocComment); }
            if (ValueString != null) { AppendString(buffer, this.ValueString); }
            if (ValueInt64 != null) { AppendInt64(buffer, this.ValueInt64.Value); }
            if (ValueDecimal != null) {
                foreach (var bits in Decimal.GetBits(this.ValueDecimal.Value)) {
                    AppendInt32(buffer, bits);
                }
            }
            if (ValueBoolean != null) { buffer.Add(this.ValueBoolean.Value ? (byte)1 : (byte)0); }

            if (Flags.Count > 0) {
                AppendInt32(buffer, Flags.Count);
                foreach (var flag in Flags) {
                    AppendInt16(buffer, flag);
                }
            }

            if (Attributes.Count > 0) {
                AppendInt32(buffer, Attributes.Count);
                foreach (var attribute in Attributes) {
                    ((Base2Ast)attribute).SerializeInto(buffer);
                }
            }

            if (Children.Count > 0) {
                AppendInt32(buffer, Children.Count);
                foreach (var child in Children) {
                    if (child != null) {
                        ((Base2Ast)child).SerializeInto(buffer);
                    } else {
                        // Null child sentinel: a block size of 0. Deserialize reads this and adds null.
                        AppendInt32(buffer, 0);
                    }
                }
            }

            // GrammarAddons are supplementary keyed sub-nodes (e.g. generic parameter lists),
            // appended last and gated by reserved[0] so older caches (0x00) still deserialize.
            if (GrammarAddons.Count > 0) {
                AppendInt32(buffer, GrammarAddons.Count);
                foreach (var addon in GrammarAddons) {
                    AppendString(buffer, addon.Key);
                    ((Base2Ast)addon.Value).SerializeInto(buffer);
                }
            }

            // Back-patch the block size now that the full block length is known.
            int blockSize = buffer.Count - blockStart;
            BinaryPrimitives.WriteInt32LittleEndian(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buffer).Slice(blockStart, 4),
                blockSize);
        }

        private static void AppendInt16(List<byte> buffer, short value)
        {
            Span<byte> tmp = stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(tmp, value);
            buffer.AddRange(tmp);
        }

        private static void AppendInt32(List<byte> buffer, int value)
        {
            Span<byte> tmp = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(tmp, value);
            buffer.AddRange(tmp);
        }

        private static void AppendInt64(List<byte> buffer, long value)
        {
            Span<byte> tmp = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(tmp, value);
            buffer.AddRange(tmp);
        }

        // Writes a length-prefixed UTF-8 string. A null or empty string writes a length of 0; the
        // present-but-empty vs null distinction for optional fields is carried by the node's bit
        // array (DocComment/ValueString) or, for LanguageMode, treated as null when the length is 0.
        private static void AppendString(List<byte> buffer, string? value)
        {
            if (string.IsNullOrEmpty(value)) {
                AppendInt32(buffer, 0);
                return;
            }
            AppendInt32(buffer, Encoding.UTF8.GetByteCount(value));
            buffer.AddRange(Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// TryDeserialize - try to deserialize a node from a byte array
        /// </summary>
        /// <param name="data">Byte array</param>
        /// <param name="node">Deserialized node</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool TryDeserialize(byte[] data, out Base2Ast? node, bool skipChildrenFlagsAndAttributes = false)
        {
            try {
                node = Deserialize(data, skipChildrenFlagsAndAttributes);
                return true;
            } catch {
                node = null;
                return false;
            }
        }

        public static bool TryDeserialize<TExpectedNodeType>(byte[] data, out TExpectedNodeType? node, bool skipChildrenFlagsAndAttributes = false)
            where TExpectedNodeType : Base2Ast
        {
            if (TryDeserialize(data, out Base2Ast? outNode, skipChildrenFlagsAndAttributes)) {
                if (outNode is TExpectedNodeType expectedNode) {
                    node = expectedNode;
                    return true;
                }
            }
            node = null;
            return false;
        }

        /// <summary>
        /// Deserialize - deserialize the node and children from a byte array.
        /// </summary>
        /// <param name="data">Byte array</param>
        /// <returns>Deserialized node</returns>
        public static Base2Ast Deserialize(byte[] data, bool skipChildrenFlagsAndAttributes = false)
        {
            if (data == null) {
                throw new Exception("Data is null");
            }

            if (data.Length < 52) {
                // 52 bytes is the minimum length of a valid node (header + positions including End*)
                throw new Exception("Invalid data length");
            }

            // Top-level block must span the whole buffer.
            if (BinaryPrimitives.ReadInt32LittleEndian(data) != data.Length) {
                throw new Exception("Invalid block size");
            }

            int offset = 0;
            return DeserializeBlock(data, ref offset, skipChildrenFlagsAndAttributes);
        }

        // Reads a single node block starting at <paramref name="offset"/> and advances the offset to
        // the end of that block. Operates on a shared ReadOnlySpan over the ORIGINAL buffer instead
        // of slicing a fresh byte[] per child/attribute (the previous implementation allocated an
        // array for every node). Do not reintroduce per-node array slicing here.
        private static Base2Ast DeserializeBlock(ReadOnlySpan<byte> data, ref int offset, bool skipChildrenFlagsAndAttributes)
        {
            int blockStart = offset;
            int blockSize = ReadInt32(data, ref offset);
            int blockEnd = blockStart + blockSize;
            if (blockSize < 52 || blockEnd > data.Length) {
                throw new Exception("Invalid block size");
            }

            byte nodeType = data[offset];
            offset += 1;

            byte bitArray = data[offset];
            offset += 1;

            long customNodeType = ReadInt64(data, ref offset);

            Type? reflectedType = GetNodeTypeClass(nodeType, customNodeType) ??
                throw new Exception("Invalid node type, binary node type id does not exist.");
            if (Activator.CreateInstance(reflectedType) is not Base2Ast nodeObj) {
                throw new Exception("Failed to create node object.");
            }

            // 2 bytes reserved. reserved[0] flags the presence of a GrammarAddons section appended
            // after Children (0x00 in older caches).
            bool hasGrammarAddons = data[offset] == 0x01;
            offset += 2;

            nodeObj.LanguageMode = ReadOptionalString(data, ref offset);
            nodeObj.Identifier = ReadString(data, ref offset);
            nodeObj.Line = ReadInt32(data, ref offset);
            nodeObj.Column = ReadInt32(data, ref offset);
            nodeObj.StartIndex = ReadInt32(data, ref offset);
            nodeObj.EndLine = ReadInt32(data, ref offset);
            nodeObj.EndColumn = ReadInt32(data, ref offset);
            nodeObj.EndIndex = ReadInt32(data, ref offset);

            nodeObj.DocComment = (bitArray & 0x08) != 0 ? ReadString(data, ref offset) : null;
            nodeObj.ValueString = (bitArray & 0x10) != 0 ? ReadString(data, ref offset) : null;
            nodeObj.ValueInt64 = (bitArray & 0x20) != 0 ? ReadInt64(data, ref offset) : null;

            if ((bitArray & 0x40) != 0) {
                int[] valueDecimalBits = [
                    ReadInt32(data, ref offset),
                    ReadInt32(data, ref offset),
                    ReadInt32(data, ref offset),
                    ReadInt32(data, ref offset),
                ];
                nodeObj.ValueDecimal = new decimal(valueDecimalBits);
            } else {
                nodeObj.ValueDecimal = null;
            }

            if ((bitArray & 0x80) != 0) {
                nodeObj.ValueBoolean = data[offset] != 0;
                offset += 1;
            } else {
                nodeObj.ValueBoolean = null;
            }

            if (!skipChildrenFlagsAndAttributes) {
                if ((bitArray & 0x02) != 0) {
                    int flagsLength = ReadInt32(data, ref offset);
                    for (int i = 0; i < flagsLength; i++) {
                        nodeObj.Flags.Add(ReadInt16(data, ref offset));
                    }
                } else {
                    nodeObj.Flags = [];
                }

                if ((bitArray & 0x04) != 0) {
                    int attributesLength = ReadInt32(data, ref offset);
                    for (int i = 0; i < attributesLength; i++) {
                        // Attributes are always non-null blocks; a 0 size would leave the offset
                        // unchanged (matching the previous slice-based behavior).
                        if (BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)) > 0) {
                            nodeObj.Attributes.Add(DeserializeBlock(data, ref offset, false));
                        }
                    }
                } else {
                    nodeObj.Attributes = [];
                }

                if ((bitArray & 0x01) != 0) {
                    int childrenLength = ReadInt32(data, ref offset);
                    for (int i = 0; i < childrenLength; i++) {
                        if (BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)) > 0) {
                            nodeObj.Children.Add(DeserializeBlock(data, ref offset, false));
                        } else {
                            // Null child sentinel: a 4-byte block size of 0.
                            nodeObj.Children.Add(null);
                            offset += 4;
                        }
                    }
                } else {
                    nodeObj.Children = [];
                }

                if (hasGrammarAddons) {
                    int grammarAddonCount = ReadInt32(data, ref offset);
                    for (int i = 0; i < grammarAddonCount; i++) {
                        var key = ReadString(data, ref offset);
                        nodeObj.GrammarAddons[key] = DeserializeBlock(data, ref offset, false);
                    }
                }
            }

            // Land exactly at the end of this block regardless of whether trailing sections were
            // skipped, so the caller can continue reading the next sibling block.
            offset = blockEnd;
            return nodeObj;
        }

        /// <summary>
        /// Reads only the <see cref="Identifier"/> and <see cref="ValueString"/> from a serialized
        /// <see cref="SrcFileAst"/> block, without constructing any node objects. The cache uses this
        /// to validate a hit's file name + content hash and, only on a confirmed match, pay for a
        /// single full deserialize. This replaces the old partial-then-full double deserialize that
        /// ran on every hit while keeping cache misses (name/hash mismatch) essentially free.
        /// </summary>
        /// <returns>True if the header was parsed; false if the data is malformed.</returns>
        public static bool TryReadSrcFileKey(ReadOnlySpan<byte> data, out string identifier, out string? valueString)
        {
            identifier = "";
            valueString = null;
            try {
                if (data.Length < 52) {
                    return false;
                }
                int offset = 0;
                int blockSize = ReadInt32(data, ref offset);
                if (blockSize != data.Length) {
                    return false;
                }
                offset += 1; // NodeType
                byte bitArray = data[offset];
                offset += 1;
                offset += 8; // CustomNodeType
                offset += 2; // reserved
                SkipString(data, ref offset);                 // LanguageMode
                identifier = ReadString(data, ref offset);    // Identifier
                offset += 24;                                 // Line, Column, StartIndex, EndLine, EndColumn, EndIndex
                if ((bitArray & 0x08) != 0) {
                    SkipString(data, ref offset);             // DocComment
                }
                if ((bitArray & 0x10) != 0) {
                    valueString = ReadString(data, ref offset); // ValueString (the file hash)
                }
                return true;
            } catch {
                identifier = "";
                valueString = null;
                return false;
            }
        }

        private static short ReadInt16(ReadOnlySpan<byte> data, ref int offset)
        {
            short value = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
            offset += 2;
            return value;
        }

        private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static long ReadInt64(ReadOnlySpan<byte> data, ref int offset)
        {
            long value = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8));
            offset += 8;
            return value;
        }

        // Reads a length-prefixed UTF-8 string; an empty payload yields "".
        private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
        {
            int length = ReadInt32(data, ref offset);
            if (length == 0) {
                return "";
            }
            string value = Encoding.UTF8.GetString(data.Slice(offset, length));
            offset += length;
            return value;
        }

        // Reads a length-prefixed UTF-8 string, mapping an empty payload to null (used for
        // LanguageMode, which does not have a presence bit).
        private static string? ReadOptionalString(ReadOnlySpan<byte> data, ref int offset)
        {
            int length = ReadInt32(data, ref offset);
            if (length == 0) {
                return null;
            }
            string value = Encoding.UTF8.GetString(data.Slice(offset, length));
            offset += length;
            return value;
        }

        // Advances past a length-prefixed UTF-8 string without decoding it.
        private static void SkipString(ReadOnlySpan<byte> data, ref int offset)
        {
            int length = ReadInt32(data, ref offset);
            offset += length;
        }

        public static TExpectedNodeType Deserialize<TExpectedNodeType>(byte[] data, bool skipChildrenFlagsAndAttributes = false)
            where TExpectedNodeType : Base2Ast
        {
            var node = Deserialize(data, skipChildrenFlagsAndAttributes);
            if (node is TExpectedNodeType expectedNode) {
                return expectedNode;
            }
            throw new Exception("Failed to deserialize AST node, expected node type \"" + typeof(TExpectedNodeType).Name + "\" but got \"" + node.GetType().Name + "\".");
        }

        /// <summary>
        /// Get the AST node class type from a node type ID and optional custom node type hash
        /// </summary>
        public static Type? GetNodeTypeClass(byte nodeType, long customNodeType = 0L) {
            Type? reflectedType = null;

            // Use registry for built-in types
            if (nodeType != AstNodeTypeRegistry.CustomNodeTypeByte) {
                try {
                    reflectedType = AstNodeTypeRegistry.GetTypeForNodeId(nodeType);
                    if (reflectedType != null) {
                        return reflectedType;
                    }
                }
                catch {
                    // Fall through to custom type lookup
                }
            } else if (customNodeType != 0) {
                try {
                    reflectedType = AstNodeTypeRegistry.GetCustomType(customNodeType);
                    if (reflectedType != null) {
                        return reflectedType;
                    }
                }
                catch {
                    // Fall through to return null
                }
            }
            return null;
        }

        internal bool ReplaceChild(Interfaces.IBase2Ast? oldChild, Interfaces.IBase2Ast? newChild)
        {
            for (var i = 0; i < this.Children.Count; i++)
            {
                if (ReferenceEquals(this.Children[i], oldChild))
                {
                    this.Children[i] = newChild;
                    return true;
                }
            }

            return false;
        }

        internal void ReplaceChildAt(int index, Interfaces.IBase2Ast? newChild)
        {
            if (index >= 0 && index < this.Children.Count)
            {
                this.Children[index] = newChild;
            }
        }

        internal void AddChild(Interfaces.IBase2Ast? child)
            => this.Children.Add(child);

        internal void ClearChildren()
            => this.Children.Clear();
    }
}