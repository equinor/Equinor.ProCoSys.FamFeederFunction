using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using Core.Models;
using Equinor.ProCoSys.PcsServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using static System.Enum;
using ExecutionContext = Microsoft.Azure.WebJobs.ExecutionContext;

namespace FamFeederFunction;

public class FamFeederFunction
{
    private readonly IFamFeederService _famFeederService;

    public FamFeederFunction(IFamFeederService famFeederService)
    {
        _famFeederService = famFeederService;
    }

    [FunctionName("FamFeederFunction")]
    public static async Task<List<string>> RunOrchestrator(
        [OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        var param = context.GetInput<QueryParameters>();
        var results = new List<string>();
        if (param.PcsTopic == PcsTopic.WorkOrderCutoff)
        {
            
            var months = new List<string> { "01", "02", "03","04","05","06","07","08","09","10","11","12" };
            foreach (var cutoffInput in months.Select(m => new WoCutoffInput(param.Plant, m)))
            {
                results.Add(await context.CallActivityAsync<string>("RunWoCutoffFeeder",cutoffInput));
            }

            return results;
        }

        results.Add(await context.CallActivityAsync<string>("RunFeeder", param));
        return results;
    }

    [FunctionName("RunWoCutoffFeeder")]
    public async Task<string> RunWoCutoffFeeder([ActivityTrigger] IDurableActivityContext context, ILogger logger)
    {
        var woCutoffInput = context.GetInput<WoCutoffInput>();
        var result = await _famFeederService.WoCutoff(woCutoffInput.Plant,woCutoffInput.Month, logger);
        logger.LogDebug($"RunFeeder returned {result}");
        return result;
    }

    [FunctionName("RunFeeder")]
    public async Task<string> RunFeeder([ActivityTrigger] IDurableActivityContext context, ILogger logger)
    {
        var runFeeder = await _famFeederService.RunFeeder(context.GetInput<QueryParameters>(), logger);
        logger.LogInformation($"RunFeeder returned {runFeeder}");
        return runFeeder;
    }

    [FunctionName("FamFeederFunction_HttpStart")]
    public async Task<IActionResult> HttpStart(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]
        HttpRequest req,
        [DurableClient] IDurableOrchestrationClient orchestrationClient, ILogger log)
    {
        var (topicString, plant) = await Deserialize(req);

        log.LogInformation($"Querying {plant} for {topicString}");

        if (topicString == null || plant == null)
            return new BadRequestObjectResult("Please provide both plant and topic");
        var parsed = TryParse(topicString, out PcsTopic topic);
        if (!parsed) return new BadRequestObjectResult("Please provide valid topic");
        var plants = await _famFeederService.GetAllPlants();
        if (!plants.Contains(plant)) return new BadRequestObjectResult("Please provide valid plant");

        var param = new QueryParameters(plant, topic);

        var instanceId = await orchestrationClient.StartNewAsync("FamFeederFunction", param);
        return orchestrationClient.CreateCheckStatusResponse(req,instanceId);
    }

    private static async Task<(string topicString, string plant)> Deserialize(HttpRequest req)
    {
        string topicString = req.Query["PcsTopic"];
        string plant = req.Query["Plant"];

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        dynamic data = JsonConvert.DeserializeObject(requestBody);
        topicString ??= data?.PcsTopic;
        plant ??= data?.Facility;
        return (topicString, plant);
    }

    [FunctionName("GetStatuses")]
    public static async Task<IActionResult> Statuses(
        [DurableClient] IDurableOrchestrationClient client,
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)]
        HttpRequest request, ExecutionContext context)
    {
        var statuses = await client.ListInstancesAsync(new OrchestrationStatusQueryCondition(), CancellationToken.None);
        return new OkObjectResult(statuses);
    }

    [FunctionName("GetStatus")]
    public static async Task<IActionResult> Status(
        [DurableClient] IDurableOrchestrationClient client,
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "status/{instanceId}")]
        HttpRequest request, string instanceId)
    {
        var statuses = await client.GetStatusAsync(instanceId);
        return new OkObjectResult(statuses);
    }

    [FunctionName(nameof(CleanupOrchestration))]
    public async Task<IActionResult> CleanupOrchestration(
        [HttpTrigger(AuthorizationLevel.Function, "post")]
        HttpRequest req,
        [DurableClient] IDurableOrchestrationClient orchestrationClient)
    {
        var instanceId = req.Query["id"];
        var requestPurgeResult = await orchestrationClient.PurgeInstanceHistoryAsync(instanceId);
        return new OkObjectResult(requestPurgeResult);
    }

    [FunctionName("TerminateInstance")]
    public static Task Terminate(
        [DurableClient] IDurableOrchestrationClient client,
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "TerminateInstance/{instanceId}")]
        HttpRequest request, string instanceId)
    {
        return client.TerminateAsync(instanceId, "reason");
    }

    private class WoCutoffInput
    {
        public string Plant { get; }
      
        public string Month { get; }

        public WoCutoffInput(string plant,  string month)
        {
            Plant = plant;
            Month = month;
        }
    }
}