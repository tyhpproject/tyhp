namespace Tyhp.LanguageServer.Analysis
{
    /// <summary>
    /// Runs one full, compacting garbage collection after the server has been idle for a delay
    /// following analysis work. Each analysis cycle allocates a full project bind and check; without
    /// this the dead cycles pile up in gen 2 while the process sits idle, the OS swaps them out, and
    /// the next collection pages them back in.
    /// </summary>
    internal sealed class IdleMemoryCompactor : IDisposable
    {
        private readonly TimeSpan _delay;
        private readonly Func<bool> _isIdle;
        private readonly Action _collect;
        private readonly Timer? _timer;
        private int _workSinceCompaction;
        private int _disposed;

        /// <param name="delay">Quiet period before collecting. Zero or negative disables compaction.</param>
        /// <param name="isIdle">False while analysis is running or queued; the timer is re-armed.</param>
        /// <param name="collect">Collection to run; defaults to <see cref="CompactNow"/>.</param>
        public IdleMemoryCompactor(TimeSpan delay, Func<bool> isIdle, Action? collect = null)
        {
            this._delay = delay;
            this._isIdle = isIdle ?? throw new ArgumentNullException(nameof(isIdle));
            this._collect = collect ?? CompactNow;
            if (delay > TimeSpan.Zero)
            {
                this._timer = new Timer(_ => this.OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
            }
        }

        /// <summary>Records analysis work and restarts the quiet-period timer.</summary>
        public void NotifyWork()
        {
            if (this._timer is null || Volatile.Read(ref this._disposed) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref this._workSinceCompaction, 1);
            this.Arm();
        }

        /// <summary>
        /// Blocking, compacting gen 2 collection in aggressive mode, which also returns freed
        /// segments to the operating system.
        /// </summary>
        public static void CompactNow()
            => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) != 0)
            {
                return;
            }

            this._timer?.Dispose();
        }

        private void Arm()
        {
            try
            {
                this._timer?.Change(this._delay, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void OnTimer()
        {
            if (Volatile.Read(ref this._disposed) != 0)
            {
                return;
            }

            if (!this._isIdle())
            {
                this.Arm();
                return;
            }

            if (Interlocked.Exchange(ref this._workSinceCompaction, 0) == 0)
            {
                return;
            }

            this._collect();
        }
    }
}
