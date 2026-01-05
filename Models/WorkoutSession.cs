using System.ComponentModel.DataAnnotations;

namespace GymTracker.Models
{
    public sealed class WorkoutSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid DefinitionId { get; set; }

        [Required]
        [StringLength(80)]
        public string NameSnapshot { get; set; } = "Workout";

        public DateOnly WorkoutDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

        [MinLength(1)]
        public List<WorkoutExercise> Exercises { get; set; } = new();

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
