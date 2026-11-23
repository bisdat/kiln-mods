// SPDX-License-Identifier: MIT
using Bisdat.BaseCleanup;
using Kiln;

namespace Kiln
{
    // Minimal contract stubs: the test compiles the production retention
    // implementation unchanged, with a fake Kiln backup inventory.
    public sealed record BackupInfo(
        string Name, string Directory, DateTimeOffset Created,
        string WorldId, int Files, int DataBundles);

    public static class Saves
    {
        public static string? BackupDirectory { get; set; }
        public static string? PendingRestore { get; set; }
        public static readonly List<BackupInfo> Catalog = [];
        public static IReadOnlyList<BackupInfo> Backups =>
            Catalog.Where(b => System.IO.Directory.Exists(b.Directory)).ToArray();
    }
}

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        checks++;
    }

    private static void MustReject(Action action, string description)
    {
        try { action(); }
        catch (InvalidOperationException) { checks++; return; }
        throw new Exception("FAIL: expected rejection — " + description);
    }

    private static BackupInfo Add(string root, DateTimeOffset date, string world, string? label = null)
    {
        label ??= CleanupBackups.Label;
        var name = $"{date:yyyyMMdd-HHmmss}-{label}";
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "backup.json"), "{}");
        var b = new BackupInfo(label, dir, date, world, 3, 1);
        Saves.Catalog.Add(b);
        return b;
    }

    private static int Count(string world, string label) =>
        Saves.Backups.Count(b => b.WorldId == world && b.Name == label);

    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "basecleanup-test-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "basecleanup-external-" + Guid.NewGuid().ToString("N"));
        const string worldA = "11111111-1111-1111-1111-111111111111";
        const string worldB = "22222222-2222-2222-2222-222222222222";
        var now = new DateTimeOffset(2026, 11, 23, 17, 0, 0, TimeSpan.Zero);
        try
        {
            Directory.CreateDirectory(root);
            Saves.BackupDirectory = root;
            var a = new List<BackupInfo>();
            for (var i = 0; i < 12; i++)
                a.Add(Add(root, now.AddMinutes(i), worldA));
            for (var i = 0; i < 3; i++)
                Add(root, now.AddHours(2).AddMinutes(i), worldB);
            var unrelated = Add(root, now.AddHours(3), worldA, "another-mod");
            var legacy = Add(root, now.AddHours(3).AddMinutes(1), worldA, "journal-op000001");

            Check(CleanupBackups.Prune(a[^1]) == 2, "only two oldest matching backups removed");
            Check(Count(worldA, CleanupBackups.Label) == 10, "ten Base Cleanup backups remain for world A");
            Check(Count(worldB, CleanupBackups.Label) == 3, "other world's copies retained");
            Check(Directory.Exists(unrelated.Directory), "unrelated mod backup preserved");
            Check(Directory.Exists(legacy.Directory), "legacy recovery backup preserved");
            Check(Directory.Exists(a[^1].Directory), "newly verified backup always retained");
            Check(!Directory.Exists(a[0].Directory) && !Directory.Exists(a[1].Directory),
                "oldest two backups actually deleted");

            MustReject(() => CleanupBackups.Verify(a[^1] with { Files = 2 }, worldA),
                "incomplete archive is not accepted");
            MustReject(() => CleanupBackups.Verify(a[^1], worldB),
                "a backup of another world is not accepted");

            // A staged restore must never be removed, and no deletion occurs
            // before every candidate has passed all safety checks.
            var oldest = Add(root, now.AddMinutes(-10), worldA);
            var nextOld = Add(root, now.AddMinutes(-9), worldA);
            Saves.PendingRestore = Path.GetFileName(oldest.Directory);
            MustReject(() => CleanupBackups.Prune(a[^1]), "staged restore is protected");
            Check(Directory.Exists(oldest.Directory) && Directory.Exists(nextOld.Directory),
                "staged restore refusal did not delete any candidate");
            Saves.PendingRestore = null;

            // Reject forged metadata aiming outside kiln/backups.
            var external = Add(outside, now.AddMinutes(-30), worldA);
            MustReject(() => CleanupBackups.Prune(a[^1]), "forged outside-root entry rejected");
            Check(Directory.Exists(oldest.Directory) && Directory.Exists(nextOld.Directory),
                "path refusal does not partially prune valid archives");
            Saves.Catalog.Remove(external);

            Check(CleanupBackups.Prune(a[^1]) == 2, "safe rotation resumes after removing external metadata");
            Check(Count(worldA, CleanupBackups.Label) == 10, "limit holds on second rotation");
            Check(Count(worldB, CleanupBackups.Label) == 3, "other world's copies still untouched");

            Console.WriteLine($"Base Cleanup backup retention: {checks} passed, 0 failed");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }
}
