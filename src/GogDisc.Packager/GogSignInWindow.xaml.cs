using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GogDisc.Core;

namespace GogDisc.Packager;

/// <summary>
/// The packager's own GOG sign-in. gogdl owns the credential file, so this only collects the
/// authorization code the browser hands back — the password never reaches this process.
/// </summary>
public partial class GogSignInWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();

    public GogSignInWindow() => InitializeComponent();

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(GogAuthentication.LoginUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Fail("The browser could not be opened: " + ex.Message); }
        CodeBox.Focus();
    }

    private void CodeBox_TextChanged(object sender, TextChangedEventArgs e) =>
        SignInButton.IsEnabled = !string.IsNullOrWhiteSpace(CodeBox.Text);

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        string code;
        try { code = GogAuthentication.ExtractAuthorizationCode(CodeBox.Text); }
        catch (Exception ex) { Fail(ex.Message); return; }

        SetBusy(true);
        MessageText.Foreground = Brush("#8F99AA");
        MessageText.Text = "Completing sign-in with GOG…";
        try
        {
            var runtime = new GogDlRuntime();
            await runtime.EnsureCurrentAsync(_cancellation.Token);
            await runtime.AuthenticateAsync(code, _cancellation.Token);
            if (!GogAuthentication.HasCredentials())
                throw new UnauthorizedAccessException(
                    "GOG did not return a usable sign-in. Authorization codes are single-use — sign in again for a fresh one.");
            DialogResult = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetBusy(false);
            Fail(ex.Message);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cancellation.Cancel();
        DialogResult = false;
    }

    private void SetBusy(bool busy)
    {
        SignInButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(CodeBox.Text);
        OpenBrowserButton.IsEnabled = !busy;
        CodeBox.IsEnabled = !busy;
    }

    private void Fail(string message)
    {
        MessageText.Foreground = Brush("#F08A5D");
        MessageText.Text = message;
    }

    private static System.Windows.Media.Brush Brush(string hex) =>
        (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
}
