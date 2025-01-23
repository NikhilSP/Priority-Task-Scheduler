using PriorityTaskScheduler.Enums;
using PriorityTaskScheduler.Model;

namespace PriorityTaskScheduler.Contract;

public class TaskWrapper<TInput, TResult>
{
    public TaskWrapper(Guid id, Delegate task, TInput taskInput, TaskPriority priority, TaskSchedulingOptions options)
    {
        Id = id;
        Task = task;
        TaskInput = taskInput;
        Priority = priority;
        Options = options;
        Metadata = new TaskMetadata { ScheduledTime = DateTime.UtcNow };
    }

    public Guid Id { get; }
    public TaskPriority Priority { get; }
    public TInput TaskInput { get; }
    public Delegate Task { get; }
    public CancellationTokenSource CancellationTokenSource { get; } = new();
    public TaskMetadata Metadata { get; }
    public TaskSchedulingOptions Options { get; }
    public TaskResult<TResult>? Result { get; set; }
}