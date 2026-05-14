using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Voidstrap.UI.ViewModels.Settings
{
    public sealed class InstanceDashboardViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        private readonly DispatcherTimer _refreshTimer;
        private readonly Dictionary<int, ProcessSample> _samples = new();
        private RobloxInstanceViewModel? _selectedInstance;
        private bool _isAutoRefreshEnabled = true;
        private string _monitoringStatusText = "Preparing monitor...";
        private string _totalMemoryText = "0 MB";
        private string _totalCpuText = "0.0%";
        private string _averageCpuText = "0.0%";
        private string _heaviestInstanceText = "None";
        private string _lastUpdatedText = "Waiting for refresh";
        private int _instanceCount;

        public ObservableCollection<RobloxInstanceViewModel> Instances { get; } = new();

        public ICommand RefreshCommand { get; }
        public ICommand TrimSelectedMemoryCommand { get; }
        public ICommand EndSelectedCommand { get; }
        public ICommand OpenSelectedFolderCommand { get; }
        public ICommand CopySelectedInfoCommand { get; }

        public InstanceDashboardViewModel()
        {
            RefreshCommand = new RelayCommand(RefreshInstances);
            TrimSelectedMemoryCommand = new RelayCommand(TrimSelectedMemory);
            EndSelectedCommand = new RelayCommand(EndSelectedProcess);
            OpenSelectedFolderCommand = new RelayCommand(OpenSelectedFolder);
            CopySelectedInfoCommand = new RelayCommand(CopySelectedInfo);

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            RefreshInstances();
        }

        public RobloxInstanceViewModel? SelectedInstance
        {
            get => _selectedInstance;
            set
            {
                if (SetProperty(ref _selectedInstance, value))
                    OnPropertyChanged(nameof(HasSelectedInstance));
            }
        }

        public bool HasSelectedInstance => SelectedInstance is not null;

        public bool HasInstances => InstanceCount > 0;

        public bool IsAutoRefreshEnabled
        {
            get => _isAutoRefreshEnabled;
            set
            {
                if (!SetProperty(ref _isAutoRefreshEnabled, value))
                    return;

                if (value)
                {
                    _refreshTimer.Start();
                    RefreshInstances();
                }
                else
                {
                    _refreshTimer.Stop();
                    LastUpdatedText = $"{LastUpdatedText} | paused";
                }
            }
        }

        public int InstanceCount
        {
            get => _instanceCount;
            private set
            {
                if (SetProperty(ref _instanceCount, value))
                    OnPropertyChanged(nameof(HasInstances));
            }
        }

        public string MonitoringStatusText
        {
            get => _monitoringStatusText;
            private set => SetProperty(ref _monitoringStatusText, value);
        }

        public string TotalMemoryText
        {
            get => _totalMemoryText;
            private set => SetProperty(ref _totalMemoryText, value);
        }

        public string TotalCpuText
        {
            get => _totalCpuText;
            private set => SetProperty(ref _totalCpuText, value);
        }

        public string AverageCpuText
        {
            get => _averageCpuText;
            private set => SetProperty(ref _averageCpuText, value);
        }

        public string HeaviestInstanceText
        {
            get => _heaviestInstanceText;
            private set => SetProperty(ref _heaviestInstanceText, value);
        }

        public string LastUpdatedText
        {
            get => _lastUpdatedText;
            private set => SetProperty(ref _lastUpdatedText, value);
        }

        public void RefreshInstances()
        {
            int selectedPid = SelectedInstance?.Pid ?? -1;
            DateTime now = DateTime.UtcNow;
            List<RobloxInstanceViewModel> next = new();

            foreach (Process process in GetRobloxProcesses())
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    process.Refresh();
                    next.Add(CreateInstance(process, now));
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("InstanceDashboard", $"Failed to read process {SafeProcessLabel(process)}: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            next = next
                .OrderBy(instance => instance.StartedAtUtc == DateTime.MinValue ? DateTime.MaxValue : instance.StartedAtUtc)
                .ThenBy(instance => instance.Pid)
                .ToList();

            Instances.Clear();
            foreach (var instance in next)
                Instances.Add(instance);

            SelectedInstance = Instances.FirstOrDefault(instance => instance.Pid == selectedPid)
                               ?? Instances.FirstOrDefault();

            UpdateSummary(next);

            var activePids = next.Select(instance => instance.Pid).ToHashSet();
            foreach (int pid in _samples.Keys.Where(pid => !activePids.Contains(pid)).ToList())
                _samples.Remove(pid);

            LastUpdatedText = $"Updated {DateTime.Now:HH:mm:ss} | {(IsAutoRefreshEnabled ? "auto refresh on" : "auto refresh off")}";
        }

        public void Start()
        {
            if (IsAutoRefreshEnabled)
                _refreshTimer.Start();

            RefreshInstances();
        }

        public void Stop() => _refreshTimer.Stop();

        private void RefreshTimer_Tick(object? sender, EventArgs e) => RefreshInstances();

        private RobloxInstanceViewModel CreateInstance(Process process, DateTime now)
        {
            int pid = process.Id;
            TimeSpan totalProcessorTime = process.TotalProcessorTime;
            double cpuPercent = 0;

            if (_samples.TryGetValue(pid, out var previous))
            {
                double elapsedMs = Math.Max(1, (now - previous.TimestampUtc).TotalMilliseconds);
                double cpuMs = Math.Max(0, (totalProcessorTime - previous.TotalProcessorTime).TotalMilliseconds);
                cpuPercent = Math.Clamp(cpuMs / elapsedMs / Math.Max(1, Environment.ProcessorCount) * 100, 0, 100);
            }

            _samples[pid] = new ProcessSample(now, totalProcessorTime);

            DateTime startTime = TryGetStartTime(process);
            TimeSpan uptime = startTime == DateTime.MinValue
                ? TimeSpan.Zero
                : DateTime.Now - startTime;

            string windowTitle = Safe(() => process.MainWindowTitle, string.Empty);
            string processPath = TryGetProcessPath(process);
            long workingSet = Safe(() => process.WorkingSet64, 0L);
            long privateMemory = Safe(() => process.PrivateMemorySize64, 0L);
            bool responding = Safe(() => process.Responding, true);
            string priority = Safe(() => process.PriorityClass.ToString(), "Unknown");
            int threadCount = Safe(() => process.Threads.Count, 0);

            return new RobloxInstanceViewModel
            {
                Pid = pid,
                ProcessName = process.ProcessName,
                DisplayName = string.IsNullOrWhiteSpace(windowTitle) ? process.ProcessName : windowTitle,
                Status = responding ? "Running" : "Not responding",
                StatusBrush = responding
                    ? new SolidColorBrush(Color.FromRgb(36, 138, 76))
                    : new SolidColorBrush(Color.FromRgb(172, 112, 28)),
                CpuPercent = cpuPercent,
                CpuText = $"{cpuPercent:F1}%",
                WorkingSetBytes = workingSet,
                MemoryText = FormatBytes(workingSet),
                PrivateMemoryText = FormatBytes(privateMemory),
                UptimeText = startTime == DateTime.MinValue ? "Unknown" : FormatDuration(uptime),
                StartedText = startTime == DateTime.MinValue ? "Unknown" : startTime.ToString("HH:mm:ss"),
                StartedAtUtc = startTime == DateTime.MinValue ? DateTime.MinValue : startTime.ToUniversalTime(),
                WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? "No visible title" : windowTitle,
                ExecutablePath = string.IsNullOrWhiteSpace(processPath) ? "Unavailable" : processPath,
                PriorityText = priority,
                ThreadCount = threadCount,
                ProcessLabel = $"{process.ProcessName} / PID {pid}"
            };
        }

        private void UpdateSummary(IReadOnlyList<RobloxInstanceViewModel> instances)
        {
            InstanceCount = instances.Count;
            TotalMemoryText = FormatBytes(instances.Sum(instance => instance.WorkingSetBytes));
            TotalCpuText = $"{instances.Sum(instance => instance.CpuPercent):F1}%";
            AverageCpuText = instances.Count == 0
                ? "0.0%"
                : $"{instances.Average(instance => instance.CpuPercent):F1}%";

            var heaviest = instances.OrderByDescending(instance => instance.WorkingSetBytes).FirstOrDefault();
            HeaviestInstanceText = heaviest is null
                ? "None"
                : $"PID {heaviest.Pid} / {heaviest.MemoryText}";

            MonitoringStatusText = instances.Count == 0
                ? "No Roblox instances detected"
                : $"{instances.Count} Roblox instance{(instances.Count == 1 ? string.Empty : "s")} online";
        }

        private void TrimSelectedMemory()
        {
            if (SelectedInstance is null)
                return;

            try
            {
                using var process = Process.GetProcessById(SelectedInstance.Pid);
                long before = process.WorkingSet64;
                EmptyWorkingSet(process.Handle);
                SetProcessWorkingSetSize(process.Handle, (IntPtr)(-1), (IntPtr)(-1));
                process.Refresh();

                Frontend.ShowMessageBox(
                    $"Memory trim completed for PID {SelectedInstance.Pid}.\n\nBefore: {FormatBytes(before)}\nAfter: {FormatBytes(process.WorkingSet64)}",
                    MessageBoxImage.Information);

                RefreshInstances();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox($"Failed to trim memory:\n{ex.Message}", MessageBoxImage.Warning);
            }
        }

        private void EndSelectedProcess()
        {
            if (SelectedInstance is null)
                return;

            var result = Frontend.ShowMessageBox(
                $"End Roblox process PID {SelectedInstance.Pid}?\n\nThis will close that instance immediately.",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                using var process = Process.GetProcessById(SelectedInstance.Pid);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2500);
                RefreshInstances();
            }
            catch (Exception ex)
            {
                Frontend.ShowMessageBox($"Failed to end process:\n{ex.Message}", MessageBoxImage.Warning);
            }
        }

        private void OpenSelectedFolder()
        {
            if (SelectedInstance is null || !File.Exists(SelectedInstance.ExecutablePath))
            {
                Frontend.ShowMessageBox("Executable path is not available for this process.", MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{SelectedInstance.ExecutablePath}\"",
                UseShellExecute = true
            });
        }

        private void CopySelectedInfo()
        {
            if (SelectedInstance is null)
                return;

            Clipboard.SetText(
                $"NullCore Instance Dashboard\n" +
                $"PID: {SelectedInstance.Pid}\n" +
                $"Process: {SelectedInstance.ProcessName}\n" +
                $"Status: {SelectedInstance.Status}\n" +
                $"CPU: {SelectedInstance.CpuText}\n" +
                $"RAM: {SelectedInstance.MemoryText}\n" +
                $"Private RAM: {SelectedInstance.PrivateMemoryText}\n" +
                $"Uptime: {SelectedInstance.UptimeText}\n" +
                $"Started: {SelectedInstance.StartedText}\n" +
                $"Priority: {SelectedInstance.PriorityText}\n" +
                $"Threads: {SelectedInstance.ThreadCount}\n" +
                $"Path: {SelectedInstance.ExecutablePath}");
        }

        private static IEnumerable<Process> GetRobloxProcesses()
        {
            string configuredName = App.Settings.Prop.RenameClientToEuroTrucks2 ? "eurotrucks2" : App.RobloxPlayerAppName;
            string[] processNames =
            {
                App.RobloxPlayerAppName,
                configuredName,
                "RobloxPlayerBeta",
                "eurotrucks2"
            };

            return processNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(name =>
                {
                    try
                    {
                        return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name));
                    }
                    catch
                    {
                        return Array.Empty<Process>();
                    }
                })
                .GroupBy(process => process.Id)
                .Select(group => group.First());
        }

        private static DateTime TryGetStartTime(Process process) =>
            Safe(() => process.StartTime, DateTime.MinValue);

        private static string TryGetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeProcessLabel(Process process)
        {
            try
            {
                return $"{process.ProcessName}:{process.Id}";
            }
            catch
            {
                return "unknown";
            }
        }

        private static T Safe<T>(Func<T> getter, T fallback)
        {
            try
            {
                return getter();
            }
            catch
            {
                return fallback;
            }
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{(int)duration.TotalDays}d {duration.Hours}h";

            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";

            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";

            return $"{Math.Max(0, duration.Seconds)}s";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";

            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";

            if (bytes < 1024L * 1024L * 1024L)
                return $"{bytes / 1024.0 / 1024.0:F1} MB";

            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        }

        public void Dispose()
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= RefreshTimer_Tick;
        }

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr minimumWorkingSetSize, IntPtr maximumWorkingSetSize);

        private sealed record ProcessSample(DateTime TimestampUtc, TimeSpan TotalProcessorTime);
    }

    public sealed class RobloxInstanceViewModel
    {
        public int Pid { get; init; }
        public string ProcessName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string ProcessLabel { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public Brush StatusBrush { get; init; } = Brushes.Transparent;
        public double CpuPercent { get; init; }
        public string CpuText { get; init; } = "0.0%";
        public long WorkingSetBytes { get; init; }
        public string MemoryText { get; init; } = "0 MB";
        public string PrivateMemoryText { get; init; } = "0 MB";
        public string UptimeText { get; init; } = "Unknown";
        public string StartedText { get; init; } = "Unknown";
        public DateTime StartedAtUtc { get; init; }
        public string WindowTitle { get; init; } = string.Empty;
        public string ExecutablePath { get; init; } = string.Empty;
        public string PriorityText { get; init; } = "Unknown";
        public int ThreadCount { get; init; }
    }
}
