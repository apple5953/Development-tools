using DevelopmentTools.ProjectStandards.Domain.Snapshots;

namespace DevelopmentTools.ProjectStandards.Domain.Services
{
    public interface ISignatureService
    {
        string ComputeSnapshotSignature(ProjectStandardsSnapshot snapshot);

        string ComputeItemSignature<T>(T item);
    }
}
