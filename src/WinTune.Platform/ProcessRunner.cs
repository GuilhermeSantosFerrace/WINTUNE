using System.Diagnostics;
using System.Globalization;
using System.Text;
using WinTune.Platform.Abstractions;

namespace WinTune.Platform;

/// <summary>
/// Executa processos sem shell: <c>UseShellExecute=false</c>, argumentos via <see cref="ProcessStartInfo.ArgumentList"/>
/// e tempo limite obrigatório. Executáveis com caminho relativo são resolvidos apenas em System32.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>Tempo limite máximo aceito.</summary>
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(10);

    static ProcessRunner()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <inheritdoc />
    public ProcessResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout <= TimeSpan.Zero || timeout > MaxTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "O tempo limite deve estar entre 0 e 10 minutos.");
        }

        var path = ResolveExecutable(executable, Environment.SystemDirectory);
        var encoding = ConsoleEncoding();
        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
            WorkingDirectory = Environment.SystemDirectory,
        };
        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg ?? throw new ArgumentException("Argumento nulo.", nameof(arguments)));
        }

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            return new ProcessResult(-1, string.Empty, "O processo não iniciou.", false);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        var exited = WaitForExit(process, timeout, cancellationToken);
        if (!exited)
        {
            TryKill(process);
            process.WaitForExit(5000);
            return new ProcessResult(-1, SafeResult(stdout), SafeResult(stderr), TimedOut: true);
        }

        process.WaitForExit();
        return new ProcessResult(process.ExitCode, SafeResult(stdout), SafeResult(stderr), false);
    }

    /// <summary>
    /// Resolve o caminho do executável. Nomes relativos só podem apontar para dentro de <paramref name="systemDirectory"/>.
    /// </summary>
    internal static string ResolveExecutable(string executable, string systemDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (executable.Contains("..", StringComparison.Ordinal) || executable.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException("Nome de executável inválido.", nameof(executable));
        }

        var full = Path.IsPathRooted(executable)
            ? Path.GetFullPath(executable)
            : Path.GetFullPath(Path.Combine(systemDirectory, executable));

        if (!Path.IsPathRooted(executable) &&
            !full.StartsWith(Path.TrimEndingDirectorySeparator(systemDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Executável fora de System32.", nameof(executable));
        }

        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Somente arquivos .exe podem ser executados.", nameof(executable));
        }

        return full;
    }

    private static bool WaitForExit(Process process, TimeSpan timeout, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.WaitForExit(100))
            {
                return true;
            }

            if (token.IsCancellationRequested)
            {
                return false;
            }
        }

        return process.HasExited;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Já terminou.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Sem permissão para finalizar; nada a fazer.
        }
    }

    private static string SafeResult(Task<string> task)
    {
        try
        {
            return task.Wait(TimeSpan.FromSeconds(5)) ? task.Result : string.Empty;
        }
        catch (AggregateException)
        {
            return string.Empty;
        }
    }

    private static Encoding ConsoleEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}

/// <summary>Lista de processos por nome, usando o snapshot do sistema (sem abrir handles).</summary>
public sealed class ProcessList : IProcessList
{
    /// <inheritdoc />
    public IReadOnlyList<string> GetProcessNames()
    {
        var processes = Process.GetProcesses();
        var names = new List<string>(processes.Length);
        foreach (var p in processes)
        {
            using (p)
            {
                names.Add(p.ProcessName);
            }
        }

        return names;
    }
}

/// <summary>Abre pastas no Explorer.</summary>
public sealed class ShellLauncher : IShellLauncher
{
    /// <inheritdoc />
    public void OpenFolder(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException(full);
        }

        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var psi = new ProcessStartInfo(explorer) { UseShellExecute = false };
        psi.ArgumentList.Add(full);
        using var process = Process.Start(psi);
    }
}
