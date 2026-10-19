using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Dependency
{
    public class DependencyReference
    {
        public string SourceAdapterKey { get; set; }

        public StableIdentity SourceIdentity { get; set; }

        public string DependsOnAdapterKey { get; set; }

        public StableIdentity DependsOnIdentity { get; set; }

        public bool IsMandatory { get; set; }

        public DependencyReference()
        {
            IsMandatory = true;
        }
    }
}
