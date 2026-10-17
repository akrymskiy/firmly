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

    /// <summary>A change was refused or failed; carries the reason. Also shown in <see cref="Note"/>.</summary>
    public event Action<string>? ChangeFailed;

    /// <summary>A timed charge ran its course and the service switched back; carries the mode it's now on.</summary>
    public event Action<string>? OverrideEnded;

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
        new("1 day", TimeSpan.FromDays(1)),
        new("2 days", TimeSpan.FromDays(2)),
        new("3 days", TimeSpan.FromDays(3)),
        new("1 week", TimeSpan.FromDays(7)),
        new("2 weeks", TimeSpan.FromDays(14)),
        new("Until a date…", TimeSpan.Zero, IsCustom: true),
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

    /// <summary>Shows that there's no connection, so nothing to offer.</summary>
    public void Clear()
    {
        IsAvailable = false;
        HasOverride = false;
        Modes.Clear();
    }

    /// <summary>Shows what the service reported.</summary>
    /// <param name="supportsOverride">Whether the service lists <see cref="IpcFeatures.ChargeOverride"/>.</param>
    public void Update(FirmwareSnapshot snapshot, bool supportsOverride)
    {
        SupportsOverride = supportsOverride;
        if (snapshot.ChargeModes is not { } modes)
        {
            Clear();
            return;
        }

        var options = modes.Modes.Select(m => new ChargeModeOption(m.Id, m.Label, Explain(m), m.Kind, m.LimitPercent)).ToList();
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
            Note = Access == FirmwareChangeAccess.AdministratorRequired ? "Only administrators can change the charge mode on this device." : "";

        // Switch back to the current mode; when that's full charge already, keep the choice (or the first mode).
        var current = RestoreModes.ToList().FindIndex(o => o.Id == modes.CurrentId);
        RestoreIndex = current >= 0 ? current : RestoreIndex >= 0 && RestoreIndex < RestoreModes.Count ? RestoreIndex : 0;
        if (rebuilt)
            OnPropertyChanged(nameof(RestoreIndex));

        // Gone after its end time: the service switched back as planned (rather than someone ending it early).
        if (_override is { } ended && snapshot.ChargeOverride is null && ended.Until <= DateTimeOffset.Now
            && SelectedIndex >= 0 && SelectedIndex < options.Count)
            OverrideEnded?.Invoke(options[SelectedIndex].Label);

        _override = snapshot.ChargeOverride;
        HasOverride = _override is not null;
        WaitingForPassword = _override?.WaitingForPassword == true;
        if (_override is { } active)
        {
            var mode = options.FirstOrDefault(o => o.Id == active.ModeId)?.Label ?? active.ModeId;
            var restore = options.FirstOrDefault(o => o.Id == active.RestoreModeId)?.Label ?? active.RestoreModeId;
            if (active.WaitingForPassword)
            {
                OverrideTitle = "Your timed full charge has ended";
                OverrideMessage = $"Switching back to {restore} needs the BIOS password.";
                if (_askedForPasswordAt != active.Until)
                {
                    _askedForPasswordAt = active.Until;
                    SwitchBackNeedsPassword?.Invoke(restore);
                }
            }
            else
            {
                OverrideTitle = $"{mode} until {active.Until.ToLocalTime():f}";
                OverrideMessage = active.KeepsBiosPassword
                    ? $"Then Firmly switches back to {restore}, with the BIOS password it keeps until then."
                    : $"Then Firmly switches back to {restore}.";
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
                Note = "Pick a time between now and 31 days from now.";
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
        Note = "Applying…";
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
            Note = ex.Message;
            ChangeFailed?.Invoke(ex.Message);
            // Move the radio buttons back to what the firmware actually holds.
            if (restoreSelection)
                OnPropertyChanged(nameof(SelectedIndex));
        }
    }

    private static string Explain(ChargeMode mode) => mode.Kind switch
    {
        ChargeModeKind.Adaptive => "Your device decides when to charge to 100%, based on how you use it.",
        ChargeModeKind.Limited => $"Stops charging at {mode.LimitPercent ?? 80}% so the battery lasts more years. Best if you're mostly plugged in.",
        ChargeModeKind.Full => "Charges to 100%. Best when you need every bit of battery, such as when traveling.",
        _ => "",
    };
}
