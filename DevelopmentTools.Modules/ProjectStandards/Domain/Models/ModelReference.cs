using System;

namespace DevelopmentTools.ProjectStandards.Domain.Models
{
    /// <summary>
    /// Input reference for identifying and resolving a target or master model.
    /// </summary>
    public class ModelReference
    {
        public string RawPath { get; set; }

        public string DisplayName { get; set; }

        public Guid? ProjectGuid { get; set; }

        public Guid? ModelGuid { get; set; }

        public string Region { get; set; }

        public string HubId { get; set; }

        public string ProjectId { get; set; }

        public string ItemId { get; set; }

        public string VersionId { get; set; }

        public bool? KnownIsWorkshared { get; set; }

        public bool? KnownIsCloudModel { get; set; }

        public bool? KnownIsPublishedSnapshot { get; set; }
    }
}
