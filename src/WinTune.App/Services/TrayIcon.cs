using System.Windows;
using Forms = System.Windows.Forms;

namespace WinTune.App.Services;

/// <summary>
/// Ícone na bandeja (somente quando o modo jogo automático está ligado). Mostra o status, reabre a janela e desliga o modo.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Window _window;

    public TrayIcon(Window window, Action disableGameMode)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        ArgumentNullException.ThrowIfNull(disableGameMode);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir WinTune", null, (_, _) => Restore());
        menu.Items.Add("Desligar modo jogo automático", null, (_, _) => disableGameMode());
        _icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "WinTune — modo jogo automático ligado",
            ContextMenuStrip = menu,
            Visible = false,
        };
        _icon.DoubleClick += (_, _) => Restore();
        _window.StateChanged += (_, _) =>
        {
            // Com o modo jogo ligado, minimizar esconde na bandeja (o monitoramento continua).
            if (_icon.Visible && _window.WindowState == WindowState.Minimized)
            {
                _window.Hide();
            }
        };
    }

    public void SetVisible(bool visible) => _icon.Visible = visible;

    public void SetStatus(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
