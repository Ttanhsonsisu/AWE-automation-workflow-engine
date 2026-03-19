using System.Linq.Expressions;
using AWE.Application.Abstractions.Persistence;
using AWE.Application.UseCases.Monitor.Daskboard;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class WorkflowDefinitionRepository(ApplicationDbContext _context) : IWorkflowDefinitionRepository
{
    public async Task<WorkflowDefinition?> GetDefinitionByIdAsync(
       Guid id,
       CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<WorkflowDefinition?> GetDefinitionByNameAndVersionAsync(
        string name,
        int version,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name == name && x.Version == version, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetAllDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowDefinitions
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ThenByDescending(x => x.Version)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetPublishedDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowDefinitions
            .AsNoTracking()
            .Where(x => x.IsPublished)
            .OrderBy(x => x.Name)
            .ThenByDescending(x => x.Version)
            .ToListAsync(cancellationToken);
    }

    public async Task AddDefinitionAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        await _context.WorkflowDefinitions.AddAsync(definition, cancellationToken);
    }

    public async Task<WorkflowDefinition?> GetLatestVersionByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowDefinitions
            .AsNoTracking()
            .Where(x => x.Name == name)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task UpdateDefinitionAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        _context.WorkflowDefinitions.Update(definition);
        return Task.CompletedTask;
    }

    public async Task DeleteDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var definition = await _context.WorkflowDefinitions.FindAsync(new object[] { id }, cancellationToken);
        if (definition != null)
        {
            _context.WorkflowDefinitions.Remove(definition);
        }
    }

    public async Task<bool> ExistsDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowDefinitions.AnyAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<long> CountAsync(
        Expression<Func<WorkflowDefinition, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        if (predicate is null)
            return await _context.WorkflowDefinitions.CountAsync(cancellationToken);

        return await _context.WorkflowDefinitions.CountAsync(predicate, cancellationToken);
    }

    public async Task<DashboardMetricsResponse> GetDashboardMetricsAsync(CancellationToken cancellationToken = default)
    {
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var totalWorkflows = await _context.Set<WorkflowDefinition>().CountAsync(cancellationToken);
        var activeWorkflows = await _context.Set<WorkflowDefinition>().CountAsync(w => w.IsPublished, cancellationToken);

        // Lấy dữ liệu 30 ngày gần nhất để tính toán các biểu đồ
        // LƯU Ý: Không dùng .ToList() ở đây để EF Core tự dịch thành SQL WHERE
        var recentInstancesQuery = _context.Set<WorkflowInstance>()
            .Where(x => x.CreatedAt >= thirtyDaysAgo);

        var totalExecutionsLast30Days = await recentInstancesQuery.CountAsync(cancellationToken);

        // PIE CHART: Thống kê trạng thái (Success, Failed, Running...)
        // SQL sinh ra: SELECT Status, COUNT(*) FROM WorkflowInstance GROUP BY Status
        var statusBreakdown = await recentInstancesQuery
            .GroupBy(x => x.Status)
            .Select(g => new ExecutionStatusCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        // LINE CHART: Thống kê số lượng chạy theo từng ngày
        // EF Core sẽ tự dịch thuộc tính .Date thành hàm DATE() trong SQL Server/PostgreSQL
        var executionTrend = recentInstancesQuery
            .AsEnumerable()
            .GroupBy(x => x.CreatedAt.Date)
            .Select(g => new DailyExecutionTrend(g.Key, g.Count()))
            .OrderBy(x => x.Date)
            .ToList();

        // Bảng cảnh báo: Lấy 5 workflow chết gần nhất để hiển thị ngay trên Dashboard
        var recentFailures = await _context.Set<WorkflowInstance>()
            .Where(x => x.Status == WorkflowInstanceStatus.Compensating || x.Status == WorkflowInstanceStatus.Failed)
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .Select(x => new RecentFailedExecution(x.Id, x.DefinitionId, x.CreatedAt)) 
            .ToListAsync(cancellationToken);

        // Trả về kết quả
        var response = new DashboardMetricsResponse(
            totalWorkflows,
            activeWorkflows,
            totalExecutionsLast30Days,
            statusBreakdown,
            executionTrend,
            recentFailures
        );

        return response;
    }

}
