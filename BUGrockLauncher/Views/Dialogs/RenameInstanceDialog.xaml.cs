using System.Windows;
using System.Windows.Input;

namespace BUGrockLauncher.Views.Dialogs
{
    public partial class RenameInstanceDialog : Window
    {
        public string NewName { get; private set; } = "";

        public RenameInstanceDialog(string currentName)
        {
            InitializeComponent();
            BUGrockLauncher.Helpers.ModalDialogHelper.AttachCenterLock(this);
            TxtName.Text = currentName;
            TxtName.SelectAll();
            Loaded += (s, e) => {
                TxtName.Focus();
                (Owner as MainWindow)?.ShowModalDim();
            };
            Closed += (s, e) => (Owner as MainWindow)?.HideModalDim();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string trimmed = TxtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                ThemedMessageBox.Show("Please enter a valid instance name.", "Name Required");
                return;
            }

            NewName = trimmed;
            DialogResult = true;
            Close();
        }
    }
}

