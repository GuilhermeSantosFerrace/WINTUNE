using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using WinTune.Core.Backup;
using WinTune.Core.Tweaks;
using WinTune.Presentation;
using WinTune.Presentation.ViewModels;
using WinTune.TestDoubles;
using Xunit;

namespace WinTune.App.Tests;

/// <summary>Carrega a XAML real (recursos, estilos, janela) e falha em qualquer erro de binding.</summary>
public class XamlSmokeTests
{
    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message is not null)
            {
                Errors.Add(message);
            }
        }
    }

    private sealed class NoDialogs : IDialogService
    {
        public bool Confirm(string title, string message) => false;

        public void Inform(string title, string message)
        {
        }
    }

    [Fact]
    public void MainWindow_LoadsAllTabs_WithoutBindingErrors()
    {
        Exception? failure = null;
        var errors = new List<string>();
        var thread = new Thread(() =>
        {
            try
            {
                var listener = new BindingErrorListener();
                PresentationTraceSources.Refresh();
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

                var app = new App();
                app.InitializeComponent();

                var registry = new FakeRegistry();
                var runner = new FakeProcessRunner();
                var fs = new FakeFileSystem();
                var clock = new FakeClock();
                var services = new AppServices
                {
                    Registry = registry,
                    Runner = runner,
                    Wmi = new FakeWmiReader(),
                    FileSystem = fs,
                    Environment = new FakeEnvironment(),
                    Clock = clock,
                    Services = new FakeServiceControl(),
                    Processes = new FakeProcessList(),
                    Shell = new FakeShellLauncher(),
                    Backup = BackupStore.Open(fs, clock, @"C:\ProgramData\WinTune\backup.json"),
                    Catalog = new TweakCatalog(registry, runner),
                };
                Services.ThemeManager.Apply(app, registry);
                var vm = new MainViewModel(services, new NoDialogs());
                vm.RefreshRecommendationsAsync().GetAwaiter().GetResult();

                var window = new MainWindow { DataContext = vm };
                var tabs = (TabControl)window.FindName("Tabs");
                foreach (TabItem tab in tabs.Items)
                {
                    tabs.SelectedItem = tab;
                    window.Measure(new Size(1100, 760));
                    window.Arrange(new Rect(0, 0, 1100, 760));
                    window.UpdateLayout();
                }

                errors.AddRange(listener.Errors);
                window.Close();
                app.Shutdown();
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue();

        failure.Should().BeNull();
        errors.Should().BeEmpty();
    }
}
