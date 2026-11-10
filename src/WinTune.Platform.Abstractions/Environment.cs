namespace WinTune.Platform.Abstractions;

/// <summary>Pastas conhecidas do Windows.</summary>
public enum KnownFolder
{
    /// <summary><c>%LOCALAPPDATA%</c>.</summary>
    LocalAppData,

    /// <summary><c>%APPDATA%</c>.</summary>
    RoamingAppData,

    /// <summary><c>%ProgramData%</c>.</summary>
    ProgramData,

    /// <summary><c>%TEMP%</c> do usuário.</summary>
    UserTemp,

    /// <summary>Perfil do usuário.</summary>
    UserProfile,

    /// <summary><c>%SystemRoot%</c>.</summary>
    Windows,

    /// <summary>Pasta Inicializar do usuário.</summary>
    Startup,

    /// <summary>Pasta Inicializar de todos os usuários.</summary>
    CommonStartup,
}

/// <summary>Relógio (injetável para testes).</summary>
public interface IClock
{
    /// <summary>Data/hora atual em UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Informações do ambiente do Windows.</summary>
public interface IEnvironmentInfo
{
    /// <summary>Caminho de uma pasta conhecida.</summary>
    string GetFolder(KnownFolder folder);

    /// <summary>Número de build do Windows (ex.: 22631).</summary>
    int OsBuild { get; }

    /// <summary>Raiz do disco do sistema (ex.: <c>C:\</c>).</summary>
    string SystemDrive { get; }

    /// <summary>Nome do usuário atual (usado só para anonimizar relatórios).</summary>
    string UserName { get; }

    /// <summary>Nome da máquina (usado só para anonimizar relatórios).</summary>
    string MachineName { get; }
}
