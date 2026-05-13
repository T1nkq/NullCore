using System.Windows;
using System.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Voidstrap.UI.Elements.Dialogs
{
    public partial class UpdatePromptDialog
    {
        public bool ShouldDownload { get; private set; }

        public UpdatePromptDialog(GithubUpdateRelease release, string currentVersion)
        {
            InitializeComponent();

            Title = $"{App.ProjectName} Update";
            RootTitleBar.Title = Title;

            string releaseTitle = string.IsNullOrWhiteSpace(release.Name)
                ? release.TagName
                : release.Name;

            UpdateTitleText.Text = $"Update available: {releaseTitle}";
            VersionText.Text = $"Current: v{currentVersion.TrimStart('v', 'V')}  |  Latest: {release.TagName}";

            string notes = NormalizeReleaseNotes(release.Body);
            ReleaseNotesText.Text = notes;
            ReleaseNotesText.MarkdownText = notes;

            Loaded += delegate
            {
                var hWnd = new WindowInteropHelper(this).Handle;
                PInvoke.FlashWindow((HWND)hWnd, true);
            };
        }

        private static string NormalizeReleaseNotes(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "No release notes were provided for this update.";

            return body.Trim();
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldDownload = true;
            DialogResult = true;
            Close();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            ShouldDownload = false;
            DialogResult = false;
            Close();
        }
    }
}
