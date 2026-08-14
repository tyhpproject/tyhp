namespace Tyhp.Tests.TestHelpers;

public static class SnapshotManager
{
    public static bool ShouldUpdateSnapshots { get; } =
        string.Equals(Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS"), "true", StringComparison.OrdinalIgnoreCase);

    public static void AssertMatchesSnapshot(string actualContent, string snapshotName, string category)
    {
        var snapshotPath = Path.Combine(TestFileManager.GetSnapshotsDirectory(), category, snapshotName);
        var snapshotDirectory = Path.GetDirectoryName(snapshotPath)!;
        Directory.CreateDirectory(snapshotDirectory);

        if (!File.Exists(snapshotPath))
        {
            if (ShouldUpdateSnapshots)
            {
                File.WriteAllText(snapshotPath, actualContent);
                return;
            }

            throw new InvalidOperationException(
                $"Snapshot '{category}/{snapshotName}' does not exist. "
                + "Re-run with UPDATE_SNAPSHOTS=true to create the baseline.");
        }

        var expected = File.ReadAllText(snapshotPath);
        if (ShouldUpdateSnapshots)
        {
            File.WriteAllText(snapshotPath, actualContent);
            return;
        }

        actualContent.Should().Be(expected, $"snapshot mismatch for {category}/{snapshotName}");
    }

    public static void UpdateSnapshot(string content, string snapshotName, string category)
    {
        var snapshotPath = Path.Combine(TestFileManager.GetSnapshotsDirectory(), category, snapshotName);
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        File.WriteAllText(snapshotPath, content);
    }
}
