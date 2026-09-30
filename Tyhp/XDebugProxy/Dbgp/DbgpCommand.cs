namespace Tyhp.XDebugProxy.Dbgp
{
    /// <summary>
    /// An IDE-to-XDebug DBGp command: <c>command_name -i transaction_id [arguments] [-- data]</c>.
    /// </summary>
    public sealed class DbgpCommand
    {
        public DbgpCommand(
            string commandName,
            string transactionId,
            IDictionary<string, string>? arguments = null,
            byte[]? data = null,
            string? rawText = null)
        {
            ArgumentNullException.ThrowIfNull(commandName);
            ArgumentNullException.ThrowIfNull(transactionId);

            this.CommandName = commandName;
            this.TransactionId = transactionId;
            this.Arguments = arguments is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(arguments, StringComparer.Ordinal);

            // -i lives exclusively in TransactionId; never let it leak into Arguments
            // (a caller-supplied dictionary could otherwise smuggle one in).
            this.Arguments.Remove(DbgpConstants.ArgumentFlags.TransactionId);

            this.Data = data;
            this.RawText = rawText ?? string.Empty;
        }

        /// <summary>Command verb (for example <c>breakpoint_set</c>).</summary>
        public string CommandName { get; set; }

        /// <summary>Value of the <c>-i</c> transaction id flag.</summary>
        public string TransactionId { get; set; }

        /// <summary>
        /// Flag/value pairs excluding <c>-i</c>. Keys include the leading dash
        /// (for example <c>-f</c>, <c>-n</c>).
        /// </summary>
        public Dictionary<string, string> Arguments { get; }

        /// <summary>Optional payload decoded from the base64 data after <c>--</c>.</summary>
        public byte[]? Data { get; set; }

        /// <summary>Original command text (without the trailing NUL), for passthrough.</summary>
        public string RawText { get; set; }

        /// <summary>Convenience accessor for <c>-f</c> (filename / file URI).</summary>
        public string? Filename
        {
            get => this.GetArgument(DbgpConstants.ArgumentFlags.Filename);
            set => this.SetArgument(DbgpConstants.ArgumentFlags.Filename, value);
        }

        /// <summary>Convenience accessor for <c>-n</c> (line number as the raw argument string).</summary>
        public string? LineNumber
        {
            get => this.GetArgument(DbgpConstants.ArgumentFlags.LineNumber);
            set => this.SetArgument(DbgpConstants.ArgumentFlags.LineNumber, value);
        }

        /// <summary>
        /// Look up an argument by flag. <paramref name="flag"/> may be <c>-f</c> or <c>f</c>.
        /// <c>-i</c> is redirected to <see cref="TransactionId"/>, since it is never stored
        /// in <see cref="Arguments"/>.
        /// </summary>
        public string? GetArgument(string flag)
        {
            ArgumentNullException.ThrowIfNull(flag);
            string key = NormalizeFlag(flag);
            if (string.Equals(key, DbgpConstants.ArgumentFlags.TransactionId, StringComparison.Ordinal))
            {
                return this.TransactionId;
            }

            return this.Arguments.TryGetValue(key, out string? value) ? value : null;
        }

        /// <summary>
        /// Set or remove an argument. A null <paramref name="value"/> removes the flag.
        /// <c>-i</c> is redirected to <see cref="TransactionId"/> instead of being stored in
        /// <see cref="Arguments"/>; a null <paramref name="value"/> is ignored for <c>-i</c>
        /// since a command always carries a transaction id.
        /// </summary>
        public void SetArgument(string flag, string? value)
        {
            ArgumentNullException.ThrowIfNull(flag);
            string key = NormalizeFlag(flag);
            if (string.Equals(key, DbgpConstants.ArgumentFlags.TransactionId, StringComparison.Ordinal))
            {
                if (value is not null)
                {
                    this.TransactionId = value;
                }

                return;
            }

            if (value is null)
            {
                this.Arguments.Remove(key);
            }
            else
            {
                this.Arguments[key] = value;
            }
        }

        private static string NormalizeFlag(string flag)
        {
            return flag.StartsWith('-') ? flag : "-" + flag;
        }
    }
}
