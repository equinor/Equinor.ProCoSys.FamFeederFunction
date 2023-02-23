using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Models;
using Equinor.ProCoSys.PcsServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace FamFeederFunction.Functions.FamFeeder;

public static class AllTopicsOrchestrator
{
    [FunctionName(nameof(AllTopicsOrchestrator))]
    public static async Task<List<string>> RunAllExceptCutoffOrchestrator(
        [OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        var plant = context.GetInput<string>();

        if (MultiPlantConstants.TryGetByMultiPlant(plant, out var validMultiPlants))
        {
            return await RunMultiPlantOrchestration(context, validMultiPlants);
        }

        var plants = await context.CallActivityAsync<List<string>>(nameof(ValidatePlantActivity),context);
        if (!plants.Contains(plant))
        {
            return new List<string> { "Please provide a valid plant" };
        }

        var topics = GetAllTopicsAsEnumerable();

        var tasksAndParams = topics.Select(topic =>
        {
            var queryParameters = new QueryParameters(plant, topic);
            return (queryParameters, context.CallActivityAsync<string>(nameof(TopicActivity), queryParameters));
        });

        var toReturn = await WhenAllWithStatusUpdate(context, tasksAndParams.ToList());
        return toReturn;
    }

    private static IEnumerable<string> GetAllTopicsAsEnumerable()
    {
        return new List<PcsTopic> { PcsTopic.Action,PcsTopic.CommPkgTask,PcsTopic.Task,PcsTopic.CommPkg,PcsTopic.McPkg,PcsTopic.Project,PcsTopic.Responsible,PcsTopic.Tag,
            PcsTopic.TagFunction,PcsTopic.PunchListItem,PcsTopic.Library,PcsTopic.WorkOrder,PcsTopic.Checklist,PcsTopic.Milestone,PcsTopic.WoChecklist,PcsTopic.SWCR,PcsTopic.SWCRSignature,PcsTopic.PipingRevision,
            PcsTopic.WoMaterial,PcsTopic.WoMilestone,PcsTopic.Stock,PcsTopic.CommPkgOperation,PcsTopic.PipingSpool,PcsTopic.LoopContent,PcsTopic.Query,PcsTopic.QuerySignature,PcsTopic.CallOff,
            PcsTopic.CommPkgQuery,PcsTopic.HeatTrace
        }.Select(t => t.ToString());
    }


    private static async Task<List<string>> RunMultiPlantOrchestration(IDurableOrchestrationContext context, IEnumerable<string> validMultiPlants)
    {
        var results = validMultiPlants.SelectMany(plant => GetAllTopicsAsEnumerable().Select(topic =>
        {
            var queryParameters = new QueryParameters(plant, topic);
            return (queryParameters, context.CallActivityAsync<string>(nameof(TopicActivity), queryParameters));
        }));

        var toReturn = await WhenAllWithStatusUpdate(context, results.ToList());
        return toReturn;
    }

    /// <summary>
    /// Update status of tasks continuously.
    /// Loops through all pending tasks and updates status of done tasks until all tasks are completed.
    /// 
    /// Pattern found here
    /// https://joonasw.net/view/track-activity-and-sub-orchestrator-progress-in-azure-durable-functions-orchestrators
    /// </summary>
    /// <param name="context"></param>
    /// <param name="tasks"></param>
    /// <returns>The combined result of all finished tasks</returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="AggregateException"></exception>
    [Deterministic]
    private static async Task<List<string>> WhenAllWithStatusUpdate(
    IDurableOrchestrationContext context,
    List<(QueryParameters queryParameters, Task<string> task)> tasks)
    {
        List<(TaskStatus TaskStatus, string custumStatus)> activityStatuses = tasks.Select(GetActivityStatusFromTaskAndParam).ToList();


        //Durable functions custom status takes a maximum payload of 16KB. For very large data sets we will not be updating the status continuously. 
        var payloadSizeInKb = (int)(Encoding.Unicode.GetByteCount(JsonSerializer.Serialize(activityStatuses.Select(s => s.custumStatus))) / 1024.0);
        var tooLargeSize = payloadSizeInKb > 14;
        if (tooLargeSize)
        {
            context.SetCustomStatus($"No custom status update because the payload would be too large. {tasks.Count} tasks");
            var returnWithoutStatus = await Task.WhenAll(tasks.Select(t => t.task));
            return returnWithoutStatus.ToList();
        }

        var doneActivityCount = 0;
        context.SetCustomStatus(activityStatuses.Select(s => s.custumStatus));

        while (doneActivityCount < tasks.Count)
        {
            // Wait for one of the not done tasks to complete
            var notDoneTasks = tasks.Where(t => TaskNotDone(t.task.Status)).Select(t => t.task);

            var doneTask = await Task.WhenAny(notDoneTasks);

            // Find which one completed
            var doneTaskIndex = tasks.FindIndex(t => ReferenceEquals(t.task, doneTask));
            
            // Sanity check
            if (doneTaskIndex < 0 || !TaskNotDone(activityStatuses[doneTaskIndex].TaskStatus))
            {
                throw new Exception(
                    "Something went wrong, completed task not found or it was already completed");
            }

            //Here we just update the existing status.
            activityStatuses[doneTaskIndex] = UpdateStatusForFinishedTask(activityStatuses[doneTaskIndex].custumStatus, doneTask);
            doneActivityCount++;

            // Only update status when not replaying
            if (!context.IsReplaying)
            {
                context.SetCustomStatus(activityStatuses.Select(s => s.custumStatus));
            }
        }

        var failedTasks = tasks.Select(t=> t.task).Where(t => t.Exception != null).ToList();
        if (failedTasks.Count > 0)
        {
            throw new AggregateException(
                "One or more operations failed", failedTasks.Select(t => t.Exception));
        }
        var toReturn = await Task.WhenAll(tasks.Select(t => t.task));
        return toReturn.ToList();
    }

    private static bool TaskNotDone(TaskStatus t) 
        => t is TaskStatus.WaitingForActivation or TaskStatus.Created 
            or TaskStatus.Running or TaskStatus.Running or TaskStatus.WaitingForChildrenToComplete or TaskStatus.WaitingToRun;

    [Deterministic]
    private static (TaskStatus,string) GetActivityStatusFromTaskAndParam((QueryParameters queryParameters, Task<string>) item)
    {
        var (parameters, task) = item;
        var pendingActivity = (task.Status,$"{parameters.Plant}({parameters.PcsTopic}) :");
        var finishedActivity = (task.Status,$"{parameters.Plant}({parameters.PcsTopic}) : Finished");
        var failedActivity = (task.Status,$"{parameters.Plant}({parameters.PcsTopic}) : Failed");
        return task.Status switch
        {
            TaskStatus.Created => pendingActivity,
            TaskStatus.WaitingForActivation => pendingActivity,
            TaskStatus.WaitingToRun => pendingActivity,
            TaskStatus.Running => pendingActivity,
            TaskStatus.WaitingForChildrenToComplete => pendingActivity,
            TaskStatus.RanToCompletion =>  finishedActivity,
            TaskStatus.Canceled => failedActivity,
            TaskStatus.Faulted => failedActivity,
            _ => throw new NotImplementedException()
        };
    }

    [Deterministic]
    private static (TaskStatus, string) UpdateStatusForFinishedTask(
        string activityStatus, Task task)
    {
        if (!task.IsCompleted)
        {
            throw new Exception(
                "Should only be called on completed tasks");
        }

        return task.Status switch
        {
            TaskStatus.RanToCompletion => (task.Status, activityStatus + "Finished"),
            TaskStatus.Canceled => (task.Status, activityStatus + "Canceled"),
            TaskStatus.Faulted => (task.Status, activityStatus + "Failed"),
            _ => throw new NotImplementedException(),
        };
    }
}