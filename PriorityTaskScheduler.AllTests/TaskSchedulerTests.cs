using System;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using System.Linq;
using System.Threading;
using PriorityTaskScheduler.Enums;
using PriorityTaskScheduler.Model;
using PriorityTaskScheduler.Scheduler;

public record TestInput(string Data);
public record TestResult(string Data);

public class TaskSchedulerTests : IDisposable
{
    private readonly TaskScheduler<TestInput, TestResult> _scheduler;

    public TaskSchedulerTests()
    {
        _scheduler = new TaskScheduler<TestInput, TestResult>();
        _scheduler.Start();
    }

    public void Dispose()
    {
        _scheduler.Dispose();
    }

    [Fact]
    public async Task ShouldExecuteSimpleTask()
    {
        // Arrange
        var input = new TestInput("test");
        var func = (TestInput i) => new TestResult(i.Data + "_processed");

        // Act
        var taskId = _scheduler.ScheduleTask(func, TaskPriority.High, input);
        var result = await _scheduler.GetTaskResult(taskId);

        // Assert
        result.HasValue.Should().BeTrue();
        result.Value.Data.Should().Be("test_processed");
    }

    [Fact]
    public async Task ShouldExecuteAsyncTask()
    {
        // Arrange
        var input = new TestInput("test");
        Func<TestInput, Task<TestResult>> func = async i =>
        {
            await Task.Delay(100);
            return new TestResult(i.Data + "_async");
        };

        // Act
        var taskId = _scheduler.ScheduleTask(func, TaskPriority.High, input);
        var result = await _scheduler.GetTaskResult(taskId);

        // Assert
        result.HasValue.Should().BeTrue();
        result.Value.Data.Should().Be("test_async");
    }

    [Fact]
    public async Task ShouldHandleVoidTask()
    {
        // Arrange
        var input = new TestInput("test");
        string? capturedData = null;
        Action<TestInput> action = i => capturedData = i.Data;

        // Act
        var taskId = _scheduler.ScheduleTask(action, TaskPriority.High, input);
        var result = await _scheduler.GetTaskResult(taskId);

        // Assert
        result.HasValue.Should().BeFalse();
        capturedData.Should().Be("test");
    }

    [Fact]
    public async Task ShouldRespectTaskPriorities()
    {
        // Arrange
        var results = new List<string>();
        Action<TestInput> createAction(string marker) => _ =>
        {
            Thread.Sleep(100); // Ensure tasks take some time
            results.Add(marker);
        };

        // Act
        _scheduler.ScheduleTask(createAction("low"), TaskPriority.Low, new TestInput(""));
        _scheduler.ScheduleTask(createAction("medium"), TaskPriority.Medium, new TestInput(""));
        _scheduler.ScheduleTask(createAction("high"), TaskPriority.High, new TestInput(""));

        // Wait for all tasks to complete
        await Task.Delay(500);

        // Assert
        results.Should().Equal("high", "medium", "low");
    }

    [Fact]
    public async Task ShouldHandleTaskCancellation()
    {
        // Arrange
        var input = new TestInput("test");
        var taskStarted = new TaskCompletionSource();
        
        Func<TestInput, Task<TestResult>> func = async i =>
        {
            taskStarted.SetResult();
            await Task.Delay(10000); // Long delay
            return new TestResult(i.Data);
        };

        // Act
        var taskId = _scheduler.ScheduleTask(func, TaskPriority.High, input);
        await taskStarted.Task; // Wait for task to start
        _scheduler.CancelTask(taskId);

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _scheduler.GetTaskResult(taskId)
        );
    }

    [Fact]
    public async Task ShouldHandleTaskTimeout()
    {
        // Arrange
        var input = new TestInput("test");
        Func<TestInput, Task<TestResult>> func = async i =>
        {
            await Task.Delay(5000); // Long delay
            return new TestResult(i.Data);
        };

        var options = new TaskSchedulingOptions
        {
            Timeout = TimeSpan.FromMilliseconds(100)
        };

        // Act & Assert
        var taskId = _scheduler.ScheduleTask(func, TaskPriority.High, input, options);
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _scheduler.GetTaskResult(taskId)
        );
    }

    [Fact]
    public async Task ShouldRetryFailedTasks()
    {
        // Arrange
        var input = new TestInput("test");
        var attempts = 0;
        
        Func<TestInput, TestResult> func = i =>
        {
            attempts++;
            if (attempts < 3)
                throw new Exception("Simulated failure");
            return new TestResult(i.Data + "_success");
        };

        var options = new TaskSchedulingOptions
        {
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        };

        // Act
        var taskId = _scheduler.ScheduleTask(func, TaskPriority.High, input, options);
        var result = await _scheduler.GetTaskResult(taskId);

        // Assert
        attempts.Should().Be(3);
        result.Value.Data.Should().Be("test_success");
    }

    [Fact]
    public async Task ShouldRespectConcurrencyLimits()
    {
        // Arrange
        var concurrentTasks = 0;
        var maxConcurrentTasks = 0;
        var lockObj = new object();

        Func<TestInput, Task<TestResult>> createTask() => async i =>
        {
            lock (lockObj)
            {
                concurrentTasks++;
                maxConcurrentTasks = Math.Max(maxConcurrentTasks, concurrentTasks);
            }

            await Task.Delay(100);

            lock (lockObj)
            {
                concurrentTasks--;
            }

            return new TestResult(i.Data);
        };

        var options = new TaskSchedulingOptions
        {
            GroupName = "test-group",
            MaxConcurrentTasks = 2
        };

        // Act
        var tasks = Enumerable.Range(0, 5)
            .Select(i => _scheduler.ScheduleTask(
                createTask(), 
                TaskPriority.High, 
                new TestInput($"task_{i}"), 
                options
            ))
            .ToList();

        await Task.WhenAll(tasks.Select(t => _scheduler.GetTaskResult(t)));

        // Assert
        maxConcurrentTasks.Should().Be(2);
    }

    [Fact]
    public async Task ShouldHandleScheduledTasks()
    {
        // Arrange
        var input = new TestInput("test");
        var executionTime = DateTime.UtcNow;
        
        Func<TestInput, TestResult> func = i =>
        {
            executionTime = DateTime.UtcNow;
            return new TestResult(i.Data);
        };

        var scheduledTime = DateTime.UtcNow.AddMilliseconds(200);
        var options = new TaskSchedulingOptions
        {
            ScheduledStartTime = scheduledTime
        };

        // Act
        var taskId = _scheduler.ScheduleTask(func, TaskPriority.High, input, options);
        var result = await _scheduler.GetTaskResult(taskId);

        // Assert
        executionTime.Should().BeCloseTo(scheduledTime, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task ShouldExecutePeriodicTasks()
    {
        // Arrange
        var executionCount = 0;
        var input = new TestInput("test");
        
        Action<TestInput> action = _ =>
        {
            Interlocked.Increment(ref executionCount);
        };

        var options = new TaskSchedulingOptions
        {
            IsPeriodic = true,
            Period = TimeSpan.FromMilliseconds(100)
        };

        // Act
        var taskId = _scheduler.ScheduleTask(action, TaskPriority.High, input, options);
        await Task.Delay(550); // Wait for ~5 executions
        _scheduler.CancelTask(taskId);

        // Assert
        executionCount.Should().BeInRange(4, 6); // Allow for some timing variance
    }

    [Fact]
    public void ShouldTrackMetrics()
    {
        // Arrange
        var input = new TestInput("test");
        Action<TestInput> action = _ => Thread.Sleep(100);

        // Act
        var taskId = _scheduler.ScheduleTask(action, TaskPriority.High, input);
        var metrics = _scheduler.GetTaskMetrics(taskId);

        // Assert
        metrics.Should().NotBeNull();
        metrics!.ScheduledTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        metrics.Status.Should().Be(TaskStatus.Running);
    }

    [Fact]
    public async Task ShouldHandleExceptionPropagation()
    {
        // Arrange
        var input = new TestInput("test");
        Exception? caughtException = null;
        _scheduler.TaskFailed += (_, info) => caughtException = info.Exception;

        Func<TestInput, TestResult> func = _ => throw new InvalidOperationException("Test exception");

        // Act
        var taskId = _scheduler.ScheduleTask(func, TaskPriority.High, input);
        
        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _scheduler.GetTaskResult(taskId)
        );
        caughtException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Test exception");
    }

    [Fact]
    public void ShouldPreventInvalidTaskTypes()
    {
        // Arrange
        var input = new TestInput("test");
        Func<string, TestResult> invalidFunc = _ => new TestResult("invalid");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            _scheduler.ScheduleTask(invalidFunc, TaskPriority.High, input)
        );
    }

    [Fact]
    public async Task ShouldDisposeCleanly()
    {
        // Arrange
        var completedTasks = 0;
        var input = new TestInput("test");
        
        Func<TestInput, Task<TestResult>> createTask() => async i =>
        {
            await Task.Delay(200);
            Interlocked.Increment(ref completedTasks);
            return new TestResult(i.Data);
        };

        // Act
        var taskIds = Enumerable.Range(0, 5)
            .Select(_ => _scheduler.ScheduleTask(createTask(), TaskPriority.High, input))
            .ToList();

        await Task.Delay(100); // Let some tasks start
        _scheduler.Dispose();

        // Assert
        completedTasks.Should().BeLessThan(5);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _scheduler.ScheduleTask(createTask(), TaskPriority.High, input)
        );
    }
}