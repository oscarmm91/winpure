using WinPure.Models;

namespace WinPure.Services;

/// <summary>Applies and reverts tweaks, always snapshotting before changing anything.</summary>
public sealed class TweakEngine
{
    private readonly BackupManager _backupManager;

    public TweakEngine(BackupManager backupManager) => _backupManager = backupManager;

    public TweakStatus GetStatus(Tweak tweak, ScanContext ctx)
    {
        // Optional actions are legacy fallbacks Windows may refuse to write; letting them
        // decide would pin a tweak to "Not applied" forever even though it did its job.
        var deciding = tweak.Actions.Where(a => !a.Optional).ToList();
        if (deciding.Count == 0) deciding = tweak.Actions.ToList();

        bool sawUnknown = false;
        foreach (var action in deciding)
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
            progress?.Report(apply ? Loc.F("Applying: {0}", Loc.T(tweak.Name)) : Loc.F("Reverting: {0}", Loc.T(tweak.Name)));
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

        // Every flush above happens BEFORE an action, and a tweak's name is recorded after it succeeds. Without this last
        // save, the final tweak of every batch never had its name on disk, and a batch whose actions capture nothing (a
        // Store removal alone) was not listed on Restore at all. Nothing changes after this point, so failing is only logged.
        try
        {
            _backupManager.SaveSession(session);
        }
        catch (Exception ex)
        {
            LogService.Log($"The backup could not record the names of this batch: {ex.Message}");
        }

        return results;
    }

    private void ApplyTweak(Tweak tweak, BackupSession session)
    {
        foreach (var action in tweak.Actions)
        {
            // Three steps, and only two of them are forgiving. A value Windows will not let us
            // write is often one it will not let us read either, so capturing an optional action
            // may fail — but the snapshot reaching disk is this class's whole promise and is
            // never excused: a failed backup must not be mistaken for a rejected legacy value.
            try
            {
                action.Capture(tweak, session.Entries);
            }
            catch (Exception ex) when (action.Optional)
            {
                LogService.Log($"{tweak.Id}: optional action could not be captured, skipping it — {ex.Message}");
                continue;
            }

            FlushSnapshot(session);

            try
            {
                action.Apply();
            }
            catch (Exception ex) when (action.Optional)
            {
                LogService.Log($"{tweak.Id}: optional action skipped — {ex.Message}");
            }
        }
    }

    private void RevertTweak(Tweak tweak, BackupSession session, IReadOnlyList<BackupSession> history)
    {
        // Undoing is a change too: snapshot the current state first so this can be undone.
        foreach (var action in tweak.Actions)
        {
            try
            {
                action.Capture(tweak, session.Entries);
            }
            catch (Exception ex) when (action.Optional)
            {
                LogService.Log($"{tweak.Id}: optional action could not be captured — {ex.Message}");
            }
        }
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
            {
                try
                {
                    action.RevertToDefault();
                }
                catch (Exception ex) when (action.Optional)
                {
                    LogService.Log($"{tweak.Id}: optional action skipped on revert — {ex.Message}");
                }
            }
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
