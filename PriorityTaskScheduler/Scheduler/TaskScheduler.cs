using System.Collections.Concurrent;
using PriorityTaskScheduler.Contract;
using PriorityTaskScheduler.Enums;
using PriorityTaskScheduler.Model;

namespace PriorityTaskScheduler.Scheduler;

public class TaskScheduler<TInput, TResult>  : IScheduler<TInput, TResult> , ISchedulerMetrics, IDisposable
{
    private readonly PriorityQueue<TaskWrapper<TInput, TResult>, TaskPriority> _taskQueue = new();
    private readonly ConcurrentDictionary<Guid, TaskWrapper<TInput, TResult>> _tasks = new();
    private readonly CancellationTokenSource _schedulerCts = new();
    private readonly object _lockObject = new();
    private Task? _processingTask;
    private bool _disposed;
    
    private readonly Dictionary<string, SemaphoreSlim> _groupSemaphores = new();
    private readonly List<TaskWrapper<TInput,TResult>> _periodicTasks = new();
    private readonly ConcurrentDictionary<Guid, Task> _runningTasks = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<TaskResult<TResult>>> _taskResults = new();
    
    public event EventHandler<(Guid TaskId, Exception Exception)>? TaskFailed;
    public event EventHandler<Guid>? TaskCompleted;
    public event EventHandler<Guid>? TaskStarted;
    
    public int QueuedTasksCount => _taskQueue.Count;
    public int RunningTasksCount => _runningTasks.Count;

    public Guid ScheduleTask(Delegate task, TaskPriority priority, TInput input, TaskSchedulingOptions? options = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TaskScheduler<TInput, TResult>));

        if (!IsValidTaskType(task))
        {
            throw new ArgumentException("Invalid task type. Must be Action<TInput>, " +
                                        "Func<TInput, Task>, Func<TInput, Task<TResult>>, or Func<TInput, TResult>", nameof(task));
        }

        options ??= new TaskSchedulingOptions();
        var taskId = Guid.NewGuid();
        var taskWrapper = new TaskWrapper<TInput, TResult>(taskId, task, input, priority, options);

        lock (_lockObject)
        {
            if (options.ScheduledStartTime.HasValue && options.ScheduledStartTime.Value > DateTime.UtcNow)
            {
                ScheduleDelayedTask(taskWrapper);
            }
            else if (options.IsPeriodic && options.Period.HasValue)
            {
                _periodicTasks.Add(taskWrapper);
                StartPeriodicTask(taskWrapper);
            }
            else
            {
                _taskQueue.Enqueue(taskWrapper, priority);
            }
            
            _tasks.TryAdd(taskId, taskWrapper);
        }

        return taskId;
    }
    
    private bool IsValidTaskType(Delegate task)
    {
        return task switch
        {
            Action<TInput> => true,
            Func<TInput, Task<TResult>> => true,
            Func<TInput, Task> => true,
            Func<TInput, TResult> => true,
            _ => false
        };
    }
    
    public void CancelTask(Guid taskId)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TaskScheduler<TInput, TResult>));

        lock (_lockObject)
        {
            if (_tasks.TryGetValue(taskId, out var taskWrapper))
            {
                taskWrapper.CancellationTokenSource.Cancel();
                _tasks.TryRemove(taskId,out _);
                Console.WriteLine($"Task {taskWrapper.Id} was canceled.");
            }
        }
    }

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TaskScheduler<TInput, TResult>));

        if (_processingTask != null)
             throw new InvalidOperationException("Scheduler is already running.");

        _processingTask = ProcessTasksAsync();
    }

    private async Task ProcessTasksAsync()
    {
        while (!_schedulerCts.Token.IsCancellationRequested)
        {
            TaskWrapper<TInput, TResult>? taskWrapper = null;
            
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

     private async void ScheduleDelayedTask(TaskWrapper<TInput, TResult> taskWrapper)
    {
        var delay = taskWrapper.Options.ScheduledStartTime!.Value - DateTime.UtcNow;
        await Task.Delay(delay);
        
        lock (_lockObject)
        {
            if (!_tasks.ContainsKey(taskWrapper.Id)) return; // Task was cancelled
            _taskQueue.Enqueue(taskWrapper, taskWrapper.Priority);
        }
    }

    private async void StartPeriodicTask(TaskWrapper<TInput, TResult> taskWrapper)
    {
        while (!taskWrapper.CancellationTokenSource.Token.IsCancellationRequested)
        {
            await ExecuteTaskAsync(taskWrapper);
            await Task.Delay(taskWrapper.Options.Period!.Value, taskWrapper.CancellationTokenSource.Token);
        }
    }

    private async Task ExecuteTaskAsync(TaskWrapper<TInput, TResult> taskWrapper)
    {
        var semaphore = GetOrCreateGroupSemaphore(taskWrapper.Options.GroupName);
        await semaphore.WaitAsync();

        // Create a TaskCompletionSource for this task
        var tcs = new TaskCompletionSource<TaskResult<TResult>>();
        _taskResults.TryAdd(taskWrapper.Id, tcs);

        try
        {
            taskWrapper.Metadata.StartTime = DateTime.UtcNow;
            taskWrapper.Metadata.Status = TaskStatus.Running;
            TaskStarted?.Invoke(this, taskWrapper.Id);

            using var timeoutCts = taskWrapper.Options.Timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(
                    taskWrapper.CancellationTokenSource.Token,
                    _schedulerCts.Token)
                : null;

            if (timeoutCts != null)
                timeoutCts.CancelAfter(taskWrapper.Options.Timeout!.Value);

            var executionTask =  timeoutCts != null ? ExecuteWithRetries(taskWrapper, timeoutCts.Token) : ExecuteWithRetries(taskWrapper, _schedulerCts.Token);
            _runningTasks.TryAdd(taskWrapper.Id, executionTask);
            
            await executionTask;

            taskWrapper.Metadata.CompletionTime = DateTime.UtcNow;
            taskWrapper.Metadata.Status = TaskStatus.RanToCompletion;
            

            // Set the result for the TaskCompletionSource
            var result = new TaskResult<TResult>
            {
                Value = executionTask.Result,
                TaskMetadata = taskWrapper.Metadata
            };
            tcs.TrySetResult(result);
            
            TaskCompleted?.Invoke(this, taskWrapper.Id);
        }
        catch (Exception ex)
        {
            taskWrapper.Metadata.Status = TaskStatus.Faulted;
            taskWrapper.Metadata.Exceptions.Add(ex);
            TaskFailed?.Invoke(this, (taskWrapper.Id, ex));
        
            // Set the exception for the TaskCompletionSource
            tcs.TrySetException(ex);
            throw;
        }
        finally
        {
            _runningTasks.TryRemove(taskWrapper.Id, out _);
            semaphore.Release();
        }
    }

    private async Task<TResult?> ExecuteWithRetries(TaskWrapper<TInput, TResult> taskWrapper, CancellationToken cancellationToken)
    {
        var retryCount = 0;
        
        while (true)
        {
            try
            {
                switch (taskWrapper.Task)
                {
                    case Action<TInput> action:
                        await Task.Run(() => action(taskWrapper.TaskInput), cancellationToken);
                        return default;
                        
                    case Func<TInput, Task<TResult>> asyncFunc:
                        return await asyncFunc(taskWrapper.TaskInput);
                
                    case Func<TInput, Task> asyncFunc:
                        await asyncFunc(taskWrapper.TaskInput);
                        return default;
                        
                    case Func<TInput, TResult> func:
                        return await Task.Run(() => func(taskWrapper.TaskInput), cancellationToken);
                        
                    default:
                        throw new InvalidOperationException("Unsupported task type.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException 
                                       && retryCount < taskWrapper.Options.MaxRetries)
            {
                retryCount++;
                taskWrapper.Metadata.RetryCount = retryCount;
                taskWrapper.Metadata.Exceptions.Add(ex);
                
                if (retryCount < taskWrapper.Options.MaxRetries)
                {
                    await Task.Delay(taskWrapper.Options.RetryDelay, cancellationToken);
                }
                else throw;
            }
        }
    }
    
    public async Task<TaskResult<TResult>> GetTaskResult(Guid taskId)
    {
        if (_taskResults.TryGetValue(taskId, out var tcs))
        {
            return await tcs.Task;
        }
        
        if (_tasks.TryGetValue(taskId, out var task) && task.Result != null)
        {
            return task.Result;
        }
        
        throw new KeyNotFoundException($"No task found with ID {taskId}");
    }

    private SemaphoreSlim GetOrCreateGroupSemaphore(string? groupName)
    {
        if (string.IsNullOrEmpty(groupName)) 
            return new SemaphoreSlim(int.MaxValue);

        lock (_lockObject)
        {
            if (!_groupSemaphores.TryGetValue(groupName, out var semaphore))
            {
                var maxConcurrent = _tasks.Values
                    .FirstOrDefault(t => t.Options.GroupName == groupName)
                    ?.Options.MaxConcurrentTasks ?? int.MaxValue;
                
                semaphore = new SemaphoreSlim(maxConcurrent);
                _groupSemaphores[groupName] = semaphore;
            }
            return semaphore;
        }
    }

    public IEnumerable<TaskMetadata> GetTaskMetrics() =>
        _tasks.Values.Select(t => t.Metadata).ToList();

    public TaskMetadata? GetTaskMetrics(Guid taskId) =>
        _tasks.TryGetValue(taskId, out var task) ? task.Metadata : null;

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