using Voidstrap.UI.ViewModels.Settings;

namespace Voidstrap.UI.Elements.Settings.Pages
{
    public partial class InstanceDashboardPage
    {
        private readonly InstanceDashboardViewModel _viewModel = new();

        public InstanceDashboardPage()
        {
            InitializeComponent();
            DataContext = _viewModel;
            Loaded += (_, _) => _viewModel.Start();
            Unloaded += (_, _) => _viewModel.Stop();
        }
    }
}
