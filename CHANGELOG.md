# Changelog

Todas as mudanças relevantes deste projeto são documentadas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e o projeto adota [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Unreleased]

### Adicionado

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
