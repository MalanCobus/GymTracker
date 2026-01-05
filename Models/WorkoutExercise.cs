using System.ComponentModel.DataAnnotations;

namespace GymTracker.Models
{
    public sealed class WorkoutExercise
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(80)]
        public string Name { get; set; } = string.Empty;

        [MinLength(1)]
        public List<ExerciseSet> Sets { get; set; } = new();
    }
}
