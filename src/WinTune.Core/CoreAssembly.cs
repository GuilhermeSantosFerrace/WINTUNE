namespace WinTune.Core;

/// <summary>
/// Marcador do assembly <c>WinTune.Core</c>. Camada de domínio sem dependência de UI
/// e sem acesso direto ao Windows (tudo passa pelas interfaces de <c>WinTune.Platform</c>).
/// </summary>
public static class CoreAssembly
{
    /// <summary>Nome lógico da camada, usado em logs e diagnósticos.</summary>
    public const string LayerName = "Core";
}
