using System.ComponentModel.DataAnnotations;

namespace GymTracker.Models
{
    public sealed class WorkoutTemplate
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(80)]
        public string Name { get; set; } = "Template";

        [MinLength(1)]
        public List<WorkoutExercise> Exercises { get; set; } = new();

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
