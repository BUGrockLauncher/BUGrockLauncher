using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using BUGrockLauncher.Services;

namespace BUGrockLauncher.Views.Dialogs
{
    public partial class UpgradeInstanceDialog : Window
    {
        public bool CreateBackup { get; private set; } = true;

        public UpgradeInstanceDialog(string instanceName, string currentVersion, string targetVersion)
        {
            InitializeComponent();
            BUGrockLauncher.Helpers.ModalDialogHelper.AttachCenterLock(this);
            CurrentVerText.Text = currentVersion;
            InstanceNameSubText.Text = instanceName;
            TargetVerText.Text = targetVersion;

            string safeName = InstanceManager.SanitizeInstanceName(instanceName);
            string dateStamp = DateTime.Now.ToString("yy-MM-dd-HH-mm");
            string backupName = $"{safeName}_v{currentVersion}-{dateStamp}.zip";
            string fullBackupPath = Path.Combine(InstanceManager.Instance.BackupsDirectory, backupName);
            BackupPathPreviewText.Text = fullBackupPath;
            Loaded += (s, e) => (Owner as MainWindow)?.ShowModalDim();
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

        private void BtnUpgrade_Click(object sender, RoutedEventArgs e)
        {
            CreateBackup = ChkCreateBackup.IsChecked == true;
            DialogResult = true;
            Close();
        }
    }
}

