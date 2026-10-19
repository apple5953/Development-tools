using System;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class WorksetSnapshot
    {
        public string Name { get; set; }

        public Guid? UniqueGuid { get; set; }

        public bool IsVisibleByDefault { get; set; }

        public bool IsOpenByDefault { get; set; }

        public string Kind { get; set; }

        public StableIdentity Identity
        {
            get { return new StableIdentity(Name, Name, IdentityType.Name); }
        }
    }
}
