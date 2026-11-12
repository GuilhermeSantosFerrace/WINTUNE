# Changelog

Todas as mudanças relevantes deste projeto são documentadas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e o projeto adota [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Unreleased]

### Adicionado

- Foco **Jogos + live (streaming)**: herda os ajustes de Jogos, adiciona o cenário "Quedas de quadros na live" e a aba **Live**
  (Wi-Fi × cabo, espaço para gravar, codificador NVENC/AMF/Quick Sync, checklist do OBS, como medir). Telemetria passa a mostrar o
  uso do codificador de vídeo da GPU e avisa quando ele satura.

- `docs/DEVELOPMENT.md`: guia de pré-requisitos, execução local, comandos de build/teste/publicação, dados do app, editor,
  regras de qualidade, CI/release e problemas comuns.
- `build.ps1`: alvos `integration`, `format` e `coverage`.

### Corrigido

- `build.ps1 run` agora compila e abre o app pelo shell (mostra o UAC); antes usava `dotnet run`, que falhava com erro 740
  em terminal sem elevação.

### Pendente para 1.0.0

- Executar o roteiro manual em VM Windows 11 (`docs/SAFETY.md`, seção 5): instalação limpa, recuperação sem o app,
  queda no meio de "Aplicar", inicialização conferida no Gerenciador de Tarefas, desinstalação sem resíduos.

## [1.0.0-rc.1] - 2026-11-10

### Adicionado

- Fase 15: publicação single-file self-contained (win-x64, comprimida), instalador Inno Setup com "Desfazer tudo" antes de
  desinstalar, modo `WinTune.exe --revert-all [--quiet]`, workflow de release por tag (instalador, portátil e `SHA256SUMS.txt`),
  README final (instalação, uso, recuperação, FAQ).
- Fase 0: estrutura da solução (`WinTune.Core`, `WinTune.Platform`, `WinTune.App`, projetos de teste).
- `global.json` fixando o SDK .NET 8.0.428; `Directory.Build.props` com `Nullable`, `LangVersion=12` e warnings como erro.
- `Directory.Packages.props` com versões exatas de dependências.
- Script `build.ps1` com alvos `build`, `test`, `run`, `publish` e `clean`.
- Workflow de CI no GitHub Actions (`windows-latest`): restore, build e testes.
- `docs/DECISIONS.md` com as decisões tomadas na Fase 0.
- Fase 1: abstrações de plataforma (`IRegistry`, `IProcessRunner`, `IWmiReader`, `IServiceControl`, `IFileSystem`,
  `IClock`, `IEnvironmentInfo`, `IProcessList`, `IShellLauncher`) e implementações reais para Windows.
- Fase 1: fakes em memória (`tests/WinTune.TestDoubles`) e testes de arquitetura (sem acesso direto ao Windows fora
  de Platform, sem APIs de rede).
- Fase 2: domínio (`Focus`, `Scenario`, `GameTarget`, `Risk`, `Severity`, `Finding`, `TweakResult`), contrato `Tweak`,
  `BackupStore` (escrita atômica, schema 1, journal, intenções de operação, arquivo corrompido movido para `.corrupt`),
  `RestorePointService` e `RegistryTweak` (rollback parcial, reversão segura contra drift, allowlist, modo simulação).
- Fase 3: catálogo v1 (`game-mode`, `hags`, `gamedvr-off`, `startup-delay`, `transparency-off`), `RecommendationEngine`
  (foco × cenários × jogos, prioridade estática, "já aplicado") e `docs/TWEAKS.md` com fonte e rollback manual de cada ajuste.
- Fase 4: `power-high` (`PowerPlanTweak`: parsing por GUID em qualquer idioma, fallback `/duplicatescheme`, detecção do plano
  AMD Ryzen Balanced, reversão exata com allowlist e detecção de drift) e `flip-model-upgrade` (preserva as demais flags
  de `DirectXUserGlobalSettings`).
- Fase 5: `Analyzer` com regras isoladas (`IDiagnosticRule`): `ram-below-rated`, `ram-single-channel`, `ram-capacity-low`,
  `rebar-unknown`, `vbs-active`, `chipset-driver-missing`, `fast-startup`, `disk-low-space`, `vcache-note`, `gpu-driver-old`;
  `SystemProfileDetector`; testes de integração somente leitura com guardas que proíbem escrita.
- Fase 6: GUI MVP em WPF/MVVM (foco, jogos, cenários, recomendações, diagnóstico, aba Jogos com verificações e guias da
  seção 4.4, registro de operações), confirmação com prévia exata, ponto de restauração, Desfazer tudo com tratamento de
  conflito, cancelamento, tema claro/escuro/alto contraste, instância única, log rotativo em `%LOCALAPPDATA%\WinTune\logs`,
  manifest `requireAdministrator`; `docs/UX.md`.
- Fase 7: telemetria ao vivo somente em modo usuário (PDH em inglês, NVML, WMI), `Sampler` 1 Hz com buffer circular e
  sessões rotuladas por jogo, `BottleneckClassifier` (GPU, CPU 1 thread, CPU total, RAM/paginação, disco, `thermal-throttle`),
  consumidores em segundo plano (`game-background-load`), aba Telemetria com gráficos leves (Polyline). Overhead medido: 0,076%.
- Fase 8: gerenciador de inicialização (Run/RunOnce HKCU/HKLM/32 bits, pastas Inicializar, tarefas ao logon via Agendador),
  editor/assinante, impacto estimado, itens protegidos; desativação reversível via `StartupApproved` atrás de opção
  experimental desligada por padrão; integração com "Desfazer tudo".
- Fase 9: limpeza segura (temporários > 24 h, miniaturas, relatórios WER, Otimização de Entrega, caches de shader opcionais)
  com prévia obrigatória, allowlist de raízes, proteção contra links/path traversal, arquivos em uso ignorados e relatório.
- Fase 10: benchmark antes/depois por jogo: rodadas a partir das sessões de telemetria, importação opcional de CSV do PresentMon
  (1.x/2.x), FPS médio/1% low/desvio do frametime, comparador com descarte da 1ª rodada, mínimo de 3 rodadas e teste de
  relevância; sessões em `%LOCALAPPDATA%\WinTune\sessions`.
- Fase 11: auditoria de serviços (estado, inicialização, fabricante, CPU/RAM do processo) e de tarefas de atualizadores;
  allowlist de atualizadores de terceiros para Automático → Manual e desabilitar tarefas, reversível, sem parar serviços.
- Fase 12: perfis salvos em `%APPDATA%\WinTune\profiles`, `games.json` editável e validado, modo jogo automático opt-in por
  jogo (detecção só pelo nome do processo, aplica ao abrir e restaura ao fechar, estado pendente após queda), ícone na bandeja.
- Fase 13: relatório exportável em Markdown e JSON (hardware, diagnóstico, ajustes ativos, benchmark, histórico, versão),
  anonimizado (sem usuário, computador, números de série ou chaves); aba Histórico (journal) e botão "Abrir pasta de logs".
- Fase 14: analisadores `Recommended` como erro em `src/`, `dotnet format` verificado no CI, cobertura mínima de 80% no Core
  (91,1%), `Reconciler` para operações interrompidas (R13) com teste de queda no meio de `Apply`, `docs/SAFETY.md`
  (política, superfície revisada, recuperação manual, roteiro em VM), `docs/ARCHITECTURE.md` e `THIRD_PARTY_NOTICES.md`.
