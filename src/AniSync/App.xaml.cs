using System.Windows;
using AniSync.Core;
using AniSync.Ui;
using Forms = System.Windows.Forms;

namespace AniSync;

public partial class App : Application
{
    Mutex? _singleInstance;
    EventWaitHandle? _showSignal;
    AppController? _controller;
    MainWindow? _window;
    Forms.NotifyIcon? _tray;
    Forms.ToolStripMenuItem? _listenItem;
    Forms.ToolStripMenuItem? _accountsItem;
    bool _trayHintShown;

    const string QuitSignalName = "AniSync.Quit";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Utilisé par l'installeur pour fermer l'appli avant une mise à jour ou une désinstallation.
        if (HasArg(e, "--quit"))
        {
            QuitRunningInstance();
            Shutdown();
            return;
        }

        // Une seule instance : relancer l'appli ré-affiche simplement la fenêtre existante.
        _singleInstance = new Mutex(true, "AniSync.SingleInstance", out bool isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "AniSync.ShowWindow");
        if (!isFirst)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => Dispatcher.BeginInvoke(ShowMain), null, Timeout.Infinite, false);
        var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitSignalName);
        ThreadPool.RegisterWaitForSingleObject(quitSignal, (_, _) => Dispatcher.BeginInvoke(Quit), null, Timeout.Infinite, true);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Erreur non gérée (interface)", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Erreur fatale", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Erreur non gérée (tâche)", args.Exception);
            args.SetObserved();
        };

        _controller = new AppController(Dispatcher);
        _window = new MainWindow(_controller);
        _window.HiddenToTray += OnHiddenToTray;

        CreateTray();
        _controller.ListeningChanged += UpdateTray;
        _controller.ProfilesChanged += UpdateAccountsMenu;
        UpdateAccountsMenu();
        _controller.Notification += (title, message) => _tray?.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.None);
        _controller.Start();

        if (!e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
            ShowMain();
    }

    void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir AniSync", null, (_, _) => ShowMain());
        _listenItem = new Forms.ToolStripMenuItem("Écoute activée", null, (_, _) => ToggleListening());
        menu.Items.Add(_listenItem);
        _accountsItem = new Forms.ToolStripMenuItem("Compte actif");
        menu.Items.Add(_accountsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => Quit());

        _tray = new Forms.NotifyIcon { ContextMenuStrip = menu, Visible = true };
        // Cliquer sur une notification (« AniSync a un doute »...) ouvre la fenêtre pour répondre.
        _tray.BalloonTipClicked += (_, _) => ShowMain();
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) ShowMain();
        };
        UpdateTray();
    }

    void UpdateTray()
    {
        if (_tray is null || _controller is null || _listenItem is null) return;
        bool on = _controller.Settings.Listening;
        _tray.Icon = TrayIcons.Get(on);
        _tray.Text = on ? "AniSync : écoute activée" : "AniSync : écoute désactivée";
        _listenItem.Checked = on;
        _listenItem.Text = on ? "Écoute activée" : "Écoute désactivée";
    }

    /// <summary>Sous-menu « Compte actif » : changer de compte en un clic depuis la zone de notification.</summary>
    void UpdateAccountsMenu()
    {
        if (_accountsItem is null || _controller is null) return;
        _accountsItem.DropDownItems.Clear();
        foreach (var profile in _controller.Profiles)
        {
            bool signedIn = _controller.IsSignedIn(profile);
            var item = new Forms.ToolStripMenuItem(signedIn ? profile.Label : $"{profile.Label} (à reconnecter)")
            {
                Checked = _controller.IsActive(profile),
                Enabled = signedIn,
            };
            item.Click += (_, _) => _controller.Activate(profile);
            _accountsItem.DropDownItems.Add(item);
        }
        if (_accountsItem.DropDownItems.Count > 0) _accountsItem.DropDownItems.Add(new Forms.ToolStripSeparator());
        _accountsItem.DropDownItems.Add("Gérer les comptes…", null, (_, _) =>
        {
            ShowMain();
            _window?.ShowAccounts();
        });
        _accountsItem.Text = _controller.Settings.ActiveProfile is { } active ? $"Compte : {active.Label}" : "Compte : aucun";
    }

    void ToggleListening()
    {
        if (_controller is null) return;
        _controller.Vm.Listening = !_controller.Vm.Listening;
    }

    void ShowMain()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    void OnHiddenToTray()
    {
        if (_trayHintShown) return;
        _trayHintShown = true;
        _tray?.ShowBalloonTip(4000, "AniSync tourne toujours",
            "L'appli reste dans la zone de notification. Clic droit sur l'icône pour quitter.", Forms.ToolTipIcon.None);
    }

    void Quit()
    {
        _controller?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        Shutdown();
    }

    static bool HasArg(StartupEventArgs e, string name) =>
        e.Args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Demande à l'instance en cours de se fermer proprement, puis attend qu'elle ait quitté.</summary>
    static void QuitRunningInstance()
    {
        if (!EventWaitHandle.TryOpenExisting(QuitSignalName, out var signal)) return;
        using (signal) signal.Set();

        foreach (var process in System.Diagnostics.Process.GetProcessesByName("AniSync"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    if (!process.WaitForExit(5000)) process.Kill();
                }
                catch
                {
                    // déjà fermée
                }
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        try { _singleInstance?.ReleaseMutex(); } catch { /* pas propriétaire */ }
        base.OnExit(e);
    }
}
