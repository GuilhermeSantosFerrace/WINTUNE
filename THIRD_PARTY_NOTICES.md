# Avisos de terceiros

Dependências com versão exata (`Directory.Packages.props`). Licenças conferidas nas páginas oficiais dos projetos.

## Distribuídas com o aplicativo

| Pacote | Versão | Licença | Projeto |
|--------|--------|---------|---------|
| CommunityToolkit.Mvvm | 8.2.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Extensions.Logging | 8.0.1 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Logging.Abstractions | 8.0.2 | MIT | https://github.com/dotnet/runtime |
| System.Management | 8.0.0 | MIT | https://github.com/dotnet/runtime |
| .NET 8 runtime (self-contained) | 8.0.x | MIT | https://github.com/dotnet/runtime |

## Somente para testes (não distribuídas)

| Pacote | Versão | Licença |
|--------|--------|---------|
| xunit | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| FluentAssertions | 7.2.0 | Apache-2.0 (a 8.x tem licença comercial; ver D-004) |
| coverlet.collector | 6.0.4 | MIT |

## Componentes externos usados em tempo de execução (não distribuídos)

- **NVML (`nvml.dll`)**: biblioteca do driver NVIDIA, carregada de `System32` se existir. Não é empacotada pelo WinTune.
- **PresentMon**: ferramenta externa opcional (MIT); o WinTune só lê o CSV que o usuário exportar.

## Avaliado e não incluído

- **LibreHardwareMonitorLib** (MPL-2.0): não incluída porque usa driver de kernel (PawnIO), com compatibilidade com
  BattlEye/VAC não confirmada (D-024).

Os textos completos das licenças MIT e Apache-2.0 estão nos repositórios acima.
