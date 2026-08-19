using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Tyhp.XDebugProxy.Dbgp
{
    public static partial class DbgpMessageParser
    {
        private static byte[] SerializeResponseXml(XElement root)
        {
            var document = new XDocument(
                new XDeclaration("1.0", DbgpConstants.DefaultEncodingName, null),
                new XElement(root));

            var settings = new XmlWriterSettings
            {
                Encoding = Utf8,
                OmitXmlDeclaration = false,
                Indent = false,
                CloseOutput = false,
                NewLineHandling = NewLineHandling.None,
            };

            using var buffer = new MemoryStream();
            using (var writer = XmlWriter.Create(buffer, settings))
            {
                document.Save(writer);
            }

            return buffer.ToArray();
        }

        private static DbgpResponse ParseResponseXml(byte[] xmlBytes)
        {
            if (xmlBytes.Length == 0)
            {
                throw new DbgpProtocolException("DBGp response XML payload is empty.");
            }

            try
            {
                using var xmlStream = new MemoryStream(xmlBytes, writable: false);
                var document = XDocument.Load(xmlStream);
                if (document.Root is null)
                {
                    throw new DbgpProtocolException("DBGp response XML has no root element.");
                }

                return new DbgpResponse(document.Root);
            }
            catch (DbgpProtocolException)
            {
                throw;
            }
            catch (XmlException ex)
            {
                throw new DbgpProtocolException("DBGp response XML is malformed.", ex);
            }
        }

        private static byte[] ReadFramedXml(byte[] rawBytes, ref int offset)
        {
            int lengthEnd = IndexOfNull(rawBytes, offset);
            if (lengthEnd < 0)
            {
                throw new DbgpProtocolException("DBGp response is missing the length NUL terminator.");
            }

            string lengthText = Utf8.GetString(rawBytes, offset, lengthEnd - offset);
            int length = ParseLengthPrefix(lengthText);
            int xmlStart = lengthEnd + 1;
            if (xmlStart + length > rawBytes.Length)
            {
                throw new DbgpProtocolException(
                    $"DBGp response is truncated: declared {length} XML bytes but only {rawBytes.Length - xmlStart} remain.");
            }

            int terminatorIndex = xmlStart + length;
            if (terminatorIndex >= rawBytes.Length || rawBytes[terminatorIndex] != DbgpConstants.NullByte)
            {
                throw new DbgpProtocolException("DBGp response is missing the trailing NUL after the XML payload.");
            }

            var xmlBytes = new byte[length];
            Buffer.BlockCopy(rawBytes, xmlStart, xmlBytes, 0, length);
            offset = terminatorIndex + 1;
            return xmlBytes;
        }

        private static byte[] FrameXml(byte[] xmlBytes)
        {
            string lengthText = xmlBytes.Length.ToString(CultureInfo.InvariantCulture);
            byte[] lengthBytes = Utf8.GetBytes(lengthText);
            var framed = new byte[lengthBytes.Length + 1 + xmlBytes.Length + 1];
            Buffer.BlockCopy(lengthBytes, 0, framed, 0, lengthBytes.Length);
            framed[lengthBytes.Length] = DbgpConstants.NullByte;
            Buffer.BlockCopy(xmlBytes, 0, framed, lengthBytes.Length + 1, xmlBytes.Length);
            framed[^1] = DbgpConstants.NullByte;
            return framed;
        }

        private static int ParseLengthPrefix(string lengthText)
        {
            string trimmed = lengthText.Trim();
            if (trimmed.Length == 0
                || !int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int length))
            {
                throw new DbgpProtocolException($"DBGp response length prefix is not a valid integer: '{lengthText}'.");
            }

            if (length < 0 || length > MaxResponseXmlBytes)
            {
                throw new DbgpProtocolException(
                    $"DBGp response length {length} is outside the allowed range (0–{MaxResponseXmlBytes}).");
            }

            return length;
        }

        private static int IndexOfNull(byte[] data, int start)
        {
            for (int i = start; i < data.Length; i++)
            {
                if (data[i] == DbgpConstants.NullByte)
                {
                    return i;
                }
            }

            return -1;
        }

        private static async Task<DbgpCommand> ReadCommandFromStreamCore(
            Stream stream,
            CancellationToken cancellationToken)
        {
            byte[] payload = await ReadUntilNullAsync(stream, MaxCommandBytes, cancellationToken)
                .ConfigureAwait(false);
            return ParseCommand(payload);
        }

        private static async Task<DbgpResponse> ReadResponseFromStreamCore(
            Stream stream,
            CancellationToken cancellationToken)
        {
            byte[] lengthBytes = await ReadUntilNullAsync(stream, 32, cancellationToken)
                .ConfigureAwait(false);
            int length = ParseLengthPrefix(Utf8.GetString(lengthBytes));
            byte[] xmlBytes = await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
            await ReadTrailingNullAsync(stream, cancellationToken).ConfigureAwait(false);
            return ParseResponseXml(xmlBytes);
        }

        private static async Task<byte[]> ReadUntilNullAsync(
            Stream stream,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            var one = new byte[1];
            while (true)
            {
                int read = await stream.ReadAsync(one.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new DbgpProtocolException("Unexpected end of stream while waiting for a NUL terminator.");
                }

                if (one[0] == DbgpConstants.NullByte)
                {
                    return buffer.ToArray();
                }

                buffer.WriteByte(one[0]);
                if (buffer.Length > maxBytes)
                {
                    throw new DbgpProtocolException($"DBGp message exceeded the maximum size of {maxBytes} bytes.");
                }
            }
        }

        private static async Task<byte[]> ReadExactAsync(
            Stream stream,
            int count,
            CancellationToken cancellationToken)
        {
            if (count == 0)
            {
                return [];
            }

            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new DbgpProtocolException(
                        $"Unexpected end of stream while reading DBGp XML payload ({offset} of {count} bytes).");
                }

                offset += read;
            }

            return buffer;
        }

        private static async Task ReadTrailingNullAsync(Stream stream, CancellationToken cancellationToken)
        {
            var one = new byte[1];
            int read = await stream.ReadAsync(one.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new DbgpProtocolException("Unexpected end of stream while reading the trailing NUL.");
            }

            if (one[0] != DbgpConstants.NullByte)
            {
                throw new DbgpProtocolException("DBGp response XML was not followed by a NUL terminator.");
            }
        }
    }
}
