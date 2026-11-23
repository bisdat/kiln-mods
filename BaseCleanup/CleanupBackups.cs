// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kiln;

namespace Bisdat.BaseCleanup;

/// <summary>
/// The only backups this mod ever prunes are successful Saves.Backup copies
/// explicitly named for Base Cleanup AND for the current world. Older versions'
/// backups, other mods' backups, and Kiln's restore snapshots are never touched.
/// </summary>
internal static class CleanupBackups
{
    internal const string Label = "bisdat-base-cleanup";
    internal const int KeepPerWorld = 10;
    private static readonly Regex FolderName = new(
        @"^\d{8}-\d{6}-bisdat-base-cleanup(?:-\d+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static void Verify(BackupInfo backup, string worldId)
    {
        if (backup.WorldId != worldId || backup.Name != Label ||
            backup.Files < 3 || backup.DataBundles < 1 ||
            string.IsNullOrWhiteSpace(backup.Directory))
            throw new InvalidOperationException(
                "Base Cleanup backup did not confirm a complete world file and matching mod-data generation");
        // Never trust an unexpected directory in backup metadata.
        ValidateOwnedFolder(backup, worldId);
    }

    // Always run AFTER the newly created full-world backup is verified.
    // Any unexpected path, incomplete listing, staged restore or deletion
    // failure prevents the voxel commit, rather than silently breaking retention.
    internal static int Prune(BackupInfo newest)
    {
        Verify(newest, newest.WorldId);
        var own = Saves.Backups
            .Where(b => b.WorldId == newest.WorldId && b.Name == Label)
            .OrderByDescending(b => PathComparer.Equals(
                Path.GetFullPath(b.Directory), Path.GetFullPath(newest.Directory)))
            .ThenByDescending(b => b.Created)
            .ThenByDescending(b => b.Directory, PathComparer)
            .ToArray();

        if (!own.Any(b => PathComparer.Equals(
                Path.GetFullPath(b.Directory), Path.GetFullPath(newest.Directory))))
            throw new InvalidOperationException(
                "New Base Cleanup backup is missing from Kiln's backup inventory; refusing cleanup");

        var excess = own.Skip(KeepPerWorld).ToArray();
        // Validate every removal target BEFORE deleting the first one.
        var dirs = excess.Select(b => ValidateOwnedFolder(b, newest.WorldId)).ToArray();
        var pending = Saves.PendingRestore;
        if (pending != null && dirs.Any(d => PathComparer.Equals(Path.GetFileName(d), pending)))
            throw new InvalidOperationException(
                "A Base Cleanup backup is staged for restore; retention will not delete it");

        foreach (var path in dirs)
            Directory.Delete(path, recursive: true);
        return dirs.Length;
    }

    private static string ValidateOwnedFolder(BackupInfo backup, string worldId)
    {
        if (backup.WorldId != worldId || backup.Name != Label)
            throw new InvalidOperationException("Refusing to remove a backup belonging to another world or mod");
        var root = Saves.BackupDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("Kiln's backup directory is unavailable");
        var trustedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(backup.Directory));
        if (!PathComparer.Equals(Path.GetDirectoryName(candidate), trustedRoot) ||
            !FolderName.IsMatch(Path.GetFileName(candidate)))
            throw new InvalidOperationException("Base Cleanup backup path did not match Kiln's owned backup directory");
        if (!Directory.Exists(candidate) || new DirectoryInfo(candidate).LinkTarget is not null ||
            !File.Exists(Path.Combine(candidate, "backup.json")))
            throw new InvalidOperationException("Base Cleanup backup is missing or is a filesystem link");
        return candidate;
    }
}
