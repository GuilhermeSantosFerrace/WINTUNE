namespace WinTune.Platform;

/// <summary>
/// Marcador do assembly <c>WinTune.Platform</c>. Único lugar autorizado a tocar o Windows
/// (registro, processos, WMI, serviços, sistema de arquivos), sempre atrás de interfaces.
/// </summary>
public static class PlatformAssembly
{
    /// <summary>Nome lógico da camada, usado em logs e diagnósticos.</summary>
    public const string LayerName = "Platform";
}
