using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Models;
using Equinor.ProCoSys.PcsServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace FamFeederFunction.Functions.FamFeeder;

public static class TopicOrchestrator
{
    [FunctionName(nameof(TopicOrchestrator))]
    public static async Task<List<string>> RunOrchestrator(
        [OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        var param = context.GetInput<QueryParameters>();

        if (MultiPlantConstants.TryGetByMultiPlant(param.Plant,out var validMultiPlants))
        {
            return await RunMultiPlantOrchestration(context, validMultiPlants, param);
        }
      
        var plants = await context.CallActivityAsync<List<string>>(nameof(ValidatePlantActivity), param);
        if (!plants.Contains(param.Plant))
        {
            return new List<string> { "Please provide a valid plant" };
        }

        if (param.PcsTopic == PcsTopic.WorkOrderCutoff.ToString())
        {
            return await RunWoCutoffOrchestration(context, param);
        }
        var singleReturn = await context.CallActivityAsync<string>(nameof(TopicActivity), param);
        return new List<string> { singleReturn };
    }

    private static async Task<List<string>> RunMultiPlantOrchestration(IDurableOrchestrationContext context, IEnumerable<string> validMultiPlants,
        QueryParameters param)
    {
        var results = validMultiPlants
            .Select(plant => new QueryParameters(plant, param.PcsTopic))
            .Select(input => context.CallActivityAsync<string>(nameof(TopicActivity), input))
            .ToList();
        var finishedTasks = await Task.WhenAll(results);
        return finishedTasks.ToList();
    }

    private static async Task<List<string>> RunWoCutoffOrchestration(IDurableOrchestrationContext context, QueryParameters param)
    {
        var months = new List<string> { "01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12" };
        var results = months
            .Select(m => (param.Plant, m))
            .Select(cutoffInput => ($"{cutoffInput.Plant}({cutoffInput.m})", context.CallActivityAsync<string>(
                nameof(CutoffForMonthActivity), cutoffInput))).ToList();
        var allFinishedTasks = await CustomStatusExtension.WhenAllWithStatusUpdate(context,results);
        return allFinishedTasks.ToList();
    }
}