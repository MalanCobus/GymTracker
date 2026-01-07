using System.ComponentModel.DataAnnotations;

namespace GymTracker.Models
{
    public sealed class ExerciseSet
    {
        [Range(0, 500, ErrorMessage = "Reps must be between 0 and 500.")]
        public int Reps { get; set; }

        [Range(0, 2000, ErrorMessage = "Weight must be between 0 and 2000.")]
        public decimal Weight { get; set; }

        /// <summary>
        /// Session-only flag: set completed during an active workout. Not deleted; can be shown again.
        /// Missing in older JSON => defaults to false.
        /// </summary>
        public bool IsCompleted { get; set; }
    }
}
