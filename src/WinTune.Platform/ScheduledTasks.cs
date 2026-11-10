using System.Runtime.InteropServices;
using WinTune.Platform.Abstractions;

namespace WinTune.Platform;

/// <summary>Acesso ao Agendador de Tarefas via COM (<c>Schedule.Service</c>).</summary>
public sealed class ScheduledTasks : IScheduledTasks
{
    private const int TaskEnumHidden = 1;
    private const int TriggerLogon = 9;
    private const int ActionExec = 0;

    /// <inheritdoc />
    public IReadOnlyList<ScheduledTaskInfo> GetTasks()
    {
        var result = new List<ScheduledTaskInfo>();
        dynamic service = Connect();
        try
        {
            Walk(service.GetFolder("\\"), result, depth: 0);
        }
        finally
        {
            Marshal.FinalReleaseComObject(service);
        }

        return result;
    }

    /// <inheritdoc />
    public void SetEnabled(string taskPath, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskPath);
        if (!taskPath.StartsWith('\\') || taskPath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Caminho de tarefa inválido.", nameof(taskPath));
        }

        var idx = taskPath.LastIndexOf('\\');
        var folderPath = idx <= 0 ? "\\" : taskPath[..idx];
        var name = taskPath[(idx + 1)..];
        dynamic service = Connect();
        try
        {
            dynamic folder = service.GetFolder(folderPath);
            dynamic task = folder.GetTask(name);
            task.Enabled = enabled;
        }
        finally
        {
            Marshal.FinalReleaseComObject(service);
        }
    }

    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }

    private static void Walk(dynamic folder, List<ScheduledTaskInfo> result, int depth)
    {
        if (depth > 16)
        {
            return;
        }

        foreach (var task in folder.GetTasks(TaskEnumHidden))
        {
            try
            {
                var def = task.Definition;
                var logon = false;
                foreach (var trigger in def.Triggers)
                {
                    if ((int)trigger.Type == TriggerLogon)
                    {
                        logon = true;
                    }
                }

                var actions = new List<string>();
                foreach (var action in def.Actions)
                {
                    if ((int)action.Type == ActionExec && action.Path is string p)
                    {
                        actions.Add(p);
                    }
                }

                result.Add(new ScheduledTaskInfo((string)task.Path, (string)task.Name, (bool)task.Enabled,
                    def.RegistrationInfo.Author as string, logon, actions));
            }
            catch (COMException)
            {
                // Tarefa sem permissão de leitura: ignorada.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        foreach (var sub in folder.GetFolders(0))
        {
            try
            {
                Walk(sub, result, depth + 1);
            }
            catch (COMException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
