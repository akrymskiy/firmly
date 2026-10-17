using Firmly.Ipc;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Firmly.App;

/// <summary>Gets the BIOS password: the saved one through Windows Hello, or by asking in the main window.</summary>
internal sealed class BiosPasswordSource(BiosPasswordGate gate) : IBiosPasswordSource
{
    // One prompt at a time: a window can show only one ContentDialog.
    private readonly SemaphoreSlim _prompt = new(1, 1);

    public async Task<BiosPasswordAnswer?> GetAsync(BiosPasswordAnswer? rejected)
    {
        // The saved one is out of date, e.g. the password was changed in the BIOS setup.
        if (rejected is { FromVault: true })
            await BiosPasswordVault.ForgetAsync();

        if (rejected is null && gate.AllowSaved && BiosPasswordVault.HasSaved
            && await BiosPasswordVault.UnlockAsync() is { } saved)
            return new(saved, FromVault: true);

        await _prompt.WaitAsync();
        try
        {
            return await AskAsync(rejected is not null);
        }
        finally
        {
            _prompt.Release();
        }
    }

    public async Task AcceptedAsync(BiosPasswordAnswer answer)
    {
        if (answer.Remember && gate.AllowSaved)
            await BiosPasswordVault.SaveAsync(answer.Password);
    }

    private async Task<BiosPasswordAnswer?> AskAsync(bool rejected)
    {
        App.ShowMainWindow();
        var root = await App.GetXamlRootAsync();
        var canRemember = gate.AllowSaved && await BiosPasswordVault.IsAvailableAsync();

        var password = new PasswordBox { Header = Text.Get("Password_Header") };
        var remember = new CheckBox
        {
            Content = Text.Get("Password_Remember"),
            Visibility = canRemember ? Visibility.Visible : Visibility.Collapsed,
        };
        var panel = new StackPanel { Spacing = 12, MaxWidth = 440 };
        panel.Children.Add(new TextBlock
        {
            Text = Text.Get("Password_Explanation"),
            TextWrapping = TextWrapping.Wrap,
        });
        if (rejected)
            panel.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Title = Text.Get("Password_RejectedTitle"),
                Message = Text.Get("Password_RejectedMessage"),
            });
        panel.Children.Add(password);
        panel.Children.Add(remember);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            RequestedTheme = (App.Window.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            Title = Text.Get("Password_Title"),
            Content = panel,
            PrimaryButtonText = Text.Get("OK"),
            CloseButtonText = Text.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        password.PasswordChanged += (_, _) => dialog.IsPrimaryButtonEnabled = password.Password.Length > 0;
        dialog.Opened += (_, _) => password.Focus(FocusState.Programmatic);

        return await dialog.ShowAsync() == ContentDialogResult.Primary && password.Password.Length > 0
            ? new(password.Password, remember.IsChecked == true)
            : null;
    }
}
