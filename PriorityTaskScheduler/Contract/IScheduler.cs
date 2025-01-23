using PriorityTaskScheduler.Enums;
using PriorityTaskScheduler.Model;

namespace PriorityTaskScheduler.Contract;

public interface IScheduler<TInput, TResult>
{
    Guid ScheduleTask(Delegate task, TaskPriority priority, TInput input, TaskSchedulingOptions? options = null);
    void CancelTask(Guid taskId);

    void Start();
    
    Task<TaskResult<TResult>> GetTaskResult(Guid taskId);
}