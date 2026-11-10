# Registro de decisões (WinTune)

Decisões técnicas e ambiguidades resolvidas durante a construção. Regra do spec (seção 0, item 4):
na dúvida, escolher a opção mais conservadora e registrar aqui.

Formato: `D-NNN — título`, com fase, contexto, decisão e consequências.

---

## D-001 — Script de build: `build.ps1` em vez de `Makefile`

- **Fase:** 0
- **Contexto:** o spec aceita `Makefile` ou `build.ps1`. O alvo é exclusivamente Windows 11.
- **Decisão:** usar `build.ps1` (PowerShell já vem no Windows; `make` não).
- **Consequências:** alvos `build`, `test`, `run`, `publish`, `clean`. O CI chama `dotnet` diretamente.

## D-002 — Versão do SDK fixada em 8.0.428 com `rollForward: latestPatch`

- **Fase:** 0
- **Contexto:** o spec pede `global.json` fixando o SDK .NET 8.
- **Decisão:** `8.0.428` (última feature band 8.0.4xx no momento) com `latestPatch`, aceitando só correções de patch
  da mesma feature band. Pré-releases desabilitados.
- **Consequências:** o CI usa `actions/setup-dotnet` lendo o mesmo `global.json`.

## D-003 — Gerenciamento central de pacotes com versão exata

- **Fase:** 0
- **Contexto:** regra 7 da seção 0 (versões exatas, sem ranges).
- **Decisão:** `Directory.Packages.props` com versões na notação `[x.y.z]` (correspondência exata no NuGet).
- **Consequências:** atualizar dependência é uma mudança explícita em um único arquivo.

## D-004 — FluentAssertions 7.2.0 (e não 8.x)

- **Fase:** 0
- **Contexto:** a partir da 8.0 o FluentAssertions passou a ter licença comercial (Xceed); a 7.x continua Apache-2.0.
- **Decisão:** fixar `FluentAssertions` em `7.2.0` para evitar dúvida de licenciamento.
- **Consequências:** revisar na Fase 14 (revisão de licenças).

## D-005 — `SupportedOSPlatformVersion` não fixado no projeto

- **Fase:** 0
- **Contexto:** com `TargetFramework=net8.0-windows` a `TargetPlatformVersion` padrão é 7.0, e o SDK recusa
  `SupportedOSPlatformVersion=10.0.22000.0` (NETSDK1135). Usar `net8.0-windows10.0.22000.0` traria as projeções
  WinRT (CsWinRT), dependência que o projeto não precisa agora.
- **Decisão:** manter `net8.0-windows` sem `SupportedOSPlatformVersion`. A exigência de Windows 11 será verificada em
  tempo de execução (build do SO) e/ou no manifest do aplicativo nas fases seguintes.
- **Consequências:** o analisador de compatibilidade de plataforma considera "Windows" genérico.

## D-006 — Camadas e frameworks-alvo

- **Fase:** 0
- **Decisão:** `WinTune.Core` em `net8.0` (sem dependência de Windows ou UI, testável em qualquer ambiente);
  `WinTune.Platform` e `WinTune.App` em `net8.0-windows`. Um teste garante que o Core não referencia Platform nem WPF.

## D-007 — Elevação adiada para a Fase 6

- **Fase:** 0
- **Contexto:** o manifest `requireAdministrator` é entregável da Fase 6, e a seção 2 sugere UI sem elevação + helper elevado.
- **Decisão:** na Fase 0 o app roda sem manifest de elevação (janela placeholder, não altera nada).

## D-008 — Interfaces de plataforma em `WinTune.Platform.Abstractions` (net8.0)

- **Fase:** 1
- **Contexto:** a seção 3.1 diz que o Core só fala com o Windows pelas interfaces de Platform, mas o Core é `net8.0`
  (testável em qualquer lugar) e as implementações reais precisam de `net8.0-windows`.
- **Decisão:** as interfaces (`IRegistry`, `IProcessRunner`, `IWmiReader`, `IServiceControl`, `IFileSystem`, `IClock`,
  `IEnvironmentInfo`, `IProcessList`, `IShellLauncher`) ficam em `WinTune.Platform.Abstractions` (`net8.0`);
  `WinTune.Platform` contém só as implementações reais.
- **Consequências:** o Core referencia apenas as abstrações. Um teste de arquitetura impede `Registry.*`/`Process.Start`
  fora de `WinTune.Platform`.

## D-009 — Fakes compartilhados em `tests/WinTune.TestDoubles`

- **Fase:** 1
- **Decisão:** os fakes (`FakeRegistry`, `FakeProcessRunner`, `FakeWmiReader`, `FakeFileSystem`, ...) ficam em uma
  biblioteca de testes usada por todos os projetos de teste, evitando duplicação.

## D-010 — Executáveis externos somente de System32

- **Fase:** 1
- **Decisão:** `ProcessRunner` resolve nomes relativos apenas dentro de `%SystemRoot%\System32` (nunca pelo PATH), aceita
  só `.exe`, rejeita `..`, exige tempo limite entre 0 e 10 min e nunca usa shell. Evita sequestro de PATH e injeção.

## D-011 — Tipo de inicialização de serviço via `ChangeServiceConfig`

- **Fase:** 1
- **Contexto:** `ServiceController` não altera o tipo de inicialização, e gravar `Start` direto no registro não atualiza
  o banco do SCM até reiniciar.
- **Decisão:** leitura via WMI (`Win32_Service`) e registro; escrita via `ChangeServiceConfigW`/`ChangeServiceConfig2W`.
  A interface não expõe iniciar/parar serviço.

## D-012 — Local e proteção do backup

- **Fase:** 2
- **Contexto:** seção 3.4 e risco R2 (o app elevado restaura valores a partir de um JSON).
- **Decisão:** backup em `%ProgramData%\WinTune\backup.json`, diretório criado com ACL protegida (Administradores e SYSTEM:
  controle total; Usuários: leitura). Além disso, **allowlist em código**: cada ajuste só restaura (hive, chave, nome) que ele
  mesmo declara; entradas estranhas são ignoradas e registradas no journal. HMAC com DPAPI **não** foi adotado: DPAPI
  de escopo máquina pode ser decifrada por qualquer processo local, então não acrescenta proteção real além da ACL.
- **Consequências:** se a ACL não puder ser aplicada (sem elevação), o app avisa e continua; a allowlist segue valendo.

## D-013 — `Revert(store, RevertMode)` e status `Conflict`

- **Fase:** 2
- **Contexto:** a seção 3.4 pede reverter sem sobrescrever drift e perguntar ao usuário.
- **Decisão:** o contrato mantém `Revert(BackupStore)` (modo seguro) e ganha a sobrecarga `Revert(BackupStore, RevertMode)`.
  No modo seguro, se o valor atual não for nem o original nem o aplicado, nada é escrito e o resultado é `Conflict`;
  a UI pergunta e, se o usuário confirmar, chama com `RevertMode.Force`. Valor atual igual ao original conta como já revertido.
- **Extras:** `DescribeChanges()` (modo simulação/dry-run, lacuna 3 da seção 10.2) e `GetAdvice(SystemProfile)`.

## D-014 — Journal de intenção (R13) desde a Fase 2

- **Fase:** 2
- **Decisão:** `BeginOperation` é persistido antes de qualquer escrita, e o original é persistido antes do primeiro valor.
  Operações sem `CompleteOperation` aparecem em `IncompleteOperations` para reconciliação no próximo início.

## D-015 — Ponto de restauração

- **Fase:** 2
- **Decisão:** `Checkpoint-Computer` via `System32\WindowsPowerShell\v1.0\powershell.exe` com comando fixo (sem entrada do usuário).
  Saída contendo "1440" com código 0 = limite de 24 h (V14); código ≠ 0 = falha (provável Proteção do Sistema desligada, V15).
  Nenhum dos dois bloqueia a aplicação: o backup JSON continua valendo (R5).

## D-016 — `startup-delay` grava também `WaitForIdleState=0`

- **Fase:** 3
- **Contexto:** em builds recentes do Windows 11 o Explorer passou a esperar o sistema ficar ocioso; `StartupDelayInMSec=0`
  sozinho deixou de ter efeito (Microsoft Q&A, ver `docs/TWEAKS.md`).
- **Decisão:** o ajuste controla os dois valores na mesma chave `Serialize`; ambos são revertidos para o estado original.

## D-017 — `power-high` e `flip-model-upgrade` nascem na Fase 4

- **Fase:** 3
- **Contexto:** lacuna 6 da seção 10.2.
- **Decisão:** padronizado: ambos são implementados na Fase 4.

## D-018 — Prioridade estática e "impacto estimado"

- **Fase:** 3
- **Contexto:** lacuna 4 da seção 10.2 (impacto nunca definido).
- **Decisão:** cada ajuste tem uma prioridade fixa (tabela em `docs/TWEAKS.md`), sem prometer ganho percentual.
  Ordenação: prioridade desc, depois nome.

## D-019 — ViewModels em `WinTune.Presentation` (net8.0)

- **Fase:** 6
- **Decisão:** ViewModels (CommunityToolkit.Mvvm 8.2.2) ficam numa biblioteca sem WPF, testada por `WinTune.Presentation.Tests`
  sem janela. `WinTune.App` contém só XAML, tema, diálogos e composição. `WinTune.App.Tests` carrega a XAML real numa thread
  STA e falha em qualquer erro de binding.

## D-020 — App inteiro elevado (por enquanto) em vez de UI + helper elevado

- **Fase:** 6
- **Contexto:** a seção 2 prefere UI sem elevação + helper elevado de vida curta (R3).
- **Decisão:** manter o app inteiro com `requireAdministrator` nesta versão.
- **Motivo:** um helper elevado exige um canal local autenticado (named pipe com ACL e verificação do processo cliente), que é
  uma superfície de ataque própria e precisa de testes específicos; para um app pessoal de uso pontual, o risco do canal mal
  feito é maior que o benefício. Mitigações: o app não abre portas, não faz rede, só executa binários de System32 com
  argumentos validados, restaura apenas a allowlist em código e o modo jogo automático é opt-in.
- **Revisar:** antes de distribuir para terceiros.

## D-021 — Textos da UI em `Resources/Strings.xaml`

- **Fase:** 6
- **Contexto:** lacuna 1 sugere `Resources.resx`. A geração de classe tipada de `.resx` não roda no `dotnet build` puro
  (depende do Visual Studio).
- **Decisão:** dicionário XAML com `sys:String` (pt-BR), referenciado por `DynamicResource`. Para outro idioma, basta um
  dicionário com as mesmas chaves. Mensagens geradas em código (Core/ViewModels) já estão em pt-BR.

## D-022 — `game-gpu-pref-high` e `game-fso-compat` viram guias (advisory)

- **Fase:** 6
- **Contexto:** regra 5 da seção 0. O significado de `GpuPreference=N` não tem tabela oficial (V9) e o valor de
  `AppCompatFlags\Layers` só tem fonte de comunidade (V18).
- **Decisão:** os dois aparecem na aba Jogos como orientação textual; o WinTune não grava esses valores.

## D-023 — Detecção de jogo e anti-cheat

- **Fase:** 6
- **Decisão:** a lista de processos vem do snapshot do sistema (`Process.GetProcesses`, só o nome); nenhum handle é aberto para o
  processo do jogo. As verificações da aba Jogos leem só arquivos da Steam (`libraryfolders.vdf`, `appmanifest_*.acf`) e o registro
  do Ubisoft Connect; nunca leem nem alteram arquivos de instalação além de confirmar que o executável existe.

## D-024 — Telemetria só em modo usuário; LibreHardwareMonitorLib não incluída

- **Fase:** 7
- **Contexto:** R1, V19-V22. A LibreHardwareMonitorLib usa o driver de kernel PawnIO (substituto do WinRing0) para vários
  sensores; a compatibilidade com BattlEye/VAC não foi confirmada. O item (3) da Fase 7 manda não incluir o provedor se não
  for possível garantir as regras (1)-(2).
- **Decisão:** não incluir a biblioteca. `UserModeSensors` usa:
  - **PDH** com `PdhAddEnglishCounter` (nomes em inglês, funciona em Windows pt-BR): `% Processor Utility` total e por thread,
    `% Processor Performance` × clock base (clock efetivo), `Memory\Available MBytes`, `Memory\Pages/sec`,
    `PhysicalDisk(_Total)\% Idle Time`, `GPU Engine(*engtype_3D)\Utilization Percentage` (fallback de GPU);
  - **NVML** (`System32\nvml.dll`, instalada pelo driver NVIDIA): uso, temperatura, clock e VRAM da GPU;
  - **WMI** `MSAcpi_ThermalZoneTemperature`: temperatura **aproximada** (zona térmica ACPI, não é o sensor do núcleo).
- **Consequências:** o "modo seguro para anti-cheat" vale sempre (não há modo com driver). Temperatura real do núcleo da CPU
  fica indisponível; a detecção de throttling da CPU usa a queda do clock efetivo sob carga.

## D-025 — Overhead da amostragem

- **Fase:** 7
- **Medição:** `SensorIntegrationTests.Sampling_At1Hz_UsesLessThanOnePercentCpu`, 20 amostras a 1 Hz em um Ryzen 5 PRO 230
  (12 threads): **0,076% da CPU da máquina** (0,92% de um núcleo). Critério da Fase 7 (< 1%) atendido.
- **Nota:** a leitura WMI da zona térmica ocorre a cada 5 amostras para reduzir custo.

## D-026 — Consumidores em segundo plano via WMI de desempenho

- **Fase:** 7
- **Decisão:** `Win32_PerfFormattedData_PerfProc_Process` (dados de desempenho do sistema; o WinTune não abre handles para os
  processos). Processos dos jogos são removidos pela comparação de nome antes de qualquer análise. Nunca finaliza processos.
