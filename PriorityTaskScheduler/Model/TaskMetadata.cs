namespace PriorityTaskScheduler.Model;

public class TaskMetadata
{
    public DateTime ScheduledTime { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? CompletionTime { get; set; }
    public TaskStatus Status { get; set; }
    public int RetryCount { get; set; }
    public List<Exception> Exceptions { get; } = new();
    public TimeSpan? ExecutionDuration => CompletionTime - StartTime;
}