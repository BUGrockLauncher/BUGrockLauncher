using System.Windows;

namespace BUGrockLauncher {
    /// <summary>
    /// Interaction logic for ProgressDialog.xaml
    /// </summary>
    public partial class ProgressDialog : Window {
        public ProgressDialog() {
            InitializeComponent();
            Loaded += (s, e) => (Owner as MainWindow)?.ShowModalDim();
            Closed += (s, e) => (Owner as MainWindow)?.HideModalDim();
        }
    }
}
