using System;
using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class ProjectParameterSnapshot
    {
        public string Name { get; set; }

        public bool IsShared { get; set; }

        public Guid? SharedParameterGuid { get; set; }

        public string DataType { get; set; }

        public string ParameterGroup { get; set; }

        public bool IsInstance { get; set; }

        public bool UserModifiable { get; set; }

        public List<string> BoundCategories { get; set; }

        public bool VariesAcrossGroups { get; set; }

        public ProjectParameterSnapshot()
        {
            BoundCategories = new List<string>();
        }

        public StableIdentity Identity
        {
            get
            {
                if (IsShared && SharedParameterGuid.HasValue)
                {
                    return StableIdentity.ForSharedParameter(SharedParameterGuid.Value, Name);
                }
                return StableIdentity.ForNamedStandard("ProjectParameter", Name);
            }
        }
    }
}
