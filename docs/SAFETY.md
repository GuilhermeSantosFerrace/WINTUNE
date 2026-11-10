# Segurança, reversão e recuperação (WinTune)

## 1. Política de reversão

- Todo ajuste guarda o **estado original exato** (inclusive "valor inexistente") **antes** da primeira escrita, em
  `%ProgramData%\WinTune\backup.json` (pasta com escrita só para Administradores/SYSTEM).
- O original **nunca é sobrescrito**: reaplicar um ajuste mantém o primeiro original guardado.
- "Desfazer tudo" restaura cada item; se outro programa ou você mudou o valor depois do WinTune, nada é sobrescrito sem
  confirmação (conflito/drift).
- **Allowlist em código:** a reversão só escreve em (hive, chave, nome), planos de energia, serviços e tarefas que o próprio
  código declara. Entradas estranhas no `backup.json` são ignoradas e registradas no histórico.
- **Intenção antes de agir (R13):** cada operação grava uma intenção. Se o WinTune for interrompido no meio, a próxima abertura
  mostra "Operação interrompida" e oferece desfazer o que ficou pela metade.
- **Ponto de restauração:** tentado antes de aplicar (`Checkpoint-Computer`). O Windows só cria um a cada 24 h e exige a
  Proteção do Sistema ligada; se falhar, o backup do WinTune continua valendo.

### O que é e o que não é reversível

| Área | Reversível pelo WinTune? |
|------|--------------------------|
| Ajustes de registro (catálogo) | Sim |
| Plano de energia | Sim (GUID anterior exato; plano duplicado é apagado) |
| Itens de inicialização (StartupApproved) | Sim |
| Serviços (Automático → Manual) | Sim (tipo exato, inclusive "atraso") |
| Tarefas de atualizadores | Sim (reabilitar) |
| Modo jogo automático | Sim (restaura ao fechar o jogo ou na próxima abertura) |
| **Limpeza de arquivos** | **Não.** Arquivos apagados não voltam. |
| Mudanças feitas por drivers/Windows Update | Não (o WinTune não as fez) |

## 2. O que o WinTune nunca faz

Seção 1.4 do spec: não altera Defender, Firewall, UAC, SmartScreen, VBS/HVCI, Secure Boot nem o Windows Update; não remove
apps; não mexe em mouse/DPI/periféricos, timer resolution, afinidade/prioridade de processos, BIOS/UEFI, overclock ou
ventoinhas; não interage com o processo ou arquivos dos jogos (sem injeção, hooks, leitura de memória, edição de arquivos);
não finaliza processos; não para serviços; não faz chamadas de rede; não coleta dados.

## 3. Superfície revisada (Fase 14)

| Item | Proteção |
|------|----------|
| Processos externos | Só `.exe` em `%SystemRoot%\System32` (nunca pelo PATH), sem shell, argumentos em lista, tempo limite obrigatório (≤ 10 min). Usados: `powercfg.exe` (verbos fixos, GUID validado por regex), `WindowsPowerShell\v1.0\powershell.exe` (comando fixo `Checkpoint-Computer`, sem entrada do usuário). O Explorer é aberto só para a pasta de logs. |
| `backup.json` | Escrita atômica, schema 1, até 5 MB, IDs validados por regex, caminhos sem `..`; inválido → movido para `.corrupt-*` e aviso. |
| `games.json` | Schema 1, até 256 KB, nomes de processo por regex, sem `..`; inválido → padrões, sem sobrescrever o arquivo. |
| Perfis / modo jogo | Até 256 KB, nome por regex (sem `\`, `/`, `..`). |
| Sessões de benchmark | Até 10 MB, schema 1; inválidas são ignoradas. CSV do PresentMon até 200 MB. |
| Limpeza | Raízes fixas, caminho real resolvido e validado, links ignorados, arquivos em uso ignorados, revalidação antes de apagar. |
| Rede | Nenhuma API de rede no código (`ArchitectureTests.NoNetworkApis`). |
| Sensores | Somente modo usuário (PDH, NVML, WMI); nenhum driver de kernel. |
| Instância única | Mutex global `WinTune.SingleInstance`. |

**Verificar ausência de rede manualmente:** abra o Monitor de Recursos (`resmon`) > Rede durante o uso do WinTune — o
processo `WinTune.exe` não deve aparecer. Opcional: crie uma regra de saída de bloqueio no Firewall para `WinTune.exe` e
confirme que nada muda no funcionamento.

## 4. Recuperação manual sem o app

Use um terminal **como administrador**. Os valores originais estão em `%ProgramData%\WinTune\backup.json` (campo
`Original` de cada entrada; `null` = o valor não existia, então apague-o). Os comandos por ajuste estão em
[`docs/TWEAKS.md`](TWEAKS.md) ("Reverter sem o app"). Resumo:

```powershell
# Registro: restaurar um DWORD ou apagar um valor que não existia
reg add "HKCU\System\GameConfigStore" /v GameDVR_Enabled /t REG_DWORD /d 1 /f
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize" /v StartupDelayInMSec /f

# Plano de energia: listar e voltar ao Equilibrado (ou ao GUID "previousScheme" do backup)
powercfg /list
powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e

# Serviço: voltar para Automático (use "delayed-auto" se o backup indicar delayedAutoStart = True)
sc.exe config AdobeARMservice start= auto

# Tarefa agendada: reabilitar
schtasks /Change /TN "\GoogleUpdateTaskMachineUA" /ENABLE

# Inicialização: reabilitar pelo Gerenciador de Tarefas > Aplicativos de inicialização, ou apagar a marca de desativado
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" /v "NomeDoItem" /f

# Último recurso: ponto de restauração do sistema
rstrui.exe
```

## 5. Roteiro manual (VM Windows 11)

Execute numa VM ou PC descartável. Marque cada item:

1. [ ] Instalar e abrir o WinTune (UAC aparece; aceitar).
2. [ ] Analisar com foco "Os dois", Siege e CS2 marcados; conferir abas Diagnóstico e Jogos.
3. [ ] Aplicar `gamedvr-off`, `startup-delay` e `power-high`; conferir no `regedit`/`powercfg /getactivescheme`.
4. [ ] Fechar o app pelo Gerenciador de Tarefas no meio de "Aplicar" (com vários itens marcados); reabrir; aceitar
       "Operação interrompida"; conferir que os valores voltaram.
5. [ ] Desfazer tudo; conferir valores originais (inclusive valores apagados quando não existiam).
6. [ ] Ligar "Permitir desativar (experimental)" na Inicialização, desativar um item de teste e conferir no Gerenciador de
       Tarefas; reativar e conferir de novo.
7. [ ] Modo jogo: ligar, abrir um executável renomeado como `cs2.exe`, conferir o plano de energia; fechar e conferir a restauração.
8. [ ] Fechar o app com o modo jogo ativo (pelo Gerenciador de Tarefas); reabrir; aceitar "Modo jogo pendente".
9. [ ] Recuperação sem o app: aplicar ajustes, desinstalar/apagar o app e reverter tudo só com os comandos da seção 4.
10. [ ] Monitor de Recursos: nenhuma conexão de rede do `WinTune.exe`.

Estado: **pendente de execução em VM** (não pôde ser feito no ambiente de desenvolvimento).
