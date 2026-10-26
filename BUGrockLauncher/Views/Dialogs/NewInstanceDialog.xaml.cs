using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BUGrockLauncher.Services;

namespace BUGrockLauncher.Views.Dialogs
{
    public partial class NewInstanceDialog : Window
    {
        public string InstanceName { get; private set; } = "";
        public string SelectedDirectory { get; private set; } = "";

        private readonly string _versionName;
        private readonly string _baseInstancesDir;

        public NewInstanceDialog(string versionDisplayName, string versionName, string suggestedName)
        {
            InitializeComponent();
            _versionName = versionName;
            _baseInstancesDir = InstanceManager.Instance.InstancesDirectory;

            VersionHeadingText.Text = versionDisplayName;
            TxtInstanceName.Text = string.IsNullOrWhiteSpace(suggestedName) ? versionName : suggestedName;
            UpdatePaths();
        }

        private void UpdatePaths()
        {
            string cleanName = InstanceManager.SanitizeInstanceName(TxtInstanceName.Text);
            string defaultPath = Path.Combine(_baseInstancesDir, cleanName);
            DefaultPathPreviewText.Text = defaultPath;
            if (string.IsNullOrWhiteSpace(TxtCustomPath.Text))
            {
                TxtCustomPath.Text = defaultPath;
            }
        }

        private void TxtInstanceName_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePaths();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void DefaultCard_Click(object sender, MouseButtonEventArgs e)
        {
            RbDefault.IsChecked = true;
        }

        private void CustomCard_Click(object sender, MouseButtonEventArgs e)
        {
            RbCustom.IsChecked = true;
        }

        private void RbDefault_Checked(object sender, RoutedEventArgs e)
        {
            if (TxtCustomPath != null) TxtCustomPath.IsEnabled = false;
            if (BtnBrowse != null) BtnBrowse.IsEnabled = false;
        }

        private void RbCustom_Checked(object sender, RoutedEventArgs e)
        {
            if (TxtCustomPath != null) TxtCustomPath.IsEnabled = true;
            if (BtnBrowse != null) BtnBrowse.IsEnabled = true;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            RbCustom.IsChecked = true;
            try
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select Destination Folder for " + TxtInstanceName.Text,
                    Multiselect = false
                };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                {
                    TxtCustomPath.Text = dlg.FolderName;
                }
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show("Failed to open folder picker: " + ex.Message, "Error");
            }
        }

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtInstanceName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ThemedMessageBox.Show("Please enter a valid instance name.", "Instance Name Required");
                return;
            }

            InstanceName = name;

            if (RbDefault.IsChecked == true)
            {
                string cleanName = InstanceManager.SanitizeInstanceName(name);
                SelectedDirectory = Path.Combine(_baseInstancesDir, cleanName);
            }
            else
            {
                string custom = TxtCustomPath.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(custom))
                {
                    ThemedMessageBox.Show("Please choose a valid folder path.", "Invalid Path");
                    return;
                }
                SelectedDirectory = Path.GetFullPath(custom);
            }

            DialogResult = true;
            Close();
        }
    }
}
