namespace WinTune.Platform.Abstractions;

/// <summary>Tipo de inicialização de um serviço.</summary>
public enum ServiceStartMode
{
    /// <summary>Desconhecido.</summary>
    Unknown,

    /// <summary>Boot (driver).</summary>
    Boot,

    /// <summary>System (driver).</summary>
    System,

    /// <summary>Automático.</summary>
    Automatic,

    /// <summary>Manual.</summary>
    Manual,

    /// <summary>Desabilitado.</summary>
    Disabled,
}

/// <summary>Configuração de inicialização de um serviço.</summary>
/// <param name="StartMode">Tipo de inicialização.</param>
/// <param name="DelayedAutoStart">Automático (atraso na inicialização).</param>
public sealed record ServiceConfig(ServiceStartMode StartMode, bool DelayedAutoStart);

/// <summary>Informações de um serviço instalado.</summary>
/// <param name="Name">Nome interno do serviço.</param>
/// <param name="DisplayName">Nome de exibição.</param>
/// <param name="State">Estado (ex.: Running, Stopped).</param>
/// <param name="Config">Configuração de inicialização.</param>
/// <param name="ImagePath">Linha de comando do executável.</param>
/// <param name="ProcessId">PID do processo hospedeiro (0 se parado).</param>
public sealed record ServiceInfo(string Name, string DisplayName, string State, ServiceConfig Config, string? ImagePath, int ProcessId);

/// <summary>Leitura e alteração (somente tipo de inicialização) de serviços. Nunca para ou inicia serviços.</summary>
public interface IServiceControl
{
    /// <summary>Lista os serviços Win32 instalados.</summary>
    IReadOnlyList<ServiceInfo> GetServices();

    /// <summary>Lê a configuração de inicialização; <c>null</c> se o serviço não existir.</summary>
    ServiceConfig? GetConfig(string serviceName);

    /// <summary>Altera o tipo de inicialização (não altera o estado de execução).</summary>
    void SetConfig(string serviceName, ServiceConfig config);
}
