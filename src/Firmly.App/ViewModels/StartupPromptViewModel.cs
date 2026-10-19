using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel;

namespace Firmly.App.ViewModels;

/// <summary>Asks once, on the Battery page, whether Firmly should start at sign-in (<see cref="App.StartupPrompt"/>).</summary>
public partial class StartupPromptViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>Asks unless it was answered before, or starting at sign-in is already on or decided elsewhere.</summary>
    public async Task CheckAsync()
    {
        // Without a tray icon, starting at sign-in would just open the window, so don't suggest it.
        if (AppSettings.StartupAsked || !App.HasTray)
            return;

        // Not from inside a XAML callback: StartupTask makes a cross-process call that pumps messages.
        await Task.Yield();
        var state = (await StartupTask.GetAsync(AppSettings.StartupTaskId)).State;
        if (state == StartupTaskState.Disabled)
            IsOpen = true;
        else
            Answered();
    }

    [RelayCommand]
    private async Task TurnOnAsync()
    {
        Answered();
        await (await StartupTask.GetAsync(AppSettings.StartupTaskId)).RequestEnableAsync();
    }

    /// <summary>Closing the question counts as no; Settings can still turn it on.</summary>
    [RelayCommand]
    private void Dismiss() => Answered();

    /// <summary>Settles the question for good, e.g. when the switch in Settings was used.</summary>
    public void Answered()
    {
        AppSettings.StartupAsked = true;
        IsOpen = false;
    }
}
