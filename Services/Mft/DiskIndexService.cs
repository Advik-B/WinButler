using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WinButler.Services.Mft;

/// <summary>
/// One shared, whole-volume disk index that every feature scan (Clean, Redirect, Dev Junk,
/// the Dashboard breakdown, Disk Explorer) reads from instead of each walking the filesystem
/// itself. A single MFT read of a drive (see <see cref="DiskScanService"/>) is turned into a
/// path → <see cref="DiskNode"/> lookup so a folder's size is an O(1) query rather than a fresh
/// recursive walk. WinButler always runs elevated, so the fast MFT path is always available.
///
/// Built lazily per drive and cached; <see cref="Invalidate"/> drops a drive's cache after a real
/// (non-dry-run) delete/move so the next scan rebuilds from post-mutation truth.
/// </summary>
public sealed class DiskIndexService
{
    private readonly DiskScanService _scan;
    private readonly object _gate = new();
    private readonly Dictionary<char, DriveIndex> _ready = new();
    private readonly Dictionary<char, (Task<DriveIndex> Task, CancellationTokenSource Cts)> _inFlight = new();
    private readonly Dictionary<char, int> _generation = new();

    public DiskIndexService(DiskScanService scan) => _scan = scan;

    /// <summary>The drive the OS (and hence the user profile / caches we scan) lives on.</summary>
    public static char SystemDrive =>
        char.ToUpperInvariant((Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\")[0]);

    /// <summary>
    /// Ensures <paramref name="drive"/> is indexed and returns the index. Single-flight: concurrent
    /// callers (e.g. the three Clean scanners running in parallel) share one MFT read. Idempotent —
    /// returns the cached index immediately once built. Await this at a scan's entry point, then the
    /// synchronous <see cref="GetSize"/> lookups the scanners issue are pure dictionary hits.
    /// </summary>
    public Task<DriveIndex> EnsureBuiltAsync(char drive, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        drive = char.ToUpperInvariant(drive);
        Task<DriveIndex> task;
        lock (_gate)
        {
            if (_ready.TryGetValue(drive, out var idx))
                return Task.FromResult(idx);
            if (_inFlight.TryGetValue(drive, out var running))
            {
                task = running.Task;
            }
            else
            {
                _generation.TryGetValue(drive, out var gen);
                // The build runs on its OWN token (cancelled only by Invalidate) — a caller's
                // token must not kill the read that other joiners are sharing.
                var cts = new CancellationTokenSource();
                task = BuildAsync(drive, gen, progress, cts);
                _inFlight[drive] = (task, cts);
            }
        }
        // Each joiner waits with its own token, so cancelling one page's scan abandons only
        // that page's wait.
        return ct.CanBeCanceled ? task.WaitAsync(ct) : task;
    }

    private async Task<DriveIndex> BuildAsync(char drive, int gen, IProgress<string>? progress, CancellationTokenSource cts)
    {
        try
        {
            var root = await _scan.ScanAsync($"{drive}:\\", progress, cts.Token).ConfigureAwait(false);
            var idx = DriveIndex.Build(drive, root);
            lock (_gate)
            {
                RemoveIfCurrent(drive, cts);
                // Publish only if we weren't invalidated mid-build (generation still matches).
                _generation.TryGetValue(drive, out var current);
                if (current == gen)
                    _ready[drive] = idx;
            }
            return idx;
        }
        catch
        {
            lock (_gate) { RemoveIfCurrent(drive, cts); }
            throw;
        }
        finally
        {
            // Safe: Invalidate only cancels under _gate while the entry is still present, and
            // both removal paths above run under _gate before this disposal.
            cts.Dispose();
        }
    }

    /// <summary>Removes this build's in-flight entry — but never a successor's (a fresh build
    /// started after an Invalidate must not be evicted by the doomed one it replaced). Call
    /// under <see cref="_gate"/>.</summary>
    private void RemoveIfCurrent(char drive, CancellationTokenSource cts)
    {
        if (_inFlight.TryGetValue(drive, out var entry) && ReferenceEquals(entry.Cts, cts))
            _inFlight.Remove(drive);
    }

    /// <summary>
    /// O(1) aggregate size for an indexed folder, or null if the path isn't in the index (e.g. an
    /// un-indexed drive) — callers then fall back to a live walk. Only directories are indexed;
    /// callers never ask this about individual files.
    /// </summary>
    public long? GetSize(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath) || absolutePath.Length < 2 || absolutePath[1] != ':')
            return null;
        char drive = char.ToUpperInvariant(absolutePath[0]);
        DriveIndex? idx;
        lock (_gate) { _ready.TryGetValue(drive, out idx); }
        return idx?.GetSize(absolutePath);
    }

    /// <summary>The built index for a drive, or null if it hasn't been built (or was invalidated).</summary>
    public DriveIndex? TryGet(char drive)
    {
        drive = char.ToUpperInvariant(drive);
        lock (_gate) { _ready.TryGetValue(drive, out var idx); return idx; }
    }

    /// <summary>
    /// Drops a drive's cached index (call after a real delete/move, before the auto-rescan). Bumps a
    /// generation counter and forgets any in-flight build so a stale result can't be published or
    /// handed to a post-invalidation caller; the next <see cref="EnsureBuiltAsync"/> rebuilds.
    /// </summary>
    public void Invalidate(char drive)
    {
        drive = char.ToUpperInvariant(drive);
        lock (_gate)
        {
            _ready.Remove(drive);
            if (_inFlight.TryGetValue(drive, out var entry))
            {
                // Actively stop the doomed build (its result would fail the generation check
                // anyway). Merely forgetting it left the raw-volume read running while the
                // post-invalidate rescan started a second concurrent MFT read of the same drive.
                try { entry.Cts.Cancel(); } catch { }
                _inFlight.Remove(drive);
            }
            _generation[drive] = (_generation.TryGetValue(drive, out var g) ? g : 0) + 1;
        }
    }
}

/// <summary>
/// An immutable per-drive index over a scanned <see cref="DiskNode"/> tree: a normalized
/// absolute-path → node map for O(1) folder-size lookups. Built once off the UI thread and never
/// mutated afterwards, so reads need no locking.
/// </summary>
public sealed class DriveIndex
{
    private readonly Dictionary<string, DiskNode> _byPath;

    public char Drive { get; }
    public DiskNode Root { get; }

    private DriveIndex(char drive, DiskNode root, Dictionary<string, DiskNode> byPath)
    {
        Drive = drive;
        Root = root;
        _byPath = byPath;
    }

    /// <summary>Aggregate real ($DATA) bytes for a folder, or null if it isn't indexed.</summary>
    public long? GetSize(string absolutePath) =>
        _byPath.TryGetValue(Normalize(absolutePath), out var n) ? n.SizeBytes : null;

    /// <summary>The indexed node for a folder path, or null.</summary>
    public DiskNode? GetNode(string absolutePath) =>
        _byPath.TryGetValue(Normalize(absolutePath), out var n) ? n : null;

    private static readonly string[] MediaFolders = { "Pictures", "Videos", "Music", "Downloads" };

    /// <summary>
    /// A coarse System / Apps / Media split of the drive's used space for the dashboard bar, using
    /// allocated bytes so the buckets reconcile with the volume's free space. Classification is by
    /// well-known location (cheap O(1) lookups, no extra walk): Windows + ProgramData + the big
    /// root paging files → System; Program Files (+ per-user Local\Programs) → Apps; the per-user
    /// media folders → Media. Everything else is left for the caller to bucket as "Other" (used −
    /// these three), which absorbs caches, user data and metadata drift.
    /// </summary>
    public DiskBreakdown ComputeBreakdown()
    {
        long Alloc(string path) => GetNode(path)?.AllocBytes ?? 0;

        long system = Alloc($@"{Drive}:\Windows") + Alloc($@"{Drive}:\ProgramData");
        foreach (var child in Root.Children)
            if (!child.IsDirectory && IsPagingFile(child.Name))
                system += child.AllocBytes;

        long apps = Alloc($@"{Drive}:\Program Files") + Alloc($@"{Drive}:\Program Files (x86)");

        long media = 0;
        var users = GetNode($@"{Drive}:\Users");
        if (users is not null)
        {
            foreach (var user in users.Children)
            {
                if (!user.IsDirectory)
                    continue;
                apps += Alloc(user.FullPath + @"\AppData\Local\Programs");
                foreach (var folder in MediaFolders)
                    media += Alloc(user.FullPath + "\\" + folder);
            }
        }
        return new DiskBreakdown(system, apps, media);
    }

    private static bool IsPagingFile(string name) =>
        name.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("swapfile.sys", StringComparison.OrdinalIgnoreCase);

    // NTFS is case-insensitive; upper-case + drop any trailing separator so lookup keys and the
    // paths callers pass (from Environment.GetFolderPath / directory enumeration) always agree.
    private static string Normalize(string p) => p.TrimEnd('\\', '/').ToUpperInvariant();

    /// <summary>
    /// Builds the path index from a scanned tree. Indexes directories only — <see cref="GetSize"/>
    /// is only ever asked about folders, and skipping the (far more numerous) file leaves keeps the
    /// map small. Iterative to avoid deep recursion on deep trees.
    /// </summary>
    public static DriveIndex Build(char drive, DiskNode root)
    {
        var dict = new Dictionary<string, DiskNode>(StringComparer.Ordinal);
        var stack = new Stack<DiskNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            dict[Normalize(n.FullPath)] = n;
            foreach (var child in n.Children)
                if (child.IsDirectory)
                    stack.Push(child);
        }
        return new DriveIndex(char.ToUpperInvariant(drive), root, dict);
    }
}

/// <summary>A coarse split of a drive's used space, in allocated bytes. "Other" (caches, user data,
/// NTFS metadata) is the remainder the caller computes as used − System − Apps − Media.</summary>
public sealed record DiskBreakdown(long System, long Apps, long Media);
