using System.ComponentModel.DataAnnotations;

namespace GymTracker.Models
{
    public sealed class WorkoutGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateOnly WorkoutDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Required]
        [StringLength(80)]
        public string Name { get; set; } = "Workout";

        [MinLength(1)]
        public List<WorkoutExercise> Exercises { get; set; } = new();

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
