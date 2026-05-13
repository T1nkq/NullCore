using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Voidstrap.UI.Elements.Settings.Pages
{
    public partial class MobilePage
    {
        private readonly string mobileFolder = Path.Combine(Paths.Base, "Mobile");
        private readonly string remoteDesktopUrl = "https://remotedesktop.google.com/access";

        public MobilePage()
        {
            InitializeComponent();
            Loaded += MobilePage_Loaded;
        }

        private async void MobilePage_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            await StartInstallationAsync();
            OpenRemoteDesktopUrl();
        }

        private void OpenRemoteDesktopUrl()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start \"\" \"{remoteDesktopUrl}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);

                Frontend.ShowMessageBox(
                    "After completing all the steps and setting up a remote device, open your tablet or phone and go to:\n" +
                    "https://remotedesktop.google.com/access\n\n" +
                    "Then follow the instructions to connect to your PC."
                );

                NavigationService.Navigate(new IntegrationsPage());
            }
            catch (Exception ex)
            {
                StatusText.Text = "Failed to open Chrome Remote Desktop URL. Copy it manually: " + remoteDesktopUrl;
                Debug.WriteLine($"Error opening URL: {ex}");
            }
        }

        private async Task StartInstallationAsync()
        {
            InstallerProgressBar.Value = 0;

            if (IsChromeRemoteDesktopInstalled())
            {
                StatusText.Text = ":3 meow";
                InstallerProgressBar.Value = 100;
                InstallerCard.Visibility = System.Windows.Visibility.Collapsed;
                CompletionCard.Visibility = System.Windows.Visibility.Visible;
                return;
            }

            if (!Directory.Exists(mobileFolder))
                Directory.CreateDirectory(mobileFolder);

            StatusText.Text = "Opening Chrome Remote Desktop setup...";
            InstallerProgressBar.Value = 100;
            InstallerCard.Visibility = System.Windows.Visibility.Collapsed;
            CompletionCard.Visibility = System.Windows.Visibility.Visible;
        }

        private bool IsChromeRemoteDesktopInstalled()
        {
            string[] uninstallKeys = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var keyPath in uninstallKeys)
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key == null) continue;
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        using (RegistryKey subKey = key.OpenSubKey(subKeyName))
                        {
                            var displayName = subKey?.GetValue("DisplayName") as string;
                            if (!string.IsNullOrEmpty(displayName) &&
                                displayName.Contains("Chrome Remote Desktop Host", StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
