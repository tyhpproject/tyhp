using System.Net.Sockets;
using Tyhp.XDebugProxy.Dbgp;

namespace Tyhp.XDebugProxy.Network
{
    /// <summary>
    /// Async DBGp framing over a TCP <see cref="NetworkStream"/> (or any <see cref="Stream"/>).
    /// Commands are read from the IDE side; responses (including <c>&lt;init&gt;</c>) from XDebug.
    /// Wire format is delegated to <see cref="DbgpMessageParser"/> — this type does not re-frame.
    /// </summary>
    public sealed class TcpConnectionHandler
    {
        public const int DefaultBufferSize = 8192;

        private readonly Stream _stream;
        private readonly int _bufferSize;

        public TcpConnectionHandler(TcpClient client, int bufferSize = DefaultBufferSize)
            : this(GetStream(client), bufferSize)
        {
            this.Client = client;
        }

        public TcpConnectionHandler(Stream stream, int bufferSize = DefaultBufferSize)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);

            this._stream = stream;
            this._bufferSize = bufferSize;
        }

        /// <summary>The wrapped client when constructed from <see cref="TcpClient"/>; otherwise null.</summary>
        public TcpClient? Client { get; }

        public Stream Stream => this._stream;

        public int BufferSize => this._bufferSize;

        public Task<DbgpCommand> ReadCommandAsync(CancellationToken cancellationToken = default)
        {
            return ReadCommandAsync(this._stream, cancellationToken);
        }

        public Task<DbgpResponse> ReadResponseAsync(CancellationToken cancellationToken = default)
        {
            return ReadResponseAsync(this._stream, cancellationToken);
        }

        public Task WriteCommandAsync(DbgpCommand command, CancellationToken cancellationToken = default)
        {
            return WriteCommandAsync(this._stream, command, cancellationToken, this._bufferSize);
        }

        public Task WriteResponseAsync(DbgpResponse response, CancellationToken cancellationToken = default)
        {
            return WriteResponseAsync(this._stream, response, cancellationToken, this._bufferSize);
        }

        /// <summary>Read one NUL-terminated IDE command. Does not read past the terminator.</summary>
        public static Task<DbgpCommand> ReadCommandAsync(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return DbgpMessageParser.ReadCommandFromStream(stream, cancellationToken);
        }

        /// <summary>
        /// Read one XDebug packet: ASCII length, NUL, XML bytes, trailing NUL.
        /// Includes the engine <c>&lt;init&gt;</c> packet sent on connect.
        /// </summary>
        public static Task<DbgpResponse> ReadResponseAsync(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return DbgpMessageParser.ReadResponseFromStream(stream, cancellationToken);
        }

        public static Task WriteCommandAsync(
            Stream stream,
            DbgpCommand command,
            CancellationToken cancellationToken = default)
        {
            return WriteCommandAsync(stream, command, cancellationToken, DefaultBufferSize);
        }

        public static Task WriteResponseAsync(
            Stream stream,
            DbgpResponse response,
            CancellationToken cancellationToken = default)
        {
            return WriteResponseAsync(stream, response, cancellationToken, DefaultBufferSize);
        }

        private static async Task WriteCommandAsync(
            Stream stream,
            DbgpCommand command,
            CancellationToken cancellationToken,
            int bufferSize)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(command);
            await WriteAllAsync(stream, DbgpMessageParser.SerializeCommand(command), bufferSize, cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task WriteResponseAsync(
            Stream stream,
            DbgpResponse response,
            CancellationToken cancellationToken,
            int bufferSize)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(response);
            await WriteAllAsync(stream, DbgpMessageParser.SerializeResponse(response), bufferSize, cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task WriteAllAsync(
            Stream stream,
            byte[] data,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                int count = Math.Min(bufferSize, data.Length - offset);
                await stream.WriteAsync(data.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
                offset += count;
            }

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static NetworkStream GetStream(TcpClient client)
        {
            ArgumentNullException.ThrowIfNull(client);
            return client.GetStream();
        }
    }
}
