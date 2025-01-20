using PriorityTaskScheduler.Contract;
using PriorityTaskScheduler.Enums;
using PriorityTaskScheduler.Scheduler;

namespace PriorityTaskScheduler;

class Program
{
    public static void Main(string[] args)
    {
        using var scheduler = new TaskScheduler<TaskInput>();

        // Example tasks
        Action<TaskInput> actionTask = (input) =>
        {
            Console.WriteLine($"Action task processing: {input.Tag}");
            Thread.Sleep(1000); 
            Console.WriteLine($"Action task completed: {input.Tag}");
        };

        Func<TaskInput, int> funcTask = (input) =>
        {
            Console.WriteLine($"Func task processing: {input.Tag}");
            Thread.Sleep(1500); 
            Console.WriteLine($"Func task completed: {input.Tag}");
            return input.Tag.Length;
        };

        Func<TaskInput, Task> asyncTask = async (input) =>
        {
            Console.WriteLine($"Async task processing: {input.Tag}");
            await Task.Delay(20000); 
            Console.WriteLine($"Async task completed: {input.Tag}");
        };

        // Schedule 
        Guid actionTaskId = scheduler.ScheduleTask(actionTask, TaskPriority.High, new TaskInput("Action Task Data") );
        Guid funcTaskId = scheduler.ScheduleTask(funcTask, TaskPriority.Medium, new TaskInput("Func Task Data") );
        Guid asyncTaskId = scheduler.ScheduleTask(asyncTask, TaskPriority.Low, new TaskInput("Async Task Data") );

        // Start 
        scheduler.Start();

        // Cancel a task
        scheduler.CancelTask(asyncTaskId);
        Task.Delay(11000).Wait();
    }
    
}