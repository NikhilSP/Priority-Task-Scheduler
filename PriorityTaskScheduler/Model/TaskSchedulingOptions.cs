namespace PriorityTaskScheduler.Model;


public class TaskSchedulingOptions
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan? Timeout { get; set; }
    public DateTime? ScheduledStartTime { get; set; }
    public bool IsPeriodic { get; set; }
    public TimeSpan? Period { get; set; }
    public int? MaxConcurrentTasks { get; set; }
    public string? GroupName { get; set; }
}