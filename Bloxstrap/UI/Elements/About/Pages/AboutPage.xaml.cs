using Voidstrap.UI.ViewModels.About;

namespace Voidstrap.UI.Elements.About.Pages
{
    public partial class AboutPage
    {
        public AboutPage()
        {
            DataContext = new AboutViewModel();
            InitializeComponent();
        }
    }
}
