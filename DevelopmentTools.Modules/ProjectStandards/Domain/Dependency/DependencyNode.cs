using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Diff;
using DevelopmentTools.ProjectStandards.Domain.Identity;
using DevelopmentTools.ProjectStandards.Domain.Models;

namespace DevelopmentTools.ProjectStandards.Domain.Dependency
{
    public class DependencyNode
    {
        public StableIdentity Identity { get; set; }

        public string AdapterKey { get; set; }

        public DiffItem DiffItem { get; set; }

        public SyncOperation Operation { get; set; }

        public HashSet<StableIdentity> DependsOn { get; set; }

        public HashSet<StableIdentity> RequiredBy { get; set; }

        public DependencyNode()
        {
            DependsOn = new HashSet<StableIdentity>();
            RequiredBy = new HashSet<StableIdentity>();
        }
    }
}
