using System.ComponentModel.DataAnnotations;

namespace GymTracker.Models
{
    public sealed class ExerciseEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required(ErrorMessage = "Exercise name is required.")]
        [StringLength(80, ErrorMessage = "Exercise name must be 80 characters or fewer.")]
        public string Name { get; set; } = string.Empty;

        public DateOnly WorkoutDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);

        [MinLength(1, ErrorMessage = "Add at least one set.")]
        public List<ExerciseSet> Sets { get; set; } = new() { new ExerciseSet { Reps = 10, Weight = 0 } };

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
