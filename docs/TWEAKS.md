# Catálogo de ajustes (WinTune)

Fonte única de verdade em código: `src/WinTune.Core/Tweaks/TweakCatalog.cs`. Um teste (`CatalogTests`) garante que todo
ajuste do catálogo tem uma seção aqui com **Fonte** e **Reverter sem o app**.

Convenções:

- **HKCU** = `HKEY_CURRENT_USER`, **HKLM** = `HKEY_LOCAL_MACHINE`.
- "Padrão do Windows" descreve o comportamento típico de uma instalação limpa; o valor pode estar **ausente** (o Windows usa
  o padrão interno). O WinTune guarda o estado exato encontrado, inclusive "ausente", e restaura exatamente isso.
- Comandos de recuperação manual devem ser executados em um terminal **como administrador** quando a chave for HKLM.
- Prioridade: tabela estática usada para ordenar a lista (lacuna 4 da seção 10.2). Maior = aparece antes.

## Tabela de prioridade

| ID | Prioridade | Marcado por padrão |
|----|-----------:|:------------------:|
| `gamedvr-off` | 70 | sim |
| `game-mode` | 60 | sim |
| `power-high` | 55 | sim (não, se houver plano AMD Ryzen Balanced) |
| `startup-delay` | 50 | sim |
| `hags` | 40 | não (experimental) |
| `flip-model-upgrade` | 30 | não (aplicabilidade restrita) |
| `transparency-off` | 20 | não (cosmético) |

---

### `game-mode`

- **O que faz:** liga o Modo de Jogo do Windows.
- **Chaves:** HKCU `Software\Microsoft\GameBar` → `AutoGameModeEnabled` (DWORD) = `1`; `AllowAutoGameMode` (DWORD) = `1`.
- **Padrão do Windows:** ligado (valores geralmente ausentes ou `1`). Valor **ausente conta como já aplicado** (é o padrão
  ligado); o app só escreve se encontrar `0`. É comum o ajuste aparecer como "já aplicado" (R8).
- **Foco / cenários:** Jogos / Travadas, FPS baixo.
- **Risco:** seguro. **Reinício:** não.
- **Fonte:** Microsoft Q&A, [Game Mode & Game Bar won't turn off](https://learn.microsoft.com/en-us/answers/questions/4108270/game-mode-game-bar-wont-turn-off)
  (valor `AutoGameModeEnabled`); Eleven Forum, [Turn On or Off Game Mode in Windows 11](https://www.elevenforum.com/t/turn-on-or-off-game-mode-in-windows-11.1447/). Confiança: comunidade.
- **Reverter sem o app:** Configurações > Jogos > Modo de Jogo, ou
  `reg add "HKCU\Software\Microsoft\GameBar" /v AutoGameModeEnabled /t REG_DWORD /d 0 /f` (use o valor original do backup).

### `hags`

- **O que faz:** liga o agendamento de GPU acelerado por hardware.
- **Chave:** HKLM `SYSTEM\CurrentControlSet\Control\GraphicsDrivers` → `HwSchMode` (DWORD) = `2` (`1` = desligado).
- **Padrão do Windows:** depende de GPU/driver; em muitas instalações com RTX 40 já vem `2`. Se já estiver ativo, o app apenas informa.
- **Foco / cenários:** Jogos / Travadas, FPS baixo. **Experimental:** desmarcado; sugerir A/B no benchmark (Fase 10).
  A justificativa de Frame Generation **não** é usada: não se aplica a Siege nem a CS2 e não foi confirmada em fonte oficial (V13).
- **Risco:** exige reinício.
- **Fonte:** Microsoft Q&A, [Where did hardware accelerated GPU scheduling go](https://learn.microsoft.com/en-us/answers/questions/4358192/where-did-hardware-accellerated-gpu-scheduling-go) (V12). Confiança: comunidade.
- **Reverter sem o app:** Configurações > Sistema > Tela > Elementos gráficos > Configurações padrão, ou
  `reg add "HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" /v HwSchMode /t REG_DWORD /d 1 /f` e reiniciar
  (se o valor não existia antes: `reg delete "HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" /v HwSchMode /f`).

### `gamedvr-off`

- **O que faz:** desliga a gravação contínua em segundo plano da Xbox Game Bar.
- **Chaves:** HKCU `System\GameConfigStore` → `GameDVR_Enabled` (DWORD) = `0`;
  HKCU `Software\Microsoft\Windows\CurrentVersion\GameDVR` → `AppCaptureEnabled` (DWORD) = `0`.
- **Padrão do Windows:** varia; muitas vezes já desligado (R8).
- **Foco / cenários:** Jogos / Travadas, FPS baixo.
- **Risco:** seguro. **Reinício:** não.
- **Fonte:** Eleven Forum, [Enable or Disable Record What Happened for Gaming Captures](https://www.elevenforum.com/t/enable-or-disable-record-what-happened-for-gaming-in-windows-11.17900/)
  e [Game Mode Issues](https://www.elevenforum.com/t/game-mode-issues.45413/post-721246) (ambos os valores). Confiança: comunidade.
- **Reverter sem o app:**
  `reg add "HKCU\System\GameConfigStore" /v GameDVR_Enabled /t REG_DWORD /d 1 /f` e
  `reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR" /v AppCaptureEnabled /t REG_DWORD /d 1 /f`
  (ou `reg delete ... /v <nome> /f` se o valor não existia).

### `startup-delay`

- **O que faz:** remove o atraso que o Explorer aplica antes de abrir os apps de inicialização.
- **Chaves:** HKCU `Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize` → `StartupDelayInMSec` (DWORD) = `0` e
  `WaitForIdleState` (DWORD) = `0`. A chave `Serialize` é criada se não existir.
  O segundo valor é necessário em builds recentes do Windows 11, nos quais o primeiro sozinho deixou de funcionar.
- **Padrão do Windows:** valores ausentes (atraso de cerca de 10 s e espera por ociosidade).
- **Foco / cenários:** Os dois / Boot lento.
- **Risco:** seguro. **Reinício:** não (vale no próximo login).
- **Fonte:** Microsoft Q&A, [Startup Apps slow after KB5064080 update](https://learn.microsoft.com/en-us/answers/questions/5546818/startup-apps-slow-after-kb5064080-update)
  e [Startup apps artificially delayed on Windows 11?](https://learn.microsoft.com/en-us/answers/questions/4059183/startup-apps-artificially-delayed-on-windows-11);
  Eleven Forum, [Enable or Disable Delay of Running Startup Apps](https://www.elevenforum.com/t/enable-or-disable-delay-of-running-startup-apps-in-windows-11.9144/). Confiança: comunidade.
- **Reverter sem o app:**
  `reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize" /v StartupDelayInMSec /f` e
  `reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize" /v WaitForIdleState /f`.

### `transparency-off`

- **O que faz:** desliga os efeitos de transparência.
- **Chave:** HKCU `Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` → `EnableTransparency` (DWORD) = `0`.
- **Padrão do Windows:** `1` (ligado).
- **Foco / cenários:** Trabalho / Lentidão com muitos apps. **Cosmético:** desmarcado por padrão. É a única mudança visual
  permitida pelo catálogo (lacuna 8 da seção 10.2).
- **Risco:** cosmético. **Reinício:** não.
- **Fonte:** Eleven Forum, [Enable or Disable Transparency Effects in Windows 11](https://www.elevenforum.com/t/enable-or-disable-transparency-effects-in-windows-11.1110/). Confiança: comunidade.
- **Reverter sem o app:** Configurações > Personalização > Cores > Efeitos de transparência, ou
  `reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" /v EnableTransparency /t REG_DWORD /d 1 /f`.

### `power-high`

- **O que faz:** ativa o plano de energia Alto desempenho.
- **Comando:** `powercfg /getactivescheme` (lê o GUID ativo por regex, independente do idioma) e
  `powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`. Se o plano não existir (comum no Windows 11),
  `powercfg /duplicatescheme 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c` cria uma cópia, que é ativada e, ao desfazer, apagada.
- **Padrão do Windows:** plano Equilibrado (`381b4222-f694-41f0-9685-ff5bb260df2e`).
- **Antes de oferecer:** se `powercfg /list` mostrar um plano com "Ryzen" no nome (plano "AMD Ryzen Balanced" do driver de
  chipset), o ajuste vem desmarcado com explicação: o ganho tende a ser menor.
- **Foco / cenários:** Jogos / Travadas, FPS baixo.
- **Risco:** seguro (aumenta consumo em repouso). **Reinício:** não.
- **Backup:** GUID anterior, GUID aplicado e GUID criado (se houver). Ao reverter, o GUID anterior só é restaurado se o
  `powercfg /list` o reconhecer (allowlist); se o plano ativo foi trocado depois, há conflito e nada é alterado sem confirmação.
- **Fonte:** Microsoft Learn, [Powercfg command-line options](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options)
  (`/setactive`, `/duplicatescheme`, `/list`, `/getactivescheme`, `/delete`). Confiança: oficial.
- **Reverter sem o app:** `powercfg /list` para ver os planos e `powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e`
  (Equilibrado) ou o GUID anterior anotado no backup (`%ProgramData%\WinTune\backup.json`, chave `previousScheme`).

### `flip-model-upgrade`

- **O que faz:** liga "Otimizações para jogos em janela" (flip model) para jogos DirectX 10/11 em janela ou janela sem borda.
- **Chave:** HKCU `Software\Microsoft\DirectX\UserGpuPreferences` → `DirectXUserGlobalSettings` (**REG_SZ**), adicionando ou
  trocando a flag `SwapEffectUpgradeEnable=1;`. Outras flags da mesma string (`HighPerfAdapter=...;VRROptimizeEnable=...;AutoHDREnable=...;`)
  são **preservadas** na aplicação e na reversão; ao desfazer, só a nossa flag volta ao valor original (ou é removida).
  Se o valor existir com outro tipo, nada é alterado.
- **Padrão do Windows:** depende da build; valor geralmente ausente.
- **Aplicabilidade:** não afeta tela cheia exclusiva nem DirectX 12. Siege (DX12): praticamente não se aplica.
  CS2: só se jogar em janela sem borda. Desmarcado por padrão.
- **Risco:** seguro. **Reinício:** não (reabrir o jogo).
- **Fonte:** Microsoft DevBlog, [Updates in Graphics and Gaming](https://devblogs.microsoft.com/directx/updates-in-graphics-and-gaming/)
  e Suporte Microsoft, [Otimizações para jogos em janela](https://support.microsoft.com/de-de/windows/hardware/display-graphics/optimizations-for-windowed-games-in-windows-11) (V11, oficial);
  formato da string: Eleven Forum (V10, comunidade).
- **Reverter sem o app:** Configurações > Sistema > Tela > Elementos gráficos > Alterar configurações gráficas padrão >
  Otimizações para jogos em janela, ou editar a string no `regedit` removendo `SwapEffectUpgradeEnable=1;`.
