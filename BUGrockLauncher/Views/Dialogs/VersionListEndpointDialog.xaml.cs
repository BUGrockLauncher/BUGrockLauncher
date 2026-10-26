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
            EndpointTextBox.Text = currentEndpoint;
            GDKUrlsEndpointTextBox.Text = currentGdkEndpoint;
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Left) DragMove();
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
