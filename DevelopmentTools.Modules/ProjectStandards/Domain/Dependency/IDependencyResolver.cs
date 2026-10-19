using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Diff;
using DevelopmentTools.ProjectStandards.Domain.Models;

namespace DevelopmentTools.ProjectStandards.Domain.Dependency
{
    public interface IDependencyResolver
    {
        DependencyGraph Build(IEnumerable<DiffItem> selectedItems, IEnumerable<DependencyReference> knownDependencies);

        IReadOnlyList<SyncOperation> Sort(DependencyGraph graph);
    }
}
