using System.Windows;
using System.Windows.Input;

namespace BUGrockLauncher
{
    public partial class VersionListEndpointDialog : Window
    {
        public event SetEndpointHandler? OnEndpointChanged;

        public delegate void SetEndpointHandler(object sender, string newUwpIdsEndpoint, string newGdkPackageUrlsEndpoint);

        public VersionListEndpointDialog(string currentEndpoint, string currentGdkEndpoint = "") {
            InitializeComponent();
            BUGrockLauncher.Helpers.ModalDialogHelper.AttachCenterLock(this);
            EndpointTextBox.Text = currentEndpoint;
            GDKUrlsEndpointTextBox.Text = currentGdkEndpoint;
            Loaded += (s, e) => (Owner as MainWindow)?.ShowModalDim();
            Closed += (s, e) => (Owner as MainWindow)?.HideModalDim();
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e) {
            // Drag disabled
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) {
            OnEndpointChanged?.Invoke(this, EndpointTextBox.Text, GDKUrlsEndpointTextBox.Text);
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            Close();
        }
    }
}

