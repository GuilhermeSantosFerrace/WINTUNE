using WinTune.Platform.Abstractions;

namespace WinTune.TestDoubles;

/// <summary>Executor de processos simulado: respostas registradas por executável e argumentos.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<(Func<string, IReadOnlyList<string>, bool> Match, Func<IReadOnlyList<string>, ProcessResult> Result)> _handlers = [];

    /// <summary>Chamadas recebidas (executável, argumentos).</summary>
    public List<(string Executable, IReadOnlyList<string> Arguments)> Calls { get; } = [];

    /// <summary>Registra uma resposta para <paramref name="executable"/> cujos argumentos começam com <paramref name="argsPrefix"/>.</summary>
    public FakeProcessRunner On(string executable, string[] argsPrefix, Func<IReadOnlyList<string>, ProcessResult> result)
    {
        _handlers.Insert(0, ((exe, args) =>
            string.Equals(Path.GetFileName(exe), executable, StringComparison.OrdinalIgnoreCase) &&
            args.Count >= argsPrefix.Length &&
            argsPrefix.Select((a, i) => string.Equals(a, args[i], StringComparison.OrdinalIgnoreCase)).All(x => x), result));
        return this;
    }

    /// <summary>Registra uma resposta fixa.</summary>
    public FakeProcessRunner On(string executable, string[] argsPrefix, string stdout, int exitCode = 0) =>
        On(executable, argsPrefix, _ => new ProcessResult(exitCode, stdout, string.Empty, false));

    public ProcessResult Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Calls.Add((executable, arguments.ToList()));
        foreach (var (match, result) in _handlers)
        {
            if (match(executable, arguments))
            {
                return result(arguments);
            }
        }

        return new ProcessResult(1, string.Empty, "sem resposta simulada", false);
    }
}

/// <summary>Lista de processos simulada.</summary>
public sealed class FakeProcessList : IProcessList
{
    /// <summary>Processos "em execução" (sem .exe).</summary>
    public List<string> Names { get; } = [];

    public IReadOnlyList<string> GetProcessNames() => Names.ToList();
}

/// <summary>Shell simulado.</summary>
public sealed class FakeShellLauncher : IShellLauncher
{
    /// <summary>Pastas abertas.</summary>
    public List<string> Opened { get; } = [];

    public void OpenFolder(string path) => Opened.Add(path);
}

/// <summary>Relógio controlável.</summary>
public sealed class FakeClock : IClock
{
    /// <summary>Hora atual simulada.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Avança o relógio.</summary>
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Ambiente simulado com pastas sob <c>C:\fake</c>.</summary>
public sealed class FakeEnvironment : IEnvironmentInfo
{
    public int OsBuild { get; set; } = 22631;

    public string SystemDrive { get; set; } = @"C:\";

    public string UserName { get; set; } = "fulano";

    public string MachineName { get; set; } = "PC-DO-FULANO";

    public string GetFolder(KnownFolder folder) => folder switch
    {
        KnownFolder.LocalAppData => @"C:\Users\fulano\AppData\Local",
        KnownFolder.RoamingAppData => @"C:\Users\fulano\AppData\Roaming",
        KnownFolder.ProgramData => @"C:\ProgramData",
        KnownFolder.UserTemp => @"C:\Users\fulano\AppData\Local\Temp",
        KnownFolder.UserProfile => @"C:\Users\fulano",
        KnownFolder.Windows => @"C:\Windows",
        KnownFolder.Startup => @"C:\Users\fulano\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup",
        KnownFolder.CommonStartup => @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp",
        _ => throw new ArgumentOutOfRangeException(nameof(folder)),
    };
}
