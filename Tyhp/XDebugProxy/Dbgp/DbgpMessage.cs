namespace Tyhp.XDebugProxy.Dbgp
{
    /// <summary>Which side of the proxy originated a DBGp message.</summary>
    public enum DbgpMessageDirection
    {
        IdeToXDebug = 0,
        XDebugToIde = 1,
    }

    /// <summary>
    /// Discriminated wrapper around either a <see cref="DbgpCommand"/> (IDE → XDebug)
    /// or a <see cref="DbgpResponse"/> (XDebug → IDE, including <c>&lt;init&gt;</c>).
    /// </summary>
    public sealed class DbgpMessage
    {
        private DbgpMessage(DbgpCommand command)
        {
            this.Command = command;
            this.Response = null;
            this.Direction = DbgpMessageDirection.IdeToXDebug;
        }

        private DbgpMessage(DbgpResponse response)
        {
            this.Command = null;
            this.Response = response;
            this.Direction = DbgpMessageDirection.XDebugToIde;
        }

        public DbgpCommand? Command { get; }

        public DbgpResponse? Response { get; }

        public DbgpMessageDirection Direction { get; }

        public bool IsCommand => this.Command is not null;

        public bool IsResponse => this.Response is not null;

        public static DbgpMessage FromCommand(DbgpCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            return new DbgpMessage(command);
        }

        public static DbgpMessage FromResponse(DbgpResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            return new DbgpMessage(response);
        }
    }

    /// <summary>
    /// Thrown when DBGp command or response bytes cannot be parsed or framed.
    /// Phase 5 will map this to <c>MessageCode.ProxyInvalidDbgpMessage</c> (7406) at the CLI boundary;
    /// this type is the protocol-layer error until then.
    /// </summary>
    public sealed class DbgpProtocolException : Exception
    {
        public DbgpProtocolException(string message)
            : base(message)
        {
        }

        public DbgpProtocolException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
