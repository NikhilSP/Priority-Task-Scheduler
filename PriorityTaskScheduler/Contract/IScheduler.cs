using PriorityTaskScheduler.Enums;

namespace PriorityTaskScheduler.Contract;

public interface IScheduler<in T>
{
    Guid ScheduleTask(Delegate task, TaskPriority priority, T input);
    void CancelTask(Guid taskId);

    void Start();
}