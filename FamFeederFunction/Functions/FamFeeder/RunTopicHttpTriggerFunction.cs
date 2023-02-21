using System;
using System.IO;
using System.Threading.Tasks;
using Core.Models;
using Equinor.ProCoSys.PcsServiceBus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using static System.Enum;
using Newtonsoft.Json;
using Core.Interfaces;

namespace FamFeederFunction.Functions.FamFeeder;

public class RunTopicHttpTriggerFunction
{
    private readonly IFamFeederService _famFeederService;

    public RunTopicHttpTriggerFunction(IFamFeederService famFeederService)
    {
        _famFeederService = famFeederService;
    }

    [FunctionName("FamFeederFunction_HttpStart")]
    public async Task<IActionResult> HttpStart(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]
        HttpRequest req,
        [DurableClient] IDurableOrchestrationClient orchestrationClient, ILogger log)
    {
        var (topicString, plant) = await DeserializeTopicAndPlant(req);

        log.LogInformation($"Querying {plant} for {topicString}");

        if (topicString == null || plant == null)
        {
            return new BadRequestObjectResult("Please provide both plant and topic");
        }

        if (!TryParse(topicString, out PcsTopic _))
        {
            return new BadRequestObjectResult("Please provide valid topic");
        }

        var plants = await _famFeederService.GetAllPlants();
        if (!plants.Contains(plant))
        {
            return new BadRequestObjectResult("Please provide valid plant");
        }

        var param = new QueryParameters(plant, topicString);
        var instanceId = await orchestrationClient.StartNewAsync("FamFeederFunction", param);
        return orchestrationClient.CreateCheckStatusResponse(req, instanceId);
    }
    private static async Task<(string topicString, string plant)> DeserializeTopicAndPlant(HttpRequest req)
    {
        string topicString = req.Query["PcsTopic"];
        string plant = req.Query["Plant"];

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        dynamic data = JsonConvert.DeserializeObject(requestBody);
        topicString ??= data?.PcsTopic;
        plant ??= data?.Facility;
        return (topicString, plant);
    }
}