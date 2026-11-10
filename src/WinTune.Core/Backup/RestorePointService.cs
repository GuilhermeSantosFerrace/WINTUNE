using WinTune.Platform.Abstractions;

namespace WinTune.Core.Backup;

/// <summary>Resultado da tentativa de criar ponto de restauração.</summary>
public enum RestorePointStatus
{
    /// <summary>Criado.</summary>
    Created,

    /// <summary>Não criado porque o Windows só permite um ponto a cada 24 h (o backup JSON continua valendo).</summary>
    SkippedFrequencyLimit,

    /// <summary>Falhou (ex.: Proteção do Sistema desligada). Não bloqueia a aplicação.</summary>
    Failed,
}

/// <summary>Resultado da criação do ponto de restauração.</summary>
/// <param name="Status">Status.</param>
/// <param name="Message">Mensagem pt-BR.</param>
public sealed record RestorePointResult(RestorePointStatus Status, string Message);

/// <summary>
/// Cria ponto de restauração via <c>Checkpoint-Computer</c> antes de aplicar ajustes.
/// Tolera falha e a janela de 24 h do Windows; registra o resultado no journal.
/// </summary>
public sealed class RestorePointService
{
    /// <summary>Caminho relativo a System32 do Windows PowerShell 5.1.</summary>
    public const string PowerShellPath = @"WindowsPowerShell\v1.0\powershell.exe";

    // Comando fixo: nenhuma entrada do usuário é interpolada.
    internal const string Command =
        "Checkpoint-Computer -Description 'WinTune' -RestorePointType 'MODIFY_SETTINGS' -WarningAction Continue -ErrorAction Stop";

    private readonly IProcessRunner _runner;
    private readonly BackupStore _store;

    /// <summary>Cria o serviço.</summary>
    public RestorePointService(IProcessRunner runner, BackupStore store)
    {
        _runner = runner;
        _store = store;
    }

    /// <summary>Tenta criar o ponto de restauração.</summary>
    public RestorePointResult TryCreate(CancellationToken cancellationToken = default)
    {
        RestorePointResult result;
        try
        {
            var r = _runner.Run(PowerShellPath,
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", Command],
                TimeSpan.FromMinutes(5), cancellationToken);
            var output = r.StandardOutput + "\n" + r.StandardError;
            if (r.TimedOut)
            {
                result = new RestorePointResult(RestorePointStatus.Failed, "Tempo esgotado ao criar o ponto de restauração.");
            }
            else if (r.ExitCode == 0 && output.Contains("1440", StringComparison.Ordinal))
            {
                result = new RestorePointResult(RestorePointStatus.SkippedFrequencyLimit,
                    "O Windows já criou um ponto de restauração nas últimas 24 h; um novo não foi criado. O backup do WinTune continua valendo.");
            }
            else if (r.ExitCode == 0)
            {
                result = new RestorePointResult(RestorePointStatus.Created, "Ponto de restauração criado.");
            }
            else
            {
                result = new RestorePointResult(RestorePointStatus.Failed,
                    "Não foi possível criar o ponto de restauração (a Proteção do Sistema pode estar desligada). " +
                    "Os ajustes continuam reversíveis pelo backup do WinTune. Detalhe: " + Trim(r.StandardError));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            result = new RestorePointResult(RestorePointStatus.Failed, "Falha ao iniciar o PowerShell: " + ex.Message);
        }

        _store.Journal("restore-point", "-", result.Status.ToString(), result.Message);
        return result;
    }

    private static string Trim(string s)
    {
        s = s.Trim();
        return s.Length > 300 ? s[..300] + "…" : s;
    }
}
