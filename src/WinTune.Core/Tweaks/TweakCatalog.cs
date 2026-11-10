using WinTune.Core.Domain;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Tweaks;

/// <summary>
/// Catálogo de ajustes automáticos (seção 4.1). Fonte única de verdade em código; documentado em <c>docs/TWEAKS.md</c>.
/// </summary>
public sealed class TweakCatalog
{
    /// <summary>Chave do Game Bar / Modo de Jogo.</summary>
    public const string GameBarKey = @"Software\Microsoft\GameBar";

    /// <summary>Chave do agendador de GPU.</summary>
    public const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

    /// <summary>Chave do Game DVR (store).</summary>
    public const string GameConfigStoreKey = @"System\GameConfigStore";

    /// <summary>Chave do Game DVR (captura).</summary>
    public const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";

    /// <summary>Chave do atraso de inicialização.</summary>
    public const string SerializeKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";

    /// <summary>Chave de personalização (transparência).</summary>
    public const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly List<Tweak> _all;

    /// <summary>Monta o catálogo com as dependências de plataforma.</summary>
    public TweakCatalog(IRegistry registry, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(runner);
        _all =
        [
            GameMode(registry),
            new HagsTweak(registry),
            GameDvrOff(registry),
            StartupDelay(registry),
            TransparencyOff(registry),
            new PowerPlanTweak(runner),
            new FlipModelUpgradeTweak(registry),
        ];
    }

    /// <summary>Todos os ajustes.</summary>
    public IReadOnlyList<Tweak> All => _all;

    /// <summary>Busca por ID.</summary>
    public Tweak? Find(string id) => _all.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    private static RegistryTweak GameMode(IRegistry r) => new(r, new TweakInfo
    {
        Id = "game-mode",
        Name = "Modo de Jogo do Windows",
        Description = "Liga o Modo de Jogo, que prioriza o jogo em primeiro plano e evita que o Windows Update instale drivers ou " +
                      "mostre notificações de reinício durante a partida. Costuma já vir ligado: nesse caso não há nada a fazer.",
        For = [Focus.Gaming],
        Helps = Scenario.Stutter | Scenario.LowFps,
        Priority = 60,
    },
    [
        RegistrySetting.DWord(RegistryHive.CurrentUser, GameBarKey, "AutoGameModeEnabled", 1),
        RegistrySetting.DWord(RegistryHive.CurrentUser, GameBarKey, "AllowAutoGameMode", 1),
    ]);

    private static RegistryTweak GameDvrOff(IRegistry r) => new(r, new TweakInfo
    {
        Id = "gamedvr-off",
        Name = "Desligar gravação em segundo plano (Game DVR)",
        Description = "Desliga a captura contínua em segundo plano da Xbox Game Bar, que codifica vídeo o tempo todo e pode custar " +
                      "desempenho. Capturas manuais (Win+Alt+PrtScn) continuam disponíveis se você religar pela Game Bar.",
        For = [Focus.Gaming],
        Helps = Scenario.Stutter | Scenario.LowFps,
        Priority = 70,
    },
    [
        RegistrySetting.DWord(RegistryHive.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled", 0),
        RegistrySetting.DWord(RegistryHive.CurrentUser, GameDvrKey, "AppCaptureEnabled", 0),
    ]);

    private static RegistryTweak StartupDelay(IRegistry r) => new(r, new TweakInfo
    {
        Id = "startup-delay",
        Name = "Remover atraso dos apps de inicialização",
        Description = "O Explorer espera alguns segundos (e, em builds recentes, o sistema ficar ocioso) antes de abrir os apps de " +
                      "inicialização. Zerar esse atraso faz os apps abrirem logo após o login. Não acelera o boot do Windows em si.",
        For = [Focus.Work, Focus.Gaming],
        Helps = Scenario.SlowBoot,
        Priority = 50,
    },
    [
        RegistrySetting.DWord(RegistryHive.CurrentUser, SerializeKey, "StartupDelayInMSec", 0),
        RegistrySetting.DWord(RegistryHive.CurrentUser, SerializeKey, "WaitForIdleState", 0),
    ]);

    private static RegistryTweak TransparencyOff(IRegistry r) => new(r, new TweakInfo
    {
        Id = "transparency-off",
        Name = "Desligar efeitos de transparência",
        Description = "Desliga a transparência (Mica/Acrylic) de menus e barra de tarefas. Ganho pequeno, perceptível só em máquinas " +
                      "com GPU integrada fraca. Muda a aparência; por isso vem desmarcado.",
        For = [Focus.Work],
        Helps = Scenario.SlowMultitasking,
        Risk = Risk.Cosmetic,
        Priority = 20,
        DefaultSelected = false,
    },
    [
        RegistrySetting.DWord(RegistryHive.CurrentUser, PersonalizeKey, "EnableTransparency", 0),
    ]);
}

/// <summary>Agendamento de GPU acelerado por hardware (HAGS): experimental, desmarcado por padrão.</summary>
public sealed class HagsTweak : RegistryTweak
{
    /// <summary>Cria o ajuste.</summary>
    public HagsTweak(IRegistry registry)
        : base(registry, new TweakInfo
        {
            Id = "hags",
            Name = "Agendamento de GPU acelerado por hardware (HAGS)",
            Description = "Deixa a GPU gerenciar a própria fila de trabalho. O ganho é inconsistente entre drivers e jogos e pode " +
                          "até piorar travadas em alguns casos: experimente e meça com o benchmark (antes/depois). Exige reiniciar.",
            For = [Focus.Gaming],
            Helps = Scenario.Stutter | Scenario.LowFps,
            Risk = Risk.NeedsReboot,
            Priority = 40,
            DefaultSelected = false,
        },
        [RegistrySetting.DWord(RegistryHive.LocalMachine, TweakCatalog.GraphicsDriversKey, "HwSchMode", 2)])
    {
    }

    /// <inheritdoc />
    public override TweakAdvice GetAdvice(SystemProfile profile)
    {
        bool applied;
        try
        {
            applied = IsApplied();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            applied = false;
        }

        return applied
            ? new TweakAdvice(true, false, "O HAGS já está ativo nesta máquina; nada a fazer.")
            : new TweakAdvice(true, false, "Experimental: compare antes/depois com o benchmark antes de manter.");
    }
}
