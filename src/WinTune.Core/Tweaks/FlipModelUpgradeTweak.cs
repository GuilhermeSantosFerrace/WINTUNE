using WinTune.Core.Domain;
using WinTune.Platform.Abstractions;

namespace WinTune.Core.Tweaks;

/// <summary>
/// Parser da string <c>DirectXUserGlobalSettings</c> (formato <c>Chave=Valor;Chave=Valor;</c>), preservando ordem e flags.
/// </summary>
public sealed class DirectXSettingsString
{
    private readonly List<KeyValuePair<string, string>> _pairs = [];

    /// <summary>Faz o parsing (null/vazio = sem flags).</summary>
    public static DirectXSettingsString Parse(string? text)
    {
        var result = new DirectXSettingsString();
        foreach (var part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            result.Set(part[..eq].Trim(), part[(eq + 1)..].Trim());
        }

        return result;
    }

    /// <summary>Quantidade de flags.</summary>
    public int Count => _pairs.Count;

    /// <summary>Lê uma flag.</summary>
    public string? Get(string key) =>
        _pairs.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>Define (ou substitui) uma flag, mantendo a posição.</summary>
    public void Set(string key, string value)
    {
        var i = _pairs.FindIndex(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
        {
            _pairs[i] = new KeyValuePair<string, string>(_pairs[i].Key, value);
        }
        else
        {
            _pairs.Add(new KeyValuePair<string, string>(key, value));
        }
    }

    /// <summary>Remove uma flag.</summary>
    public void Remove(string key) => _pairs.RemoveAll(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public override string ToString() => string.Concat(_pairs.Select(p => $"{p.Key}={p.Value};"));
}

/// <summary>
/// "Otimizações para jogos em janela": liga <c>SwapEffectUpgradeEnable=1</c> preservando as demais flags.
/// Aplicabilidade restrita (DX10/DX11 em janela/sem borda).
/// </summary>
public sealed class FlipModelUpgradeTweak : RegistryTweak
{
    /// <summary>Chave de preferências de GPU do usuário.</summary>
    public const string UserGpuPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>Nome do valor global.</summary>
    public const string GlobalSettingsName = "DirectXUserGlobalSettings";

    /// <summary>Flag controlada.</summary>
    public const string Flag = "SwapEffectUpgradeEnable";

    /// <summary>Cria o ajuste.</summary>
    public FlipModelUpgradeTweak(IRegistry registry)
        : base(registry, new TweakInfo
        {
            Id = "flip-model-upgrade",
            Name = "Otimizações para jogos em janela (flip model)",
            Description = "Faz jogos DirectX 10/11 em modo janela ou janela sem borda usarem o modelo de apresentação \"flip\", que " +
                          "reduz latência e permite VRR. Não afeta tela cheia exclusiva nem jogos DirectX 12: no Siege (DX12) " +
                          "praticamente não se aplica; no CS2, só se você jogar em janela sem borda. Desmarcado por padrão.",
            For = [Focus.Gaming],
            Helps = Scenario.Stutter | Scenario.LowFps,
            Priority = 30,
            DefaultSelected = false,
        },
        [
            new RegistrySetting(RegistryHive.CurrentUser, UserGpuPreferencesKey, GlobalSettingsName,
                desired: current =>
                {
                    var s = ParseCurrent(current);
                    s.Set(Flag, "1");
                    return RegistryValue.FromString(s.ToString());
                },
                isSatisfied: current => current?.Kind == RegistryValueKind.String && ParseCurrent(current).Get(Flag) == "1",
                mergeRestore: (current, original) =>
                {
                    // Restaura apenas a nossa flag, preservando o que outros mudaram depois.
                    if (current is not null && current.Kind != RegistryValueKind.String)
                    {
                        return current;
                    }

                    var s = DirectXSettingsString.Parse(current?.Text);
                    var originalFlag = original?.Kind == RegistryValueKind.String ? DirectXSettingsString.Parse(original.Text).Get(Flag) : null;
                    if (originalFlag is null)
                    {
                        s.Remove(Flag);
                    }
                    else
                    {
                        s.Set(Flag, originalFlag);
                    }

                    if (s.Count == 0)
                    {
                        return original is null ? null : RegistryValue.FromString(string.Empty);
                    }

                    return RegistryValue.FromString(s.ToString());
                }),
        ])
    {
    }

    private static DirectXSettingsString ParseCurrent(RegistryValue? current)
    {
        if (current is null)
        {
            return DirectXSettingsString.Parse(null);
        }

        if (current.Kind != RegistryValueKind.String)
        {
            throw new InvalidOperationException($"{GlobalSettingsName} existe com tipo inesperado ({current.Kind}); nada foi alterado.");
        }

        return DirectXSettingsString.Parse(current.Text);
    }
}
