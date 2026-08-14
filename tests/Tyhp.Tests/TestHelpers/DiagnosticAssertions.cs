using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers.Conformance;

namespace Tyhp.Tests.TestHelpers;

public static class DiagnosticAssertions
{
    public static void ShouldHaveErrorCount(this DiagnosticBag bag, int expectedCount)
    {
        bag.ErrorCount.Should().Be(expectedCount);
    }

    public static void ShouldContainErrorCode(this DiagnosticBag bag, MessageCode code)
    {
        bag.Errors.Should().Contain(d => d.Code == code);
    }

    public static void ShouldContainErrorCode(this DiagnosticBag bag, int code)
    {
        bag.Errors.Should().Contain(d => (int)d.Code == code);
    }

    public static void AssertExpectations(DiagnosticBag diagnostics, ConformanceExpectation? expectation)
    {
        expectation ??= new ConformanceExpectation();

        var hasAnyExpectation = expectation.NoDiagnostics.HasValue
            || expectation.ErrorCount.HasValue
            || expectation.WarningCount.HasValue
            || expectation.Codes is { Count: > 0 };
        hasAnyExpectation.Should().BeTrue(
            "a conformance case must specify at least one expectation "
            + "(noDiagnostics, errorCount, warningCount, or codes); none were found "
            + "(check for a typo'd expect key)");

        if (expectation.NoDiagnostics == true)
        {
            diagnostics.All.Should().BeEmpty();
            return;
        }

        if (expectation.ErrorCountExact.HasValue)
        {
            diagnostics.ErrorCount.Should().Be(expectation.ErrorCountExact.Value);
        }

        if (expectation.ErrorCountMin.HasValue)
        {
            diagnostics.ErrorCount.Should().BeGreaterThanOrEqualTo(expectation.ErrorCountMin.Value);
        }

        if (expectation.ErrorCountMax.HasValue)
        {
            diagnostics.ErrorCount.Should().BeLessThanOrEqualTo(expectation.ErrorCountMax.Value);
        }

        if (expectation.WarningCountExact.HasValue)
        {
            diagnostics.WarningCount.Should().Be(expectation.WarningCountExact.Value);
        }

        if (expectation.Codes is { Count: > 0 })
        {
            foreach (var code in expectation.Codes)
            {
                diagnostics.Errors.Should().Contain(d => (int)d.Code == code);
            }
        }
    }
}
