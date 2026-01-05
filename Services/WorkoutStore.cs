using GymTracker.Models;

namespace GymTracker.Services
{
    public sealed class WorkoutStore
    {
        private const string GroupsKeyV1 = "gymtracker.groups.v1";
        private const string TemplatesKeyV1 = "gymtracker.templates.v1";

        // Migration (your previous app versions)
        private static readonly string[] LegacyEntryKeys =
        {
        "gymtracker.entries.v3",
        "workoutpwa.entries.v3"
    };

        private readonly ILocalStorageService _storage;

        public WorkoutStore(ILocalStorageService storage) => _storage = storage;

        public List<WorkoutGroup> Groups { get; private set; } = new();
        public List<WorkoutTemplate> Templates { get; private set; } = new();

        public async Task LoadAsync(CancellationToken ct = default)
        {
            Groups = await _storage.GetAsync<List<WorkoutGroup>>(GroupsKeyV1, ct) ?? new List<WorkoutGroup>();
            Groups = SortGroups(Groups);

            Templates = await _storage.GetAsync<List<WorkoutTemplate>>(TemplatesKeyV1, ct) ?? new List<WorkoutTemplate>();
            Templates = SortTemplates(Templates);

            if (Groups.Count == 0)
            {
                var migrated = await TryMigrateFromLegacyEntriesAsync(ct);
                if (migrated)
                    await SaveAsync(ct);
            }
        }

        public async Task SaveAsync(CancellationToken ct = default)
        {
            Groups = SortGroups(Groups);
            Templates = SortTemplates(Templates);

            await _storage.SetAsync(GroupsKeyV1, Groups, ct);
            await _storage.SetAsync(TemplatesKeyV1, Templates, ct);
        }

        public IReadOnlyList<WorkoutGroup> GetGroupsByDate(DateOnly date)
            => Groups.Where(g => g.WorkoutDate == date).OrderByDescending(g => g.UpdatedAt).ToList();

        public WorkoutGroup? GetGroup(Guid id) => Groups.FirstOrDefault(g => g.Id == id);
        public WorkoutTemplate? GetTemplate(Guid id) => Templates.FirstOrDefault(t => t.Id == id);

        public async Task<WorkoutGroup> CreateGroupAsync(DateOnly date, string name, CancellationToken ct = default)
        {
            var g = new WorkoutGroup
            {
                Id = Guid.NewGuid(),
                WorkoutDate = date,
                Name = string.IsNullOrWhiteSpace(name) ? "Workout" : name.Trim(),
                Exercises = new List<WorkoutExercise>(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            Groups.Insert(0, g);
            await SaveAsync(ct);
            return g;
        }

        public async Task DeleteGroupAsync(Guid groupId, CancellationToken ct = default)
        {
            Groups.RemoveAll(g => g.Id == groupId);
            await SaveAsync(ct);
        }

        public async Task UpdateGroupAsync(WorkoutGroup updated, CancellationToken ct = default)
        {
            var idx = Groups.FindIndex(g => g.Id == updated.Id);
            if (idx < 0) return;

            NormalizeGroup(updated);
            updated.UpdatedAt = DateTimeOffset.UtcNow;

            Groups[idx] = updated;
            await SaveAsync(ct);
        }

        public async Task<WorkoutTemplate> SaveGroupAsTemplateAsync(Guid groupId, string templateName, CancellationToken ct = default)
        {
            var g = GetGroup(groupId) ?? throw new InvalidOperationException("Group not found.");

            var t = new WorkoutTemplate
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(templateName) ? g.Name : templateName.Trim(),
                Exercises = CloneExercises(g.Exercises),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            Templates.Insert(0, t);
            await SaveAsync(ct);
            return t;
        }

        public async Task DeleteTemplateAsync(Guid templateId, CancellationToken ct = default)
        {
            Templates.RemoveAll(t => t.Id == templateId);
            await SaveAsync(ct);
        }

        public async Task<WorkoutGroup> CreateGroupFromTemplateAsync(DateOnly date, Guid templateId, string? groupNameOverride = null, CancellationToken ct = default)
        {
            var t = GetTemplate(templateId) ?? throw new InvalidOperationException("Template not found.");

            var g = new WorkoutGroup
            {
                Id = Guid.NewGuid(),
                WorkoutDate = date,
                Name = string.IsNullOrWhiteSpace(groupNameOverride) ? t.Name : groupNameOverride.Trim(),
                Exercises = CloneExercises(t.Exercises),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            Groups.Insert(0, g);
            await SaveAsync(ct);
            return g;
        }

        private static void NormalizeGroup(WorkoutGroup g)
        {
            g.Name = (g.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(g.Name)) g.Name = "Workout";

            g.Exercises ??= new List<WorkoutExercise>();
            g.Exercises.RemoveAll(e => e is null);

            foreach (var e in g.Exercises)
            {
                e.Name = (e.Name ?? string.Empty).Trim();
                e.Sets ??= new List<ExerciseSet>();
                e.Sets.RemoveAll(s => s is null);
                if (e.Sets.Count == 0) e.Sets.Add(new ExerciseSet { Reps = 10, Weight = 0 });
            }
        }

        private static List<WorkoutExercise> CloneExercises(IEnumerable<WorkoutExercise> exercises)
            => exercises.Select(e => new WorkoutExercise
            {
                Id = Guid.NewGuid(),
                Name = e.Name,
                Sets = e.Sets.Select(s => new ExerciseSet { Reps = s.Reps, Weight = s.Weight }).ToList()
            }).ToList();

        private static List<WorkoutGroup> SortGroups(IEnumerable<WorkoutGroup> groups)
            => groups
                .OrderByDescending(g => g.WorkoutDate)
                .ThenByDescending(g => g.UpdatedAt)
                .ToList();

        private static List<WorkoutTemplate> SortTemplates(IEnumerable<WorkoutTemplate> templates)
            => templates
                .OrderByDescending(t => t.UpdatedAt)
                .ToList();

        private async Task<bool> TryMigrateFromLegacyEntriesAsync(CancellationToken ct)
        {
            foreach (var key in LegacyEntryKeys)
            {
                var legacy = await _storage.GetAsync<List<LegacyExerciseEntryV3>>(key, ct);
                if (legacy is not { Count: > 0 }) continue;

                var byDate = legacy.GroupBy(e => e.WorkoutDate)
                    .OrderByDescending(g => g.Key);

                foreach (var dateGroup in byDate)
                {
                    var group = new WorkoutGroup
                    {
                        Id = Guid.NewGuid(),
                        WorkoutDate = dateGroup.Key,
                        Name = "Workout",
                        Exercises = dateGroup.Select(e => new WorkoutExercise
                        {
                            Id = Guid.NewGuid(),
                            Name = e.Name ?? string.Empty,
                            Sets = e.Sets?.Select(s => new ExerciseSet { Reps = s.Reps, Weight = s.Weight }).ToList() ?? new List<ExerciseSet>()
                        }).ToList(),
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };

                    NormalizeGroup(group);
                    Groups.Add(group);
                }

                Groups = SortGroups(Groups);
                return true;
            }

            return false;
        }

        private sealed class LegacyExerciseEntryV3
        {
            public Guid Id { get; set; }
            public string? Name { get; set; }
            public DateOnly WorkoutDate { get; set; }
            public List<ExerciseSet>? Sets { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
        }
    }
}
