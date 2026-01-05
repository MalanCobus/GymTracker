using GymTracker.Models;

namespace GymTracker.Services
{
    public sealed class WorkoutStore
    {
        private const string StorageKeyV3 = "workoutpwa.entries.v3";
        private const string StorageKeyV2 = "workoutpwa.entries.v2";
        private const string StorageKeyV1 = "workoutpwa.entries.v1";

        private readonly ILocalStorageService _storage;

        public WorkoutStore(ILocalStorageService storage) => _storage = storage;

        public List<ExerciseEntry> Entries { get; private set; } = new();

        public async Task LoadAsync(CancellationToken ct = default)
        {
            var v3 = await _storage.GetAsync<List<ExerciseEntry>>(StorageKeyV3, ct);
            if (v3 is { Count: > 0 })
            {
                Entries = Sort(v3);
                return;
            }

            var v2 = await _storage.GetAsync<List<LegacyExerciseEntryV2>>(StorageKeyV2, ct);
            if (v2 is { Count: > 0 })
            {
                Entries = Sort(v2.Select(ConvertFromV2).ToList());
                await SaveAsync(ct);
                return;
            }

            var v1 = await _storage.GetAsync<List<LegacyExerciseEntryV1>>(StorageKeyV1, ct);
            if (v1 is { Count: > 0 })
            {
                Entries = Sort(v1.Select(ConvertFromV1).ToList());
                await SaveAsync(ct);
                return;
            }

            Entries = new();
        }

        public async Task SaveAsync(CancellationToken ct = default)
        {
            Entries = Sort(Entries);
            await _storage.SetAsync(StorageKeyV3, Entries, ct);
        }

        public async Task AddAsync(ExerciseEntry entry, CancellationToken ct = default)
        {
            entry.Id = Guid.NewGuid();
            entry.Name = entry.Name.Trim();
            entry.CreatedAt = DateTimeOffset.UtcNow;
            entry.UpdatedAt = entry.CreatedAt;

            Normalize(entry);

            Entries.Insert(0, entry);
            await SaveAsync(ct);
        }

        public async Task UpdateAsync(ExerciseEntry updated, CancellationToken ct = default)
        {
            var idx = Entries.FindIndex(e => e.Id == updated.Id);
            if (idx < 0) return;

            updated.Name = updated.Name.Trim();
            Normalize(updated);

            var existing = Entries[idx];
            existing.Name = updated.Name;
            existing.WorkoutDate = updated.WorkoutDate;
            existing.Sets = updated.Sets;
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            await SaveAsync(ct);
        }

        public async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            Entries.RemoveAll(e => e.Id == id);
            await SaveAsync(ct);
        }

        public async Task ClearAllAsync(CancellationToken ct = default)
        {
            Entries.Clear();
            await _storage.RemoveAsync(StorageKeyV3, ct);
            await _storage.RemoveAsync(StorageKeyV2, ct);
            await _storage.RemoveAsync(StorageKeyV1, ct);
        }

        public async Task<int> CopyFromDateAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
        {
            if (fromDate == toDate) return 0;

            var src = Entries
                .Where(e => e.WorkoutDate == fromDate)
                .OrderBy(e => e.UpdatedAt)
                .ToList();

            if (src.Count == 0) return 0;

            var now = DateTimeOffset.UtcNow;

            var clones = src.Select(e => new ExerciseEntry
            {
                Id = Guid.NewGuid(),
                Name = e.Name,
                WorkoutDate = toDate,
                Sets = e.Sets.Select(s => new ExerciseSet { Reps = s.Reps, Weight = s.Weight }).ToList(),
                CreatedAt = now,
                UpdatedAt = now
            }).ToList();

            Entries.InsertRange(0, clones);
            await SaveAsync(ct);
            return clones.Count;
        }

        private static void Normalize(ExerciseEntry entry)
        {
            entry.Sets ??= new List<ExerciseSet>();
            entry.Sets.RemoveAll(s => s is null);
            if (entry.Sets.Count == 0)
                entry.Sets.Add(new ExerciseSet { Reps = 10, Weight = 0 });
        }

        private static List<ExerciseEntry> Sort(IEnumerable<ExerciseEntry> entries)
            => entries
                .OrderByDescending(e => e.WorkoutDate)
                .ThenByDescending(e => e.UpdatedAt)
                .ToList();

        private static ExerciseEntry ConvertFromV2(LegacyExerciseEntryV2 v2)
            => new()
            {
                Id = v2.Id,
                Name = v2.Name ?? string.Empty,
                WorkoutDate = v2.WorkoutDate,
                Sets = ExpandLegacySets(v2.Sets, v2.Weight),
                CreatedAt = v2.CreatedAt,
                UpdatedAt = v2.UpdatedAt
            };

        private static ExerciseEntry ConvertFromV1(LegacyExerciseEntryV1 v1)
        {
            var localCreated = v1.CreatedAt.ToLocalTime().DateTime;
            return new ExerciseEntry
            {
                Id = v1.Id,
                Name = v1.Name ?? string.Empty,
                WorkoutDate = DateOnly.FromDateTime(localCreated),
                Sets = ExpandLegacySets(v1.Sets, v1.Weight),
                CreatedAt = v1.CreatedAt,
                UpdatedAt = v1.UpdatedAt
            };
        }

        private static List<ExerciseSet> ExpandLegacySets(int sets, decimal weight)
        {
            sets = Math.Clamp(sets, 1, 100);
            var list = new List<ExerciseSet>(sets);
            for (var i = 0; i < sets; i++)
                list.Add(new ExerciseSet { Reps = 0, Weight = weight }); // reps unknown during migration
            return list;
        }

        private sealed class LegacyExerciseEntryV2
        {
            public Guid Id { get; set; }
            public string? Name { get; set; }
            public int Sets { get; set; }
            public decimal Weight { get; set; }
            public DateOnly WorkoutDate { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
        }

        private sealed class LegacyExerciseEntryV1
        {
            public Guid Id { get; set; }
            public string? Name { get; set; }
            public int Sets { get; set; }
            public decimal Weight { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
        }
    }
}
