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

## D-027 — Inicialização: listagem por padrão; desativar é experimental e desligado

- **Fase:** 8
- **Contexto:** a Fase 8 manda implementar só a listagem se o formato binário de `StartupApproved` não for confirmado em
  documentação. V17: só há fontes de comunidade (`02` = habilitado, `03` = desabilitado).
- **Decisão:** a listagem é o comportamento padrão. A desativação (`03 00 00 00` + FILETIME) está implementada e testada com
  registro fake, mas fica atrás da opção **"Permitir desativar (experimental)"**, desligada a cada abertura do app. O usuário deve
  conferir no Gerenciador de Tarefas. Valores com primeiro byte diferente de `02`/`03` aparecem como "Desconhecido" e não são alterados.
- **Reativar:** só para itens que o próprio WinTune desativou (há backup). A restauração volta ao binário original exato
  (ou apaga o valor se ele não existia).
- **Proteção:** bloqueados itens com editor/assinante Microsoft, componentes de segurança do Windows, fabricantes de
  segurança (antivírus e agentes corporativos como Cisco, CrowdStrike, Zscaler...), fabricantes de hardware/drivers,
  RunOnce e tarefas agendadas (somente leitura nesta tela). O assinante é lido do certificado embutido sem validar a cadeia;
  isso só pode tornar o bloqueio mais restritivo, nunca liberar algo.
- **Allowlist na reversão:** o ID é um hash de (origem, nome); a reconstrução procura apenas nas 5 chaves `StartupApproved`
  fixas em código e em nomes que existem no registro real.
- **Pendente (manual):** validar no Windows 11 real que desativar/reativar gera o mesmo estado exibido pelo Gerenciador de Tarefas.

## D-028 — Limpeza: allowlist fixa, prévia obrigatória, sem Lixeira

- **Fase:** 9
- **Decisão:**
  - Raízes calculadas só a partir de pastas conhecidas (`%TEMP%`, `%LOCALAPPDATA%`, `%ProgramData%`, `%SystemRoot%`), nunca de
    entrada do usuário. Cada arquivo tem o caminho real resolvido (`GetFinalPathNameByHandle`) e precisa estar sob a raiz real.
  - Links simbólicos/junções: arquivos que são links são ignorados; diretórios que são links não são percorridos.
  - Arquivos em uso (não abrem com acesso exclusivo) são ignorados; `%TEMP%` só apaga arquivos com mais de 24 h.
  - `Execute` só aceita uma `CleanupPlan` criada pelo mesmo serviço (`Preview`) e revalida cada arquivo antes de apagar.
  - **Lixeira não é usada:** as categorias são caches e temporários regeneráveis (não são documentos do usuário); enviar para
    a Lixeira só adiaria a liberação de espaço. A UI diz explicitamente que a limpeza é irreversível e não entra em "Desfazer tudo".
  - Cache do Otimização de Entrega e caches de shader vêm desmarcados; shader com aviso de recompilação.
- **Medição real (somente prévia, sem apagar):** 3.138 arquivos / 1,1 GB candidatos em `%TEMP%`; 196 em uso e 34 recentes ignorados.

## D-029 — Benchmark: método estatístico e fonte de frametime

- **Fase:** 10
- **Fonte de frametime:** somente CSV exportado do PresentMon (ferramenta externa, opcional), colunas `MsBetweenPresents` (1.x)
  ou `FrameTime` (2.x), filtrando pela coluna `Application` com o executável do jogo. O WinTune não captura quadros.
- **Métricas:** FPS médio = 1000 / frametime médio; 1% low = 1000 / percentil 99 do frametime; desvio padrão do frametime.
  Telemetria: temperatura máx. da GPU, RAM máx., paginação média, uso médio da GPU.
- **Rodadas (R7):** mínimo de 3 por condição; a 1ª de cada condição é descartada (shader/cache).
- **Relevância:** uma diferença só conta se (a) a variação relativa ≥ limiar configurável (padrão 3%) **e** (b) a diferença das médias
  for maior que 2 erros padrão combinados (`2·√(s²a/na + s²b/nb)`). Caso contrário: "sem diferença relevante".
- **Sem frametime:** só telemetria; o veredito geral fica "dados insuficientes" (nenhum FPS é estimado).
- **Jogos:** o relatório nunca mistura jogos; rodadas de jogos diferentes geram aviso.
- **Persistência:** `%LOCALAPPDATA%\WinTune\sessions\*.json` (schema 1, até 10 MB; arquivos inválidos são ignorados e registrados).

## D-030 — Allowlist de serviços e tarefas de terceiros

- **Fase:** 11
- **Serviços (nomes exatos):** `AdobeARMservice`, `gupdate`, `gupdatem`, `brave`, `bravem`, `MozillaMaintenance`,
  `LGHUBUpdaterService`. Só são alteráveis se existirem, estiverem em Automático e o executável **não** for da Microsoft nem
  estiver sob `%SystemRoot%`. A lista é propositalmente curta (regra 5: não inventar nomes); confirmar na máquina real e
  ampliar com cuidado.
- **Tarefas (prefixo do caminho, autor não-Microsoft):** `\GoogleUpdateTaskMachine*`, `\Adobe Acrobat Update Task*`,
  `\BraveSoftwareUpdateTaskMachine*`. Só habilitar/desabilitar; nunca apagar.
- **Mudança:** apenas Automático → Manual (via `ChangeServiceConfig`); o serviço nunca é parado. A reversão restaura o tipo
  exato (inclusive "Automático (atraso)") e só aceita, do backup, o valor "Automático"; drift = conflito.
- **IDs:** `service-manual:<nome>` (reconstrução só aceita nomes da allowlist em código) e `task-disable:<hash do caminho>`
  (reconstrução só entre tarefas existentes que casam com a allowlist).
- **Verificação real (somente leitura):** 323 serviços auditados; nenhum serviço da Microsoft aparece como alterável.

## D-031 — Modo jogo automático e `games.json`

- **Fase:** 12
- **`games.json`** (`%APPDATA%\WinTune\games.json`): criado com os padrões na primeira execução; validado ao carregar (schema 1,
  até 256 KB, só `RainbowSixSiege`/`Cs2`, nomes de processo `^[A-Za-z0-9_.\- ]{1,64}$`, até 10 por jogo, executável relativo
  sem `..`). Arquivo inválido **nunca é sobrescrito**: o app usa os padrões e avisa. O usuário pode, por exemplo, adicionar
  `RainbowSix_BE.exe` depois de conferir na instalação (V4).
- **Detecção:** só pela lista de nomes de processo (snapshot do sistema). Nada é lido do processo do jogo.
- **Ao abrir o jogo:** grava a intenção (`gamemode-state.json`) antes de agir, aplica os ajustes do perfil do jogo (padrão:
  `power-high`, o `game-profile-power` da seção 4.4) usando os mesmos `Tweak` e o mesmo backup/journal, e registra quais aplicou.
  Ajustes que o usuário já aplicou manualmente (já têm backup) não são tocados.
- **Ao fechar o jogo, trocar de jogo ou desligar o modo:** reverte só o que o modo jogo aplicou (modo seguro: drift = conflito, nada é sobrescrito).
- **Queda:** se o arquivo de estado existir ao abrir o app, o usuário é perguntado se quer restaurar.
- **Opt-in:** desligado por padrão. O monitoramento roda enquanto o app está aberto; com o modo ligado, aparece o ícone na
  bandeja (abrir / desligar modo jogo) e minimizar esconde a janela na bandeja.
- **WinForms** é usado só para o `NotifyIcon`; os usings implícitos de WinForms/Drawing foram removidos para evitar ambiguidade,
  e o aviso WFAC010 (DPI no manifest, específico de WinForms) foi suprimido no projeto do App.

## D-032 — Analisadores e formatação (Fase 14)

- **Decisão:** `AnalysisMode=Recommended` para os projetos de `src/` (warnings como erro). Corrigidos: CA1001 (ViewModels e App
  descartáveis), CA1806 (códigos de retorno do PDH), CA1838 (buffer `char[]` no P/Invoke), CA1822 (onde fazia sentido), CA1720
  (renomeado `PowerScheme.SchemeGuid`).
- **Supressões com justificativa:** CA1716 em `Tweak.For` (nome do contrato da seção 3.3); CA1720 em `RegistryValueKind.String`
  (espelha REG_SZ); CA1822 em `RecommendationEngine.Recommend` e `BottleneckClassifier.Classify` (API de instância mantida);
  CA1826/CA1859/CA1848 desligados no `.editorconfig` (micro-otimizações sem efeito nesta escala).
- **Projetos de teste:** conjunto padrão de analisadores (o `Recommended` exige, por exemplo, nomes sem `_`, incompatível com a
  convenção de nomes de teste).
- **CI:** `dotnet format --verify-no-changes`, build com analisadores como erro, testes e **cobertura mínima de 80%** no Core
  (medida: 91,1% de linhas, 77,8% de ramos; código gerado por source generators excluído via `coverage.runsettings`).

## D-033 — Reconciliação de operações interrompidas

- **Fase:** 14
- **Decisão:** `Reconciler` roda na abertura do app (com confirmação). Para cada intenção sem conclusão: se não há backup, nada
  foi escrito (o original é salvo antes da 1ª escrita) e a intenção é encerrada; se há backup, o ajuste é revertido no modo
  seguro (valores ainda no original são ignorados; valores no "aplicado" voltam ao original). Testado simulando a morte do
  processo depois da 1ª escrita (`ResilienceTests`). O teste com processo real (matar `WinTune.exe`) está no roteiro manual
  de `docs/SAFETY.md`.

## D-034 — Rede

- **Fase:** 14
- **Decisão:** nenhuma API de rede no código (teste `ArchitectureTests.NoNetworkApis`). A verificação de atualização opt-in
  (Fase 15) **não** foi implementada; a verificação por firewall/Monitor de Recursos está no roteiro manual.

## D-035 — Empacotamento: Inno Setup, single-file comprimido, sem assinatura

- **Fase:** 15
- **Instalador:** **Inno Setup 6** (não MSIX). Motivo: o app exige `requireAdministrator`, escreve em HKLM/ProgramData e
  usa um desinstalador com lógica própria ("Desfazer tudo" antes de remover); MSIX roda em contêiner com virtualização de
  registro/arquivos e restrições a apps elevados, o que conflita com o propósito do app.
- **Desinstalador:** pergunta se deve rodar `WinTune.exe --revert-all`. Se tudo for revertido, apaga `%ProgramData%\WinTune`;
  se não, mantém o backup para recuperação manual e avisa. Dados pessoais (perfis, sessões, logs) só são apagados com confirmação.
- **Executável:** `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`, com bibliotecas nativas do WPF
  embutidas e compressão (≈ 69 MB). Símbolos embutidos (`DebugType=embedded`) e sem XML docs no pacote.
- **Assinatura:** não há certificado; o SmartScreen avisa na primeira execução (documentado no README). Os releases publicam
  `SHA256SUMS.txt`.
- **CI:** o instalador é compilado a cada push (validação do script). No runner, se o ISCC não existir, instala
  `innosetup` 6.4.3 via Chocolatey (versão fixa). Localmente o Inno Setup não estava instalado; a compilação do script foi
  validada no CI.
- **Verificação de atualização opt-in:** não implementada (o app continua sem nenhuma chamada de rede).

## D-036 — Versão 1.0.0-rc.1 em vez de 1.0.0

- **Fase:** 15
- **Contexto:** o aceite da Fase 15 exige instalação limpa, uso completo e desinstalação sem resíduos numa VM Windows 11, e a
  Fase 14 exige o roteiro manual de recuperação em VM. Isso não pôde ser executado no ambiente de desenvolvimento.
- **Decisão:** publicar a tag `v1.0.0-rc.1` (pré-release). A `v1.0.0` deve ser criada depois de concluir o roteiro de
  `docs/SAFETY.md` (seção 5).
