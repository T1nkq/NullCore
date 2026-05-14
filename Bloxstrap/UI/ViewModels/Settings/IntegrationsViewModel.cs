using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using Voidstrap.Integrations;
using Wpf.Ui.Appearance;

namespace Voidstrap.UI.ViewModels.Settings
{
    public class IntegrationsViewModel : NotifyPropertyChangedViewModel
    {
        public ICommand AddIntegrationCommand => new RelayCommand(AddIntegration);
        public ICommand DeleteIntegrationCommand => new RelayCommand(DeleteIntegration);
        public ICommand BrowseIntegrationLocationCommand => new RelayCommand(BrowseIntegrationLocation);

        private readonly string _appStoragePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Roblox\LocalStorage\appStorage.json");
        private JsonObject _jsonData;
        private readonly ActivityWatcher _watcher;

        public IntegrationsViewModel(ActivityWatcher watcher)
        {
            _watcher = watcher;

            LoadSettings();
        }

        private void AddIntegration()
        {
            CustomIntegrations.Add(new CustomIntegration()
            {
                Name = Strings.Menu_Integrations_Custom_NewIntegration
            });

            SelectedCustomIntegrationIndex = CustomIntegrations.Count - 1;

            OnPropertyChanged(nameof(SelectedCustomIntegrationIndex));
            OnPropertyChanged(nameof(IsCustomIntegrationSelected));
        }

        private bool _robloxSystemTray;
        public bool RobloxSystemTray
        {
            get => _robloxSystemTray;
            set
            {
                if (_robloxSystemTray != value)
                {
                    _robloxSystemTray = value;
                    OnPropertyChanged(nameof(RobloxSystemTray));
                    SaveSetting("MinimizeToTray", value);
                }
            }
        }

        private bool _launchStartup;
        public bool LaunchStartup
        {
            get => _launchStartup;
            set
            {
                if (_launchStartup != value)
                {
                    _launchStartup = value;
                    OnPropertyChanged(nameof(LaunchStartup));
                    SaveSetting("LaunchAtStartup", value);
                }
            }
        }

        private void LoadSettings()
        {
            if (!File.Exists(_appStoragePath))
                return;

            var jsonText = File.ReadAllText(_appStoragePath);
            _jsonData = JsonNode.Parse(jsonText)?.AsObject();

            if (_jsonData == null)
                return;

            RobloxSystemTray = GetBool("MinimizeToTray");
            LaunchStartup = GetBool("LaunchAtStartup");
        }

        private bool GetBool(string key)
        {
            if (_jsonData[key] == null)
                return false;

            return bool.TryParse(_jsonData[key]?.ToString(), out bool result) && result;
        }

        private void SaveSetting(string key, bool value)
        {
            if (_jsonData == null)
                _jsonData = new JsonObject();

            _jsonData[key] = value.ToString().ToLower();

            Directory.CreateDirectory(Path.GetDirectoryName(_appStoragePath)!);
            File.WriteAllText(_appStoragePath,
                _jsonData.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private void DeleteIntegration()
        {
            if (SelectedCustomIntegration is null)
                return;

            CustomIntegrations.Remove(SelectedCustomIntegration);

            if (CustomIntegrations.Count > 0)
            {
                SelectedCustomIntegrationIndex = CustomIntegrations.Count - 1;
                OnPropertyChanged(nameof(SelectedCustomIntegrationIndex));
            }

            OnPropertyChanged(nameof(IsCustomIntegrationSelected));
        }

        private void BrowseIntegrationLocation()
        {
            if (SelectedCustomIntegration is null)
                return;

            var dialog = new OpenFileDialog
            {
                Filter = $"{Strings.Menu_AllFiles}|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            SelectedCustomIntegration.Name = dialog.SafeFileName;
            SelectedCustomIntegration.Location = dialog.FileName;
            OnPropertyChanged(nameof(SelectedCustomIntegration));
        }

        public bool ActivityTrackingEnabled
        {
            get => App.Settings.Prop.EnableActivityTracking;

            set
            {
                App.Settings.Prop.EnableActivityTracking = value;

                if (!value)
                {
                    ShowServerDetailsEnabled = value;
                    DisableAppPatchEnabled = value;

                    OnPropertyChanged(nameof(ShowServerDetailsEnabled));
                    OnPropertyChanged(nameof(DisableAppPatchEnabled));
                }
            }
        }

        public bool ShowServerDetailsEnabled
        {
            get => App.Settings.Prop.ShowServerDetails;
            set => App.Settings.Prop.ShowServerDetails = value;
        }

        public bool joinGameNotify
        {
            get => App.Settings.Prop.NotificationWindowShow;
            set => App.Settings.Prop.NotificationWindowShow = value;
        }

        public bool PlayerLogsEnabled
        {
            get => App.FastFlags.GetPreset("Players.LogLevel") == "trace";
            set
            {
                App.FastFlags.SetPreset("Players.LogLevel", value ? "trace" : null);
                App.FastFlags.SetPreset("Players.LogPattern", value ? "ExpChat/mountClientApp" : null);
            }
        }

        public bool UncapFPS
        {
            get => RobloxSettings.IsUncapped();
            set => RobloxSettings.SetUncapped(value);
        }

        public bool DisableAppPatchEnabled
        {
            get => App.Settings.Prop.UseDisableAppPatch;
            set => App.Settings.Prop.UseDisableAppPatch = value;
        }

        public ObservableCollection<CustomIntegration> CustomIntegrations
        {
            get => App.Settings.Prop.CustomIntegrations;
            set => App.Settings.Prop.CustomIntegrations = value;
        }

        public CustomIntegration? SelectedCustomIntegration { get; set; }
        public int SelectedCustomIntegrationIndex { get; set; }
        public bool IsCustomIntegrationSelected => SelectedCustomIntegration is not null;
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
