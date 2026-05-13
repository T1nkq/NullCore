using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using Voidstrap.UI.Elements.Dialogs;
using Voidstrap.UI.ViewModels.Settings;
using Wpf.Ui.Controls;
using Wpf.Ui.Hardware;

namespace Voidstrap.UI.Elements.Settings.Pages
{
    public partial class ChannelPage
    {
        public ChannelPage()
        {
            InitializeComponent();
            _ = AutoUpdateRobloxVersionAsync();
            DataContext = new ChannelViewModel();
        }

        private void ToggleSwitch_Checked_1(object sender, RoutedEventArgs e)
        {
            HardwareAcceleration.DisableAllAnimations();
            HardwareAcceleration.FreeMemory();
            HardwareAcceleration.OptimizeVisualRendering();
            HardwareAcceleration.DisableTransparencyEffects();
            HardwareAcceleration.MinimizeMemoryFootprint();
        }

        private void ToggleSwitch_Unchecked_1(object sender, RoutedEventArgs e)
        {
            Frontend.ShowMessageBox(
                "Please restart the application to re-enable animations.",
                MessageBoxImage.Warning,
                MessageBoxButton.OK
            );
        }

        private async Task AutoUpdateRobloxVersionAsync()
        {
            while (true)
            {
                try
                {
                    await GetRobloxVersionAPPAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AutoUpdate] Error updating Roblox version: {ex.Message}");
                }
                await Task.Delay(1000);
            }
        }

        private async Task GetRobloxVersionAPPAsync()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string localStoragePath = Path.Combine(localAppData, "Roblox", "LocalStorage");

                if (!Directory.Exists(localStoragePath))
                {
                    RobloxVersionAPP.Header = "Not Installed";
                    return;
                }

                var files = Directory.GetFiles(localStoragePath, "memProfStorage*.json", SearchOption.TopDirectoryOnly);
                if (files.Length == 0)
                {
                    RobloxVersionAPP.Header = "Not Installed";
                    return;
                }

                string? version = null;

                foreach (var file in files)
                {
                    try
                    {
                        string jsonContent = await File.ReadAllTextAsync(file);

                        var match = Regex.Match(jsonContent, "\"AppVersion\"\\s*:\\s*\"([^\"]+)\"");
                        if (match.Success)
                        {
                            version = match.Groups[1].Value;
                            break;
                        }
                    }
                    catch (IOException ioEx)
                    {
                        Debug.WriteLine($"[RobloxVersion] Error reading {file}: {ioEx.Message}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(version))
                {
                    RobloxVersionAPP.Header = $"Roblox {version}";
                }
                else
                {
                    RobloxVersionAPP.Header = "Not Installed";
                }
            }
            catch (Exception ex)
            {
                RobloxVersionAPP.Header = $"Roblox Version Error: {ex.Message}";
            }
        }

        private async void Check_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
                var release = await global::GithubUpdater.GetLatestReleaseAsync();

                if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                {
                    Frontend.ShowMessageBox(
                        "Could not fetch the latest GitHub release.",
                        MessageBoxImage.Warning
                    );
                    return;
                }

                if (global::GithubUpdater.IsNewerVersion(release.TagName, currentVersion))
                {
                    var dialog = new UpdatePromptDialog(release, currentVersion)
                    {
                        Owner = Window.GetWindow(this)
                    };

                    dialog.ShowDialog();

                    if (!dialog.ShouldDownload)
                        return;

                    string? originalButtonContent = null;
                    if (sender is System.Windows.Controls.Button button)
                    {
                        originalButtonContent = button.Content?.ToString();
                        button.Content = "Downloading...";
                    }

                    if (sender is FrameworkElement element)
                        element.IsEnabled = false;

                    bool updateStarted = await global::GithubUpdater.DownloadAndInstallUpdate(release.TagName);
                    if (updateStarted)
                    {
                        Application.Current.Shutdown();
                    }
                    else
                    {
                        if (sender is System.Windows.Controls.Button failedButton)
                            failedButton.Content = originalButtonContent ?? "Check for Updates";

                        if (sender is FrameworkElement failedElement)
                            failedElement.IsEnabled = true;

                        Frontend.ShowMessageBox(
                            "Update download failed. Check the release assets on GitHub.",
                            MessageBoxImage.Error
                        );
                    }
                }
                else
                {
                    Frontend.ShowMessageBox(
                        $"You are already running the latest version of {App.ProjectName}."
                    );
                }
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox(
                    $"Error checking for updates:\n{ex.Message}"
                );
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string basePath = Paths.Base;
                string appSettingsPath = Path.Combine(basePath, "AppSettings.json");

                if (File.Exists(appSettingsPath))
                {
                    File.Delete(appSettingsPath);
                }
                Process.Start(new ProcessStartInfo // this alr caused me enough fucking pain as is
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    UseShellExecute = true
                });

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox($"An error occurred: {ex.Message}");
            }
        }

        private void OpenChannelListDialog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ChannelListsDialog();
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
        }

    }
}
