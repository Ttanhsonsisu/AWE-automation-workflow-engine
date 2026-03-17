using AWE.Domain.Common;
using System.Text.Json;

namespace AWE.Domain.Entities;

/// <summary>
/// Implements atomic join barrier for parallel branch synchronization
/// Prevents race conditions when multiple tokens arrive at a join node
/// </summary>
public class JoinBarrier : Entity
{
    /// <summary>
    /// Reference to parent workflow instance
    /// </summary>
    public Guid InstanceId { get; private set; }

    /// <summary>
    /// ID of the join node in the DAG
    /// </summary>
    public string StepId { get; private set; } = string.Empty;

    /// <summary>
    /// Number of incoming branches required to release the barrier
    /// </summary>
    public int RequiredCount { get; private set; }

    /// <summary>
    /// List of pointer IDs that have arrived at this barrier
    /// Stored as JSON array: ["pointer-id-1", "pointer-id-2", ...]
    /// Prevents counting the same token twice
    /// </summary>
    public JsonDocument ArrivedTokens { get; private set; }

    /// <summary>
    /// Whether the barrier has been released and continuation pointer created
    /// Once true, this barrier is immutable
    /// </summary>
    public bool IsReleased { get; private set; }

    /// <summary>
    /// When this barrier was created
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Navigation property
    /// </summary>
    public virtual WorkflowInstance Instance { get; private set; } = null!;

    // Private constructor for EF Core
    private JoinBarrier() { }

    public JoinBarrier(Guid instanceId, string stepId, int requiredCount)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            throw new ArgumentException("Step ID cannot be empty", nameof(stepId));

        if (requiredCount < 2)
            throw new ArgumentException("Join barrier requires at least 2 branches", nameof(requiredCount));

        InstanceId = instanceId;
        StepId = stepId;
        RequiredCount = requiredCount;
        ArrivedTokens = JsonDocument.Parse("[]");
        IsReleased = false;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Register arrival of a token at this barrier
    /// Returns true if this arrival completes the barrier (last token arrived)
    /// </summary>
    public bool RegisterArrival(Guid pointerId)
    {
        if (IsReleased)
            throw new InvalidOperationException("Cannot register arrival on released barrier");

        // Parse current arrived tokens
        var arrivedList = JsonSerializer.Deserialize<List<string>>(ArrivedTokens) ?? new List<string>();
        var pointerIdString = pointerId.ToString();

        // Check if already registered (idempotency)
        if (arrivedList.Contains(pointerIdString))
            return false; // Already counted, don't count twice

        // Add new arrival
        arrivedList.Add(pointerIdString);

        // Update the JSON document
        ArrivedTokens = JsonDocument.Parse(JsonSerializer.Serialize(arrivedList));

        // Check if barrier is now complete
        if (arrivedList.Count >= RequiredCount)
        {
            IsReleased = true;
            return true; // Barrier complete, caller should create continuation pointer
        }

        return false; // More tokens needed
    }

    /// <summary>
    /// Get the count of tokens that have arrived
    /// </summary>
    public int GetArrivedCount()
    {
        var arrivedList = JsonSerializer.Deserialize<List<string>>(ArrivedTokens) ?? new List<string>();
        return arrivedList.Count;
    }

    /// <summary>
    /// Check if a specific pointer has already arrived
    /// </summary>
    public bool HasArrived(Guid pointerId)
    {
        var arrivedList = JsonSerializer.Deserialize<List<string>>(ArrivedTokens) ?? new List<string>();
        return arrivedList.Contains(pointerId.ToString());
    }

    /// <summary>
    /// Get all arrived pointer IDs
    /// </summary>
    public IReadOnlyList<Guid> GetArrivedPointers()
    {
        var arrivedList = JsonSerializer.Deserialize<List<string>>(ArrivedTokens) ?? new List<string>();
        return arrivedList.Select(Guid.Parse).ToList();
    }
}
