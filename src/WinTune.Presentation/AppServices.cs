using WinTune.Core.Backup;
using WinTune.Core.Games;
using WinTune.Core.Startup;
using WinTune.Core.Tweaks;
using WinTune.Platform.Abstractions;

namespace WinTune.Presentation;

/// <summary>Dependências da interface (composição feita no App ou nos testes).</summary>
public sealed class AppServices
{
    /// <summary>Registro.</summary>
    public required IRegistry Registry { get; init; }

    /// <summary>Executor de processos.</summary>
    public required IProcessRunner Runner { get; init; }

    /// <summary>WMI.</summary>
    public required IWmiReader Wmi { get; init; }

    /// <summary>Sistema de arquivos.</summary>
    public required IFileSystem FileSystem { get; init; }

    /// <summary>Ambiente.</summary>
    public required IEnvironmentInfo Environment { get; init; }

    /// <summary>Relógio.</summary>
    public required IClock Clock { get; init; }

    /// <summary>Serviços do Windows.</summary>
    public required IServiceControl Services { get; init; }

    /// <summary>Lista de processos (somente nomes).</summary>
    public required IProcessList Processes { get; init; }

    /// <summary>Shell.</summary>
    public required IShellLauncher Shell { get; init; }

    /// <summary>Backup.</summary>
    public required BackupStore Backup { get; init; }

    /// <summary>Catálogo.</summary>
    public required TweakCatalog Catalog { get; init; }

    /// <summary>Jogos-alvo.</summary>
    public IReadOnlyList<GameDefinition> Games { get; set; } = GameDefinition.Defaults;

    /// <summary>Cria os sensores (somente modo usuário). Null = telemetria indisponível.</summary>
    public Func<IHardwareSensors>? SensorsFactory { get; init; }

    /// <summary>Uso por processo (consumidores em segundo plano).</summary>
    public IProcessUsageReader? ProcessUsage { get; init; }

    /// <summary>Gerenciador de inicialização.</summary>
    public StartupManager? Startup { get; init; }

    /// <summary>
    /// Resolvedor extra de IDs de backup (ajustes dinâmicos de fases posteriores).
    /// </summary>
    public Func<string, Tweak?>? ResolveTweak { get; init; }

    /// <summary>Resolve um ID de backup para o ajuste capaz de revertê-lo (catálogo, inicialização e extras).</summary>
    public Tweak? Resolve(string id) => Catalog.Find(id) ?? Startup?.Resolve(id) ?? ResolveTweak?.Invoke(id);
}

/// <summary>Diálogos (implementado com MessageBox no App e com respostas fixas nos testes).</summary>
public interface IDialogService
{
    /// <summary>Pergunta sim/não.</summary>
    bool Confirm(string title, string message);

    /// <summary>Mostra uma informação.</summary>
    void Inform(string title, string message);
}
