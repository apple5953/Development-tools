using System;

namespace DevelopmentTools.ProjectStandards.Domain.Models
{
    /// <summary>
    /// Fully resolved descriptor of a Revit model, detailing its source classification,
    /// stable cloud identities, and capability flags (CanRead, CanWrite, CanSynchronize).
    /// </summary>
    public class ModelDescriptor
    {
        public string DisplayName { get; set; }

        public ModelSourceType SourceType { get; set; }

        public string LocalPath { get; set; }

        public bool IsCloudModel { get; set; }

        public bool IsWorkshared { get; set; }

        public bool IsPublishedSnapshot { get; set; }

        public bool CanRead { get; set; }

        public bool CanWrite { get; set; }

        public bool CanSynchronize { get; set; }

        public string HubId { get; set; }

        public string ProjectId { get; set; }

        public string ItemId { get; set; }

        public string VersionId { get; set; }

        public Guid? ProjectGuid { get; set; }

        public Guid? ModelGuid { get; set; }

        public string Region { get; set; }

        public string RevitVersion { get; set; }

        public string FormattedSourceDescription
        {
            get
            {
                switch (SourceType)
                {
                    case ModelSourceType.LocalFile:
                        return "Local File";
                    case ModelSourceType.NetworkFile:
                        return "Network Share File";
                    case ModelSourceType.DesktopConnectorFile:
                        return "Desktop Connector Docs File";
                    case ModelSourceType.DesktopConnectorPublishedSnapshot:
                        return "Desktop Connector Published Snapshot (Read-Only)";
                    case ModelSourceType.RevitCloudModel:
                        return "Live Revit Cloud Model";
                    case ModelSourceType.RevitCloudWorksharedModel:
                        return "Live Revit Cloud Workshared Model";
                    default:
                        return SourceType.ToString();
                }
            }
        }
    }
}
