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
public partial class App : Application
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
            MessageBox.Show("O WinTune já está aberto.", "WinTune", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        _loggerFactory = LoggerFactory.Create(b => b.AddProvider(new FileLoggerProvider(LogDirectory)).SetMinimumLevel(LogLevel.Information));
        var log = _loggerFactory.CreateLogger("WinTune");
        log.LogInformation("WinTune iniciado. Versão {Version}", typeof(App).Assembly.GetName().Version);

        var services = Compose(log, out var startupWarnings);
        Services = services;
        ThemeManager.Apply(this, services.Registry);

        var dialogs = new MessageBoxDialogService();
        var vm = new MainViewModel(services, dialogs, _loggerFactory.CreateLogger<MainViewModel>());
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();

        foreach (var warning in startupWarnings)
        {
            vm.AddLog(warning);
            dialogs.Inform("WinTune", warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
        return new AppServices
        {
            Registry = registry,
            Runner = runner,
            Wmi = wmi,
            FileSystem = fs,
            Environment = env,
            Clock = clock,
            Services = new ServiceControl(wmi, registry),
            Processes = new ProcessList(),
            Shell = new ShellLauncher(),
            Backup = backup,
            Catalog = catalog,
        };
    }
}
