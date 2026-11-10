# WinTune — Especificação de Montagem (para execução por agente de IA)

> Documento autocontido. Entregue este arquivo inteiro a um agente de IA e peça:
> "Execute este spec fase a fase. Ao fim de cada fase, rode os testes, mostre os critérios de aceite
> atendidos e só então avance."

---

## 0. Como o agente deve trabalhar

1. Execute **uma fase por vez**, na ordem. Não pule fases e não antecipe código de fases futuras.
2. Ao fim de cada fase: `dotnet build`, `dotnet test`, e verifique **todos** os critérios de aceite da fase.
3. Faça um commit por fase (Conventional Commits, ex.: `feat(core): add backup store`).
4. Se algo for ambíguo, escolha a opção **mais conservadora** (menos invasiva) e registre em `docs/DECISIONS.md`.
5. **Nunca** invente caminhos de registro, GUIDs ou APIs. Se não conseguir confirmar em documentação oficial, trate o ajuste como "somente aviso" (advisory), não como ajuste automático.
6. Código em **inglês**. Documentação (README, docstrings/XML docs, este tipo de arquivo) em **português (pt-BR)**.
7. Dependências com **versão exata** (sem `*`, sem ranges).

---

## 1. Visão do produto

**WinTune** é um aplicativo desktop para **Windows 11** que diagnostica gargalos e aplica ajustes
**seguros e reversíveis** para melhorar o desempenho de um PC pessoal, sem descaracterizar o sistema.

### 1.1 Máquina-alvo (usada para calibrar recomendações)

| Item | Valor |
|------|-------|
| CPU | AMD Ryzen 7 5700X3D (8C/16T, Zen 3, 1 CCD, 3D V-Cache) |
| GPU | NVIDIA GeForce RTX 4070 Super |
| RAM | 16 GB (provável gargalo de capacidade e possível EXPO/XMP desligado) |
| SO | Windows 11 |

O app deve funcionar em qualquer PC Windows 11, mas as heurísticas devem ser parametrizadas pelo hardware detectado.

### 1.2 Requisitos do usuário

- O usuário escolhe o **foco**: Trabalho, Jogos ou Os dois.
- O usuário marca os **cenários com problema**: travadas (stutter), FPS baixo, lentidão com muitos apps,
  boot lento, temperatura alta, carregamentos longos.
- Quando o foco inclui Jogos (Jogos ou Os dois), o usuário escolhe **qual(is) jogo(s)** otimizar. Nesta versão há
  exatamente dois alvos, que podem ser marcados juntos: **Rainbow Six Siege** e **Counter-Strike 2 (CS2)**.
  Detalhes na seção 4.3.
- O app recomenda ajustes conforme foco + cenários + jogos; o usuário pode desmarcar qualquer item.
- Otimizar o máximo possível **sem descaracterizar** a máquina (sem remover apps, sem desligar segurança,
  sem alterar aparência de forma agressiva, sem mudar comportamento de periféricos).
- GUI nativa. Tudo reversível.

### 1.3 Princípios (invioláveis)

1. **Reversibilidade total.** Todo ajuste guarda o estado original e pode ser desfeito.
2. **Transparência.** Cada ajuste mostra: o que muda, onde (chave/comando), por que ajuda, risco, se exige reinício.
3. **Diagnóstico antes de mudança.** Gargalos de hardware/BIOS são apontados, não "consertados" com tweaks placebo.
4. **Mínimo privilégio de dano.** Sem desativar Defender, Windows Update, UAC, VBS/HVCI ou telemetria automaticamente.
5. **Honestidade.** Nada de "boost de X%". Medir antes/depois (Fase 10) e mostrar o resultado real.
6. **Idempotência.** Aplicar duas vezes não corrompe o backup; o backup original nunca é sobrescrito por estado já otimizado.

### 1.4 Fora de escopo (não implementar, em nenhuma fase)

- Desativar/alterar Windows Defender, Firewall, UAC, SmartScreen, VBS/HVCI, Secure Boot.
- Desativar o Windows Update ou serviços críticos (Audio, Network, Event Log, etc.).
- Limpeza agressiva de registro ("registry cleaner").
- Alterar aceleração do mouse, DPI, polling ou configurações de periféricos.
- Mexer em timer resolution global, afinidade/prioridade permanente de processos, ou "debloat" que remova apps da Microsoft.
- Overclock, undervolt, alteração de BIOS/UEFI ou de curva de ventoinha.
- **Qualquer interação com o processo ou os arquivos dos jogos que possa acionar anti-cheat** (BattlEye no Siege, VAC no CS2):
  sem injeção de DLL, sem hooks, sem leitura de memória do jogo, sem edição de arquivos de instalação, sem alterar
  afinidade/prioridade do processo do jogo. O app só altera configurações do **Windows** e do **perfil de GPU/DirectX do usuário**,
  e lê o nome do processo (somente existência) para detectar que o jogo está aberto.
- Editar `GameSettings.ini` (Siege) ou `autoexec.cfg`/vídeo do CS2 automaticamente: apenas **recomendações em texto**; o usuário aplica.
- Telemetria própria, coleta de dados do usuário ou chamadas de rede (exceto verificação de atualização opt-in na Fase 13).

---

## 2. Stack e decisões técnicas

| Decisão | Escolha | Justificativa |
|---------|---------|---------------|
| Linguagem | C# 12 / .NET 8 (LTS) | API nativa de registro, WMI, processos, serviços; binário único; ótima integração WPF |
| UI | WPF (XAML) | Nativo Windows, maduro, sem WebView; uso baixo de recursos |
| Padrão de UI | MVVM com `CommunityToolkit.Mvvm` (versão exata) | Testabilidade; evita code-behind pesado |
| WMI | `System.Management` 8.0.0 | Leitura de RAM, VBS, discos |
| Telemetria de hardware | `LibreHardwareMonitorLib` (versão exata; confirmar licença MPL-2.0 e compatibilidade com .NET 8) | Temperatura/uso/clock de CPU e GPU NVIDIA sem driver próprio |
| Serviços Windows | `System.ServiceProcess.ServiceController` | Auditoria de serviços |
| Testes | xUnit + `Microsoft.NET.Test.Sdk` + FluentAssertions (versões exatas) | Padrão do ecossistema |
| Logs | `Microsoft.Extensions.Logging` + arquivo rotativo em `%LOCALAPPDATA%\WinTune\logs` | Auditoria local |
| Empacotamento | `dotnet publish` single-file self-contained + instalador MSIX ou Inno Setup | Ver Fase 13 |
| CI | GitHub Actions `windows-latest` | Build + testes a cada push |

**Elevação:** o app exige administrador (`requireAdministrator` no manifest) porque altera HKLM e planos de energia.
Operações de somente leitura devem funcionar mesmo assim, sem exigir nada além disso.

**Meta de privilégio (Fase 6 em diante):** preferir **UI sem elevação** + um **helper elevado** de vida curta (processo separado,
invocado só para aplicar/reverter, comunicando por canal local autenticado) em vez de manter a janela inteira, a bandeja e o modo jogo
rodando como administrador o tempo todo. Se o agente optar por manter o app inteiro elevado, registrar o motivo em `DECISIONS.md`.

---

## 3. Arquitetura

### 3.1 Camadas

```
┌──────────────────────────────────────────────────────────────┐
│ WinTune.App (WPF)                                              │
│   Views (XAML) ── ViewModels (MVVM) ── Services de UI          │
├──────────────────────────────────────────────────────────────┤
│ WinTune.Core  (sem dependência de UI, 100% testável)           │
│   Domain: Focus, Scenario, Risk, Finding, TweakResult          │
│   Tweaks: Tweak (abstract), RegistryTweak, PowerPlanTweak,     │
│           ServiceTweak, StartupTweak, Catalog                  │
│   Recommendation: RecommendationEngine                         │
│   Backup: BackupStore, RestorePointService                     │
│   Diagnostics: Analyzer (regras), Rule engine                  │
│   Telemetry: Sampler, BottleneckClassifier                     │
│   Benchmark: Session, Comparator                               │
├──────────────────────────────────────────────────────────────┤
│ WinTune.Platform (adaptadores do Windows, atrás de interfaces) │
│   IRegistry, IProcessRunner, IWmiReader, IServiceControl,      │
│   IFileSystem, IHardwareSensors                                │
└──────────────────────────────────────────────────────────────┘
```

**Regra:** `Core` só fala com o Windows através das interfaces de `Platform`. Isso permite testar tudo com
fakes (`FakeRegistry`, `FakeProcessRunner`) em CI, sem tocar no sistema real.

### 3.2 Estrutura de diretórios

```
WinTune/
├── README.md
├── CHANGELOG.md
├── Makefile                      # ou build.ps1 (build, test, run, publish, clean)
├── .gitignore
├── .editorconfig
├── global.json                   # fixa a versão do SDK .NET
├── Directory.Build.props         # Nullable, warnings-as-errors, versão
├── WinTune.sln
├── docs/
│   ├── DECISIONS.md              # decisões e ambiguidades resolvidas
│   ├── TWEAKS.md                 # catálogo gerado/mantido: cada ajuste documentado
│   ├── ARCHITECTURE.md
│   └── SAFETY.md                 # política de segurança e rollback
├── src/
│   ├── WinTune.Core/
│   ├── WinTune.Platform/
│   └── WinTune.App/
├── tests/
│   ├── WinTune.Core.Tests/
│   └── WinTune.Platform.Tests/   # testes de integração marcados [Trait("Category","Integration")]
└── .github/workflows/ci.yml
```

### 3.3 Contrato central: `Tweak`

```csharp
public abstract class Tweak
{
    public abstract string Id { get; }                 // estável, kebab-case
    public abstract string Name { get; }
    public abstract string Description { get; }         // pt-BR, explica o porquê
    public abstract Focus[] For { get; }
    public virtual Scenario Helps => Scenario.None;     // None = vale para qualquer cenário
    public virtual Risk Risk => Risk.Safe;              // Safe | Cosmetic | NeedsReboot
    public abstract bool IsApplied();
    public abstract TweakResult Apply(BackupStore store);
    public abstract bool CanRevert(BackupStore store);
    public abstract TweakResult Revert(BackupStore store);
}
```

Regras do contrato:
- `Apply` **grava o valor original no `BackupStore` antes de alterar** e nunca sobrescreve um backup já existente.
- `Revert` restaura o valor original; se o valor original era "inexistente", **apaga** o valor.
- `IsApplied` compara com o valor desejado lendo o estado real (não confia em cache).
- Falha parcial em `Apply` (tweak com vários valores) deve fazer rollback automático dos valores já gravados.

### 3.4 `BackupStore`

- Arquivo JSON em `%APPDATA%\WinTune\backup.json`, escrita atômica (grava `.tmp` e renomeia).
- Versão de schema (`"schema": 1`) para migrações futuras.
- **Anti-adulteração (obrigatório desde a Fase 2):** o app roda como administrador e o `Revert` escreve no registro a partir deste arquivo.
  Portanto o `Revert` **nunca confia no caminho/nome lidos do JSON**: só restaura entradas cujo (hive, caminho, nome) pertença ao `Catalog`
  do próprio ID do tweak (allowlist em código). Entrada fora da allowlist é ignorada e registrada no journal. Preferir guardar o backup em
  `%ProgramData%\WinTune` com ACL que permita escrita só a Administradores, e/ou assinar o arquivo com HMAC cuja chave é protegida por DPAPI (escopo máquina).
- **Revert seguro contra drift:** antes de restaurar, comparar o valor atual com o valor que o WinTune aplicou. Se for diferente (outro programa ou o usuário mudou depois), **não sobrescrever**: perguntar ao usuário.
- Guarda por `TweakId`: lista de valores originais (hive, caminho, nome, tipo, valor) + extras (ex.: GUID do plano de energia anterior, estado anterior de serviços).
- Mantém histórico de operações (`journal`): timestamp, ação, tweak, resultado.

### 3.5 Motor de recomendação

```
recomendado(t) = (foco == Both || t.For contém foco)
              && (nenhum cenário marcado || t.Helps == None || (t.Helps & cenários) != 0)
```

Extensões (Fase 5+): ordenar por impacto estimado e filtrar ajustes cujo hardware-alvo não existe
(ex.: ajuste NVIDIA em máquina sem GPU NVIDIA).

---

## 4. Catálogo de ajustes (fonte única de verdade)

Cada item abaixo **só pode ser implementado se o agente confirmar o caminho/comando em documentação oficial
ou fonte confiável**; caso contrário, vira "advisory" (somente texto). Documentar cada um em `docs/TWEAKS.md`
com: ID, o que faz, chave/comando exato, valor padrão do Windows, risco, reinício, fonte consultada.

### 4.1 Ajustes automáticos (aplicar/reverter)

| ID | Foco | Cenários | Mecanismo | Observações |
|----|------|----------|-----------|-------------|
| `game-mode` | Jogos | Stutter, LowFps | HKCU `Software\Microsoft\GameBar`: `AutoGameModeEnabled=1`, `AllowAutoGameMode=1` | Seguro |
| `hags` | Jogos | Stutter, LowFps | HKLM `SYSTEM\CurrentControlSet\Control\GraphicsDrivers`: `HwSchMode=2` | Reinício. **Ganho inconsistente entre drivers/jogos** e pode piorar stutter em alguns casos. Já vem ativo em muitas instalações com RTX 40: se `IsApplied()` já for verdadeiro, apenas informar. Classificar como "experimentar e medir" (desmarcado por padrão, sugerir A/B na Fase 10). O argumento de Frame Generation **não se aplica ao Siege nem ao CS2**; verificar a alegação em fonte oficial antes de citá-la na UI |
| `gamedvr-off` | Jogos | Stutter, LowFps | HKCU `System\GameConfigStore`: `GameDVR_Enabled=0`; HKCU `Software\Microsoft\Windows\CurrentVersion\GameDVR`: `AppCaptureEnabled=0` | Mantém gravação manual |
| `power-high` | Jogos | Stutter, LowFps | `powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c` (com fallback `/duplicatescheme`) | **Antes de oferecer**, detectar se o chipset AMD instalou o plano "AMD Ryzen Balanced"; nesse caso explicar que o ganho é menor e deixar desmarcado por padrão |
| `startup-delay` | Ambos | SlowBoot | HKCU `Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize`: `StartupDelayInMSec=0` | Criar a chave se não existir |
| `transparency-off` | Trabalho | SlowMultitasking | HKCU `Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`: `EnableTransparency=0` | Cosmético; desmarcado por padrão |
| `flip-model-upgrade` | Jogos | Stutter, LowFps | HKCU `Software\Microsoft\DirectX\UserGpuPreferences`: valor **string** `DirectXUserGlobalSettings` com `SwapEffectUpgradeEnable=1;` (formato verificado; a string pode conter outras flags como `HighPerfAdapter=...;VRROptimizeEnable=...;AutoHDREnable=...`, que devem ser **preservadas**) | Requer REG_SZ no `RegistryTweak`. **Aplicabilidade restrita:** segundo a Microsoft, afeta jogos DX10/DX11 em janela ou janela sem borda. Não afeta tela cheia exclusiva nem DX12. Para Siege (migrando para DX12) praticamente não se aplica; para CS2 só se o usuário jogar em janela sem borda. Desmarcado por padrão |

### 4.2 Somente aviso (advisory — nunca alterar sozinho)

| ID | Detecção | Mensagem |
|----|----------|----------|
| `ram-below-rated` | WMI `Win32_PhysicalMemory`: `ConfiguredClockSpeed < Speed` | Ativar EXPO/XMP na BIOS; impacto grande em Ryzen 5000 (FCLK/memória) |
| `ram-single-channel` | 1 pente detectado | Usar dual channel |
| `ram-capacity-low` | Total ≤ 16 GB | Possível paginação em jogos modernos + navegador; considerar 32 GB |
| `rebar-unknown` | Não detectável por API | Verificar no painel NVIDIA/GPU-Z; ativar Above 4G Decoding + Resizable BAR na BIOS |
| `vbs-active` | WMI `Win32_DeviceGuard.VirtualizationBasedSecurityStatus == 2` | Pode custar alguns % de FPS; **decisão do usuário**, o app não desativa |
| `chipset-driver-missing` | Ausência de driver de chipset AMD / plano Ryzen Balanced | Instalar driver de chipset oficial da AMD |
| `gpu-driver-old` | Versão do driver NVIDIA via sensores/WMI | Sugerir atualização pelo site oficial (sem baixar nada) |
| `fast-startup` | `HiberbootEnabled=1` | Pode causar comportamento estranho após "desligar"; informar, não mudar |
| `disk-low-space` | Disco do sistema < 15% livre | Afeta SSD (wear leveling/shader cache); sugerir limpeza |
| `thermal-throttle` | Telemetria (Fase 5) | Sugerir revisar airflow/pasta térmica |
| `vcache-note` | Detecta 5700X3D | Informar: CPU de CCD único, **não** precisa do driver "AMD 3D V-Cache Optimizer" (esse é para modelos dual-CCD) |

### 4.3 Perfis por jogo (alvos: Rainbow Six Siege e CS2)

O usuário pode marcar um, outro ou ambos. A seleção filtra ajustes e avisos e define o que a Fase 12 detecta.

**Modelo de domínio**

```csharp
[Flags]
public enum GameTarget { None = 0, RainbowSixSiege = 1, Cs2 = 2 }
```

- `Tweak` ganha `virtual GameTarget Games => GameTarget.None;` (None = vale para qualquer jogo / ajuste geral de Jogos).
- Recomendação passa a ser:

```
recomendado(t) = foco_ok && cenario_ok
              && (foco não inclui Jogos || jogos == None || t.Games == None || (t.Games & jogos) != 0)
```

**Definição dos jogos** (arquivo `games.json`, editável, com schema validado):

| Jogo | Nomes de processo a detectar (confirmar no PC real) | Plataformas | Anti-cheat |
|------|------------------------------------------------------|-------------|------------|
| Rainbow Six Siege | `RainbowSix.exe` (verificado, ver seção 11). `RainbowSix_Vulkan.exe` é **legado** (Vulkan removido em set/2024). `RainbowSix_BE.exe` **não foi confirmado**: não usar sem conferir na instalação | Ubisoft Connect / Steam (Steam App ID 359550, verificado) | BattlEye hoje; a Ubisoft sinalizou anti-cheat próprio em modo kernel (fonte secundária, ver seção 11) |
| Counter-Strike 2 | `cs2.exe` | Steam | VAC |

O agente **deve confirmar** os nomes de executável em fonte oficial ou na instalação real do usuário antes de fixá-los,
e permitir que o usuário edite a lista no `games.json`.

**Perfil de GPU/DirectX por jogo (sem tocar no jogo)**
- Preferência de GPU por aplicativo (Configurações do Windows > Gráficos) é gravada em HKCU
  `Software\Microsoft\DirectX\UserGpuPreferences`, valor **string** cujo nome é o caminho do `.exe` e o dado é
  `GpuPreference=2;` (alto desempenho). Em PC com uma única GPU não traz ganho: oferecer só se houver mais de uma GPU.
  Confirmar formato em documentação oficial antes de implementar.
- `flip-model-upgrade` (seção 4.1) entra como recomendado para ambos os jogos **somente se** o jogo usar DX11/DX12 em modo janela/sem borda;
  descrever no ajuste que não afeta tela cheia exclusiva.

**Recomendações em texto por jogo (advisory; o app não altera arquivos do jogo)**

| Jogo | Aviso | Observação para o agente |
|------|-------|--------------------------|
| Siege | Perfil de CPU: o jogo é sensível a CPU/memória; EXPO/XMP e dual channel têm alto impacto | Hipótese a ser validada com a telemetria (Fase 7); não prometer ganho |
| Siege | API gráfica | **Vulkan foi removido do Siege (set/2024)**; o jogo migrou para DX12 e mantinha DX11 como alternativa, com notícias de que o DX11 sairá. Ler a API realmente disponível no launcher/jogo antes de orientar; sugerir **testar** as opções existentes com o benchmark (Fase 10) |
| Siege | Limite de FPS/VSync dentro do jogo e Reflex (NVIDIA) | Verificar no jogo; registrar como orientação, não como ajuste |
| CS2 | Jogo muito dependente de CPU e cache; o 5700X3D tende a se beneficiar | Hipótese; validar com Fase 7/10 |
| CS2 | Opções de lançamento do Steam e `autoexec.cfg` | **Só listar sugestões e remeter à documentação oficial/comunidade confiável**; não inventar flags; o usuário aplica |
| CS2 | NVIDIA Reflex / limite de FPS no jogo | Orientação textual |
| Ambos | Driver NVIDIA recente, Modo de Jogo ligado, HAGS (testar), Game DVR desligado | Já cobertos pelos ajustes automáticos da seção 4.1 |

**Telemetria e benchmark por jogo**
- Fase 7: rótulo da sessão inclui o jogo detectado (`siege`, `cs2`).
- Fase 10: comparar antes/depois **por jogo**; o relatório nunca mistura jogos diferentes.

**Regra de ouro:** a detecção de jogo é apenas "o processo X existe?" (lista de processos do Windows). Nada além disso.

### 4.4 Catálogo específico por jogo

Premissa honesta: Siege e CS2 são jogos limitados principalmente por **CPU, memória e carga em segundo plano**, não por GPU
(RTX 4070 Super é folgada). Por isso o catálogo por jogo foca em **medir e remover ruído**, e quase nada em "tweak de registro".
Tudo abaixo respeita a seção 1.4 (sem tocar no jogo/anti-cheat). Coluna **Tipo**: `Auto` = aplica/reverte; `Check` = leitura com
aviso; `Guia` = texto para o usuário aplicar no jogo/painel NVIDIA.

| ID | Jogos | Tipo | O que faz | Observações / a confirmar |
|----|-------|------|-----------|---------------------------|
| `game-profile-power` | Siege, CS2 | Auto | Troca para o plano de energia escolhido apenas enquanto o jogo roda e restaura ao fechar (Fase 12) | Reutiliza `PowerPlanTweak`; estado pendente restaurado após crash |
| `game-gpu-pref-high` | Siege, CS2 | Auto | `GpuPreference=2;` para o `.exe` do jogo em `UserGpuPreferences` | **Só oferecer se houver mais de uma GPU**; senão é no-op. Formato do valor a confirmar |
| `game-fso-compat` | Siege, CS2 | Auto, experimental | Flag de compatibilidade por executável (otimizações de tela cheia) em `AppCompatFlags\Layers` (HKCU) | **Desmarcado por padrão.** Benefício controverso; só recomendar se a Fase 10 mostrar ganho. Confirmar valor exato em doc oficial. Grava no registro do usuário, não no jogo |
| `game-install-drive` | Siege, CS2 | Check | Localiza a instalação (Steam: `libraryfolders.vdf`; Ubisoft Connect: chaves de registro) e avisa se está em HDD, SATA lento ou disco quase cheio | App IDs e caminhos a confirmar na máquina real |
| `game-refresh-rate` | Siege, CS2 | Check | Compara a taxa de atualização atual com a máxima do monitor | Causa comum de "FPS baixo": monitor ficou em 60 Hz. Somente aviso (alterar o modo de vídeo é do usuário) |
| `game-background-load` | Siege, CS2 | Check | Lista, durante o jogo, os maiores consumidores de CPU/RAM/disco e overlays conhecidos (Discord, overlay Steam, overlay NVIDIA, Xbox Game Bar, suítes de RGB, gravadores, navegador) | **Nunca finaliza processos.** Só exibe e sugere. Lista de overlays em `games.json`, editável |
| `game-ram-headroom` | Siege, CS2 | Check | Antes/durante o jogo: avisa se a RAM disponível < limiar (padrão 6 GB) e mostra quem consome | Mais relevante com 16 GB |
| `game-shader-cache-state` | Siege, CS2 | Check | Mostra tamanho dos caches de shader (DirectX/NVIDIA) | Recomendar **não apagar** salvo diagnóstico de bug visual; primeiro carregamento fica mais lento ao limpar |
| `game-driver-age` | Siege, CS2 | Check | Idade/versão do driver NVIDIA | Sem download automático |
| `game-guide-siege` | Siege | Guia | Checklist textual: limitador de FPS, V-Sync, NVIDIA Reflex, escala de resolução, **comparar as APIs que o jogo oferecer hoje (DX11/DX12) com o benchmark (Fase 10)** | **Vulkan não existe mais no Siege.** Não afirmar qual API é melhor. Itens/nomes de menu a conferir na versão atual do jogo |
| `game-guide-cs2` | CS2 | Guia | Checklist textual: Reflex, limitador de FPS, modo de janela (tela cheia vs sem borda), opções de MSAA, `autoexec.cfg`, opções de lançamento do Steam | Só listar o que for confirmado em fonte oficial/comunidade confiável; **não inventar flags**; o usuário aplica |
| `game-guide-nvcp` | Siege, CS2 | Guia | Checklist do Painel de Controle NVIDIA por programa (modo de baixa latência, gerenciamento de energia) | App não grava perfil de driver |

Regras:
- `Check` e `Guia` nunca alteram o sistema e aparecem em uma aba "Jogos" com o veredito e a evidência (valor medido).
- `Auto` experimental só é oferecido após o usuário ter ao menos uma sessão de referência (Fase 10) para comparar.

---

## 5. Fases de construção

Legenda: **Entregáveis** = o que deve existir; **Aceite** = como provar que acabou.

---

### Fase 0 — Fundação do repositório

**Objetivo:** esqueleto de projeto, padrões e CI.

**Entregáveis**
- Solução com 3 projetos src e 2 de teste (ver 3.2).
- `global.json` fixando SDK .NET 8; `Directory.Build.props` com `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=12`.
- `.editorconfig`, `.gitignore` (dotnet + `.env`), `README.md` (estrutura do projeto: o que é, pré-requisitos, instalar, rodar, testar).
- `Makefile` ou `build.ps1` com alvos: `build`, `test`, `run`, `publish`, `clean`.
- `CHANGELOG.md` (Keep a Changelog, seção `[Unreleased]`).
- Workflow CI (`.github/workflows/ci.yml`): restore, build, test em `windows-latest`.
- `docs/DECISIONS.md` vazio com cabeçalho.

**Aceite**
- [ ] `dotnet build` e `dotnet test` passam (0 warnings).
- [ ] CI verde.
- [ ] README descreve como rodar e testar.

---

### Fase 1 — Plataforma e abstrações (`WinTune.Platform`)

**Objetivo:** isolar tudo que toca o Windows atrás de interfaces.

**Entregáveis**
- Interfaces: `IRegistry` (Get/Set/Delete para DWord, String; CreateKey), `IProcessRunner` (executa comando, captura stdout/exit code, timeout), `IWmiReader` (consulta tipada), `IServiceControl`, `IFileSystem`, `IClock`.
- Implementações reais (`WindowsRegistry`, `ProcessRunner`, ...).
- Fakes em memória no projeto de testes (`FakeRegistry`, `FakeProcessRunner`).
- `ProcessRunner`: sem shell, argumentos como lista (sem concatenação de string -> sem injeção), timeout obrigatório.

**Aceite**
- [ ] Testes de unidade dos fakes.
- [ ] Nenhum `Registry.*` ou `Process.Start` direto fora de `Platform`.

---

### Fase 2 — Núcleo de domínio e backup (`WinTune.Core`)

**Objetivo:** tipos, `Tweak`, `BackupStore`, `RestorePointService`.

**Entregáveis**
- Enums/records: `Focus`, `Scenario` (flags), `GameTarget` (flags, seção 4.3), `Risk`, `Severity`, `Finding`, `TweakResult` (Success/Failed/Skipped + mensagem).
- `Tweak` abstrato (contrato 3.3).
- `BackupStore` (3.4) com escrita atômica, schema versionado e journal.
- `RestorePointService`: cria ponto de restauração via `Checkpoint-Computer` (PowerShell) antes de aplicar; tolera falha e a janela de 24 h do Windows; registra o resultado no journal.
- `RegistryTweak` (DWord e String, múltiplos valores, rollback parcial automático).

**Aceite**
- [ ] Testes (com `FakeRegistry`): aplicar grava backup; reverter restaura; valor inexistente é apagado ao reverter; reaplicar não sobrescreve backup; falha no 2º valor reverte o 1º.
- [ ] Teste de corrupção: `backup.json` inválido não derruba o app (backup movido para `.corrupt` e aviso ao usuário).

---

### Fase 3 — Catálogo v1 e motor de recomendação

**Objetivo:** ajustes da seção 4.1 e recomendação por foco/cenário.

**Entregáveis**
- `Catalog` com os ajustes 4.1 (exceto `power-high` e `flip-model-upgrade` se a Fase 3 ficar grande; entram na Fase 4).
- `RecommendationEngine` (3.5) + ordenação por impacto.
- `docs/TWEAKS.md` com cada ajuste documentado (campos exigidos na seção 4).

**Aceite**
- [ ] Testes de recomendação cobrindo: foco Work/Gaming/Both × cenários marcados/não marcados × jogos (nenhum, Siege, CS2, ambos).
- [ ] Foco Work ignora a seleção de jogos.
- [ ] Todo ajuste do catálogo tem entrada em `TWEAKS.md` com fonte.

---

### Fase 4 — Plano de energia e ajustes baseados em comando

**Objetivo:** `PowerPlanTweak` e `flip-model-upgrade`.

**Entregáveis**
- `PowerPlanTweak`: lê plano ativo (regex de GUID sobre `powercfg /getactivescheme`, independente do idioma), guarda anterior, ativa Alto desempenho com fallback `/duplicatescheme`, reverte para o anterior.
- Detecção do plano "AMD Ryzen Balanced" (nome do plano via `powercfg /list`) para ajustar a recomendação.
- `flip-model-upgrade` com suporte a preservar outras flags da string `DirectXUserGlobalSettings`.

**Aceite**
- [ ] Testes com `FakeProcessRunner` para parsing de saída em PT-BR e EN-US.
- [ ] Reverter volta exatamente ao GUID anterior.

---

### Fase 5 — Diagnóstico estático (`Analyzer`)

**Objetivo:** todos os avisos da seção 4.2 que não dependem de telemetria ao vivo.

**Entregáveis**
- Engine de regras: cada regra é uma classe pequena (`IDiagnosticRule`) com `Evaluate(IWmiReader, IRegistry) -> IEnumerable<Finding>`.
- Regras: `ram-below-rated`, `ram-single-channel`, `ram-capacity-low`, `rebar-unknown`, `vbs-active`, `chipset-driver-missing`, `fast-startup`, `disk-low-space`, `vcache-note`.
- Falha de uma regra não derruba as outras (cada regra isolada em try/catch com `Finding` de erro informativo).

**Aceite**
- [ ] Testes por regra com WMI fake (casos positivo e negativo).
- [ ] Rodar em máquina real não lança exceção e não escreve em nada.

---

### Fase 6 — GUI MVP

**Objetivo:** primeira versão utilizável.

**Entregáveis**
- Janela principal (MVVM) com: seleção de foco (3 opções), **seleção de jogos (2 checkboxes: Rainbow Six Siege e CS2; visível e habilitada apenas quando o foco é Jogos ou Os dois)**, seleção de cenários (6 checkboxes), lista de ajustes recomendados (checkbox, nome, descrição, risco, status "já aplicado"), painel de diagnóstico, botões **Analisar**, **Aplicar selecionados**, **Desfazer tudo**.
- Estados: ocioso, analisando, aplicando (barra de progresso, UI não congela: operações em `Task` com `CancellationToken`).
- Confirmação antes de aplicar, resumindo o que será alterado e se exige reinício.
- Resultado por ajuste (sucesso/falha/ignorado) em um painel de log.
- Acessibilidade: navegação por teclado, `AutomationProperties.Name`, contraste adequado, suporte a tema claro/escuro do Windows 11.
- Manifest `requireAdministrator`.

**Aceite**
- [ ] Fluxo ponta a ponta manual: escolher foco/cenários -> analisar -> aplicar -> desfazer.
- [ ] Testes de ViewModel (sem UI) para seleção, filtragem e comandos.
- [ ] Nenhuma operação longa roda na thread de UI.

---

### Fase 7 — Telemetria ao vivo e classificação de gargalo

**Objetivo:** dizer *qual componente limita* durante o uso.

**Entregáveis**
- `IHardwareSensors` (implementação com LibreHardwareMonitorLib): uso/clock/temperatura de CPU, uso/clock/temperatura/VRAM de GPU, uso de RAM, taxa de paginação (`Memory\Pages/sec` via contadores de desempenho), uso de disco.
- **Modo seguro para anti-cheat (obrigatório):** `LibreHardwareMonitorLib` pode carregar um **driver de kernel** para ler certos sensores.
  Isso pode ser sinalizado por BattlEye/VAC ou bloqueado pela lista de drivers vulneráveis do Windows 11. Portanto:
  (1) com Siege ou CS2 em execução, usar **somente fontes em modo usuário** (contadores de desempenho PDH, WMI e NVML para a GPU NVIDIA);
  (2) o provedor com driver de kernel só pode ser iniciado **fora** de sessão de jogo e descarregado antes de o jogo abrir;
  (3) confirmar na versão exata da biblioteca se ela usa driver de kernel e qual; se não der para garantir (1)-(2), não incluir esse provedor.
- `Sampler`: amostragem a 1 Hz com buffer circular; modo "sessão" que grava durante X minutos.
- `BottleneckClassifier`: regras sobre a janela de amostras, por exemplo:
  - GPU ≥ 95% de uso sustentado -> limitado por GPU.
  - Uma thread de CPU saturada com GPU < 85% -> limitado por CPU (single-thread).
  - Memória > 90% + paginação alta -> limitado por RAM.
  - Temperatura/clock caindo sob carga -> throttling térmico/energia (`thermal-throttle`).
- Gráfico simples em tempo real (sem biblioteca pesada; `Canvas`/`Polyline` ou biblioteca leve com versão exata).
- Botão "Iniciar sessão de diagnóstico" -> resumo "Durante esta sessão: gargalo principal = X".

**Aceite**
- [ ] Classificador testado com séries sintéticas para cada caso.
- [ ] Overhead do app em amostragem < 1% de CPU (medir e registrar em `docs/DECISIONS.md`).
- [ ] Se o sensor não estiver disponível (sem permissão, hardware não suportado), o app degrada com mensagem clara.

---

### Fase 8 — Gerenciador de inicialização

**Objetivo:** reduzir tempo de boot/login sem remover nada.

**Entregáveis**
- Listagem de itens de startup: HKCU/HKLM `Run`, `RunOnce`, pasta Startup, tarefas agendadas "At logon" (somente leitura via Task Scheduler API).
- Mostrar: nome, editor/publisher (assinatura digital quando disponível), caminho, impacto estimado (heurística simples: tamanho + presença na lista de "alto impacto conhecido").
- **Desativar = mover para estado desabilitado reversível**, usando o mecanismo nativo `StartupApproved` (`HKCU/HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{Run|Run32|StartupFolder}`) — o mesmo do Gerenciador de Tarefas. **Confirmar o formato binário em documentação** antes de implementar; se não confirmado, implementar apenas a listagem.
- Nunca tocar em itens assinados pela Microsoft nem em drivers/segurança; esses aparecem bloqueados.
- Cada desativação entra no `BackupStore` e é reversível.

**Aceite**
- [ ] Testes com registro fake para listar/desativar/reativar.
- [ ] Desativar e reativar um item de teste no Windows 11 real gera o mesmo estado exibido pelo Gerenciador de Tarefas.

---

### Fase 9 — Limpeza segura (shader cache, temporários)

**Objetivo:** liberar espaço sem risco.

**Entregáveis**
- Categorias com **prévia obrigatória de tamanho** antes de apagar:
  - Arquivos temporários do usuário (`%TEMP%`) — apenas arquivos mais antigos que 24 h e não bloqueados.
  - Cache de miniaturas, relatórios de erro do Windows, cache do Delivery Optimization (se acessível).
  - Caches de shader (DirectX, NVIDIA): **opcional, desmarcado por padrão**, com aviso de que o primeiro carregamento dos jogos ficará mais lento enquanto recompila.
- Enviar para a Lixeira quando aplicável; caso contrário, apagar apenas dentro de caminhos allowlist (lista fixa e testada; jamais caminho vindo de entrada do usuário sem normalização).
- Proteção contra path traversal/links simbólicos: resolver caminho real e validar que está sob a allowlist antes de apagar.
- Relatório: bytes liberados, arquivos ignorados e motivo.

**Aceite**
- [ ] Testes com `FakeFileSystem`: allowlist respeitada, symlink fora da allowlist ignorado, arquivo em uso ignorado.
- [ ] Nenhuma exclusão acontece sem confirmação da prévia.
- [ ] Limpeza não é "reversível" por natureza: a UI deve dizer isso explicitamente.

---

### Fase 10 — Benchmark antes/depois

**Objetivo:** provar (ou desmentir) ganhos.

**Entregáveis**
- "Sessão de medição": o usuário roda seu jogo/atividade por N minutos; o app grava telemetria (Fase 7) + estatísticas de frametime **se** houver fonte confiável (ex.: ler CSV exportado do PresentMon, ferramenta externa opcional; não embutir captura de frames própria).
- Comparador: sessão A (antes) vs B (depois) com média, P1 low, desvio, temperatura máxima, uso de RAM máximo.
- Salvar sessões em `%LOCALAPPDATA%\WinTune\sessions\*.json`.
- Relatório claro: melhoria/regressão/sem diferença estatisticamente relevante (limiar configurável) — **sem promessas**.

**Aceite**
- [ ] Testes do comparador com dados sintéticos.
- [ ] Se não houver dados de frametime, o app mostra apenas métricas de telemetria, sem inventar FPS.

---

### Fase 11 — Auditoria de serviços e tarefas agendadas (conservadora)

**Objetivo:** identificar o que consome recursos em segundo plano, sem quebrar o sistema.

**Entregáveis**
- Listagem de serviços com: estado, tipo de inicialização, fabricante, uso de CPU/RAM (por processo associado).
- Lista **allowlist de serviços de terceiros** que o app pode sugerir mudar de Automático para Manual (ex.: updaters de fabricantes de periféricos). Serviços da Microsoft e qualquer serviço não classificado = somente leitura.
- Mudança de tipo de inicialização reversível (guarda o valor original); nunca parar serviço em execução à força.
- Mesma lógica para tarefas agendadas de updaters de terceiros (somente desabilitar/reabilitar).

**Aceite**
- [ ] Testes: serviço Microsoft nunca aparece como alterável.
- [ ] Reverter restaura o tipo de inicialização exato.

---

### Fase 12 — Perfis e modo jogo automático

**Objetivo:** alternar configuração conforme o contexto.

**Entregáveis**
- Perfis salvos (nome, foco, cenários, ajustes selecionados) em `%APPDATA%\WinTune\profiles\*.json`.
- Detecção de jogo em execução a partir do `games.json` (seção 4.3): Rainbow Six Siege e CS2, apenas pela existência do processo. Perfil automático por jogo (cada jogo pode ter um conjunto de ajustes diferente). Sem tocar no processo do jogo (anti-cheat).
- Ao detectar jogo: ativar perfil "Jogo" (ex.: plano de energia alto desempenho); ao fechar: restaurar o estado anterior. Tudo via os mesmos `Tweak` (mesmo backup/journal).
- Ícone na bandeja (system tray) com status e atalho para desativar o modo automático. Funciona **opt-in**.

**Aceite**
- [ ] Testes do orquestrador com processos simulados: entrar/sair do jogo deixa o sistema no estado original.
- [ ] Crash do app com modo jogo ativo: na próxima abertura, o app detecta estado pendente e oferece restaurar.

---

### Fase 13 — Relatórios, exportação e suporte

**Objetivo:** rastreabilidade para o usuário.

**Entregáveis**
- Exportar relatório (Markdown e JSON): hardware detectado, achados do diagnóstico, ajustes aplicados, resultados de benchmark, versão do app.
- Tela "Histórico" lendo o `journal` do `BackupStore`.
- Botão "Abrir pasta de logs".
- **Nenhum dado sai da máquina**; exportação é sempre arquivo local escolhido pelo usuário.

**Aceite**
- [ ] Relatório gerado não contém nome de usuário, serial ou chaves; apenas modelos de hardware e configurações.
- [ ] Testes de serialização.

---

### Fase 14 — Segurança, robustez e conformidade

**Objetivo:** endurecer antes do release.

**Entregáveis**
- Revisão de todas as chamadas a processos externos (sem shell, sem concatenação, timeout, validação de saída).
- Validação de toda entrada vinda de arquivo (perfis, backup, sessões): schema, limites de tamanho, tratamento de erro.
- Confirmar que o app não faz chamadas de rede (verificar por busca no código e por teste de firewall).
- Revisão de licenças das dependências (LibreHardwareMonitorLib MPL-2.0 etc.) e arquivo `THIRD_PARTY_NOTICES.md`.
- Análise estática: `dotnet format --verify-no-changes`, analisadores Roslyn habilitados como erro.
- `docs/SAFETY.md`: política de rollback, o que o app nunca faz, como recuperar manualmente caso o app não abra (comandos `reg`/`powercfg` documentados).
- Teste de resiliência: matar o processo no meio de `Apply` e verificar que o próximo start detecta e reverte o estado parcial.

**Aceite**
- [ ] Todos os itens acima concluídos e documentados.
- [ ] Nenhum warning de analisador.
- [ ] Roteiro manual de "recuperação sem o app" validado em VM.

---

### Fase 15 — Empacotamento, instalação e release

**Objetivo:** distribuição para uso pessoal.

**Entregáveis**
- `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` documentado no Makefile/`build.ps1`.
- Instalador: Inno Setup **ou** MSIX (escolher um e registrar em `DECISIONS.md`); atalho no menu Iniciar; desinstalador que **não** deixa ajustes ativos sem avisar (oferece "Desfazer tudo" antes de remover).
- Assinatura de código: opcional para uso pessoal; se não houver certificado, documentar o aviso do SmartScreen.
- Versionamento SemVer; `CHANGELOG.md` atualizado; tag `v1.0.0`.
- Verificação de atualização **opt-in** (desligada por padrão), se implementada: apenas consulta de versão em HTTPS, sem enviar dados.

**Aceite**
- [ ] Instalação limpa em uma VM Windows 11, uso completo, desinstalação sem resíduos.
- [ ] README final com instalação, uso, recuperação e FAQ.

---

## 6. Estratégia de testes (transversal)

| Nível | Alvo | Ferramenta |
|-------|------|------------|
| Unidade | Core inteiro (tweaks, backup, recomendação, regras, classificador) | xUnit + fakes |
| Integração | Platform contra o Windows real | xUnit com `Trait("Category","Integration")`, executado só localmente/VM |
| ViewModel | Comandos e estado da UI | xUnit, sem janela |
| Manual | Fluxo ponta a ponta, recuperação sem o app | Checklist em `docs/SAFETY.md` |

Regras:
- Cobertura mínima de **80%** em `WinTune.Core`.
- Testes de integração nunca rodam no CI padrão e **nunca** alteram um PC que não seja VM/descartável sem confirmação explícita.
- Todo bug corrigido ganha um teste de regressão.

---

## 7. Definição de pronto (Definition of Done) de qualquer fase

- [ ] Build e testes passam, 0 warnings.
- [ ] Critérios de aceite da fase marcados.
- [ ] Docs atualizadas (README, `TWEAKS.md`, `DECISIONS.md`, XML docs públicas).
- [ ] `CHANGELOG.md` atualizado em `[Unreleased]`.
- [ ] Commit com Conventional Commits.
- [ ] Nada fora do escopo da seção 1.4 foi implementado.

---

## 8. Roadmap resumido

```
F0  Fundação ──► F1 Platform ──► F2 Core+Backup ──► F3 Catálogo+Reco ──► F4 Energia/Comando
                                                                              │
F5 Diagnóstico ──► F6 GUI MVP  (primeira versão utilizável) ◄────────────────┘
                       │
                       ├─► F7 Telemetria ──► F10 Benchmark
                       ├─► F8 Startup ──► F11 Serviços/Tarefas
                       ├─► F9 Limpeza
                       └─► F12 Perfis/Modo jogo ──► F13 Relatórios ──► F14 Segurança ──► F15 Release
```

Marcos:
- **M1 (F0–F6):** app utilizável com diagnóstico e ajustes reversíveis.
- **M2 (F7–F10):** medição real do gargalo e prova de ganho.
- **M3 (F11–F13):** otimização ampla de segundo plano, automação e relatórios.
- **M4 (F14–F15):** endurecimento e distribuição.

---

## 9. Prompt sugerido para sua IA

> Leia integralmente o arquivo `WINTUNE_BUILD_SPEC.md`. Execute **somente a Fase 0** agora: crie o
> repositório conforme a seção 3.2, siga as regras da seção 0 e confirme cada critério de aceite.
> Quando terminar, mostre o resultado de `dotnet build` e `dotnet test` e aguarde minha ordem para a próxima fase.

---

## 10. Registro de riscos, lacunas e advogado do diabo

### 10.1 Riscos (com mitigação já refletida acima)

| # | Risco | Severidade | Mitigação |
|---|-------|-----------|-----------|
| R1 | Driver de kernel dos sensores pode ser sinalizado por BattlEye/VAC ou bloqueado pelo Windows | Alta | Modo seguro de anti-cheat na Fase 7 (somente fontes em modo usuário durante jogo) |
| R2 | `backup.json` adulterável por qualquer processo do usuário; o app elevado escreve no registro a partir dele (escalonamento de privilégio) | Alta | Allowlist em código no `Revert`, ACL em ProgramData, HMAC (seção 3.4) |
| R3 | App inteiro rodando elevado, inclusive bandeja e modo jogo | Média | UI sem elevação + helper elevado (seção 2) |
| R4 | Reverter sobrescreve mudança feita depois por outro programa/usuário | Média | Revert seguro contra drift (seção 3.4) |
| R5 | `Checkpoint-Computer` falha se a Proteção do Sistema estiver desligada (comum no Windows 11) | Média | Detectar e avisar; não bloquear, porque o backup JSON continua valendo |
| R6 | Executável sem assinatura, elevado e que mexe no registro tende a ser marcado como PUP/malware por antivírus/SmartScreen | Média | Documentar; assinar se possível; evitar nomes/telas de "otimizador mágico"; não empacotar com ofuscação |
| R7 | Benchmark com 1 execução é ruído (variação entre rodadas, mapa, cache de shader) | Média | Mínimo de 3 rodadas por condição, mapa/cenário fixo, descartar a 1ª rodada (shader), reportar variação |
| R8 | Ajustes viram no-op: Game Mode costuma vir ativo, Game DVR muitas vezes desligado, HAGS já ativo | Média | `IsApplied()` mostra "já aplicado"; a UI deve dizer claramente "nada a fazer aqui" |
| R9 | Limpeza apaga algo útil (shader cache, temp em uso por instalador) | Média | Prévia, allowlist, desmarcado por padrão, texto de irreversibilidade |
| R10 | Nomes de executável, App IDs e chaves de registro desatualizam com updates do jogo/Windows | Média | `games.json` editável, validação de schema, nunca assumir que existem |
| R11 | Windows Update ou driver NVIDIA reverte configurações | Baixa | Botão "Reanalisar" e aviso quando um ajuste aplicado deixa de estar ativo |
| R12 | Segunda instância do app aplicando ao mesmo tempo | Baixa | Mutex nomeado global |
| R13 | Interrupção (queda de energia, kill) no meio do `Apply` | Média | Journal com intenção gravada antes de agir; reconciliação no próximo start (Fase 14, mas o journal nasce na Fase 2) |

### 10.2 Lacunas do spec original (a tratar)

1. **Strings da UI e localização:** definir `Resources.resx` pt-BR desde a Fase 6; o spec só fala de idioma de código/docs.
2. **Compatibilidade de versões do Windows 11** (23H2/24H2/25H2): chaves e comportamento mudam; criar matriz de teste em VM e checagem de build do SO antes de cada ajuste.
3. **Modo "simulação" (dry-run):** mostrar exatamente o que seria feito sem escrever nada; útil para confiança e para testes.
4. **Estimativa de impacto** citada na ordenação de recomendações nunca é definida: ou definir uma tabela estática de prioridade ou remover o termo.
5. **Telemetria de consentimento/privacidade** da própria sessão de medição (o app só grava local; deixar explícito na UI).
6. **Inconsistência interna:** a Fase 3 permite adiar `power-high` e `flip-model-upgrade` para a Fase 4; padronizar para que nasçam na Fase 4.
7. **Ordem de segurança:** a anti-adulteração do backup e o journal precisam existir na Fase 2, não só na Fase 14 (já corrigido na seção 3.4).
8. **Definição de "descaracterizar"** é subjetiva; registrar uma lista objetiva de mudanças visuais/comportamentais permitidas (hoje só `transparency-off`, desmarcado).
9. **Critérios de qualidade de UX** são fracos (sem wireframe nem textos). Criar `docs/UX.md` com telas e copy antes da Fase 6.
10. **Plano de rollback manual** (comandos `reg` e `powercfg` para desfazer sem o app) só aparece na Fase 14; mover o rascunho para a Fase 3, quando o catálogo existe.

### 10.3 Advogado do diabo (contra o que foi modelado)

**1. "Você não precisa de um app de 16 fases."**
Para um PC pessoal, quase todo o valor está em: ligar EXPO/XMP, Resizable BAR, subir para 32 GB, fechar overlays/launchers e ajustar as configs dos dois jogos.
Isso cabe em um checklist e um script PowerShell de diagnóstico de 100 linhas. As Fases 11 (serviços), 9 (limpeza) e 13 (relatórios)
têm retorno baixo e risco não trivial. Sugestão: **tratar o MVP como Fases 0-7 e 10**, reavaliar o resto só se houver dor real.

**2. "O diagnóstico prova que o app não resolve o seu gargalo."**
O próprio spec lista o maior ganho provável (RAM nominal/capacidade) como *advisory*. O software pode ser, na prática, um leitor de status com botões
que mudam coisas de efeito marginal. Se o cenário de jogo for CPU/RAM-bound, os ajustes automáticos da 4.1 mudam pouco.

**3. "Parte do catálogo pode ser placebo ou piorar."**
HAGS tem resultados mistos; plano Alto Desempenho pode ser irrelevante com o plano do chipset AMD; otimizações de tela cheia são controversas.
A correção foi marcá-los como experimentais e exigir A/B antes de recomendar. Se o benchmark não provar ganho, a conclusão correta é **não aplicar**.

**4. "O argumento do HAGS estava errado para estes jogos."**
Frame Generation não é usado por Siege nem CS2, então justificar HAGS por isso era irrelevante (e a própria alegação precisava de verificação).
Lição: o catálogo original foi escrito para "uma RTX 40" e não para os dois jogos escolhidos. A seção 4.4 corrige a ênfase.

**5. "A telemetria contradiz a regra de anti-cheat que você mesmo criou."**
Sensores com driver de kernel durante um jogo com BattlEye violam o espírito da seção 1.4. Era uma contradição real do desenho; foi corrigida (R1),
mas mostra que decisões de dependência precisam ser auditadas contra os princípios.

**6. "O backup que protege você era uma porta de escalonamento."**
Um app elevado que restaura valores lidos de um JSON editável pelo usuário é uma superfície de ataque clássica. O desenho original ignorava isso (R2).

**7. "Ferramentas de 'otimização' têm reputação ruim por boas razões."**
Mesmo bem-intencionado, um .exe elevado que altera o registro é indistinguível, para antivírus e para você daqui a 6 meses, de um "tweaker" duvidoso.
Sem assinatura e sem testes reais em VM, a manutenção a cada update do Windows vira custo contínuo.

**8. "Medir ganho é mais difícil do que o spec sugere."**
Sem PresentMon (ferramenta externa) não há frametime; sem rodadas repetidas e cenário fixo (por exemplo, um mapa de benchmark do CS2, uma partida personalizada ou replay
no Siege), qualquer "melhoria" está dentro do ruído. O benchmark é a fase que decide se o resto vale a pena e deveria vir **antes** de investir em Fases 8-12.

**9. "Quem mantém `games.json`?"**
Nomes de processo, App IDs e overlays mudam. Sem testes de contrato nem fallback, o modo jogo falha em silêncio quando o jogo atualiza.

**10. "Risco de falsa segurança na reversibilidade."**
Reverter só cobre o que o app guardou. Limpeza de arquivos, mudanças feitas pelo driver/Windows Update no meio do caminho e o ponto de restauração desligado
significam que "tudo reversível" é verdade para o registro/serviços/energia, não para tudo. A UI deve dizer isso com precisão.

### 10.4 Recomendação resultante

1. Executar **Fases 0-5** (fundação até diagnóstico) e a **Fase 6** (GUI), com as correções R2, R4 e R13 já embutidas.
2. Antes de seguir, rodar o diagnóstico na máquina real e **corrigir primeiro o que for hardware/BIOS** (EXPO/XMP, Resizable BAR, refresh do monitor, RAM).
3. Executar **Fases 7 e 10** para provar se algum ajuste automático muda algo nos dois jogos.
4. Só então decidir sobre Fases 8, 9, 11, 12, 13. Cortar o que não mostrou valor.

---

## 11. Matriz de verificação de fontes

Data da verificação: 2026-11-10. Legenda de confiança: **Oficial** (documentação do fabricante/Microsoft/Ubisoft/Valve),
**Comunidade** (fórum, guia, Q&A de usuários), **Não verificado**. Onde só há fonte de comunidade, o agente deve tratar o item como
"confirmar na máquina real antes de automatizar" (regra da seção 0).

| # | Item do spec | Resultado | Confiança | Fonte |
|---|--------------|-----------|-----------|-------|
| V1 | Steam App ID do Siege = 359550 | Confirmado | Oficial (loja Steam) | https://store.steampowered.com/app/359550/ |
| V2 | `RainbowSix.exe` é o executável principal do Siege | Confirmado | Comunidade (Steam) | https://steamcommunity.com/app/359550/discussions/1/1743391819089144214/ |
| V3 | `RainbowSix_Vulkan.exe` existe | Existia, mas **Vulkan foi removido** (Operation Twin Shells, set/2024); tratar como legado | Oficial (Ubisoft: página de testes Vulkan) + Comunidade/imprensa (remoção) | https://www.ubisoft.com/en-us/game/rainbow-six/siege/news-updates/6VFn74oMO2nVQGZxvFhBb5/vulkan-api-testing-on-live-pc ; https://insider-gaming.com/vulkan-api-rainbow-six-removed/ |
| V4 | `RainbowSix_BE.exe` | **Não confirmado** nas buscas | Não verificado | Conferir na pasta de instalação |
| V5 | Siege usa BattlEye | Confirmado (FAQ oficial). Há relato de que a Ubisoft adotará anti-cheat próprio em modo kernel (ago/2026) em **fonte secundária** | Oficial (BattlEye) / Comunidade (mudança) | https://www.ubisoft.com/en-us/game/rainbow-six/siege/news-updates/2999gdyIXLbeiZP78R7545/battleye-faq ; https://tier1settings.com/rainbow-six-siege-battleye-error-fix/ |
| V6 | Siege migra para DX12 e DX11 pode sair | Notícias de imprensa; detalhes mudam entre temporadas | Comunidade/imprensa | https://siege.gg/news/ubisoft-confirms-dx11-is-leaving-rainbow-six-siege-with-operation-prep-phase |
| V7 | Steam App ID do CS2 = 730; executável em `...\Counter-Strike Global Offensive\game\bin\win64\cs2.exe` | Confirmado | Comunidade (Steam) | https://steamcommunity.com/app/730/discussions/0/3821921664845031432 |
| V8 | CS2 em Windows: DX11 por padrão, Vulkan opcional por `-vulkan` | Consistente em vários guias; **sem página oficial da Valve achada** | Comunidade | https://steamcommunity.com/sharedfiles/filedetails/?id=3198674462 |
| V9 | `UserGpuPreferences`: valor por `.exe` com dado `GpuPreference=N;` | Chave e formato confirmados. **Significado de N (0/1/2)**: os resultados não deram tabela oficial clara | Comunidade (Microsoft Q&A) | https://learn.microsoft.com/en-us/answers/questions/4314188/how-to-change-default-graphics-(universal-default) |
| V10 | `DirectXUserGlobalSettings` (REG_SZ) com `SwapEffectUpgradeEnable=1;` | Confirmado, incluindo outras flags na mesma string | Comunidade (Eleven Forum / guias) | https://www.elevenforum.com/t/find-gpu-ven-dev-subsys-values-for-highperfadapter-option-using-wmic-or-other-method.30127/ |
| V11 | "Otimizações para jogos em janela" aplica-se a DX10/DX11 em janela/sem borda | Confirmado | Oficial (Microsoft DevBlog / Suporte) | https://devblogs.microsoft.com/directx/updates-in-graphics-and-gaming/ ; https://support.microsoft.com/de-de/windows/hardware/display-graphics/optimizations-for-windowed-games-in-windows-11 |
| V12 | `HwSchMode`: 2 liga, 1 desliga (HKLM `...\Control\GraphicsDrivers`) | Confirmado por respostas em Microsoft Q&A; sem referência de especificação | Comunidade (Microsoft Q&A) | https://learn.microsoft.com/en-us/answers/questions/4358192/where-did-hardware-accellerated-gpu-scheduling-go |
| V13 | "HAGS é pré-requisito de DLSS Frame Generation" | Repetido por vários sites; **não achei fonte oficial da NVIDIA**. Irrelevante para Siege/CS2 | Comunidade | https://nerdschalk.com/how-to-fix-frame-generation-greyed-out-requirements-settings-troubleshooting-for-dlss-3-rtx-40-50-series/ |
| V14 | `Checkpoint-Computer`: só em Windows cliente e **no máximo 1 ponto de restauração por dia** | Confirmado | Oficial (Microsoft Learn) | https://learn.microsoft.com/el-gr/powershell/module/microsoft.powershell.management/checkpoint-computer?view=powershell-5.1 |
| V15 | Proteção do Sistema precisa estar ligada para criar o ponto | Plausível e consistente com a UI "Turn on system protection", mas **não apareceu como frase oficial nos resultados** | Não verificado | https://learn.microsoft.com/en-us/answers/questions/3311968/how-do-i-turn-on-system-protection |
| V16 | `Win32_DeviceGuard.VirtualizationBasedSecurityStatus == 2` = VBS ativo e em execução | Confirmado; consulta com `Get-CimInstance Win32_DeviceGuard` | Comunidade (moderador Microsoft Q&A) + Oficial (suporte Microsoft: checar "Running" no msinfo32) | https://learn.microsoft.com/pt-br/answers/questions/3146532/desativa-o-device-guard ; https://support.microsoft.com/help/5042562 |
| V17 | `StartupApproved\{Run,Run32,StartupFolder}`: binário iniciando com `02` = habilitado, `03` = desabilitado (`08`/`09` = bloqueado pelo sistema) | Consistente em várias fontes; **sem documentação oficial achada** | Comunidade | https://stackoverflow.com/questions/78451156/how-to-disable-enable-startup-programs-through-registry-editor |
| V18 | Flag de compatibilidade por `.exe` em `HKCU\...\AppCompatFlags\Layers` com `~ DISABLEDXMAXIMIZEDWINDOWEDMODE` | Confirmado como método usado; a própria resposta da Microsoft Q&A diz que vale para executáveis em disco local e não para arquivos protegidos | Comunidade (Microsoft Q&A) | https://learn.microsoft.com/en-us/answers/questions/2280232/value-data-in-hkey-current-usersoftwaremicrosoftwi |
| V19 | WinRing0 é vulnerável (CVE-2020-14979) e bloqueado/alertado pelo Defender | Confirmado | Oficial (Microsoft Defender Security Intelligence / suporte) | https://www.microsoft.com/en-us/wdsi/threats/malware-encyclopedia-description?Name=VulnerableDriver:WinNT/Winring0.G&ThreatID=2147947097 |
| V20 | LibreHardwareMonitor trocou WinRing0 por PawnIO | Confirmado pelo histórico do repositório ("Swap WinRing0 to PawnIO", set/2025). **Ainda é driver de kernel**; compatibilidade com BattlEye/VAC **não verificada** | Oficial (repositório do projeto) | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| V21 | LibreHardwareMonitorLib suporta .NET 8, licença MPL-2.0 | Confirmado | Oficial (README) | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/README.md |
| V22 | Compatibilidade de ferramentas de monitoramento com BattlEye/VAC | **Não verificado**; a Ubisoft informa em geral que software pode funcionar sem ser suportado | Comunidade | https://steamcommunity.com/app/359550/discussions/1/1836811737978513669/ |

### 11.1 Impacto das verificações no spec

1. **Corrigido:** o guia do Siege não menciona mais Vulkan (V3).
2. **Corrigido:** `flip-model-upgrade` virou item restrito e desmarcado, porque só vale para DX10/11 em janela/sem borda (V11).
3. **Mantido como "confirmar na máquina":** significado de `GpuPreference=N` (V9), `StartupApproved` (V17), `RainbowSix_BE.exe` (V4), CS2 DX11 por padrão (V8), Proteção do Sistema (V15).
4. **Risco R1 permanece:** o PawnIO é mais seguro que o WinRing0, mas continua sendo driver de kernel. Manter a regra de não carregá-lo com Siege/CS2 abertos até haver evidência oficial de compatibilidade (V20, V22).
5. **Tendência a monitorar:** o anti-cheat do Siege pode mudar (V5). O agente deve reler a política de anti-cheat antes de liberar qualquer release.
