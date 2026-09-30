using Xunit;

namespace Tyhp.Tests.TestHelpers;

/// <summary>
/// Tests that mutate process-wide state (<c>Message</c> localizer,
/// <see cref="Directory.SetCurrentDirectory"/>, <see cref="Environment.ExitCode"/>,
/// <see cref="Console.Out"/> / <see cref="Console.Error"/> redirection). Must not overlap other
/// collections — any test that reads or writes one of these process-wide values, even without
/// asserting on it, belongs here so a concurrently-running class cannot flip it mid-test.
/// </summary>
[CollectionDefinition("ProcessGlobalState", DisableParallelization = true)]
public sealed class ProcessGlobalStateCollection
{
}
