using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Voidstrap.UI.Elements.About;
using Voidstrap.UI.Elements.Dialogs;
using SymbolRegular = Wpf.Ui.Common.SymbolRegular;

namespace Voidstrap.UI.ViewModels.Settings
{
    public class MainWindowViewModel : NotifyPropertyChangedViewModel
    {
        private static readonly Brush OnlineBrush = CreateBrush("#FF6EE7C8");
        private static readonly Brush WarningBrush = CreateBrush("#FFFFC857");
        private static readonly Brush MutedBrush = CreateBrush("#FF7A8794");
        private static readonly Brush SuccessBrush = CreateBrush("#FF64F4A8");

        private string _networkStatusText = "Проверка";
        private Brush _networkStatusBrush = WarningBrush;
        private string _updateStatusText = "Проверка...";
        private Brush _updateStatusBrush = MutedBrush;
        private SymbolRegular _updateStatusIcon = SymbolRegular.Info20;
        private GithubUpdateRelease? _latestRelease;
        private bool _isCheckingUpdate;

        public string AppVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(); // fakin version
        public ICommand OpenAboutCommand => new RelayCommand(OpenAbout);

        public ICommand SaveSettingsCommand => new RelayCommand(SaveSettings);

        public ICommand SaveAndLaunchSettingsCommand => new RelayCommand(SaveAndLaunchSettings);

        public ICommand CheckUpdateCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand CloseWindowCommand => new RelayCommand(CloseWindow);

        public EventHandler? RequestSaveNoticeEvent;
        public EventHandler? RequestSaveLaunchNoticeEvent;

        public EventHandler? RequestCloseWindowEvent;

        public MainWindowViewModel()
        {
            CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync);
            _ = RefreshStatusAsync(false);
        }

        public string NetworkStatusText
        {
            get => _networkStatusText;
            private set => SetProperty(ref _networkStatusText, value);
        }

        public Brush NetworkStatusBrush
        {
            get => _networkStatusBrush;
            private set => SetProperty(ref _networkStatusBrush, value);
        }

        public string UpdateStatusText
        {
            get => _updateStatusText;
            private set => SetProperty(ref _updateStatusText, value);
        }

        public Brush UpdateStatusBrush
        {
            get => _updateStatusBrush;
            private set => SetProperty(ref _updateStatusBrush, value);
        }

        public SymbolRegular UpdateStatusIcon
        {
            get => _updateStatusIcon;
            private set => SetProperty(ref _updateStatusIcon, value);
        }

        public bool TestModeEnabled
        {
            get => App.LaunchSettings.TestModeFlag.Active;
            set
            {
                if (value && !App.State.Prop.TestModeWarningShown)
                {
                    var result = Frontend.ShowMessageBox(Strings.Menu_TestMode_Prompt, MessageBoxImage.Information, MessageBoxButton.YesNo);

                    if (result != MessageBoxResult.Yes)
                        return;

                    App.State.Prop.TestModeWarningShown = true;
                }

                App.LaunchSettings.TestModeFlag.Active = value;
            }
        }

        private void OpenAbout() => new MainWindow().ShowDialog();

        private void CloseWindow() => RequestCloseWindowEvent?.Invoke(this, EventArgs.Empty);

        private async Task CheckUpdateAsync()
        {
            if (_isCheckingUpdate)
            {
                Frontend.ShowMessageBox("NullCore уже проверяет обновления. Подожди пару секунд и нажми еще раз.", MessageBoxImage.Information);
                return;
            }

            await RefreshStatusAsync(true);

            if (_latestRelease is null)
            {
                Frontend.ShowMessageBox("Не удалось проверить обновления NullCore. Проверь интернет и попробуй еще раз.", MessageBoxImage.Warning);
                return;
            }

            if (!GithubUpdater.IsNewerVersion(_latestRelease.TagName, App.Version))
            {
                Frontend.ShowMessageBox("Установлена актуальная версия NullCore.", MessageBoxImage.Information);
                return;
            }

            var dialog = new UpdatePromptDialog(_latestRelease, App.Version)
            {
                Owner = Application.Current?.MainWindow
            };

            dialog.ShowDialog();

            if (!dialog.ShouldDownload)
                return;

            SetUpdateStatus("Загрузка...", WarningBrush, SymbolRegular.ArrowDownload24);

            bool installed = await GithubUpdater.DownloadAndInstallUpdate(_latestRelease.TagName);
            if (!installed)
            {
                SetUpdateStatus("Ошибка", WarningBrush, SymbolRegular.Info20);
                Frontend.ShowMessageBox("NullCore нашел обновление, но не смог скачать или подготовить установку.", MessageBoxImage.Error);
                return;
            }

            App.SoftTerminate();
        }

        private async Task RefreshStatusAsync(bool interactive)
        {
            if (_isCheckingUpdate)
                return;

            _isCheckingUpdate = true;
            SetNetworkStatus("Проверка", WarningBrush);
            SetUpdateStatus("Проверка...", MutedBrush, SymbolRegular.Info20);

            try
            {
                _latestRelease = await GithubUpdater.GetLatestReleaseAsync();

                if (_latestRelease is null)
                {
                    SetNetworkStatus("Offline", WarningBrush);
                    SetUpdateStatus("Нет связи", WarningBrush, SymbolRegular.Info20);
                    return;
                }

                SetNetworkStatus("Online", OnlineBrush);

                if (GithubUpdater.IsNewerVersion(_latestRelease.TagName, App.Version))
                {
                    SetUpdateStatus("Есть обновление", WarningBrush, SymbolRegular.ArrowDownload24);
                    return;
                }

                SetUpdateStatus("Актуально", SuccessBrush, SymbolRegular.CheckboxChecked24);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("MainWindowViewModel::RefreshStatusAsync", $"Update status check failed: {ex}");
                _latestRelease = null;
                SetNetworkStatus("Offline", WarningBrush);
                SetUpdateStatus("Нет связи", WarningBrush, SymbolRegular.Info20);

                if (interactive)
                    Frontend.ShowMessageBox("Не удалось проверить обновления NullCore. Проверь интернет и попробуй еще раз.", MessageBoxImage.Warning);
            }
            finally
            {
                _isCheckingUpdate = false;
            }
        }

        private void SetNetworkStatus(string text, Brush brush)
        {
            NetworkStatusText = text;
            NetworkStatusBrush = brush;
        }

        private void SetUpdateStatus(string text, Brush brush, SymbolRegular icon)
        {
            UpdateStatusText = text;
            UpdateStatusBrush = brush;
            UpdateStatusIcon = icon;
        }

        private static Brush CreateBrush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        private void SaveSettings()
        {
            const string LOG_IDENT = "MainWindowViewModel::SaveSettings";

            App.Settings.Save();
            App.State.Save();
            App.FastFlags.Save();

            foreach (var pair in App.PendingSettingTasks)
            {
                var task = pair.Value;

                if (task.Changed)
                {
                    App.Logger.WriteLine(LOG_IDENT, $"Executing pending task '{task}'");
                    task.Execute();
                }
            }

            App.PendingSettingTasks.Clear();

            RequestSaveNoticeEvent?.Invoke(this, EventArgs.Empty);
        }

        public void SaveAndLaunchSettings()
        {
            SaveSettings();
            RequestSaveLaunchNoticeEvent?.Invoke(this, EventArgs.Empty);
            LaunchHandler.LaunchRoblox(LaunchMode.Player);
        }

        protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, newValue))
            {
                return false;
            }

            field = newValue;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName ?? throw new ArgumentNullException(nameof(propertyName))));

            return true;
        }
    }
}
