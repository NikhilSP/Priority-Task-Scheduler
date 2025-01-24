using PriorityTaskScheduler.Model;

namespace PriorityTaskScheduler.Contract;

public class TaskResult<TResult>
{
    public TResult? Value { get; set; } 
    public bool HasValue => Value != null;
    public TaskMetadata? TaskMetadata { get; set; }

    public static TaskResult<TResult> From(TResult value) => new() { Value = value };
    public static TaskResult<TResult> Empty() => new() { };
}