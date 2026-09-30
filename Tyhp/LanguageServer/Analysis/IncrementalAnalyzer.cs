namespace Tyhp.LanguageServer.Analysis
{
    using System.Collections.Concurrent;
    using Tyhp.LanguageServer.Configuration;

    /// <summary>
    /// Per-document debounce for analysis requests. Changes to one URI do not delay
    /// another URI's timer.
    /// </summary>
    public sealed class IncrementalAnalyzer : IDisposable
    {
        private readonly AnalysisService _analysis;
        private readonly ServerConfiguration _configuration;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new(StringComparer.Ordinal);
        private int _disposed;

        public IncrementalAnalyzer(AnalysisService analysis, ServerConfiguration configuration)
        {
            this._analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
            this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Queues analysis for <paramref name="uri"/>. When <paramref name="debounce"/> is
        /// true, waits <see cref="ServerConfiguration.DebounceDelay"/> and coalesces
        /// rapid follow-up requests for the same URI.
        /// </summary>
        public void Request(Uri uri, bool debounce)
        {
            ArgumentNullException.ThrowIfNull(uri);
            if (this._disposed != 0)
            {
                return;
            }

            if (!this._analysis.IsAnalyzableUri(uri))
            {
                return;
            }

            this.CancelPending(uri);
            var cts = new CancellationTokenSource();
            this._pending[ToKey(uri)] = cts;
            _ = this.RunAsync(uri, debounce, cts);
        }

        /// <summary>
        /// Cancels a pending debounce (and in-flight wait) for <paramref name="uri"/>.
        /// </summary>
        public void Cancel(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            this.CancelPending(uri);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) != 0)
            {
                return;
            }

            foreach (KeyValuePair<string, CancellationTokenSource> pair in this._pending)
            {
                CancelAndDispose(pair.Value);
            }

            this._pending.Clear();
        }

        private async Task RunAsync(Uri uri, bool debounce, CancellationTokenSource cts)
        {
            try
            {
                if (debounce)
                {
                    int delay = Math.Max(0, this._configuration.DebounceDelay);
                    if (delay > 0)
                    {
                        await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                    }
                }

                cts.Token.ThrowIfCancellationRequested();
                await this._analysis.AnalyzeDocumentAsync(uri, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this._analysis.ReportAnalysisFailure(uri, ex);
            }
            finally
            {
                this._pending.TryRemove(new KeyValuePair<string, CancellationTokenSource>(ToKey(uri), cts));
                CancelAndDispose(cts);
            }
        }

        private void CancelPending(Uri uri)
        {
            if (this._pending.TryRemove(ToKey(uri), out CancellationTokenSource? existing))
            {
                CancelAndDispose(existing);
            }
        }

        private static void CancelAndDispose(CancellationTokenSource cts)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            cts.Dispose();
        }

        private static string ToKey(Uri uri)
            => uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString();
    }
}
