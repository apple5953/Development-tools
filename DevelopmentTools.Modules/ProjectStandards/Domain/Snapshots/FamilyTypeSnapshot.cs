using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class FamilyTypeParameterSnapshot
    {
        public string Name { get; set; }

        public string ParameterStableKey { get; set; }

        public string Value { get; set; }
    }

    public class FamilyTypeSnapshot
    {
        public string FamilyName { get; set; }

        public string TypeName { get; set; }

        public string CategoryName { get; set; }

        public List<FamilyTypeParameterSnapshot> Parameters { get; set; }

        public FamilyTypeSnapshot()
        {
            Parameters = new List<FamilyTypeParameterSnapshot>();
        }

        public StableIdentity Identity
        {
            get { return StableIdentity.ForFamilyType(FamilyName, TypeName); }
        }
    }
}
