using System.Windows;
using System.Windows.Input;

namespace BUGrockLauncher.Views.Dialogs
{
    public partial class DuplicateInstanceDialog : Window
    {
        public string NewInstanceName { get; private set; } = "";

        public DuplicateInstanceDialog(string currentName, string versionNumber)
        {
            InitializeComponent();
            BUGrockLauncher.Helpers.ModalDialogHelper.AttachCenterLock(this);
            SourceInfoText.Text = $"Clone \"{currentName}\" (v{versionNumber})";
            TxtNewName.Text = $"{currentName} - Copy";
            TxtNewName.SelectAll();
            Loaded += (s, e) => {
                TxtNewName.Focus();
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

        private void BtnClone_Click(object sender, RoutedEventArgs e)
        {
            string trimmed = TxtNewName.Text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                ThemedMessageBox.Show("Please enter a valid instance name.", "Name Required");
                return;
            }

            NewInstanceName = trimmed;
            DialogResult = true;
            Close();
        }
    }
}

