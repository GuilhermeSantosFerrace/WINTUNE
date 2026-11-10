# Arquitetura do WinTune

```
WinTune.App (WPF, net8.0-windows)          XAML, temas, diálogos, bandeja, composição
   │
WinTune.Presentation (net8.0)              ViewModels (CommunityToolkit.Mvvm), testáveis sem janela
   │
WinTune.Core (net8.0)                      domínio, ajustes, backup, recomendação, diagnóstico,
   │                                       telemetria, inicialização, limpeza, benchmark, serviços,
   │                                       perfis/modo jogo, relatórios, reconciliação
WinTune.Platform.Abstractions (net8.0)     interfaces: IRegistry, IProcessRunner, IWmiReader, IServiceControl,
   │                                       IFileSystem, IClock, IEnvironmentInfo, IProcessList, IShellLauncher,
   │                                       IHardwareSensors, IProcessUsageReader, IScheduledTasks
WinTune.Platform (net8.0-windows)          implementações reais (registro, processos, WMI, SCM, arquivos,
                                           PDH/NVML, Agendador de Tarefas)
```

Regra: só `WinTune.Platform` toca o Windows (teste `ArchitectureTests`). O Core é 100% testável com os fakes de
`tests/WinTune.TestDoubles`.

## Módulos do Core

| Pasta | Conteúdo |
|-------|----------|
| `Domain` | `Focus`, `Scenario`, `GameTarget`, `Risk`, `Severity`, `Finding`, `TweakResult`, `SystemProfile` |
| `Tweaks` | `Tweak` (contrato), `RegistryTweak`, `PowerPlanTweak`, `FlipModelUpgradeTweak`, `TweakCatalog` |
| `Backup` | `BackupStore` (JSON atômico, journal, intenções), `RestorePointService`, `Reconciler` |
| `Recommendations` | `RecommendationEngine` |
| `Diagnostics` | `Analyzer`, regras estáticas, `SystemProfileDetector` |
| `Games` | `GameDefinition`, `GamesConfigLoader` (`games.json`), `GameInstallLocator`, verificações/guias |
| `Telemetry` | `Sampler`, `BottleneckClassifier`, `BackgroundLoadInspector` |
| `Startup` | `StartupManager`, `StartupDisableTweak` |
| `Cleanup` | `CleanupService` |
| `Benchmark` | `FrameStats`, `PresentMonCsv`, `Comparator`, `BenchmarkStore` |
| `Services` | `ServiceAuditor`, `ServiceManualTweak`, `TaskDisableTweak` |
| `Profiles` | `ProfileStore`, `GameModeOrchestrator` |
| `Reports` | `ReportBuilder` |

## Dados locais

| Arquivo | Conteúdo |
|---------|----------|
| `%ProgramData%\WinTune\backup.json` | originais, journal, intenções (escrita só Administradores) |
| `%APPDATA%\WinTune\games.json` | jogos, processos, overlays conhecidos |
| `%APPDATA%\WinTune\profiles\*.json` | perfis |
| `%APPDATA%\WinTune\gamemode.json` / `gamemode-state.json` | configuração / estado do modo jogo |
| `%LOCALAPPDATA%\WinTune\sessions\*.json` | rodadas de benchmark |
| `%LOCALAPPDATA%\WinTune\logs\wintune*.log` | log rotativo (1 MB × 10) |

Nenhum dado sai da máquina.
