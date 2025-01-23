namespace PriorityTaskScheduler.Contract;

public class TaskResult<TResult>
{
    public TResult? Value { get; set; }
    public bool HasValue { get; set; }

    public static TaskResult<TResult> From(TResult value) => new() { Value = value, HasValue = true };
    public static TaskResult<TResult> Empty() => new() { HasValue = false };
}