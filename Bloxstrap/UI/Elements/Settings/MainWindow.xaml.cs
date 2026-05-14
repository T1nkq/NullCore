using Microsoft.VisualBasic.ApplicationServices;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Voidstrap.Integrations;
using Voidstrap.UI.Elements.Base;
using Voidstrap.UI.Elements.Controls;
using Voidstrap.UI.Elements.Dialogs;
using Voidstrap.UI.Elements.Settings.Pages;
using Voidstrap.UI.ViewModels.Settings;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Mvvm.Contracts;

namespace Voidstrap.UI.Elements.Settings
{
    public partial class MainWindow : INavigationWindow
    {
        private Models.Persistable.WindowState _state => App.State.Prop.SettingsWindow;
        private bool _isSaveAndLaunchClicked = false;
        private readonly Random _snowRandom = new();
        private readonly List<Snowflake> _snowflakes = new();
        private readonly DispatcherTimer _snowTimer;
        private readonly DispatcherTimer _visibilityTimer = new DispatcherTimer();
        private bool _spotifyInitialized = false;
        private Vector _currentOffset;
        private Vector _targetOffset;
        private double _currentRotation;
        private double _targetRotation;
        private DispatcherTimer _searchDebounceTimer;
        private List<TextBlock> _allTextBlocksCache = new List<TextBlock>();
        private Page _lastPage = null;
        private const double MaxOffset = 0.04;
        private const double MaxRotation = 5.0;
        private const double FollowSpeed = 0.035;
        private readonly Dictionary<Wpf.Ui.Controls.NavigationItem, Wpf.Ui.Common.SymbolRegular> _defaultIcons = new();
        private readonly List<Type> _pagesToHideSearchBox = new List<Type> // idfk my lazy bum ass didnt wanna spent 4000hours tranna figure another way for all tis bullshit of work took me 1 day for this shit FAHHHHHHHHHH WSEIEWMIEWOMHGEW
        {
        typeof(FastFlagEditorPage),
        typeof(NewsPage),
        typeof(NvidiaFFlagEditorPage),
        typeof(ReleasesPage),
        typeof(DonoPage),
        };

        public MainWindow(bool showAlreadyRunningWarning)
        {
            InitializeComponent();
            InitializeViewModel();
            InitializeWindowState();
            UpdateButtonContent();
            RegisterHoverIcons();
            GlobalSearchBox.TextChanged += GlobalSearchBox_TextChanged;
            GlobalSearchBox.LostFocus += GlobalSearchBox_LostFocus;
            // shi finna be laggy :sob:
            _visibilityTimer.Interval = TimeSpan.FromSeconds(0.8);
            _visibilityTimer.Tick += (s, e) => UpdateFastFlagEditorVisibility();
            _visibilityTimer.Start();
            _snowTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _snowTimer.Tick += SnowTimer_Tick;
            Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;

            RootFrame.Navigated += RootFrame_Navigated;

            App.Logger.WriteLine("MainWindow", "Initializing settings window");

            if (showAlreadyRunningWarning)
                _ = ShowAlreadyRunningSnackbarAsync();
        }


        private void RegisterHoverIcons()
        {
            IEnumerable<Wpf.Ui.Controls.NavigationItem> allItems = RootNavigation.Items
                .OfType<Wpf.Ui.Controls.NavigationItem>()
                .Concat(RootNavigation.Footer.OfType<Wpf.Ui.Controls.NavigationItem>())
                .Where(i => i.Tag != null);

            foreach (Wpf.Ui.Controls.NavigationItem item in allItems)
            {
                Wpf.Ui.Common.SymbolRegular defaultIcon = item.Icon;
                _defaultIcons[item] = defaultIcon;

                item.MouseEnter += (s, e) =>
                {
                    if (Enum.TryParse<Wpf.Ui.Common.SymbolRegular>(item.Tag?.ToString(), out Wpf.Ui.Common.SymbolRegular hoverIcon))
                        item.Icon = hoverIcon;
                };

                item.MouseLeave += (s, e) =>
                {
                    item.Icon = defaultIcon;
                };
            }
        }

        private void RootFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            _allTextBlocksCache.Clear();
            _lastPage = null;

            object currentPage = e.Content;
            if (currentPage != null && _pagesToHideSearchBox.Contains(currentPage.GetType()))
            {
                GlobalSearchBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                GlobalSearchBox.Visibility = Visibility.Visible;
            }

            Wpf.Ui.Controls.NavigationItem selectedItem = RootNavigation.Items
                .OfType<Wpf.Ui.Controls.NavigationItem>()
                .Concat(RootNavigation.Footer.OfType<Wpf.Ui.Controls.NavigationItem>())
                .FirstOrDefault(i => i.IsActive);

            if (selectedItem != null && _defaultIcons.TryGetValue(selectedItem, out Wpf.Ui.Common.SymbolRegular icon))
                BreadcrumbIcon.Symbol = icon;
        }

        //fuck man I dont even understand whats going on in this code dont go asking me ?? nvm it was just 3am I do understand..
        private void GlobalSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_searchDebounceTimer == null)
            {
                _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _searchDebounceTimer.Tick += (s, args) =>
                {
                    _searchDebounceTimer.Stop();
                    PerformSearch(GlobalSearchBox.Text.Trim().ToLower());
                };
            }

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void GlobalSearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            GlobalSearchBox.Text = "";
            PerformSearch("");
        }

        private void PerformSearch(string query)
        {
            if (!(RootFrame.Content is Page page)) return;

            if (page != _lastPage)
            {
                _allTextBlocksCache.Clear();
                _lastPage = page;
            }

            if (!_allTextBlocksCache.Any())
                CacheAllTextBlocks(page);

            if (string.IsNullOrEmpty(query))
            {
                foreach (var tb in _allTextBlocksCache)
                    tb.Background = Brushes.Transparent;
                return;
            }

            var matches = new List<TextBlock>();

            foreach (var tb in _allTextBlocksCache)
            {
                if (IsFuzzyMatch(tb.Text, query))
                {
                    tb.Background = (SolidColorBrush)SystemParameters.WindowGlassBrush; // fuckass windows accent color
                    FlashHighlight(tb);
                    matches.Add(tb);
                }
                else
                {
                    tb.Background = Brushes.Transparent;
                }
            }

            ScrollToClosestMatch(matches);
        }

        private void CacheAllTextBlocks(Page page)
        {
            _allTextBlocksCache.Clear();

            void Recurse(DependencyObject parent)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);

                    if (child is TextBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text))
                        _allTextBlocksCache.Add(textBlock);

                    Recurse(child);
                }
            }

            Recurse(page);
        }

        private void ScrollToClosestMatch(List<TextBlock> matches)
        {
            if (!matches.Any()) return;

            foreach (var textBlock in matches)
            {
                ScrollViewer scrollViewer = null;
                DependencyObject parent = textBlock;
                while (parent != null)
                {
                    if (parent is ScrollViewer sv)
                    {
                        scrollViewer = sv;
                        break;
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }

                if (scrollViewer != null)
                {
                    GeneralTransform transform = textBlock.TransformToAncestor(scrollViewer);
                    Point position = transform.Transform(new Point(0, 0));

                    double viewportHeight = scrollViewer.ViewportHeight;
                    double elementTop = position.Y;
                    double elementBottom = elementTop + textBlock.ActualHeight;

                    if (elementBottom < 0 || elementTop > viewportHeight)
                    {
                        SmoothScrollTo(scrollViewer, scrollViewer.VerticalOffset + position.Y);
                    }

                    break;
                }
            }
        }

        private void SmoothScrollTo(ScrollViewer scrollViewer, double targetOffset)
        {
            double startOffset = scrollViewer.VerticalOffset;
            double distance = targetOffset - startOffset;
            int steps = 15;
            int currentStep = 0;

            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (s, e) =>
            {
                currentStep++;
                double t = (double)currentStep / steps;
                t = t * t * (3 - 2 * t);
                scrollViewer.ScrollToVerticalOffset(startOffset + distance * t);

                if (currentStep >= steps)
                    timer.Stop();
            };
            timer.Start();
        }

        private void FlashHighlight(TextBlock tb)
        {
            var originalBrush = tb.Background;
            DispatcherTimer flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            flashTimer.Tick += (s, e) =>
            {
                flashTimer.Stop();
                tb.Background = originalBrush;
            };
            flashTimer.Start();
        }

        private static bool IsFuzzyMatch(string text, string query)
        {
            text = text.ToLower();
            query = query.ToLower();

            if (text.Contains(query)) return true;

            int distance = LevenshteinDistance(text, query);
            int threshold = Math.Max(1, query.Length / 3);
            return distance <= threshold;
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost
                    );
                }
            }
            return d[n, m];
        }

        private void AnimateOpacity(UIElement element, double toOpacity, double durationSeconds = 0.5)
        {
            if (element == null) return;

            var animation = new DoubleAnimation
            {
                To = toOpacity,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        private void RootGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement fe)
                return;

            var pos = e.GetPosition(fe);
            var nx = (pos.X / fe.ActualWidth - 0.5) * 2;
            var ny = (pos.Y / fe.ActualHeight - 0.5) * 2;

            _targetOffset = new Vector(
                nx * MaxOffset,
                ny * MaxOffset
            );

            _targetRotation = nx * MaxRotation;
        }

        private void RootGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            _targetOffset = new Vector(0, 0); // blah this just resets the values all back to normal value 0 :)
            _targetRotation = 0;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            _currentOffset += (_targetOffset - _currentOffset) * FollowSpeed;
            _currentRotation += (_targetRotation - _currentRotation) * FollowSpeed;

            BackgroundGradientTranslate.X = _currentOffset.X;
            BackgroundGradientTranslate.Y = _currentOffset.Y;
            BackgroundGradientRotate.Angle = _currentRotation;
        }

        private void UpdateFastFlagEditorVisibility()
        {
            if (FastFlagEditorNavItem == null)
                return;

            var shouldBeVisible = !App.Settings.Prop.LockDefault;
            if (FastFlagEditorNavItem.Visibility == (shouldBeVisible ? Visibility.Visible : Visibility.Collapsed))
                return;

            FastFlagEditorNavItem.Visibility = shouldBeVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            InitializeNavigation();
            if (SnowCanvas != null)
                SnowCanvas.Visibility = Visibility.Collapsed;

            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            var storyboard = TryFindResource("IntroStoryboard") as Storyboard;
            if (storyboard != null)
            {
                storyboard.Completed += (_, _) =>
                {
                    IntroOverlay.Visibility = Visibility.Collapsed;
                    IntroOverlay.Opacity = 1.0;
                };

                IntroOverlay.Visibility = Visibility.Visible;
                storyboard.Begin(IntroOverlay, true);
            }
            else
            {
                IntroOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private Size _lastSnowCanvasSize = Size.Empty;
        private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (SnowCanvas == null)
                return;

            var newSize = new Size(SnowCanvas.ActualWidth, SnowCanvas.ActualHeight);
            if (newSize.Width <= 0 || newSize.Height <= 0)
                return;
            const double minDelta = 20.0;
            if (Math.Abs(newSize.Width - _lastSnowCanvasSize.Width) < minDelta &&
                Math.Abs(newSize.Height - _lastSnowCanvasSize.Height) < minDelta)
                return;

            _lastSnowCanvasSize = newSize;
            InitSnow();
        }

        private const int FlakeCount = 40;
        private void InitSnow()
        {
            if (SnowCanvas == null) return;

            double width = SnowCanvas.ActualWidth;
            double height = SnowCanvas.ActualHeight;

            if (width <= 0 || height <= 0)
                return;

            if (_snowflakes.Count == FlakeCount)
                return;

            _snowflakes.Clear();
            SnowCanvas.Children.Clear();

            for (int i = 0; i < FlakeCount; i++)
            {
                double size = _snowRandom.Next(2, 6);
                var ellipse = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = Brushes.White,
                    Opacity = _snowRandom.NextDouble() * 0.6 + 0.3
                };
                SnowCanvas.Children.Add(ellipse);

                _snowflakes.Add(new Snowflake
                {
                    Shape = ellipse,
                    X = _snowRandom.NextDouble() * width,
                    Y = _snowRandom.NextDouble() * height,
                    SpeedY = 0.7 + _snowRandom.NextDouble() * 1.5,
                    DriftX = -0.3 + _snowRandom.NextDouble() * 0.6,
                    Size = size
                });
            }
        }

        private void UpdateSnow()
        {
            if (SnowCanvas == null) return;

            double width = SnowCanvas.ActualWidth;
            double height = SnowCanvas.ActualHeight;

            for (int i = 0; i < _snowflakes.Count; i++)
            {
                var flake = _snowflakes[i];
                flake.Y += flake.SpeedY;
                flake.X += flake.DriftX;

                if (flake.Y > height + flake.Size) flake.Y = -flake.Size;
                if (flake.X < -flake.Size) flake.X = width + flake.Size;
                else if (flake.X > width + flake.Size) flake.X = -flake.Size;

                Canvas.SetLeft(flake.Shape, flake.X);
                Canvas.SetTop(flake.Shape, flake.Y);
            }
        }

        private void SnowTimer_Tick(object? sender, EventArgs e)
        {
            UpdateSnow();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            _snowTimer.Stop();
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            _snowTimer.Stop();
        }

        private sealed class Snowflake
        {
            public Ellipse Shape { get; set; } = null!;
            public double X { get; set; }
            public double Y { get; set; }
            public double SpeedY { get; set; }
            public double DriftX { get; set; }
            public double Size { get; set; }
        }

        #region Initialization

        private void InitializeViewModel()
        {
            var viewModel = new MainWindowViewModel();
            DataContext = viewModel;

            viewModel.RequestSaveNoticeEvent += OnRequestSaveNotice;
            viewModel.RequestSaveLaunchNoticeEvent += OnRequestSaveLaunchNotice;
            viewModel.RequestCloseWindowEvent += OnRequestCloseWindow;
        }

        private void UpdateButtonContent()
        {
            if (InstallLaunchButton == null)
                return;

            string versionsPath = Paths.Versions;

            InstallLaunchButton.Content =
                (Directory.Exists(versionsPath) && Directory.EnumerateFileSystemEntries(versionsPath).Any())
                    ? "Save and Launch"
                    : "Install";
        }

        private void InitializeWindowState()
        {
            if (_state.LeftUpdateV2 > SystemParameters.VirtualScreenWidth || _state.TopUpdateV2 > SystemParameters.VirtualScreenHeight)
            {
                _state.LeftUpdateV2 = 0;
                _state.TopUpdateV2 = 0;
            }

            if (_state.WidthUpdateV2 > 0) Width = _state.WidthUpdateV2;
            if (_state.HeightUpdateV2 > 0) Height = _state.HeightUpdateV2;

            if (_state.LeftUpdateV2 > 0 && _state.TopUpdateV2 > 0)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = _state.LeftUpdateV2;
                Top = _state.TopUpdateV2;
            }
        }

        private void InitializeNavigation()
        {
            if (RootNavigation == null)
                return;

            RootNavigation.SelectedPageIndex = App.State.Prop.LastPage;
            RootNavigation.Navigated += SaveNavigation;
        }

        #endregion
        #region Snackbar Events

        private void OnRequestSaveNotice(object? sender, EventArgs e)
        {
            if (!_isSaveAndLaunchClicked)
                SettingsSavedSnackbar.Show();
        }

        private void OnRequestSaveLaunchNotice(object? sender, EventArgs e)
        {
            if (!_isSaveAndLaunchClicked)
                SettingsSavedLaunchSnackbar.Show();
        }

        private async Task ShowAlreadyRunningSnackbarAsync()
        {
            await Task.Delay(225);
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.InvokeAsync(() => AlreadyRunningSnackbar?.Show());
        }

        #endregion
        #region ViewModel Events

        private async void OnRequestCloseWindow(object? sender, EventArgs e)
        {
            await Task.Yield();
            Close();
        }

        private void OnSaveAndLaunchButtonClick(object sender, EventArgs e)
        {
            _isSaveAndLaunchClicked = true;
        }

        #endregion

        #region Window Events

        private void WpfUiWindow_Closing(object sender, CancelEventArgs e)
        {
            SaveWindowState();
        }

        private void WpfUiWindow_Closed(object sender, EventArgs e)
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            if (App.LaunchSettings.TestModeFlag.Active)
                LaunchHandler.LaunchRoblox(LaunchMode.Player);
            else
                App.SoftTerminate();
        }

        private void SaveWindowState()
        {
            _state.WidthUpdateV2 = Width;
            _state.HeightUpdateV2 = Height;
            _state.TopUpdateV2 = Top;
            _state.LeftUpdateV2 = Left;

            App.State.Save();
        }

        #endregion

        #region Navigation

        private void SaveNavigation(INavigation sender, RoutedNavigationEventArgs e)
        {
            App.State.Prop.LastPage = RootNavigation.SelectedPageIndex;
        }

        #endregion

        #region INavigationWindow Implementation

        public Frame GetFrame() => RootFrame;
        public INavigation GetNavigation() => RootNavigation;
        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);
        public void SetPageService(IPageService pageService) => RootNavigation.PageService = pageService;
        public void ShowWindow() => Show();
        public void CloseWindow() => Close();

        #endregion

        #region Placeholder Events

        private void NavigationItem_Click(object sender, RoutedEventArgs e) { }
        private void NavigationItem_Click_1(object sender, RoutedEventArgs e) { }


        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
        }


        private void Button_Click_2(object sender, RoutedEventArgs e) { }

        #endregion
    }
}
