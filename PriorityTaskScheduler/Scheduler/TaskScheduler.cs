using PriorityTaskScheduler.Contract;
using PriorityTaskScheduler.Enums;

namespace PriorityTaskScheduler.Scheduler;

public class TaskScheduler<T> : IScheduler<T>
{
    private readonly PriorityQueue<TaskWrapper<T>, TaskPriority> _taskQueue = new();
    private readonly Dictionary<Guid, TaskWrapper<T>> _tasks = new();


    public Guid ScheduleTask(Delegate task, TaskPriority priority, T input)
    {
        if (task is not (Action<T> or Func<T, Task> or Func<T, object>))
        {
            throw new ArgumentException("Invalid task type.", nameof(task));
        }

        var taskId = Guid.NewGuid();
        var taskWrapper = new TaskWrapper<T>(taskId, task, input, priority);
        _taskQueue.Enqueue(taskWrapper, priority);
        _tasks.Add(taskId, taskWrapper);
        return taskId;
    }

    public void CancelTask(Guid taskId)
    {
        if (_tasks.ContainsKey(taskId))
        {
            // cancel task if running
            _tasks.Remove(taskId);
        }
    }

    public void Start()
    {
        Task.Run(ProcessTasks);
    }

    private void ProcessTasks()
    {
        while (true)
        {
            if (_taskQueue.Count > 0)
            {
                var taskWrapper = _taskQueue.Dequeue();
                if (_tasks.ContainsKey(taskWrapper.Id))
                {
                    _tasks.Remove(taskWrapper.Id);
                    ExecuteTask(taskWrapper);
                }
            }
            else
            {
                // improve
                Task.Delay(100).Wait();
            }
        }
    }

    private void ExecuteTask(TaskWrapper<T> taskWrapper)
    {
        try
        {
            switch (taskWrapper.Task)
            {
                case Action<T> action:
                    action(taskWrapper.TaskInput);
                    break;
                case Func<T, Task> asyncFunc:
                    asyncFunc(taskWrapper.TaskInput).Wait(); // improve
                    break;
                case Func<T, object> func:
                    func(taskWrapper.TaskInput);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported task type.");
            }
        }
        catch (Exception ex)
        {
            // log, retry, etc.
            Console.WriteLine($"Task {taskWrapper.Id} failed: {ex.Message}");
        }
    }
}