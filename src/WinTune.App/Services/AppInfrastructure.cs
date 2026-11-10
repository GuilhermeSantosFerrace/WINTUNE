using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;
using WinTune.Platform.Abstractions;
using WinTune.Presentation;

namespace WinTune.App.Services;

/// <summary>Diálogos com MessageBox.</summary>
public sealed class MessageBoxDialogService : IDialogService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current?.MainWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
        == MessageBoxResult.Yes;

    public void Inform(string title, string message) =>
        MessageBox.Show(Application.Current?.MainWindow!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}

/// <summary>Seleciona tema claro/escuro (configuração do Windows) ou alto contraste.</summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static void Apply(Application app, IRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(registry);
        string theme;
        if (SystemParameters.HighContrast)
        {
            theme = "HighContrast";
        }
        else
        {
            int? light;
            try
            {
                light = registry.GetDWord(RegistryHive.CurrentUser, PersonalizeKey, "AppsUseLightTheme");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                light = null;
            }

            theme = light == 0 ? "Dark" : "Light";
        }

        var dictionaries = app.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.Contains("Themes/", StringComparison.Ordinal) == true &&
                                                       !d.Source.OriginalString.EndsWith("Styles.xaml", StringComparison.Ordinal));
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/WinTune;component/Themes/{theme}.xaml", UriKind.Absolute),
        };
        if (current is null)
        {
            dictionaries.Insert(1, replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(current)] = replacement;
        }
    }
}

/// <summary>Logger em arquivo rotativo em <c>%LOCALAPPDATA%\WinTune\logs</c> (1 MB por arquivo, mantém 10).</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 1024 * 1024;
    private const int MaxFiles = 10;
    private readonly string _directory;
    private readonly object _gate = new();

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                var path = Path.Combine(_directory, "wintune.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    Rotate(path);
                }

                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Log nunca derruba o app.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void Rotate(string path)
    {
        for (var i = MaxFiles - 1; i >= 1; i--)
        {
            var src = Path.Combine(_directory, $"wintune.{i}.log");
            var dst = Path.Combine(_directory, $"wintune.{i + 1}.log");
            if (File.Exists(src))
            {
                File.Move(src, dst, overwrite: true);
            }
        }

        File.Move(path, Path.Combine(_directory, "wintune.1.log"), overwrite: true);
        var overflow = Path.Combine(_directory, $"wintune.{MaxFiles + 1}.log");
        if (File.Exists(overflow))
        {
            File.Delete(overflow);
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = string.Create(CultureInfo.InvariantCulture,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{logLevel}] {category}: {formatter(state, exception)}{(exception is null ? string.Empty : " | " + exception)}");
            provider.Write(line);
        }
    }
}
