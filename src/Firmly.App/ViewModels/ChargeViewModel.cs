using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Firmly.Core;
using Firmly.Ipc;

namespace Firmly.App.ViewModels;

/// <summary>One charge mode on the Battery page: the vendor's name, and what it does in plain words.</summary>
/// <param name="LimitPercent">Where charging stops, for <see cref="ChargeModeKind.Limited"/>.</param>
public sealed record ChargeModeOption(string Id, string Label, string Explanation, ChargeModeKind Kind, int? LimitPercent = null)
{
    // Screen readers name each radio button and list item by this.
    public override string ToString() => $"{Label}. {Explanation}";
}

/// <summary>A length of time to charge fully for; or, when <paramref name="IsCustom"/>, until a chosen date and time.</summary>
public sealed record OverrideDuration(string Label, TimeSpan Length, bool IsCustom = false)
{
    public override string ToString() => Label;
}

/// <summary>The Battery page's charge controls: the charge mode, and a full charge for a while.</summary>
/// <param name="show">Shows a fresh snapshot everywhere, after a change.</param>
/// <param name="passwords">Adds the BIOS password to changes when one is set.</param>
public partial class ChargeViewModel(FirmlyClient client, Action<FirmwareSnapshot> show, BiosPasswordGate passwords) : ObservableObject
{
    private ChargeOverride? _override;
    private ChargeOverrideEnd? _lastEnd;
    private bool _hadSnapshot;

    /// <summary>A change was refused or failed; carries the reason. Also shown in <see cref="Note"/>.</summary>
    public event Action<string>? ChangeFailed;

    /// <summary>
    /// A timed charge ran its course while Firmly was running, and the service switched back (or found it couldn't);
    /// carries what to tell the user, and when it ended, for <see cref="DismissEndNote(DateTimeOffset)"/>.
    /// </summary>
    public event Action<string, DateTimeOffset>? OverrideEnded;

    /// <summary>
    /// A timed charge has ended, but switching back needs the BIOS password, which the service doesn't have; carries
    /// the mode to switch back to. Raised once per timed charge.
    /// </summary>
    public event Action<string>? SwitchBackNeedsPassword;

    // The end time of the timed charge SwitchBackNeedsPassword was raised for.
    private DateTimeOffset? _askedForPasswordAt;

    public ObservableCollection<ChargeModeOption> Modes { get; } = [];

    /// <summary>Index of the firmware's current mode in <see cref="Modes"/>; -1 when it matches none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentLimitPercent))]
    public partial int SelectedIndex { get; set; } = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange))]
    public partial FirmwareChangeAccess Access { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModes), nameof(IsOverrideOffered))]
    public partial bool IsAvailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverrideOffered))]
    public partial bool SupportsOverride { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange))]
    public partial bool IsBusy { get; set; }

    /// <summary>Why something didn't work, or who may change the mode; empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; set; } = "";

    public bool HasModes => IsAvailable && Modes.Count > 0;

    public bool HasNote => Note.Length > 0;

    public bool CanChange => Access == FirmwareChangeAccess.Allowed && !IsBusy;

    /// <summary>Where the current mode stops charging; null when it charges fully or decides by itself.</summary>
    public int? CurrentLimitPercent =>
        SelectedIndex >= 0 && SelectedIndex < Modes.Count && Modes[SelectedIndex].Kind == ChargeModeKind.Limited
            ? Modes[SelectedIndex].LimitPercent
            : null;

    /// <summary>Whether to offer a full charge for a while: the device has a full-charge mode and none is running.</summary>
    public bool IsOverrideOffered => IsAvailable && SupportsOverride && FullMode is not null && !HasOverride;

    // --- Full charge for a while ---

    public IReadOnlyList<OverrideDuration> Durations { get; } =
    [
        new(Text.Get("Duration_1Day"), TimeSpan.FromDays(1)),
        new(Text.Get("Duration_2Days"), TimeSpan.FromDays(2)),
        new(Text.Get("Duration_3Days"), TimeSpan.FromDays(3)),
        new(Text.Get("Duration_1Week"), TimeSpan.FromDays(7)),
        new(Text.Get("Duration_2Weeks"), TimeSpan.FromDays(14)),
        new(Text.Get("Duration_Custom"), TimeSpan.Zero, IsCustom: true),
    ];

    /// <summary>Index in <see cref="Durations"/>; 3 days to start with.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomDuration))]
    public partial int DurationIndex { get; set; } = 2;

    /// <summary>The last of <see cref="Durations"/>: until <see cref="CustomDate"/> at <see cref="CustomTime"/>.</summary>
    public int CustomDurationIndex => Durations.Count - 1;

    public bool IsCustomDuration => DurationIndex == CustomDurationIndex;

    /// <summary>For <see cref="IsCustomDuration"/>; 3 days from now to start with.</summary>
    [ObservableProperty]
    public partial DateTimeOffset? CustomDate { get; set; } = DateTimeOffset.Now.Date.AddDays(3);

    /// <summary>For <see cref="IsCustomDuration"/>: the time of day; the next whole hour to start with.</summary>
    [ObservableProperty]
    public partial TimeSpan CustomTime { get; set; } = TimeSpan.FromHours(Math.Min(23, DateTime.Now.Hour + 1));

    /// <summary>The first and last day <see cref="CustomDate"/> may be: the service allows up to 31 days.</summary>
    public DateTimeOffset FirstCustomDate => DateTimeOffset.Now.Date;

    public DateTimeOffset LastCustomDate => DateTimeOffset.Now.Date.AddDays(31);

    /// <summary>The modes it can switch back to: every mode but full charge.</summary>
    public ObservableCollection<ChargeModeOption> RestoreModes { get; } = [];

    /// <summary>Index in <see cref="RestoreModes"/>.</summary>
    [ObservableProperty]
    public partial int RestoreIndex { get; set; } = -1;

    /// <summary>The device's full-charge mode, if it has one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverrideOffered))]
    public partial ChargeModeOption? FullMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverrideOffered))]
    public partial bool HasOverride { get; set; }

    /// <summary>e.g. "Full charge until Sunday, October 19, 2026 12:00 PM".</summary>
    [ObservableProperty]
    public partial string OverrideTitle { get; set; } = "";

    /// <summary>e.g. "Then Firmly switches back to Maximize Battery Health Management."</summary>
    [ObservableProperty]
    public partial string OverrideMessage { get; set; } = "";

    /// <summary>The timed charge has ended, and switching back waits for the BIOS password.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverrideSeverity))]
    public partial bool WaitingForPassword { get; set; }

    public Microsoft.UI.Xaml.Controls.InfoBarSeverity OverrideSeverity => WaitingForPassword
        ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning
        : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;

    // --- How the last one ended ---

    /// <summary>
    /// The service ended the last timed charge by itself, and this user hasn't closed the note about it yet. Shown
    /// whether or not Firmly was running then; gone once another timed charge starts.
    /// </summary>
    [ObservableProperty]
    public partial bool HasEndNote { get; set; }

    /// <summary>e.g. "On Saturday, October 18, 2026 3:12 PM, Firmly switched the battery back to ...".</summary>
    [ObservableProperty]
    public partial string EndNoteMessage { get; set; } = "";

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Controls.InfoBarSeverity EndNoteSeverity { get; set; }

    /// <summary>Closes the note about how the last timed charge ended, for this user.</summary>
    [RelayCommand]
    private void DismissEndNote()
    {
        if (_lastEnd is { } end)
            DismissEndNote(end.EndedAt);
    }

    /// <summary>Closes the note about a timed charge that ended at <paramref name="endedAt"/>, or before.</summary>
    public void DismissEndNote(DateTimeOffset endedAt)
    {
        if (endedAt > AppSettings.EndNoteClosedUpTo)
            AppSettings.EndNoteClosedUpTo = endedAt;
        ShowEndNote();
    }

    private void ShowEndNote()
    {
        HasEndNote = _lastEnd is { } end && !HasOverride && end.EndedAt > AppSettings.EndNoteClosedUpTo;
        if (_lastEnd is not { } last)
            return;

        EndNoteMessage = last.SwitchedBack
            ? Text.Format("Charge_EndedSwitchedBack", Label(last.RestoreModeId), last.EndedAt.ToLocalTime())
            : Text.Format("Charge_EndedStayed", Label(last.RestoreModeId), last.EndedAt.ToLocalTime(), Label(last.ModeId));
        EndNoteSeverity = last.SwitchedBack
            ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success
            : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
    }

    /// <summary>Shows that there's no connection, so nothing to offer.</summary>
    public void Clear()
    {
        IsAvailable = false;
        HasOverride = false;
        HasEndNote = false;
        Modes.Clear();
    }

    /// <summary>Shows what the service reported.</summary>
    /// <param name="supportsOverride">Whether the service lists <see cref="IpcFeatures.ChargeOverride"/>.</param>
    /// <param name="reportsOverrideEnd">Whether the service lists <see cref="IpcFeatures.ChargeOverrideEnd"/>.</param>
    public void Update(FirmwareSnapshot snapshot, bool supportsOverride, bool reportsOverrideEnd)
    {
        SupportsOverride = supportsOverride;
        if (snapshot.ChargeModes is not { } modes)
        {
            Clear();
            return;
        }

        var options = modes.Modes.Select(m => new ChargeModeOption(m.Id, Label(m), Explain(m), m.Kind, m.LimitPercent)).ToList();
        var rebuilt = !options.SequenceEqual(Modes);
        if (rebuilt)
        {
            Modes.Clear();
            foreach (var option in options)
                Modes.Add(option);

            RestoreModes.Clear();
            foreach (var option in options.Where(o => o.Kind != ChargeModeKind.Full))
                RestoreModes.Add(option);
            FullMode = options.FirstOrDefault(o => o.Kind == ChargeModeKind.Full);
        }

        IsAvailable = true;
        OnPropertyChanged(nameof(HasModes));
        SelectedIndex = options.FindIndex(o => o.Id == modes.CurrentId);
        // Refilling the list clears the radio buttons' selection, so push it again even when unchanged.
        if (rebuilt)
            OnPropertyChanged(nameof(SelectedIndex));
        Access = snapshot.ChargeModeAccess;
        if (!IsBusy)
            Note = Access == FirmwareChangeAccess.AdministratorRequired ? Text.Get("Charge_AdminOnly") : "";

        // Switch back to the current mode; when that's full charge already, keep the choice (or the first mode).
        var current = RestoreModes.ToList().FindIndex(o => o.Id == modes.CurrentId);
        RestoreIndex = current >= 0 ? current : RestoreIndex >= 0 && RestoreIndex < RestoreModes.Count ? RestoreIndex : 0;
        if (rebuilt)
            OnPropertyChanged(nameof(RestoreIndex));

        if (reportsOverrideEnd)
        {
            // Ended since the last look: say so now. One that ended while Firmly wasn't running is only noted.
            if (snapshot.LastOverrideEnd is { } end && _hadSnapshot && end.EndedAt != _lastEnd?.EndedAt
                && end.EndedAt > AppSettings.EndNoteClosedUpTo)
                OverrideEnded?.Invoke(end.SwitchedBack
                    ? Text.Format("Notify_EndedText", Label(end.RestoreModeId))
                    : Text.Format("Notify_StayedText", Label(end.RestoreModeId)), end.EndedAt);
            _lastEnd = snapshot.LastOverrideEnd;
        }
        // An older service doesn't say: gone after its end time means it switched back as planned (rather than
        // someone ending it early).
        else if (_override is { } ended && snapshot.ChargeOverride is null && ended.Until <= DateTimeOffset.Now
            && SelectedIndex >= 0 && SelectedIndex < options.Count)
            OverrideEnded?.Invoke(Text.Format("Notify_EndedText", options[SelectedIndex].Label), ended.Until);
        _hadSnapshot = true;

        _override = snapshot.ChargeOverride;
        HasOverride = _override is not null;
        ShowEndNote();
        WaitingForPassword = _override?.WaitingForPassword == true;
        if (_override is { } active)
        {
            var mode = options.FirstOrDefault(o => o.Id == active.ModeId)?.Label ?? active.ModeId;
            var restore = options.FirstOrDefault(o => o.Id == active.RestoreModeId)?.Label ?? active.RestoreModeId;
            if (active.WaitingForPassword)
            {
                OverrideTitle = Text.Get("Charge_EndedTitle");
                OverrideMessage = Text.Format("Charge_WaitingForPassword", restore);
                if (_askedForPasswordAt != active.Until)
                {
                    _askedForPasswordAt = active.Until;
                    SwitchBackNeedsPassword?.Invoke(restore);
                }
            }
            else
            {
                OverrideTitle = Text.Format("Charge_Until", mode, active.Until.ToLocalTime());
                OverrideMessage = Text.Format(active.KeepsBiosPassword ? "Charge_ThenBackKeepingPassword" : "Charge_ThenBack", restore);
            }
        }
    }

    /// <summary>The user picked the mode at <paramref name="index"/>; switches to it if it's a real change.</summary>
    public void RequestMode(int index)
    {
        if (index >= 0 && index < Modes.Count && index != SelectedIndex && CanChange)
            _ = RunAsync(password => client.SetChargeModeAsync(Modes[index].Id, password), restoreSelection: true);
    }

    [RelayCommand]
    private Task StartOverrideAsync()
    {
        if (!IsOverrideOffered || !CanChange || FullMode is not { } full
            || DurationIndex < 0 || DurationIndex >= Durations.Count || RestoreIndex < 0 || RestoreIndex >= RestoreModes.Count)
            return Task.CompletedTask;

        var now = DateTimeOffset.Now;
        var until = now + Durations[DurationIndex].Length;
        if (IsCustomDuration)
        {
            until = CustomDate is { } date ? new DateTimeOffset(date.Date + CustomTime) : default;
            // A little short of the service's limit, so a slow click can't push it over.
            if (until <= now.AddMinutes(1) || until > now.AddDays(31).AddMinutes(-1))
            {
                Note = Text.Get("Charge_PickTime");
                return Task.CompletedTask;
            }
        }
        var restore = RestoreModes[RestoreIndex].Id;
        // With a password, the service may keep it to switch back by itself, if the user chose so in Settings.
        return RunAsync(password => client.SetChargeOverrideAsync(full.Id, restore, until, password,
            keepBiosPassword: password is not null && passwords.AllowSaved && AppSettings.SwitchBackByItself));
    }

    [RelayCommand]
    private Task SwitchBackNowAsync() => RunAsync(password => client.CancelChargeOverrideAsync(password));

    /// <param name="change">Gets the BIOS password, or null when none is needed.</param>
    private async Task RunAsync(Func<string?, Task<FirmwareSnapshot>> change, bool restoreSelection = false)
    {
        IsBusy = true;
        Note = Text.Get("Applying");
        try
        {
            var snapshot = await passwords.CallAsync(change);
            IsBusy = false;
            Note = "";
            show(snapshot);
        }
        catch (OperationCanceledException)
        {
            // No password given: nothing changed.
            IsBusy = false;
            Note = "";
            if (restoreSelection)
                OnPropertyChanged(nameof(SelectedIndex));
        }
        catch (FirmlyServiceException ex)
        {
            IsBusy = false;
            Note = Text.ForError(ex);
            ChangeFailed?.Invoke(Note);
            // Move the radio buttons back to what the firmware actually holds.
            if (restoreSelection)
                OnPropertyChanged(nameof(SelectedIndex));
        }
    }

    private static string Explain(ChargeMode mode) => mode.Kind switch
    {
        ChargeModeKind.Adaptive => Text.Get("Charge_ExplainAdaptive"),
        ChargeModeKind.Limited => Text.Format("Charge_ExplainLimited", mode.LimitPercent ?? 80),
        ChargeModeKind.Full => Text.Get("Charge_ExplainFull"),
        _ => "",
    };

    // The vendor's name for the mode, in the user's language when Firmly has it.
    private static string Label(ChargeMode mode) => Text.GetOr($"ChargeMode_{mode.Id.Replace('-', '_')}", mode.Label);

    // For a mode by id alone, which the firmware may no longer offer: the name Firmly has for it, or the id.
    private string Label(string modeId) =>
        Modes.FirstOrDefault(o => o.Id == modeId)?.Label ?? Text.GetOr($"ChargeMode_{modeId.Replace('-', '_')}", modeId);
}
