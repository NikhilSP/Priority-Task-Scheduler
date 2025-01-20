using PriorityTaskScheduler.Enums;

namespace PriorityTaskScheduler.Contract;

public class TaskWrapper<T>(Guid id, Delegate task, T taskInput, TaskPriority priority)
{
    public Guid Id { get; } = id;

    public TaskPriority Priority { get; } = priority;

    public T TaskInput { get; } = taskInput;

    public Delegate Task { get; } = task;

    public CancellationTokenSource CancellationTokenSource { get; } = new ();
}