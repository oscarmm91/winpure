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
    /// Applies and/or reverts a batch of tweaks, recording everything it touches in one
    /// backup session. Two guarantees hold for every single action:
    /// the snapshot of the current state reaches disk BEFORE the system is modified, and
    /// reverting restores the value this machine actually had — not a default from the catalog.
    /// </summary>
    public List<TweakResult> ApplyChanges(
        IEnumerable<(Tweak tweak, bool apply)> changes,
        IProgress<string>? progress = null)
    {
        var changeList = changes.ToList();
        var session = _backupManager.CreateSession();
        var results = new List<TweakResult>();

        // Read the backup history once: a revert needs to know what each tweak found on
        // this machine the first time it was applied.
        var history = changeList.Any(c => !c.apply)
            ? _backupManager.ListSessions()
            : new List<BackupSession>();

        foreach (var (tweak, apply) in changeList)
        {
            progress?.Report($"{(apply ? "Applying" : "Reverting")}: {tweak.Name}");
            try
            {
                if (apply) ApplyTweak(tweak, session);
                else RevertTweak(tweak, session, history);

                if (!session.TweakNames.Contains(tweak.Name)) session.TweakNames.Add(tweak.Name);
                LogService.Log($"{(apply ? "Applied" : "Reverted")}: {tweak.Name}");
                results.Add(new TweakResult { Tweak = tweak, Success = true, WasApply = apply });
            }
            catch (Exception ex)
            {
                LogService.Log($"FAILED {(apply ? "apply" : "revert")} {tweak.Name}: {ex.Message}");
                results.Add(new TweakResult { Tweak = tweak, Success = false, WasApply = apply, Message = ex.Message });
            }
        }

        return results;
    }

    private void ApplyTweak(Tweak tweak, BackupSession session)
    {
        foreach (var action in tweak.Actions)
        {
            action.Capture(tweak, session.Entries);
            FlushSnapshot(session);
            action.Apply();
        }
    }

    private void RevertTweak(Tweak tweak, BackupSession session, IReadOnlyList<BackupSession> history)
    {
        // Undoing is a change too: snapshot the current state first so this can be undone.
        foreach (var action in tweak.Actions)
            action.Capture(tweak, session.Entries);
        FlushSnapshot(session);

        var recorded = BackupManager.FindLatestEntriesFor(tweak.Id, history);
        if (recorded.Count > 0)
        {
            int failures = _backupManager.RestoreEntries(recorded);
            if (failures > 0)
                throw new InvalidOperationException($"{failures} of {recorded.Count} original values could not be restored (see the log).");
        }
        else
        {
            // Never applied through WinPure (or its backup is gone): the catalog default is
            // all we have. It is a guess about a stock system, not about this one.
            LogService.Log($"No backup found for {tweak.Id}; falling back to the catalog default.");
            foreach (var action in tweak.Actions)
                action.RevertToDefault();
        }
    }

    /// <summary>
    /// Persists the snapshot mid-batch. Failing here must stop the caller from touching the
    /// system: applying without a backup on disk is exactly what this class promises not to do.
    /// </summary>
    private void FlushSnapshot(BackupSession session)
    {
        try
        {
            _backupManager.SaveSession(session);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Backup could not be saved, so nothing was changed: {ex.Message}", ex);
        }
    }
}
