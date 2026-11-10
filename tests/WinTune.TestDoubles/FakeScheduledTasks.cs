using WinTune.Platform.Abstractions;

namespace WinTune.TestDoubles;

/// <summary>Agendador simulado.</summary>
public sealed class FakeScheduledTasks : IScheduledTasks
{
    public Dictionary<string, ScheduledTaskInfo> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FakeScheduledTasks Add(string path, bool enabled = true, bool logon = false, string? author = null, params string[] actions)
    {
        Tasks[path] = new ScheduledTaskInfo(path, path.Split('\\')[^1], enabled, author, logon, actions);
        return this;
    }

    public IReadOnlyList<ScheduledTaskInfo> GetTasks() => Tasks.Values.ToList();

    public void SetEnabled(string taskPath, bool enabled)
    {
        if (!Tasks.TryGetValue(taskPath, out var t))
        {
            throw new InvalidOperationException("Tarefa inexistente.");
        }

        Tasks[taskPath] = t with { Enabled = enabled };
    }
}
