using System.Text;

namespace Tyhp.XDebugProxy.Dbgp
{
    /// <summary>
    /// Parse and serialize DBGp wire messages. Commands are NUL-terminated text;
    /// responses are <c>length\0xml\0</c>. Stream helpers work against any
    /// <see cref="Stream"/> (including <see cref="MemoryStream"/>) and never over-read
    /// past a message boundary.
    /// </summary>
    public static partial class DbgpMessageParser
    {
        /// <summary>UTF-8 without BOM — default DBGp encoding.</summary>
        public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private const int MaxCommandBytes = 16 * 1024 * 1024;
        private const int MaxResponseXmlBytes = 64 * 1024 * 1024;

        /// <summary>
        /// Parse a NUL-terminated IDE command from UTF-8 bytes. A trailing NUL is optional.
        /// </summary>
        /// <exception cref="DbgpProtocolException">The bytes are not a valid DBGp command.</exception>
        public static DbgpCommand ParseCommand(byte[] rawBytes)
        {
            ArgumentNullException.ThrowIfNull(rawBytes);
            return ParseCommand(Utf8.GetString(StripTrailingNull(rawBytes)));
        }

        /// <summary>
        /// Parse an IDE command from text. A trailing NUL is optional and ignored.
        /// Unquoted argument values run until the next flag (<c>-x</c>) or <c>--</c>, so
        /// filenames may contain spaces. Double-quoted values may use <c>\"</c> and <c>\\</c>.
        /// </summary>
        /// <exception cref="DbgpProtocolException">The text is not a valid DBGp command.</exception>
        public static DbgpCommand ParseCommand(string rawText)
        {
            ArgumentNullException.ThrowIfNull(rawText);

            string text = rawText.TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new DbgpProtocolException("DBGp command is empty.");
            }

            int index = 0;
            SkipWhitespace(text, ref index);
            string commandName = ReadUntilWhitespace(text, ref index);
            if (commandName.Length == 0)
            {
                throw new DbgpProtocolException("DBGp command is missing a command name.");
            }

            var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
            string transactionId = string.Empty;
            byte[]? data = null;

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    break;
                }

                if (IsDataSeparator(text, index))
                {
                    index += DbgpConstants.DataSeparator.Length;
                    SkipWhitespace(text, ref index);
                    data = DecodeBase64Payload(text[index..].Trim());
                    break;
                }

                if (text[index] != '-' || index + 1 >= text.Length || !IsFlagNameChar(text[index + 1]))
                {
                    throw new DbgpProtocolException(
                        $"Unexpected token in DBGp command '{commandName}' at position {index}.");
                }

                int flagStart = index;
                index++;
                while (index < text.Length && IsFlagNameChar(text[index]))
                {
                    index++;
                }

                string flag = text[flagStart..index];
                SkipWhitespace(text, ref index);
                string value = ReadArgumentValue(text, ref index);

                if (string.Equals(flag, DbgpConstants.ArgumentFlags.TransactionId, StringComparison.Ordinal))
                {
                    transactionId = value;
                }
                else
                {
                    arguments[flag] = value;
                }
            }

            return new DbgpCommand(commandName, transactionId, arguments, data, text);
        }

        /// <summary>
        /// Serialize a command to UTF-8 wire bytes ending with a NUL. <c>-i</c> is always
        /// emitted first; remaining flags follow dictionary insertion order. Values with
        /// spaces, quotes, or a leading dash are double-quoted.
        /// </summary>
        /// <exception cref="DbgpProtocolException">The command name is missing.</exception>
        public static byte[] SerializeCommand(DbgpCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (string.IsNullOrWhiteSpace(command.CommandName))
            {
                throw new DbgpProtocolException("Cannot serialize a DBGp command without a command name.");
            }

            var builder = new StringBuilder();
            builder.Append(command.CommandName);
            builder.Append(' ');
            builder.Append(DbgpConstants.ArgumentFlags.TransactionId);
            builder.Append(' ');
            builder.Append(EncodeArgumentValue(command.TransactionId));

            foreach (KeyValuePair<string, string> pair in command.Arguments)
            {
                if (string.Equals(pair.Key, DbgpConstants.ArgumentFlags.TransactionId, StringComparison.Ordinal))
                {
                    continue;
                }

                builder.Append(' ');
                builder.Append(pair.Key.StartsWith('-') ? pair.Key : "-" + pair.Key);
                builder.Append(' ');
                builder.Append(EncodeArgumentValue(pair.Value));
            }

            if (command.Data is not null)
            {
                builder.Append(' ');
                builder.Append(DbgpConstants.DataSeparator);
                builder.Append(' ');
                if (command.Data.Length > 0)
                {
                    builder.Append(Convert.ToBase64String(command.Data, Base64FormattingOptions.None));
                }
            }

            builder.Append('\0');
            return Utf8.GetBytes(builder.ToString());
        }

        /// <summary>
        /// Parse an XDebug packet from <c>length\0xml\0</c> framing. <paramref name="rawBytes"/>
        /// may contain extra trailing bytes; only the first framed message is consumed.
        /// </summary>
        /// <exception cref="DbgpProtocolException">Framing or XML is invalid.</exception>
        public static DbgpResponse ParseResponse(byte[] rawBytes)
        {
            ArgumentNullException.ThrowIfNull(rawBytes);

            int offset = 0;
            byte[] xmlBytes = ReadFramedXml(rawBytes, ref offset);
            return ParseResponseXml(xmlBytes);
        }

        /// <summary>
        /// Serialize a response to <c>length\0xml\0</c>. <c>length</c> is the UTF-8 byte
        /// length of the XML, which is written with a UTF-8 declaration and no BOM.
        /// </summary>
        public static byte[] SerializeResponse(DbgpResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            return FrameXml(SerializeResponseXml(response.RootElement));
        }

        /// <summary>
        /// Read one command by consuming bytes until a NUL. Does not read past that NUL,
        /// so concatenated messages on the same stream remain available.
        /// </summary>
        /// <exception cref="DbgpProtocolException">The stream ends before a complete command.</exception>
        public static Task<DbgpCommand> ReadCommandFromStream(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return ReadCommandFromStreamCore(stream, cancellationToken);
        }

        /// <summary>
        /// Read one response: ASCII length, NUL, exactly that many XML bytes, trailing NUL.
        /// Reads one byte at a time for the length prefix and trailing NUL so a subsequent
        /// message is not consumed. XML payload reads may be chunked.
        /// </summary>
        /// <exception cref="DbgpProtocolException">The stream ends before a complete response, or XML is invalid.</exception>
        public static Task<DbgpResponse> ReadResponseFromStream(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return ReadResponseFromStreamCore(stream, cancellationToken);
        }
    }
}
