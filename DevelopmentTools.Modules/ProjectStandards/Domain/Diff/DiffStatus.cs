namespace DevelopmentTools.ProjectStandards.Domain.Diff
{
    public enum DiffStatus
    {
        Equal,
        Missing,
        Extra,
        Modified,
        RenameCandidate,
        DependencyMissing,
        Conflict,
        Unsupported,
        Stale,
        ReadOnlySource
    }
}
