using WinPure.Models;

namespace WinPure.Services;

/// <summary>Applies and reverts tweaks, always snapshotting before changing anything.</summary>
public sealed class TweakEngine
{
    private readonly BackupManager _backupManager;

    public TweakEngine(BackupManager backupManager) => _backupManager = backupManager;

    public TweakStatus GetStatus(Tweak tweak, ScanContext ctx)
    {
        bool sawUnknown = false;
        foreach (var action in tweak.Actions)
        {
            bool? applied = action.IsApplied(ctx);
            if (applied is null) { sawUnknown = true; continue; }
            if (!applied.Value) return TweakStatus.Pending;
        }
        return sawUnknown ? TweakStatus.Unknown : TweakStatus.Optimized;
    }

    /// <summary>
    /// Applies and/or reverts a batch of tweaks in one session with a single backup snapshot.
    /// </summary>
    public List<TweakResult> ApplyChanges(
        IEnumerable<(Tweak tweak, bool apply)> changes,
        IProgress<string>? progress = null)
    {
        var session = _backupManager.CreateSession();
        var results = new List<TweakResult>();

        foreach (var (tweak, apply) in changes)
        {
            progress?.Report($"{(apply ? "Applying" : "Reverting")}: {tweak.Name}");
            try
            {
                if (apply)
                {
                    foreach (var action in tweak.Actions)
                        action.Apply(tweak, session.Entries);
                    session.TweakNames.Add(tweak.Name);
                    LogService.Log($"Applied: {tweak.Name}");
                }
                else
                {
                    foreach (var action in tweak.Actions)
                        action.RevertToDefault();
                    LogService.Log($"Reverted: {tweak.Name}");
                }
                results.Add(new TweakResult { Tweak = tweak, Success = true, WasApply = apply });
            }
            catch (Exception ex)
            {
                LogService.Log($"FAILED {(apply ? "apply" : "revert")} {tweak.Name}: {ex.Message}");
                results.Add(new TweakResult { Tweak = tweak, Success = false, WasApply = apply, Message = ex.Message });
            }
        }

        if (session.Entries.Count > 0)
            _backupManager.SaveSession(session);

        return results;
    }
}
