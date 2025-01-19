using PriorityTaskScheduler.Contract;
using PriorityTaskScheduler.Enums;
using PriorityTaskScheduler.Scheduler;

namespace PriorityTaskScheduler;

class Program
{
    static void Main(string[] args)
    {
        IScheduler<TaskInput> scheduler = new TaskScheduler<TaskInput>();
       
        Action<TaskInput> actionTask = (input) => Console.WriteLine($"Action task processing: {input.Tag}");
        Func<TaskInput, int> funcTask = (input) => { Console.WriteLine($"Func task processing: {input.Tag}"); return input.Tag.Length; };
        Func<TaskInput, Task> asyncTask = async (input) =>
        {
            Console.WriteLine($"Async task processing: {input.Tag}");
            await Task.Delay(100);
            Console.WriteLine($"Async task completed: {input.Tag}");
        };

        var actionTaskId = scheduler.ScheduleTask(actionTask, TaskPriority.High, new TaskInput("Action"));
        var funcTaskId =  scheduler.ScheduleTask(funcTask, TaskPriority.Low, new TaskInput("Func"));
        var asyncTaskId = scheduler.ScheduleTask(asyncTask, TaskPriority.Medium, new TaskInput("Async"));
        
    }
    
}