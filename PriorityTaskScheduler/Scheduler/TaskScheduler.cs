using PriorityTaskScheduler.Contract;
using PriorityTaskScheduler.Enums;

namespace PriorityTaskScheduler.Scheduler;

public class TaskScheduler<T> : IScheduler<T>, IDisposable
{
    private readonly PriorityQueue<TaskWrapper<T>, TaskPriority> _taskQueue = new();
    private readonly Dictionary<Guid, TaskWrapper<T>> _tasks = new();
    private readonly CancellationTokenSource _schedulerCts = new();
    private readonly object _lockObject = new();
    private Task? _processingTask;
    private bool _disposed;

    public Guid ScheduleTask(Delegate task, TaskPriority priority, T input)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TaskScheduler<T>));

        if (task is not (Action<T> or Func<T, Task> or Func<T, int>))
        {
            throw new ArgumentException("Invalid task type.", nameof(task));
        }

        var taskId = Guid.NewGuid();
        var taskWrapper = new TaskWrapper<T>(taskId, task, input, priority);
        
        lock (_lockObject)
        {
            _taskQueue.Enqueue(taskWrapper, priority);
            _tasks.Add(taskId, taskWrapper);
        }
        
        return taskId;
    }

    public void CancelTask(Guid taskId)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TaskScheduler<T>));

        lock (_lockObject)
        {
            if (_tasks.TryGetValue(taskId, out var taskWrapper))
            {
                taskWrapper.CancellationTokenSource.Cancel();
                _tasks.Remove(taskId);
                Console.WriteLine($"Task {taskWrapper.Id} was canceled.");
            }
        }
    }

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TaskScheduler<T>));

        if (_processingTask != null)
             throw new InvalidOperationException("Scheduler is already running.");

        _processingTask = ProcessTasksAsync();
    }

    private async Task ProcessTasksAsync()
    {
        while (!_schedulerCts.Token.IsCancellationRequested)
        {
            TaskWrapper<T>? taskWrapper = null;
            
            lock (_lockObject)
            {
                if (_taskQueue.Count > 0)
                {
                    taskWrapper = _taskQueue.Dequeue();
                }
            }

            if (taskWrapper == null)
            {
                await Task.Delay(100, _schedulerCts.Token);
                continue;
            }

            if (_tasks.ContainsKey(taskWrapper.Id))
            {
                _ = ExecuteTaskAsync(taskWrapper);
            }
        }
    }

    private async Task ExecuteTaskAsync(TaskWrapper<T> taskWrapper)
    {
        try
        {
            if (taskWrapper.CancellationTokenSource.IsCancellationRequested)
                return;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                taskWrapper.CancellationTokenSource.Token,
                _schedulerCts.Token);

            switch (taskWrapper.Task)
            {
                case Action<T> action:
                    await Task.Run(() => action(taskWrapper.TaskInput), linkedCts.Token);
                    break;
                    
                case Func<T, Task> asyncFunc:
                    await asyncFunc(taskWrapper.TaskInput);
                    break;
                    
                case Func<T, int> func:
                    await Task.Run(() => func(taskWrapper.TaskInput), linkedCts.Token);
                    break;
                    
                default:
                    throw new InvalidOperationException("Unsupported task type.");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Task {taskWrapper.Id} was canceled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Task {taskWrapper.Id} failed: {ex.Message}");
        }
        finally
        {
            lock (_lockObject)
            {
                _tasks.Remove(taskWrapper.Id);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _schedulerCts.Cancel();
        _schedulerCts.Dispose();
        
        foreach (var task in _tasks.Values)
        {
            task.CancellationTokenSource.Cancel();
            task.CancellationTokenSource.Dispose();
        }
        
        _tasks.Clear();
        
        while (_taskQueue.Count > 0)
        {
            var task = _taskQueue.Dequeue();
            task.CancellationTokenSource.Dispose();
        }
    }
}