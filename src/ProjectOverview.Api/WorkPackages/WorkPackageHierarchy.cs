namespace ProjectOverview.Api.WorkPackages;

/// <summary>
/// What a work package looks like to clients. For a package with sub work packages the status and
/// dates are derived from its children; its own stored values are kept but not shown.
/// </summary>
public sealed record EffectiveWorkPackage(
    WorkPackageStatus Status, DateOnly? StartDate, DateOnly? EndDate, bool IsDerived);

public static class WorkPackageHierarchy
{
    /// <summary>
    /// Resolves the effective values for all packages of one project.
    /// Status of a parent: all children discarded gives discarded; otherwise, ignoring discarded children,
    /// all done gives done, all open gives open, and anything else inProgress.
    /// Dates of a parent: earliest child start and latest child end (empty when no child has one).
    /// </summary>
    public static Dictionary<Guid, EffectiveWorkPackage> Resolve(IReadOnlyCollection<WorkPackage> packages)
    {
        var childrenOf = packages
            .Where(p => p.ParentId is not null)
            .GroupBy(p => p.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var byId = packages.ToDictionary(p => p.Id);
        var resolved = new Dictionary<Guid, EffectiveWorkPackage>();
        var resolving = new HashSet<Guid>();

        EffectiveWorkPackage Resolve(WorkPackage package)
        {
            if (resolved.TryGetValue(package.Id, out var known)) return known;

            // Cycles are rejected when a package is moved; this only keeps bad data from looping forever.
            if (!childrenOf.TryGetValue(package.Id, out var children) || !resolving.Add(package.Id))
                return resolved[package.Id] = new(package.Status, package.StartDate, package.EndDate, IsDerived: false);

            var effectiveChildren = children.Select(Resolve).ToList();
            resolving.Remove(package.Id);

            return resolved[package.Id] = new(
                DeriveStatus(effectiveChildren.Select(c => c.Status)),
                effectiveChildren.Where(c => c.StartDate is not null).Select(c => c.StartDate).Min(),
                effectiveChildren.Where(c => c.EndDate is not null).Select(c => c.EndDate).Max(),
                IsDerived: true);
        }

        foreach (var package in byId.Values) Resolve(package);
        return resolved;
    }

    private static WorkPackageStatus DeriveStatus(IEnumerable<WorkPackageStatus> childStatuses)
    {
        var active = childStatuses.Where(s => s != WorkPackageStatus.Discarded).ToList();
        if (active.Count == 0) return WorkPackageStatus.Discarded;
        if (active.All(s => s == WorkPackageStatus.Done)) return WorkPackageStatus.Done;
        if (active.All(s => s == WorkPackageStatus.Open)) return WorkPackageStatus.Open;
        return WorkPackageStatus.InProgress;
    }
}
