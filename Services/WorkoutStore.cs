using System.Text.Json;
using GymTracker.Models;

namespace GymTracker.Services;

/// <summary>
/// Stores:
///  - WorkoutDefinitions (saved workouts; no dates)
///  - WorkoutSessions (history; created on Do workout)
/// Backed by localStorage.
/// </summary>
public sealed class WorkoutStore
{
    private const string DefinitionsKeyV1 = "gymtracker.definitions.v1";
    private const string SessionsKeyV1 = "gymtracker.sessions.v1";

    // Legacy key (if you still have it from old versions)
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

        // Optional migration attempt (safe to keep)
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

    public IReadOnlyList<(DateOnly Date, List<WorkoutSession> Items)> GetHistoryGroupedByDate()
        => Sessions
            .GroupBy(s => s.WorkoutDate)
            .OrderByDescending(g => g.Key)
            .Select(g => (g.Key, g.OrderByDescending(x => x.StartedAt).ToList()))
            .ToList();

    // -----------------------
    // Definitions CRUD
    // -----------------------

    public async Task DeleteDefinitionAsync(Guid definitionId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        Definitions.RemoveAll(d => d.Id == definitionId);
        await SaveAsync(ct);
    }

    /// <summary>
    /// Create or update a definition.
    /// This is what you call ONLY when user presses Save on the edit page.
    /// </summary>
    public async Task<WorkoutDefinition> UpsertDefinitionAsync(WorkoutDefinition def, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        var now = DateTimeOffset.UtcNow;

        if (def.Id == Guid.Empty)
        {
            def.Id = Guid.NewGuid();
            def.CreatedAt = now;
            def.UpdatedAt = now;

            // No normalization that adds defaults.
            NormalizeDefinition(def);

            Definitions.Insert(0, def);
            await SaveAsync(ct);
            return def;
        }

        var idx = Definitions.FindIndex(d => d.Id == def.Id);
        if (idx < 0)
        {
            def.CreatedAt = now;
            def.UpdatedAt = now;

            NormalizeDefinition(def);

            Definitions.Insert(0, def);
            await SaveAsync(ct);
            return def;
        }

        var existing = Definitions[idx];
        def.CreatedAt = existing.CreatedAt;
        def.UpdatedAt = now;

        NormalizeDefinition(def);

        Definitions[idx] = def;
        await SaveAsync(ct);
        return def;
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
        await EnsureLoadedAsync(ct);

        var def = GetDefinition(definitionId) ?? throw new InvalidOperationException("Workout not found.");

        if (def.Exercises.Count == 0)
            throw new InvalidOperationException("Add at least one exercise before starting a workout.");

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
        await EnsureLoadedAsync(ct);

        var idx = Sessions.FindIndex(s => s.Id == updated.Id);
        if (idx < 0) return;

        var existing = Sessions[idx];

        updated.DefinitionId = existing.DefinitionId;
        updated.WorkoutDate = existing.WorkoutDate;
        updated.StartedAt = existing.StartedAt;
        updated.CreatedAt = existing.CreatedAt;
        updated.UpdatedAt = DateTimeOffset.UtcNow;

        NormalizeSession(updated);

        Sessions[idx] = updated;
        await SaveAsync(ct);
    }

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        Sessions.RemoveAll(s => s.Id == sessionId);
        await SaveAsync(ct);
    }

    // -----------------------
    // Normalization (IMPORTANT: no default exercise / set creation)
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
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Sessions = Sessions
            .OrderByDescending(s => s.WorkoutDate)
            .ThenByDescending(s => s.StartedAt)
            .ThenByDescending(s => s.UpdatedAt)
            .ToList();
    }

    private static void NormalizeDefinition(WorkoutDefinition d)
    {
        if (d.Id == Guid.Empty) d.Id = Guid.NewGuid();

        d.Name ??= string.Empty;
        d.Exercises ??= new List<WorkoutExercise>();
        d.Exercises.RemoveAll(e => e is null);

        foreach (var e in d.Exercises)
        {
            if (e.Id == Guid.Empty) e.Id = Guid.NewGuid();
            e.Name ??= string.Empty;

            e.Sets ??= new List<ExerciseSet>();
            e.Sets.RemoveAll(s => s is null);
            // DO NOT add default sets
        }

        if (d.CreatedAt == default) d.CreatedAt = DateTimeOffset.UtcNow;
        if (d.UpdatedAt == default) d.UpdatedAt = d.CreatedAt;
    }

    private static void NormalizeSession(WorkoutSession s)
    {
        if (s.Id == Guid.Empty) s.Id = Guid.NewGuid();
        if (s.DefinitionId == Guid.Empty) s.DefinitionId = Guid.NewGuid();

        s.NameSnapshot ??= string.Empty;

        s.Exercises ??= new List<WorkoutExercise>();
        s.Exercises.RemoveAll(e => e is null);

        foreach (var e in s.Exercises)
        {
            if (e.Id == Guid.Empty) e.Id = Guid.NewGuid();
            e.Name ??= string.Empty;

            e.Sets ??= new List<ExerciseSet>();
            e.Sets.RemoveAll(x => x is null);

            // DO add 1 set in sessions? We do NOT here; keep whatever user has.
        }

        if (s.CreatedAt == default) s.CreatedAt = DateTimeOffset.UtcNow;
        if (s.UpdatedAt == default) s.UpdatedAt = s.CreatedAt;
    }

    private static List<WorkoutExercise> CloneExercises(IEnumerable<WorkoutExercise> exercises)
        => exercises.Select(e => new WorkoutExercise
        {
            Id = Guid.NewGuid(),
            Name = e.Name,
            Sets = e.Sets.Select(s => new ExerciseSet { Reps = s.Reps, Weight = s.Weight }).ToList()
        }).ToList();

    // -----------------------
    // Optional migration support (safe to keep; no changes needed)
    // -----------------------

    private async Task<bool> TryMigrateLegacyGroupsAsync(CancellationToken ct)
    {
        var legacy = await _storage.GetAsync<List<LegacyWorkoutGroupV1>>(LegacyGroupsKeyV1, ct);
        if (legacy is not { Count: > 0 }) return false;

        var defByOrigin = new Dictionary<Guid, WorkoutDefinition>();

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
                    Name = g.Name ?? "",
                    Exercises = g.Exercises?.Select(CloneLegacyExercise).ToList() ?? new List<WorkoutExercise>(),
                    CreatedAt = now,
                    UpdatedAt = now
                };

                NormalizeDefinition(def);
                defByOrigin[origin] = def;
            }
        }

        Definitions = defByOrigin.Values.ToList();

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
            Name = e.Name ?? "",
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
