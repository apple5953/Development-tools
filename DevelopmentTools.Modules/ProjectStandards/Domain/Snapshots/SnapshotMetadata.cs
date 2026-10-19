using System;
using DevelopmentTools.ProjectStandards.Domain.Models;

namespace DevelopmentTools.ProjectStandards.Domain.Snapshots
{
    public class SnapshotMetadata
    {
        public string ModelName { get; set; }

        public ModelSourceType SourceType { get; set; }

        public string RevitVersion { get; set; }

        public DateTime ScanTime { get; set; }

        public string PluginVersion { get; set; }

        public string LocalPath { get; set; }

        public Guid? ProjectGuid { get; set; }

        public Guid? ModelGuid { get; set; }

        public string CloudVersionId { get; set; }

        public string CloudVersionNumber { get; set; }

        public long FileSize { get; set; }

        public DateTime? LastWriteTimeUtc { get; set; }

        public string HubId { get; set; }

        public string ProjectId { get; set; }

        public string ItemId { get; set; }

        public string ModelStableIdentity
        {
            get
            {
                if (ProjectGuid.HasValue && ModelGuid.HasValue)
                {
                    return string.Format("{0:D}::{1:D}", ProjectGuid.Value, ModelGuid.Value);
                }
                return LocalPath ?? ModelName ?? string.Empty;
            }
        }
    }
}
