using DevelopmentTools.ProjectStandards.Domain.Diff;
using DevelopmentTools.ProjectStandards.Domain.Snapshots;

namespace DevelopmentTools.ProjectStandards.Domain.Services
{
    public interface IDiffEngine
    {
        StandardsDiffResult Compare(ProjectStandardsSnapshot master, ProjectStandardsSnapshot target);
    }
}
