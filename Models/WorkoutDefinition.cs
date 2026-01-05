using System.ComponentModel.DataAnnotations;

namespace GymTracker.Models
{
    public sealed class WorkoutDefinition
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(80)]
        public string Name { get; set; } = string.Empty;

        public List<WorkoutExercise> Exercises { get; set; } = new();

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
