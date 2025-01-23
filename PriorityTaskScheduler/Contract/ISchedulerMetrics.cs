using PriorityTaskScheduler.Model;

namespace PriorityTaskScheduler.Contract;

public interface ISchedulerMetrics
{
    int QueuedTasksCount { get; }
    int RunningTasksCount { get; }
    IEnumerable<TaskMetadata> GetTaskMetrics();
    TaskMetadata? GetTaskMetrics(Guid taskId);
}