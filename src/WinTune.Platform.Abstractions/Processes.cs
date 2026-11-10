namespace WinTune.Platform.Abstractions;

/// <summary>Resultado de um processo externo.</summary>
/// <param name="ExitCode">Código de saída (-1 se expirou ou não iniciou).</param>
/// <param name="StandardOutput">Saída padrão capturada.</param>
/// <param name="StandardError">Saída de erro capturada.</param>
/// <param name="TimedOut">Indica se o processo foi finalizado por tempo limite.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    /// <summary>Verdadeiro se terminou dentro do prazo com código 0.</summary>
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

/// <summary>
/// Executa programas externos sem shell. Argumentos são passados como lista (sem concatenação),
/// e o tempo limite é obrigatório.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Executa <paramref name="executable"/>. Nomes relativos são resolvidos somente dentro de
    /// <c>%SystemRoot%\System32</c> (nunca pelo PATH).
    /// </summary>
    ProcessResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>Lista de processos em execução, apenas pelo nome (sem abrir handles para os processos).</summary>
public interface IProcessList
{
    /// <summary>Nomes de processos em execução, sem a extensão <c>.exe</c>.</summary>
    IReadOnlyList<string> GetProcessNames();
}

/// <summary>Ações de shell sem elevação de risco (abrir pasta no Explorer).</summary>
public interface IShellLauncher
{
    /// <summary>Abre uma pasta existente no Explorador de Arquivos.</summary>
    void OpenFolder(string path);
}
