using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Firmly.Core;
using Firmly.Ipc;

namespace Firmly.App.ViewModels;

/// <summary>One charge mode on the Battery page: the vendor's name, and what it does in plain words.</summary>
public sealed record ChargeModeOption(string Id, string Label, string Explanation, ChargeModeKind Kind)
{
    // Screen readers name each radio button and list item by this.
    public override string ToString() => $"{Label}. {Explanation}";
}

/// <summary>A length of time to charge fully for.</summary>
public sealed record OverrideDuration(string Label, TimeSpan Length)
{
    public override string ToString() => Label;
}

/// <summary>The Battery page's charge controls: the charge mode, and a full charge for a while.</summary>
/// <param name="show">Shows a fresh snapshot everywhere, after a change.</param>
public partial class ChargeViewModel(FirmlyClient client, Action<FirmwareSnapshot> show) : ObservableObject
{
    private ChargeOverride? _override;

    /// <summary>A change was refused or failed; carries the reason. Also shown in <see cref="Note"/>.</summary>
    public event Action<string>? ChangeFailed;

    public ObservableCollection<ChargeModeOption> Modes { get; } = [];

    /// <summary>Index of the firmware's current mode in <see cref="Modes"/>; -1 when it matches none.</summary>
    [ObservableProperty]
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
    ];

    /// <summary>Index in <see cref="Durations"/>; 3 days to start with.</summary>
    [ObservableProperty]
    public partial int DurationIndex { get; set; } = 2;

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

        var options = modes.Modes.Select(m => new ChargeModeOption(m.Id, m.Label, Explain(m), m.Kind)).ToList();
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

        _override = snapshot.ChargeOverride;
        HasOverride = _override is not null;
        if (_override is { } active)
        {
            var mode = options.FirstOrDefault(o => o.Id == active.ModeId)?.Label ?? active.ModeId;
            var restore = options.FirstOrDefault(o => o.Id == active.RestoreModeId)?.Label ?? active.RestoreModeId;
            OverrideTitle = $"{mode} until {active.Until.ToLocalTime():f}";
            OverrideMessage = $"Then Firmly switches back to {restore}.";
        }
    }

    /// <summary>The user picked the mode at <paramref name="index"/>; switches to it if it's a real change.</summary>
    public void RequestMode(int index)
    {
        if (index >= 0 && index < Modes.Count && index != SelectedIndex && CanChange)
            _ = RunAsync(() => client.SetChargeModeAsync(Modes[index].Id), restoreSelection: true);
    }

    [RelayCommand]
    private Task StartOverrideAsync()
    {
        if (!IsOverrideOffered || !CanChange || FullMode is not { } full
            || DurationIndex < 0 || DurationIndex >= Durations.Count || RestoreIndex < 0 || RestoreIndex >= RestoreModes.Count)
            return Task.CompletedTask;

        var until = DateTimeOffset.Now + Durations[DurationIndex].Length;
        return RunAsync(() => client.SetChargeOverrideAsync(full.Id, RestoreModes[RestoreIndex].Id, until));
    }

    [RelayCommand]
    private Task SwitchBackNowAsync() => RunAsync(() => client.CancelChargeOverrideAsync());

    private async Task RunAsync(Func<Task<FirmwareSnapshot>> change, bool restoreSelection = false)
    {
        IsBusy = true;
        Note = "Applying…";
        try
        {
            var snapshot = await change();
            IsBusy = false;
            Note = "";
            show(snapshot);
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
