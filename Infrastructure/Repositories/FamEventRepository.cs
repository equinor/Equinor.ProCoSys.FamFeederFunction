﻿using Core.Interfaces;
using Core.Models;
using Equinor.ProCoSys.PcsServiceBus.Queries;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Reflection.Metadata;
using System.Text.Json;
using Core.Models.Search;
using Dapper;
using Equinor.ProCoSys.PcsServiceBus.Interfaces;
using Action = Core.Models.Action;
using CommPkg = Core.Models.CommPkg;
using CommPkgQuery = Core.Models.CommPkgQuery;
using Document = Core.Models.Document;
using McPkg = Core.Models.McPkg;
using Tag = Core.Models.Tag;

namespace Infrastructure.Repositories;

public class FamEventRepository : IFamEventRepository
{
    private readonly AppDbContext _context;
    public FamEventRepository(AppDbContext context) 
        => _context = context;
    public async Task<List<string>> GetSwcrAttachments(string plant) => await Query<SwcrAttachment>(SwcrAttachmentQuery.GetQuery(null, plant));
    public async Task<List<string>> GetSwcrOtherReferences(string plant) => await Query<SwcrOtherReference>(SwcrOtherReferenceQuery.GetQuery(null, plant));
    public async Task<List<string>> GetSwcrType(string plant) => await Query<SwcrType>(SwcrTypeQuery.GetQuery(null, plant));
    public async Task<List<string>> GetActions(string plant) => await Query<Action>(ActionQuery.GetQuery(null, plant));
    public async Task<List<string>> GetCommPkgTasks(string plant) => await Query<CommPkgTask>(CommPkgTaskQuery.GetQuery(null, null, plant));
    public async Task<List<string>> GetTasks(string plant) => await Query<TaskEvent>(TaskQuery.GetQuery(null, plant));
    public async Task<List<string>> GetMcPackages(string plant) => await Query<McPkg>(McPkgQuery.GetQuery(null,plant));
    public async Task<List<string>> GetCommPackages(string plant) => await Query<CommPkg>(Equinor.ProCoSys.PcsServiceBus.Queries.CommPkgQuery.GetQuery(null,plant));
    public async Task<List<string>> GetCommPkgOperations(string plant) => await Query<CommPkgOperation>(CommPkgOperationQuery.GetQuery(null,plant));
    public async Task<List<string>> GetPunchItems(string plant) => await Query<PunchListItem>(PunchListItemQuery.GetQuery(null,plant));
    public async Task<List<string>> GetWorkOrders(string plant) => await Query<WorkOrder>(WorkOrderQuery.GetQuery(null, plant));
    public async Task<List<string>> GetCheckLists(string plant) => await Query<Checklist>(ChecklistQuery.GetQuery(null,plant));
    public async Task<List<string>> GetTags(string plant) => await Query<Tag>(TagQuery.GetQuery(null,plant));
    public async Task<List<string>> GetMcPkgMilestones(string plant) => await Query<IMcPkgMilestoneEventV1>(McPkgMilestoneQuery.GetQuery(null,plant));
    public async Task<List<string>> GetProjects(string plant) => await Query<Project>(ProjectQuery.GetQuery(null, plant));
    public async Task<List<string>> GetSwcr(string plant) => await Query<Swcr>(SwcrQuery.GetQuery(null,plant));
    public async Task<List<string>> GetSwcrSignature(string plant) => await Query<SwcrSignature>(SwcrSignatureQuery.GetQuery(null, plant));
    public async Task<List<string>> GetWoChecklists(string plant) => await Query<WorkOrderChecklist>(WorkOrderChecklistQuery.GetQuery(null,null, plant));
    public async Task<List<string>> GetQuery(string plant) => await Query<Query>(QueryQuery.GetQuery(null,plant));
    public async Task<List<string>> GetQuerySignature(string plant) => await Query<QuerySignature>(QuerySignatureQuery.GetQuery(null, plant));
    public async Task<List<string>> GetPipingRevision(string plant) => await Query<PipingRevision>(PipingRevisionQuery.GetQuery(null,plant));
    public async Task<List<string>> GetPipingSpool(string plant) => await Query<PipingSpool>(PipingSpoolQuery.GetQuery(null,plant));
    public async Task<List<string>> GetWoMilestones(string plant) => await Query<WorkOrderMilestone>(WorkOrderMilestoneQuery.GetQuery(null,null,plant));
    public async Task<List<string>> GetWoMaterials(string plant) => await Query<WorkOrderMaterial>(WorkOrderMaterialQuery.GetQuery(null,plant));
    public async Task<List<string>> GetStock(string plant) => await Query<Stock>(StockQuery.GetQuery(null,plant));
    public async Task<List<string>> GetResponsible(string plant) => await Query<Responsible>(ResponsibleQuery.GetQuery(null, plant));
    public async Task<List<string>> GetLibrary(string plant) => await Query<Library>(LibraryQuery.GetQuery(null, plant));
    public async Task<List<string>> GetDocument(string plant) => await Query<Document>(DocumentQuery.GetQuery(null,plant));
    public async Task<List<string>> GetLoopContent(string plant) => await Query<LoopContent>(LoopContentQuery.GetQuery(null, plant));
    public async Task<List<string>> GetCallOff(string plant) => await Query<CallOff>(CallOffQuery.GetQuery(null,plant));
    public async Task<List<string>> GetCommPkgQuery(string plant) => await Query<CommPkgQuery>(CommPkgQueryQuery.GetQuery(null, null, plant));
    public async Task<List<string>> GetWoCutoffsByWeekAndPlant(string cutoffWeek, string plant) => await Query<WorkOrderCutoff>(WorkOrderCutoffQuery.GetQuery(null, cutoffWeek, plant,null));
    public async Task<List<string>> GetHeatTrace(string plant) => await Query<HeatTrace>(HeatTraceQuery.GetQuery(null, plant));
    public async Task<List<string>> GetLibraryField(string plant) => await Query<LibraryField>(LibraryFieldQuery.GetQuery(null, plant));


    private async Task<List<string>> Query<T>((string queryString, DynamicParameters parameters) query) where T : IHasEventType
    {
        var connection = _context.Database.GetDbConnection();
        var connectionWasClosed = connection.State != ConnectionState.Open;
        if (connectionWasClosed)
        {
            await _context.Database.OpenConnectionAsync();
        }

        try
        {
            List<T> events = connection.Query<T>(query.queryString, query.parameters).ToList();
            if (events.Count == 0)
            {
              //  _logger.LogError("Object/Entity with id {ObjectId} did not return anything", objectId);
                return new List<string>();
            }


            List<string> list = events.Select(e => JsonSerializer.Serialize(e)).ToList();
            return list;
        }
        finally
        {
            //If we open it, we have to close it.
            if (connectionWasClosed)
            {
                await _context.Database.CloseConnectionAsync();
            }
        }
    }
    
    
    private async Task<List<string>> ExecuteQuery(string query)
    {
        var dbConnection = _context.Database.GetDbConnection();
        var connectionWasClosed = dbConnection.State != ConnectionState.Open;
        if (connectionWasClosed)
        {
            await _context.Database.OpenConnectionAsync();
        }
        try
        {
            await using var command = dbConnection.CreateCommand();
            command.CommandText = query;
            await using var result = await command.ExecuteReaderAsync();
            var entities = new List<string>();

            while (await result.ReadAsync())
            {
                //Last row
                if (!result.HasRows)
                {
                    continue;
                }

                var s = (string)result[0];

                //For local debugging
                entities.Add(s.Replace("\"WoNo\" : \"���\"", "\"WoNo\" : \"ÆØÅ\""));
            }

            return entities;
        }
        finally
        {
            //If we open it, we have to close it.
            if (connectionWasClosed)
            {
                await _context.Database.CloseConnectionAsync();
            }
        }
    }    
}