namespace WinTune.Platform.Abstractions;

/// <summary>Tarefa agendada (somente os campos que o WinTune usa).</summary>
/// <param name="Path">Caminho completo (ex.: <c>\GoogleUpdateTaskMachineUA</c>).</param>
/// <param name="Name">Nome.</param>
/// <param name="Enabled">Habilitada.</param>
/// <param name="Author">Autor declarado.</param>
/// <param name="HasLogonTrigger">Tem gatilho "Ao fazer logon".</param>
/// <param name="ActionPaths">Executáveis das ações.</param>
public sealed record ScheduledTaskInfo(string Path, string Name, bool Enabled, string? Author, bool HasLogonTrigger, IReadOnlyList<string> ActionPaths);

/// <summary>Agendador de Tarefas: leitura e habilitar/desabilitar (nunca cria, apaga ou executa tarefas).</summary>
public interface IScheduledTasks
{
    /// <summary>Lista todas as tarefas (inclui ocultas).</summary>
    IReadOnlyList<ScheduledTaskInfo> GetTasks();

    /// <summary>Habilita ou desabilita uma tarefa existente.</summary>
    void SetEnabled(string taskPath, bool enabled);
}
