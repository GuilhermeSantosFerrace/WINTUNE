using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using WinTune.App.Services;
using WinTune.Core.Backup;
using WinTune.Core.Tweaks;
using WinTune.Platform;
using WinTune.Platform.Abstractions;
using WinTune.Presentation;
using WinTune.Presentation.ViewModels;

namespace WinTune.App;

/// <summary>
/// Ponto de entrada: instância única, composição das dependências, tema e janela principal.
/// </summary>
public partial class App : Application, IDisposable
{
    private const string MutexName = @"Global\WinTune.SingleInstance";
    private Mutex? _mutex;
    private ILoggerFactory? _loggerFactory;

    /// <summary>Serviços compostos (disponível após o início).</summary>
    internal AppServices? Services { get; private set; }

    /// <summary>Pasta de logs.</summary>
    internal static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinTune", "logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // R12: impede duas instâncias aplicando ao mesmo tempo.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("O WinTune já está aberto. Feche-o antes de continuar.", "WinTune", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(3);
            return;
        }

        _loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FileLoggerProvider(LogDirectory)).SetMinimumLevel(LogLevel.Information));
        var log = _loggerFactory.CreateLogger("WinTune");
        log.LogInformation("WinTune iniciado. Versão {Version}", typeof(App).Assembly.GetName().Version);

        var services = Compose(log, out var startupWarnings);

        // Modo sem interface usado pelo desinstalador: desfaz tudo e sai (0 = tudo revertido; 2 = pendências).
        if (e.Args.Any(a => string.Equals(a, "--revert-all", StringComparison.OrdinalIgnoreCase)))
        {
            var report = UndoService.RevertAll(services);
            log.LogInformation("--revert-all: {Summary}", report.Summary);
            if (!e.Args.Any(a => string.Equals(a, "--quiet", StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(report.Summary, "WinTune — Desfazer tudo", MessageBoxButton.OK,
                    report.AllReverted ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }

            Shutdown(report.AllReverted ? 0 : 2);
            return;
        }
        Services = services;
        ThemeManager.Apply(this, services.Registry);

        var dialogs = new MessageBoxDialogService();
        var vm = new MainViewModel(services, dialogs, _loggerFactory.CreateLogger<MainViewModel>());
        _viewModel = vm;
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;

        _tray = new TrayIcon(window, () => vm.Profiles.IsGameModeEnabled = false);
        vm.Profiles.GameModeEnabledChanged += (_, on) => _tray.SetVisible(on);
        vm.Profiles.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ProfilesViewModel.GameModeStatus))
            {
                _tray.SetStatus("WinTune — " + vm.Profiles.GameModeStatus);
            }
        };

        window.Show();

        foreach (var warning in startupWarnings)
        {
            vm.AddLog(warning);
            dialogs.Inform("WinTune", warning);
        }

        _ = vm.InitializeAsync();
    }

    private TrayIcon? _tray;
    private MainViewModel? _viewModel;

    /// <inheritdoc />
    public void Dispose()
    {
        _viewModel?.Dispose();
        _tray?.Dispose();
        _tray = null;
        _loggerFactory?.Dispose();
        _loggerFactory = null;
        GC.SuppressFinalize(this);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _tray?.Dispose();
        _loggerFactory?.Dispose();
        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Não era o dono.
            }

            _mutex.Dispose();
        }

        base.OnExit(e);
    }

    internal static AppServices Compose(ILogger log, out List<string> warnings)
    {
        warnings = [];
        var registry = new WindowsRegistry();
        var runner = new ProcessRunner();
        var wmi = new WmiReader();
        var fs = new WindowsFileSystem();
        var env = new WindowsEnvironment();
        var clock = new SystemClock();

        if (env.OsBuild < 22000)
        {
            warnings.Add($"Este Windows (build {env.OsBuild}) não é Windows 11. O WinTune foi feito e testado para Windows 11; alguns ajustes podem não se aplicar.");
        }

        var backupPath = BackupStore.DefaultPath(env);
        try
        {
            fs.EnsureAdminOnlyDirectory(Path.GetDirectoryName(backupPath)!);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            log.LogWarning(ex, "Não foi possível restringir a pasta do backup.");
            warnings.Add("Não foi possível proteger a pasta do backup (é preciso executar como administrador). O backup continua funcionando, mas sem a proteção extra de permissões.");
        }

        var backup = BackupStore.Open(fs, clock, backupPath);
        if (backup.LoadWarning is not null)
        {
            log.LogWarning("Backup: {Warning}", backup.LoadWarning);
        }

        var catalog = new TweakCatalog(registry, runner);
        var games = Core.Games.GamesConfigLoader.Load(fs, Core.Games.GamesConfigLoader.DefaultPath(env));
        if (games.Warning is not null)
        {
            warnings.Add(games.Warning);
        }

        var tasks = new ScheduledTasks();
        var serviceControl = new ServiceControl(wmi, registry);
        var usage = new Platform.Sensors.ProcessUsageReader(wmi);
        return new AppServices
        {
            Games = games.Games,
            KnownBackgroundProcesses = games.KnownBackgroundProcesses,
            Startup = new Core.Startup.StartupManager(registry, fs, env, tasks),
            ServiceAuditor = new Core.Services.ServiceAuditor(serviceControl, tasks, fs, env, usage),
            SensorsFactory = () => new Platform.Sensors.UserModeSensors(wmi),
            ProcessUsage = usage,
            Registry = registry,
            Runner = runner,
            Wmi = wmi,
            FileSystem = fs,
            Environment = env,
            Clock = clock,
            Services = serviceControl,
            Processes = new ProcessList(),
            Shell = new ShellLauncher(),
            Backup = backup,
            Catalog = catalog,
        };
    }
}
