using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BUGrockCheck
{
    public class MainForm : Form
    {
        // Colors
        private static readonly Color BgMain = Color.FromArgb(19, 20, 26);
        private static readonly Color BgCard = Color.FromArgb(24, 25, 33);
        private static readonly Color BorderCard = Color.FromArgb(35, 37, 48);
        private static readonly Color AccentGreen = Color.FromArgb(56, 161, 105);
        private static readonly Color AccentMagenta = Color.FromArgb(208, 92, 227);
        private static readonly Color DangerRed = Color.FromArgb(229, 62, 62);
        private static readonly Color WarningOrange = Color.FromArgb(221, 107, 32);
        private static readonly Color TextMuted = Color.FromArgb(142, 146, 164);

        // Cards
        private Panel cardNet8;
        private Panel cardVC;
        private Panel cardWV;
        private Panel cardDev;
        private Panel cardAppX;
        private Panel cardClip;
        private Panel cardGaming;
        private Panel cardWUA;

        // Status Labels
        private Label lblNet8Status;
        private Label lblVCStatus;
        private Label lblWVStatus;
        private Label lblDevStatus;
        private Label lblAppXStatus;
        private Label lblClipStatus;
        private Label lblGamingStatus;
        private Label lblWUAStatus;

        // Fix Buttons
        private Button btnNet8Fix;
        private Button btnVCFix;
        private Button btnWVFix;
        private Button btnDevFix;
        private Button btnAppXFix;
        private Button btnClipFix;
        private Button btnGamingFix;
        private Button btnWUAFix;

        // Footer Controls
        private Button btnLaunch;
        private Button btnRecheck;
        private Label lblSummary;
        private ProgressBar prgDownload;

        private bool isNet8Ok = false;
        private bool isVcOk = false;

        [DllImport("DwmApi")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, int[] attrValue, int attrSize);

        public MainForm()
        {
            InitializeComponent();
            ApplyDarkTitleBar();
            RunAllChecks();
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 10)
                {
                    int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
                    if (Environment.OSVersion.Version.Build < 19041) DWMWA_USE_IMMERSIVE_DARK_MODE = 19;
                    DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, new int[] { 1 }, 4);
                }
            }
            catch { }
        }

        private void InitializeComponent()
        {
            this.Text = "BUGrock Launcher - Requirements Checker";
            this.Size = new Size(700, 780);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = BgMain;
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Header Panel
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 85,
                BackColor = Color.FromArgb(15, 16, 21),
                Padding = new Padding(24, 16, 24, 0)
            };

            Label title = new Label
            {
                Text = "BUGrock Launcher",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(20, 14)
            };

            Label subBadge = new Label
            {
                Text = "PRE-FLIGHT CHECK",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = AccentMagenta,
                BackColor = Color.FromArgb(43, 21, 48),
                Padding = new Padding(6, 2, 6, 2),
                AutoSize = true,
                Location = new Point(title.Right + 12, 19)
            };

            Label subtitle = new Label
            {
                Text = "Checks whether your Windows PC has all required runtimes & services to play Minecraft Bedrock.",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = TextMuted,
                AutoSize = true,
                Location = new Point(22, 48)
            };

            header.Controls.Add(title);
            header.Controls.Add(subBadge);
            header.Controls.Add(subtitle);
            this.Controls.Add(header);

            // Container for cards
            Panel body = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(20, 10, 20, 10)
            };

            int y = 95;
            cardNet8 = CreateCheckCard("1. Microsoft .NET 8.0 Desktop Runtime (x64)",
                "Powers the launcher application, background tasks, and mod installer.",
                ref lblNet8Status, ref btnNet8Fix, "Download & Install", OnDownloadNet8, ref y, body);

            cardVC = CreateCheckCard("2. Visual C++ 2015–2022 Redistributable (x64)",
                "Required by native C++ shims, GDK decryption helpers, and Bedrock engine.",
                ref lblVCStatus, ref btnVCFix, "Download & Install", OnDownloadVC, ref y, body);

            cardWV = CreateCheckCard("3. Microsoft Edge WebView2 Runtime",
                "Used for the built-in Addon Browser (MCPEDL & CurseForge) and Server Hub.",
                ref lblWVStatus, ref btnWVFix, "Download & Install", OnDownloadWV, ref y, body);

            cardDev = CreateCheckCard("4. Windows Developer Mode",
                "Allows Windows to register sideloaded and extracted UWP Minecraft versions.",
                ref lblDevStatus, ref btnDevFix, "Open Settings", OnOpenDevSettings, ref y, body);

            cardAppX = CreateCheckCard("5. AppX Deployment Service (AppXSvc)",
                "Required to install and register packages.",
                ref lblAppXStatus, ref btnAppXFix, "Enable Service", delegate { EnableAndStartService("AppXSvc", "AppX Deployment Service"); }, ref y, body);

            cardClip = CreateCheckCard("6. Client License Service (ClipSvc)",
                "Required to decrypt modern GDK versions using your Store license.",
                ref lblClipStatus, ref btnClipFix, "Enable Service", delegate { EnableAndStartService("ClipSvc", "Client License Service"); }, ref y, body);

            cardGaming = CreateCheckCard("7. Gaming Services (GamingServices)",
                "Required for modern Bedrock versions & protocol activation.",
                ref lblGamingStatus, ref btnGamingFix, "Install / Fix", OnFixGamingServices, ref y, body);

            cardWUA = CreateCheckCard("8. Windows Update Service (wuauserv)",
                "Required to fetch official download packages from Microsoft servers.",
                ref lblWUAStatus, ref btnWUAFix, "Enable Service", delegate { EnableAndStartService("wuauserv", "Windows Update"); }, ref y, body);

            this.Controls.Add(body);

            // Bottom Bar
            Panel footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 85,
                BackColor = Color.FromArgb(15, 16, 21),
                Padding = new Padding(20, 15, 20, 15)
            };

            prgDownload = new ProgressBar
            {
                Location = new Point(20, 10),
                Size = new Size(644, 6),
                Visible = false,
                Style = ProgressBarStyle.Continuous
            };
            footer.Controls.Add(prgDownload);

            lblSummary = new Label
            {
                Location = new Point(20, 25),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.White,
                Text = "Checking requirements..."
            };
            footer.Controls.Add(lblSummary);

            btnRecheck = new Button
            {
                Text = "Re-Check",
                Size = new Size(110, 36),
                Location = new Point(410, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 37, 48),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            btnRecheck.FlatAppearance.BorderColor = Color.FromArgb(50, 52, 65);
            btnRecheck.Click += delegate { RunAllChecks(); };
            footer.Controls.Add(btnRecheck);

            btnLaunch = new Button
            {
                Text = "Launch BUGrock",
                Size = new Size(130, 36),
                Location = new Point(534, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = AccentGreen,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            btnLaunch.FlatAppearance.BorderSize = 0;
            btnLaunch.Click += OnLaunchClicked;
            footer.Controls.Add(btnLaunch);

            this.Controls.Add(footer);
        }

        private Panel CreateCheckCard(string titleText, string descText, ref Label lblStatus, ref Button btnFix, string fixText, EventHandler fixHandler, ref int y, Panel parent)
        {
            Panel card = new Panel
            {
                Location = new Point(20, y),
                Size = new Size(644, 76),
                BackColor = BgCard,
                BorderStyle = BorderStyle.None
            };
            card.Paint += delegate(object sender, PaintEventArgs e) {
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, BorderCard, ButtonBorderStyle.Solid);
            };

            Label title = new Label
            {
                Text = titleText,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(14, 12),
                AutoSize = true
            };
            card.Controls.Add(title);

            lblStatus = new Label
            {
                Text = "Checking...",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = TextMuted,
                Location = new Point(420, 13),
                Size = new Size(210, 20),
                TextAlign = ContentAlignment.TopRight
            };
            card.Controls.Add(lblStatus);

            Label desc = new Label
            {
                Text = descText,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = TextMuted,
                Location = new Point(15, 38),
                Size = new Size(460, 30)
            };
            card.Controls.Add(desc);

            if (fixText != null && fixHandler != null)
            {
                btnFix = new Button
                {
                    Text = fixText,
                    Location = new Point(500, 36),
                    Size = new Size(130, 28),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(43, 108, 176),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    Visible = false
                };
                btnFix.FlatAppearance.BorderSize = 0;
                btnFix.Click += fixHandler;
                card.Controls.Add(btnFix);
            }

            parent.Controls.Add(card);
            y += 88;
            return card;
        }

        private struct ServiceCheckResult
        {
            public bool IsInstalled;
            public bool IsRunning;
            public bool IsDisabled;
            public string StatusText;
            public Color StatusColor;
        }

        private ServiceCheckResult CheckWindowsService(string serviceName)
        {
            ServiceCheckResult res = new ServiceCheckResult();
            try
            {
                int startType = -1;
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName))
                {
                    if (k != null)
                    {
                        res.IsInstalled = true;
                        var val = k.GetValue("Start");
                        if (val != null) startType = Convert.ToInt32(val);
                    }
                }

                if (!res.IsInstalled)
                {
                    res.StatusText = "[ MISSING: Not Installed ]";
                    res.StatusColor = (serviceName == "GamingServices") ? WarningOrange : DangerRed;
                    return res;
                }

                res.IsDisabled = (startType == 4);

                try
                {
                    using (ServiceController sc = new ServiceController(serviceName))
                    {
                        res.IsRunning = (sc.Status == ServiceControllerStatus.Running);
                    }
                }
                catch { }

                if (res.IsRunning)
                {
                    res.StatusText = "[ PASS: Running ]";
                    res.StatusColor = AccentGreen;
                }
                else if (res.IsDisabled)
                {
                    res.StatusText = "[ DISABLED: Service Disabled ]";
                    res.StatusColor = DangerRed;
                }
                else
                {
                    res.StatusText = "[ PASS: Ready (On-Demand) ]";
                    res.StatusColor = AccentGreen;
                }
            }
            catch
            {
                res.StatusText = "[ WARNING: Query Error ]";
                res.StatusColor = WarningOrange;
            }
            return res;
        }

        public void RunAllChecks()
        {
            // 1. .NET 8 Desktop Runtime Check
            isNet8Ok = false;
            string netDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
            string net8Ver = "";
            if (Directory.Exists(netDir))
            {
                foreach (var d in Directory.GetDirectories(netDir))
                {
                    string name = Path.GetFileName(d);
                    if (name.StartsWith("8."))
                    {
                        isNet8Ok = true;
                        net8Ver = name;
                        break;
                    }
                }
            }
            if (isNet8Ok)
            {
                lblNet8Status.Text = "[ PASS: v" + net8Ver + " ]";
                lblNet8Status.ForeColor = AccentGreen;
                if (btnNet8Fix != null) btnNet8Fix.Visible = false;
            }
            else
            {
                lblNet8Status.Text = "[ MISSING: .NET 8 Required ]";
                lblNet8Status.ForeColor = DangerRed;
                if (btnNet8Fix != null) btnNet8Fix.Visible = true;
            }

            // 2. Visual C++ 2015-2022 x64 Check
            isVcOk = false;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
                {
                    if (k != null && Convert.ToInt32(k.GetValue("Installed", 0)) == 1)
                    {
                        isVcOk = true;
                    }
                }
            }
            catch { }
            if (!isVcOk)
            {
                string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (File.Exists(Path.Combine(sys32, "vcruntime140.dll")) && File.Exists(Path.Combine(sys32, "msvcp140.dll")))
                {
                    isVcOk = true;
                }
            }
            if (isVcOk)
            {
                lblVCStatus.Text = "[ PASS: Installed ]";
                lblVCStatus.ForeColor = AccentGreen;
                if (btnVCFix != null) btnVCFix.Visible = false;
            }
            else
            {
                lblVCStatus.Text = "[ MISSING: VC++ Required ]";
                lblVCStatus.ForeColor = DangerRed;
                if (btnVCFix != null) btnVCFix.Visible = true;
            }

            // 3. WebView2 Check
            bool isWvOk = false;
            string wvVer = "";
            try
            {
                string[] candidates = new string[] {
                    @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                    @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                    @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-2342-4246-8608-2A214300305F}",
                    @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-2342-4246-8608-2A214300305F}"
                };
                foreach (var c in candidates)
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(c))
                    {
                        if (k != null)
                        {
                            var pv = k.GetValue("pv") as string;
                            if (!string.IsNullOrEmpty(pv) && pv != "0.0.0.0")
                            {
                                isWvOk = true;
                                wvVer = pv;
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
            if (!isWvOk)
            {
                string edgeDir = @"C:\Program Files (x86)\Microsoft\EdgeWebView\Application";
                if (Directory.Exists(edgeDir)) isWvOk = true;
            }

            if (isWvOk)
            {
                lblWVStatus.Text = "[ PASS: " + (wvVer != "" ? "v" + wvVer : "Ready") + " ]";
                lblWVStatus.ForeColor = AccentGreen;
                if (btnWVFix != null) btnWVFix.Visible = false;
            }
            else
            {
                lblWVStatus.Text = "[ MISSING: Recommended ]";
                lblWVStatus.ForeColor = WarningOrange;
                if (btnWVFix != null) btnWVFix.Visible = true;
            }

            // 4. Developer Mode Check
            bool isDevOk = false;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"))
                {
                    if (k != null && Convert.ToInt32(k.GetValue("AllowDevelopmentWithoutDevLicense", 0)) == 1)
                    {
                        isDevOk = true;
                    }
                }
            }
            catch { }
            if (isDevOk)
            {
                lblDevStatus.Text = "[ PASS: Enabled ]";
                lblDevStatus.ForeColor = AccentGreen;
                if (btnDevFix != null) btnDevFix.Visible = false;
            }
            else
            {
                lblDevStatus.Text = "[ DISABLED: Recommended ]";
                lblDevStatus.ForeColor = WarningOrange;
                if (btnDevFix != null) btnDevFix.Visible = true;
            }

            // 5. AppXSvc Check
            var appxRes = CheckWindowsService("AppXSvc");
            lblAppXStatus.Text = appxRes.StatusText;
            lblAppXStatus.ForeColor = appxRes.StatusColor;
            if (btnAppXFix != null) btnAppXFix.Visible = (appxRes.IsDisabled || !appxRes.IsInstalled);

            // 6. ClipSvc Check
            var clipRes = CheckWindowsService("ClipSvc");
            lblClipStatus.Text = clipRes.StatusText;
            lblClipStatus.ForeColor = clipRes.StatusColor;
            if (btnClipFix != null) btnClipFix.Visible = (clipRes.IsDisabled || !clipRes.IsInstalled);

            // 7. GamingServices Check
            var gamingRes = CheckWindowsService("GamingServices");
            lblGamingStatus.Text = gamingRes.StatusText;
            lblGamingStatus.ForeColor = gamingRes.StatusColor;
            if (btnGamingFix != null)
            {
                btnGamingFix.Visible = (!gamingRes.IsInstalled || gamingRes.IsDisabled);
                btnGamingFix.Text = (!gamingRes.IsInstalled) ? "Install via Store" : "Enable Service";
            }

            // 8. wuauserv Check
            var wuaRes = CheckWindowsService("wuauserv");
            lblWUAStatus.Text = wuaRes.StatusText;
            lblWUAStatus.ForeColor = wuaRes.StatusColor;
            if (btnWUAFix != null) btnWUAFix.Visible = (wuaRes.IsDisabled || !wuaRes.IsInstalled);

            // Overall Evaluation
            bool criticalServicesOk = (!appxRes.IsDisabled && !clipRes.IsDisabled && !wuaRes.IsDisabled);

            if (isNet8Ok && isVcOk && criticalServicesOk)
            {
                lblSummary.Text = "System is Ready to Play!";
                lblSummary.ForeColor = AccentGreen;
                btnLaunch.BackColor = AccentGreen;
                btnLaunch.Enabled = true;
            }
            else if (!isNet8Ok || !isVcOk)
            {
                lblSummary.Text = "Prerequisites Missing (Install above to play)";
                lblSummary.ForeColor = DangerRed;
                btnLaunch.BackColor = Color.FromArgb(70, 75, 90);
                btnLaunch.Enabled = false;
            }
            else
            {
                lblSummary.Text = "Services Disabled (Enable highlighted services)";
                lblSummary.ForeColor = WarningOrange;
                btnLaunch.BackColor = AccentGreen;
                btnLaunch.Enabled = true;
            }
        }

        private void DownloadAndRun(string url, string targetFileName, string friendlyName)
        {
            prgDownload.Visible = true;
            prgDownload.Value = 0;
            lblSummary.Text = "Downloading " + friendlyName + "...";
            lblSummary.ForeColor = AccentMagenta;

            string tempFile = Path.Combine(Path.GetTempPath(), targetFileName);

            Thread t = new Thread(delegate()
            {
                try
                {
                    using (WebClient client = new WebClient())
                    {
                        client.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                prgDownload.Value = Math.Min(100, Math.Max(0, e.ProgressPercentage));
                            });
                        };
                        client.DownloadFileCompleted += delegate(object s, AsyncCompletedEventArgs e)
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                prgDownload.Visible = false;
                                if (e.Error != null)
                                {
                                    MessageBox.Show("Download failed: " + e.Error.Message, "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    RunAllChecks();
                                    return;
                                }

                                try
                                {
                                    lblSummary.Text = "Installing " + friendlyName + "... Follow installer prompts.";
                                    Process proc = Process.Start(tempFile);
                                    proc.WaitForExit();
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Failed to launch installer: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                                RunAllChecks();
                            });
                        };
                        client.DownloadFileAsync(new Uri(url), tempFile);
                    }
                }
                catch (Exception ex)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        prgDownload.Visible = false;
                        MessageBox.Show("Failed to start download: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        RunAllChecks();
                    });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void OnDownloadNet8(object sender, EventArgs e)
        {
            DownloadAndRun("https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe", "windowsdesktop-runtime-8.0-win-x64.exe", ".NET 8.0 Desktop Runtime");
        }

        private void OnDownloadVC(object sender, EventArgs e)
        {
            DownloadAndRun("https://aka.ms/vs/17/release/vc_redist.x64.exe", "vc_redist.x64.exe", "Visual C++ 2015-2022");
        }

        private void OnDownloadWV(object sender, EventArgs e)
        {
            DownloadAndRun("https://go.microsoft.com/fwlink/p/?LinkId=2124703", "MicrosoftEdgeWebview2Setup.exe", "WebView2 Runtime");
        }

        private void OnOpenDevSettings(object sender, EventArgs e)
        {
            try
            {
                Process.Start("ms-settings:developers");
            }
            catch
            {
                MessageBox.Show("Please open Windows Settings -> System (or Privacy & Security) -> For developers, and toggle Developer Mode to ON.", "Developer Mode", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void EnableAndStartService(string serviceName, string friendlyName)
        {
            try
            {
                lblSummary.Text = "Enabling " + friendlyName + "...";
                lblSummary.ForeColor = AccentMagenta;

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c sc config " + serviceName + " start= demand & net start " + serviceName,
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process p = Process.Start(psi);
                p.WaitForExit();
                RunAllChecks();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not enable service '" + serviceName + "':\n" + ex.Message, "Service Configuration", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RunAllChecks();
            }
        }

        private void OnFixGamingServices(object sender, EventArgs e)
        {
            var res = CheckWindowsService("GamingServices");
            if (!res.IsInstalled)
            {
                try
                {
                    Process.Start("ms-windows-store://pdp/?productid=9MWPM2CQNLHN");
                }
                catch
                {
                    try
                    {
                        Process.Start("powershell.exe", "-Command Start-Process 'ms-windows-store://pdp/?productid=9MWPM2CQNLHN'");
                    }
                    catch { }
                }
            }
            else
            {
                EnableAndStartService("GamingServices", "Gaming Services");
            }
        }

        private void OnLaunchClicked(object sender, EventArgs e)
        {
            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            string launcherExe = Path.Combine(launcherDir, "BUGrock_Launcher.exe");

            if (!File.Exists(launcherExe))
            {
                string alt1 = Path.Combine(launcherDir, "BUGrock_Launcher_Bin", "BUGrock_Launcher.exe");
                string alt2 = Path.Combine(launcherDir, "..", "BUGrock_Launcher_Bin", "BUGrock_Launcher.exe");
                if (File.Exists(alt1)) launcherExe = Path.GetFullPath(alt1);
                else if (File.Exists(alt2)) launcherExe = Path.GetFullPath(alt2);
            }

            if (File.Exists(launcherExe))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = launcherExe,
                        WorkingDirectory = Path.GetDirectoryName(launcherExe)
                    });
                    this.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to start BUGrock Launcher:\n" + ex.Message, "Launch Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("Could not find 'BUGrock_Launcher.exe' in the current folder.\nPlease place BUGrock_Check.exe inside the launcher folder.", "Launcher Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
