using System.Collections.Generic;
using DevelopmentTools.ProjectStandards.Domain.Identity;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class BrowserOrganizationSnapshot
    {
        public string Name { get; set; }

        public string OrganizationType { get; set; }

        public bool IsCurrentActive { get; set; }

        public List<string> FolderGroupingParameters { get; set; }

        public List<string> FilterRules { get; set; }

        public BrowserOrganizationSnapshot()
        {
            FolderGroupingParameters = new List<string>();
            FilterRules = new List<string>();
        }

        public StableIdentity Identity
        {
            get
            {
                string key = string.Format("{0}::{1}", OrganizationType ?? "Views", Name);
                return StableIdentity.ForNamedStandard("BrowserOrganization", key);
            }
        }
    }
}
