using GymTracker.Models;

namespace GymTracker.Models;

public sealed class AppBackupV1
{
    public int Version { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<WorkoutDefinition> Definitions { get; set; } = new();
    public List<WorkoutSession> Sessions { get; set; } = new();
}
