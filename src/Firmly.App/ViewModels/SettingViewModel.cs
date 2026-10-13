using CommunityToolkit.Mvvm.ComponentModel;
using Firmly.Core;
using Firmly.Ipc;

namespace Firmly.App.ViewModels;

/// <summary>One firmware setting; picking a different value sends it to the service straight away.</summary>
public partial class SettingViewModel : ObservableObject
{
    private readonly FirmlyClient _client;

    public SettingViewModel(SettingInfo info, FirmlyClient client)
    {
        _client = client;
        Name = info.Setting.Name;
        Options = info.Setting.PossibleValues;
        CurrentValue = info.Setting.CurrentValue;
        SelectedValue = info.Setting.CurrentValue;
        CanChange = info.Access == FirmwareChangeAccess.Allowed;
        Note = info.Access == FirmwareChangeAccess.AdministratorRequired ? "Only administrators can change this." : "";
    }

    public string Name { get; }

    public IReadOnlyList<string> Options { get; }

    [ObservableProperty]
    public partial string CurrentValue { get; set; }

    [ObservableProperty]
    public partial string? SelectedValue { get; set; }

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

    partial void OnSelectedValueChanged(string? value)
    {
        if (value is not null && value != CurrentValue && !IsBusy)
            _ = ApplyAsync(value);
    }

    private async Task ApplyAsync(string value)
    {
        IsBusy = true;
        Note = "Applying…";
        try
        {
            var updated = await _client.SetSettingAsync(Name, value);
            CurrentValue = updated.Setting.CurrentValue;
            Note = "";
        }
        catch (FirmlyServiceException ex)
        {
            Note = ex.Message;
        }
        finally
        {
            IsBusy = false;
            // Show what the firmware actually holds, whether or not the change went through.
            SelectedValue = CurrentValue;
        }
    }
}
