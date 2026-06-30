using System.Threading;
using System.Threading.Tasks;
using WinButler.Models;

namespace WinButler.Services;

/// <summary>
/// The single execution chokepoint for every destructive action. When
/// <c>dryRun</c> is true it performs NO filesystem mutation — it only reports what
/// would happen. All deletion in the app must go through here.
/// </summary>
public interface ICleaner
{
    Task<CleanResult> CleanAsync(CleanupTarget target, bool dryRun, CancellationToken ct = default);
}
