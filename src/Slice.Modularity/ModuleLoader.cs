namespace Slice.Modularity;

public sealed record ModuleDescriptor(Type Type, SliceModule Instance);

/// <summary>Thrown when the module <see cref="DependsOnAttribute"/> graph contains a cycle.</summary>
public sealed class SliceModuleCycleException(string message) : Exception(message);

/// <summary>
/// Resolves the full module set from a root module by walking <see cref="DependsOnAttribute"/>,
/// then returns them in dependency-first (topological) order.
/// </summary>
public static class ModuleLoader
{
    public static IReadOnlyList<ModuleDescriptor> LoadModules(Type rootModuleType)
    {
        var allTypes = new HashSet<Type>();
        Collect(rootModuleType, allTypes);

        var sorted = TopologicalSort(allTypes);
        return sorted.Select(t => new ModuleDescriptor(t, (SliceModule)Activator.CreateInstance(t)!)).ToList();
    }

    private static void Collect(Type moduleType, HashSet<Type> acc)
    {
        if (!typeof(SliceModule).IsAssignableFrom(moduleType))
            throw new ArgumentException($"'{moduleType.Name}' is not a {nameof(SliceModule)}.");
        if (!acc.Add(moduleType))
            return;
        foreach (var dep in Dependencies(moduleType))
            Collect(dep, acc);
    }

    private static IEnumerable<Type> Dependencies(Type moduleType)
        => moduleType.GetCustomAttributes(typeof(DependsOnAttribute), false)
            .Cast<DependsOnAttribute>()
            .SelectMany(a => a.DependedModuleTypes);

    // Kahn's algorithm: dependencies come before dependents.
    private static List<Type> TopologicalSort(HashSet<Type> types)
    {
        var inDegree = types.ToDictionary(t => t, _ => 0);
        var dependents = types.ToDictionary(t => t, _ => new List<Type>());

        foreach (var type in types)
            foreach (var dep in Dependencies(type).Where(types.Contains))
            {
                inDegree[type]++;
                dependents[dep].Add(type);
            }

        // stable: ready set ordered by name so sibling order is deterministic
        var ready = new SortedSet<Type>(
            inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key),
            Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.FullName, b.FullName)));

        var result = new List<Type>(types.Count);
        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            result.Add(next);
            foreach (var dependent in dependents[next])
                if (--inDegree[dependent] == 0)
                    ready.Add(dependent);
        }

        if (result.Count != types.Count)
        {
            var cyclic = string.Join(", ", types.Except(result).Select(t => t.Name));
            throw new SliceModuleCycleException($"Module dependency cycle detected involving: {cyclic}.");
        }

        return result;
    }
}
