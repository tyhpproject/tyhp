namespace Tyhp.CLI
{
    using Tyhp.Domain.Diagnostics;
    using Tyhp.Domain.Exceptions;

    /// <summary>
    /// Base class for all CLI action runners.
    /// Actions that produce compilation results should return a CompilationResult from Start().
    /// Actions that do not produce compilation results (debug, generate-tyhpdef, etc.) should return null.
    /// </summary>
    public abstract class ActionRunnerBase : IDisposable
    {
        private bool disposedValue;

        /// <summary>
        /// Starts the action and returns a compilation result if applicable.
        /// </summary>
        /// <param name="cancellationToken">Token for canceling the action.</param>
        /// <returns>A CompilationResult if the action produces one (build, lint), otherwise null.</returns>
        public abstract CompilationResult? Start(CancellationToken cancellationToken);

        protected static void DisplayCheckerSummary(CompilationResult result)
        {
            // The checker only runs when parsing and binding completed without errors, in
            // which case CompilationService records a positive CheckDuration. When the check
            // phase is skipped (earlier pipeline errors, no scope tree, or SkipChecking),
            // CheckDuration is left at TimeSpan.Zero. Use that as the authoritative "did the
            // checker run" signal — a non-null GlobalScope is NOT sufficient, because the
            // binder still produces a scope tree even when it reports errors.
            if (result.CheckDuration > TimeSpan.Zero)
            {
                Message.Success(
                    "CLI_CheckPhaseCompleted",
                    result.CheckDuration.TotalSeconds,
                    result.CheckErrorCount,
                    result.Diagnostics.WarningCount);
            }
            else if (result.Diagnostics.HasErrors)
            {
                Message.Info("CLI_CheckerSkippedDueToErrors");
            }
        }

        protected static void DisplayBinderSummary(CompilationResult result)
        {
            if (result.GlobalScope != null)
            {
                Message.Success(
                    "CLI_BinderPhaseCompleted",
                    result.BindDuration.TotalSeconds,
                    result.ParsedFiles?.Count ?? 0);

                var fileCount = result.GlobalScope.FileScopeCount;
                var (symbolCount, scopeCount) = result.GlobalScope.GetCounts();

                int unresolvedCount = 0;
                int duplicateCount = 0;
                foreach (var d in result.Diagnostics.All)
                {
                    if (d.Code == MessageCode.BinderSymbolNotFound) unresolvedCount++;
                    else if (d.Code == MessageCode.BinderDuplicateSymbolDeclaration) duplicateCount++;
                }

                Message.Display("CLI_FilesBound", fileCount);
                Message.Display("CLI_SymbolsRegistered", symbolCount);
                Message.Display("CLI_ScopesCreated", scopeCount);
                if (unresolvedCount > 0)
                    Message.Display("CLI_UnresolvedReferences", unresolvedCount);
                if (duplicateCount > 0)
                    Message.Display("CLI_DuplicateDeclarations", duplicateCount);
            }
            else if (result.Diagnostics.HasErrors)
            {
                Message.Info("CLI_BinderNoScopeTree");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue) {
                if (disposing) {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

}