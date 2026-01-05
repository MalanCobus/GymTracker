using GymTracker.Models;

namespace GymTracker.Services
{
    /// <summary>
    /// Local-only store backed by browser localStorage.
    /// Manages workout groups (sessions) and supports starting a "current workout"
    /// by copying a group to today's date (once per day per origin).
    /// </summary>
    public sealed class WorkoutStore
    {
        private const string DefinitionsKeyV1 = "gymtracker.definitions.v1";
        private const string SessionsKeyV1 = "gymtracker.sessions.v1";

        // Legacy from your previous design
        private const string LegacyGroupsKeyV1 = "gymtracker.groups.v1";

        private readonly ILocalStorageService _storage;
        private bool _loaded;

        public WorkoutStore(ILocalStorageService storage) => _storage = storage;

        public List<WorkoutDefinition> Definitions { get; private set; } = new();
        public List<WorkoutSession> Sessions { get; private set; } = new();

        public async Task EnsureLoadedAsync(CancellationToken ct = default)
        {
            if (_loaded) return;
            await LoadAsync(ct);
            _loaded = true;
        }

        public async Task LoadAsync(CancellationToken ct = default)
        {
            Definitions = await _storage.GetAsync<List<WorkoutDefinition>>(DefinitionsKeyV1, ct) ?? new List<WorkoutDefinition>();
            Sessions = await _storage.GetAsync<List<WorkoutSession>>(SessionsKeyV1, ct) ?? new List<WorkoutSession>();

            NormalizeAll();

            // If brand new keys are empty, try migrating your old dated groups
            if (Definitions.Count == 0 && Sessions.Count == 0)
            {
                var migrated = await TryMigrateLegacyGroupsAsync(ct);
                if (migrated)
                    await SaveAsync(ct);
            }
        }

        public async Task SaveAsync(CancellationToken ct = default)
        {
            NormalizeAll();

            await _storage.SetAsync(DefinitionsKeyV1, Definitions, ct);
            await _storage.SetAsync(SessionsKeyV1, Sessions, ct);
        }

        // -----------------------
        // Queries
        // -----------------------

        public WorkoutDefinition? GetDefinition(Guid id) => Definitions.FirstOrDefault(d => d.Id == id);
        public WorkoutSession? GetSession(Guid id) => Sessions.FirstOrDefault(s => s.Id == id);

        public IReadOnlyList<WorkoutSession> GetSessionsByDate(DateOnly date)
            => Sessions
                .Where(s => s.WorkoutDate == date)
                .OrderByDescending(s => s.StartedAt)
                .ThenByDescending(s => s.UpdatedAt)
                .ToList();

        public IReadOnlyList<(DateOnly Date, List<WorkoutSession> Items)> GetHistoryGroupedByDate()
            => Sessions
                .GroupBy(s => s.WorkoutDate)
                .OrderByDescending(g => g.Key)
                .Select(g => (g.Key, g.OrderByDescending(x => x.StartedAt).ToList()))
                .ToList();

        // -----------------------
        // Definitions CRUD
        // -----------------------

        public async Task<WorkoutDefinition> CreateDefinitionAsync(string name, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;

            var d = new WorkoutDefinition
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(name) ? "Workout" : name.Trim(),
                Exercises = new List<WorkoutExercise>
            {
                new WorkoutExercise
                {
                    Id = Guid.NewGuid(),
                    Name = "Exercise",
                    Sets = new List<ExerciseSet> { new ExerciseSet { Reps = 10, Weight = 0 } }
                }
            },
                CreatedAt = now,
                UpdatedAt = now
            };

            NormalizeDefinition(d);

            Definitions.Insert(0, d);
            await SaveAsync(ct);
            return d;
        }

        public async Task UpdateDefinitionAsync(WorkoutDefinition updated, CancellationToken ct = default)
        {
            var idx = Definitions.FindIndex(d => d.Id == updated.Id);
            if (idx < 0) return;

            var existing = Definitions[idx];

            updated.CreatedAt = existing.CreatedAt;
            updated.UpdatedAt = DateTimeOffset.UtcNow;

            NormalizeDefinition(updated);

            Definitions[idx] = updated;
            await SaveAsync(ct);
        }

        public async Task DeleteDefinitionAsync(Guid definitionId, CancellationToken ct = default)
        {
            Definitions.RemoveAll(d => d.Id == definitionId);
            await SaveAsync(ct);
        }

        // -----------------------
        // Sessions (history)
        // -----------------------

        /// <summary>
        /// Creates (or reuses) today's session for the given definition.
        /// Reuse rule: if a session exists for that definition on that date, return the most recent one.
        /// </summary>
        public async Task<WorkoutSession> StartWorkoutAsync(Guid definitionId, DateOnly date, CancellationToken ct = default)
        {
            var def = GetDefinition(definitionId) ?? throw new InvalidOperationException("Workout not found.");

            var existing = Sessions
                .Where(s => s.DefinitionId == definitionId && s.WorkoutDate == date)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefault();

            if (existing is not null)
                return existing;

            var now = DateTimeOffset.UtcNow;

            var session = new WorkoutSession
            {
                Id = Guid.NewGuid(),
                DefinitionId = def.Id,
                NameSnapshot = def.Name,
                WorkoutDate = date,
                StartedAt = now,
                Exercises = CloneExercises(def.Exercises),
                CreatedAt = now,
                UpdatedAt = now
            };

            NormalizeSession(session);

            Sessions.Insert(0, session);
            await SaveAsync(ct);
            return session;
        }

        public async Task UpdateSessionAsync(WorkoutSession updated, CancellationToken ct = default)
        {
            var idx = Sessions.FindIndex(s => s.Id == updated.Id);
            if (idx < 0) return;

            var existing = Sessions[idx];

            updated.DefinitionId = existing.DefinitionId;
            updated.NameSnapshot = (updated.NameSnapshot ?? existing.NameSnapshot).Trim();
            updated.WorkoutDate = existing.WorkoutDate; // sessions keep their original date
            updated.StartedAt = existing.StartedAt;
            updated.CreatedAt = existing.CreatedAt;
            updated.UpdatedAt = DateTimeOffset.UtcNow;

            NormalizeSession(updated);

            Sessions[idx] = updated;
            await SaveAsync(ct);
        }

        public async Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
        {
            Sessions.RemoveAll(s => s.Id == sessionId);
            await SaveAsync(ct);
        }

        // -----------------------
        // Helpers
        // -----------------------

        private void NormalizeAll()
        {
            Definitions ??= new List<WorkoutDefinition>();
            Sessions ??= new List<WorkoutSession>();

            foreach (var d in Definitions)
                NormalizeDefinition(d);

            foreach (var s in Sessions)
                NormalizeSession(s);

            Definitions = Definitions
                .OrderByDescending(d => d.UpdatedAt)
                .ThenBy(d => d.Name)
                .ToList();

            Sessions = Sessions
                .OrderByDescending(s => s.WorkoutDate)
                .ThenByDescending(s => s.StartedAt)
                .ThenByDescending(s => s.UpdatedAt)
                .ToList();
        }

        private static void NormalizeDefinition(WorkoutDefinition d)
        {
            d.Id = d.Id == Guid.Empty ? Guid.NewGuid() : d.Id;

            d.Name = (d.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(d.Name)) d.Name = "Workout";

            d.Exercises ??= new List<WorkoutExercise>();
            d.Exercises.RemoveAll(e => e is null);

            if (d.Exercises.Count == 0)
            {
                d.Exercises.Add(new WorkoutExercise
                {
                    Id = Guid.NewGuid(),
                    Name = "Exercise",
                    Sets = new List<ExerciseSet> { new ExerciseSet { Reps = 10, Weight = 0 } }
                });
            }

            foreach (var e in d.Exercises)
            {
                e.Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id;

                e.Name = (e.Name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(e.Name)) e.Name = "Exercise";

                e.Sets ??= new List<ExerciseSet>();
                e.Sets.RemoveAll(s => s is null);
                if (e.Sets.Count == 0)
                    e.Sets.Add(new ExerciseSet { Reps = 10, Weight = 0 });
            }
        }

        private static void NormalizeSession(WorkoutSession s)
        {
            s.Id = s.Id == Guid.Empty ? Guid.NewGuid() : s.Id;
            s.DefinitionId = s.DefinitionId == Guid.Empty ? Guid.NewGuid() : s.DefinitionId;

            s.NameSnapshot = (s.NameSnapshot ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(s.NameSnapshot)) s.NameSnapshot = "Workout";

            s.Exercises ??= new List<WorkoutExercise>();
            s.Exercises.RemoveAll(e => e is null);

            if (s.Exercises.Count == 0)
            {
                s.Exercises.Add(new WorkoutExercise
                {
                    Id = Guid.NewGuid(),
                    Name = "Exercise",
                    Sets = new List<ExerciseSet> { new ExerciseSet { Reps = 10, Weight = 0 } }
                });
            }

            foreach (var e in s.Exercises)
            {
                e.Id = e.Id == Guid.Empty ? Guid.NewGuid() : e.Id;

                e.Name = (e.Name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(e.Name)) e.Name = "Exercise";

                e.Sets ??= new List<ExerciseSet>();
                e.Sets.RemoveAll(x => x is null);
                if (e.Sets.Count == 0)
                    e.Sets.Add(new ExerciseSet { Reps = 10, Weight = 0 });
            }
        }

        private static List<WorkoutExercise> CloneExercises(IEnumerable<WorkoutExercise> exercises)
            => exercises.Select(e => new WorkoutExercise
            {
                Id = Guid.NewGuid(),
                Name = e.Name,
                Sets = e.Sets.Select(s => new ExerciseSet { Reps = s.Reps, Weight = s.Weight }).ToList()
            }).ToList();

        // -----------------------
        // Migration (legacy groups -> definitions + sessions)
        // -----------------------

        private async Task<bool> TryMigrateLegacyGroupsAsync(CancellationToken ct)
        {
            var legacy = await _storage.GetAsync<List<LegacyWorkoutGroupV1>>(LegacyGroupsKeyV1, ct);
            if (legacy is not { Count: > 0 }) return false;

            // Map origin -> definition
            var defByOrigin = new Dictionary<Guid, WorkoutDefinition>();

            // Create definitions based on originGroupId (or id)
            foreach (var g in legacy)
            {
                var origin = g.OriginGroupId ?? g.Id;
                if (origin == Guid.Empty) continue;

                if (!defByOrigin.ContainsKey(origin))
                {
                    var now = DateTimeOffset.UtcNow;
                    var def = new WorkoutDefinition
                    {
                        Id = Guid.NewGuid(),
                        Name = g.Name ?? "Workout",
                        Exercises = g.Exercises?.Select(CloneLegacyExercise).ToList() ?? new List<WorkoutExercise>(),
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    NormalizeDefinition(def);
                    defByOrigin[origin] = def;
                }
            }

            Definitions = defByOrigin.Values.ToList();

            // Create sessions for each legacy group instance
            foreach (var g in legacy)
            {
                var origin = g.OriginGroupId ?? g.Id;
                if (origin == Guid.Empty) continue;
                if (!defByOrigin.TryGetValue(origin, out var def)) continue;

                var now = DateTimeOffset.UtcNow;

                var session = new WorkoutSession
                {
                    Id = Guid.NewGuid(),
                    DefinitionId = def.Id,
                    NameSnapshot = def.Name,
                    WorkoutDate = g.WorkoutDate,
                    StartedAt = g.UpdatedAt == default ? now : g.UpdatedAt,
                    Exercises = g.Exercises?.Select(CloneLegacyExercise).ToList() ?? CloneExercises(def.Exercises),
                    CreatedAt = now,
                    UpdatedAt = now
                };

                NormalizeSession(session);
                Sessions.Add(session);
            }

            NormalizeAll();
            return true;
        }

        private static WorkoutExercise CloneLegacyExercise(LegacyWorkoutExercise e)
            => new WorkoutExercise
            {
                Id = Guid.NewGuid(),
                Name = e.Name ?? "Exercise",
                Sets = (e.Sets ?? new List<ExerciseSet>())
                    .Select(s => new ExerciseSet { Reps = s.Reps, Weight = s.Weight })
                    .ToList()
            };

        private sealed class LegacyWorkoutGroupV1
        {
            public Guid Id { get; set; }
            public DateOnly WorkoutDate { get; set; }
            public string? Name { get; set; }
            public List<LegacyWorkoutExercise>? Exercises { get; set; }
            public Guid? OriginGroupId { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
        }

        private sealed class LegacyWorkoutExercise
        {
            public Guid Id { get; set; }
            public string? Name { get; set; }
            public List<ExerciseSet>? Sets { get; set; }
        }
    }
}
