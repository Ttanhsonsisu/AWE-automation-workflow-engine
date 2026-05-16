using System.Text.Json;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.WorkflowEngine.Services;
using AWE.WorkflowEngine.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AWE.WorkflowEngine.Tests;

public class JoinBarrierServiceTests
{
    [Fact]
    public async Task EvaluateBarrierAsync_WhenNotAllIncomingBranchesArrived_KeepsBarrierClosed()
    {
        var instance = NewInstance();
        var arrived = new ExecutionPointer(instance.Id, "join");
        var repo = new InMemoryExecutionPointerRepository(arrived);
        var sut = NewService(repo);

        var result = await sut.EvaluateBarrierAsync(instance, "join", totalIncomingEdges: 2);

        Assert.False(result.IsBarrierBroken);
        Assert.False(result.IsDeadPath);
        Assert.Null(result.PointerToDispatch);
    }

    [Fact]
    public async Task EvaluateBarrierAsync_WhenAllBranchesArrived_SelectsOnePendingPointerAndCompletesRedundantPointers()
    {
        var instance = NewInstance();
        var first = new ExecutionPointer(instance.Id, "join");
        var second = new ExecutionPointer(instance.Id, "join");
        var repo = new InMemoryExecutionPointerRepository(first, second);
        var sut = NewService(repo);

        var result = await sut.EvaluateBarrierAsync(instance, "join", totalIncomingEdges: 2);

        Assert.True(result.IsBarrierBroken);
        Assert.False(result.IsDeadPath);
        Assert.Equal(first.Id, result.PointerToDispatch?.Id);
        Assert.Equal(ExecutionPointerStatus.Pending, first.Status);
        Assert.Equal(ExecutionPointerStatus.Completed, second.Status);
    }

    [Fact]
    public async Task EvaluateBarrierAsync_WhenOneBranchAlreadyCompleted_DoesNotDispatchAgain()
    {
        var instance = NewInstance();
        var completed = new ExecutionPointer(instance.Id, "join")
        {
            Status = ExecutionPointerStatus.Completed
        };
        var pending = new ExecutionPointer(instance.Id, "join");
        var repo = new InMemoryExecutionPointerRepository(completed, pending);
        var sut = NewService(repo);

        var result = await sut.EvaluateBarrierAsync(instance, "join", totalIncomingEdges: 2);

        Assert.True(result.IsBarrierBroken);
        Assert.False(result.IsDeadPath);
        Assert.Null(result.PointerToDispatch);
        Assert.Equal(ExecutionPointerStatus.Pending, pending.Status);
    }

    [Fact]
    public async Task EvaluateBarrierAsync_WhenAllIncomingBranchesAreSkipped_PropagatesDeadPath()
    {
        var instance = NewInstance();
        var skippedA = new ExecutionPointer(instance.Id, "join");
        var skippedB = new ExecutionPointer(instance.Id, "join");
        skippedA.Skip();
        skippedB.Skip();

        var repo = new InMemoryExecutionPointerRepository(skippedA, skippedB);
        var sut = NewService(repo);

        var result = await sut.EvaluateBarrierAsync(instance, "join", totalIncomingEdges: 2);

        Assert.True(result.IsBarrierBroken);
        Assert.True(result.IsDeadPath);
        Assert.Null(result.PointerToDispatch);
    }

    private static JoinBarrierService NewService(InMemoryExecutionPointerRepository repo) =>
        new(
            new ContendedDistributedLockProvider(),
            repo,
            NullLogger<JoinBarrierService>.Instance);

    private static WorkflowInstance NewInstance() =>
        new(Guid.NewGuid(), 1, JsonDocument.Parse("{}"), isTestInstance: true);
}
