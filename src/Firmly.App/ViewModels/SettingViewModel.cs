using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Firmly.Core;
using Firmly.Ipc;

namespace Firmly.App.ViewModels;

/// <summary>One firmware setting. The view calls <see cref="RequestChange"/> when the user picks a value.</summary>
public partial class SettingViewModel : ObservableObject
{
    private readonly FirmlyClient _client;
    private readonly Func<Task> _reloadAll;

    /// <param name="reloadAll">Called after a successful change, since other settings can depend on this one.</param>
    public SettingViewModel(SettingInfo info, FirmlyClient client, Func<Task> reloadAll)
    {
        _client = client;
        _reloadAll = reloadAll;
        Name = info.Setting.Name;
        Options = [.. info.Setting.PossibleValues];
        Note = "";
        CurrentValue = "";
        Update(info);
    }

    public string Name { get; }

    public string[] Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedIndex))]
    public partial string CurrentValue { get; set; }

    /// <summary>The firmware's value in <see cref="Options"/>; -1 (blank) when the firmware reports something else.</summary>
    public int SelectedIndex => Array.IndexOf(Options, CurrentValue);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsValue))]
    public partial bool CanChange { get; set; }

    public bool ShowsValue => !CanChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; set; }

    public bool HasNote => Note.Length > 0;

    /// <summary>Shows a fresh reading from the service. Ignored while a change is in flight.</summary>
    public void Update(SettingInfo info)
    {
        if (IsBusy)
            return;

        CurrentValue = info.Setting.CurrentValue;
        CanChange = info.Access == FirmwareChangeAccess.Allowed;
        Note = info.Access == FirmwareChangeAccess.AdministratorRequired ? "Only administrators can change this." : "";
    }

    /// <summary>Handles SelectionChanged of this card's ComboBox.</summary>
    public void OnPickerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The binding only ever selects the firmware's current value (one-way), so a different selection can only
        // be the user's pick. A ComboBox reused from another card can briefly hold that card's items: only act
        // when the items are this setting's own.
        if (sender is ComboBox { SelectedIndex: >= 0 and var index } picker && ReferenceEquals(picker.ItemsSource, Options))
            RequestChange(Options[index]);
    }

    /// <summary>The user picked <paramref name="value"/>; sends it to the service if it's a real change.</summary>
    public void RequestChange(string value)
    {
        if (CanChange && !IsBusy && value != CurrentValue && Options.Contains(value))
            _ = ApplyAsync(value);
    }

    private async Task ApplyAsync(string value)
    {
        IsBusy = true;
        Note = "Applying…";
        try
        {
            var updated = await _client.SetSettingAsync(Name, value);
            IsBusy = false;
            Update(updated);
        }
        catch (FirmlyServiceException ex)
        {
            IsBusy = false;
            Note = ex.Message;
            // Move the ComboBox back to what the firmware actually holds.
            OnPropertyChanged(nameof(SelectedIndex));
            return;
        }

        // e.g. Battery Health Manager switches Adaptive Battery Optimizer on and off.
        await _reloadAll();
    }
}
