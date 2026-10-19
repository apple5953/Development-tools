namespace DevelopmentTools.ProjectStandards.Domain.Models
{
    /// <summary>
    /// Supported Model Source Types for Multi-RVT Project Standards Manager.
    /// </summary>
    public enum ModelSourceType
    {
        /// <summary>
        /// Local disk RVT file (e.g. C:\Projects\ModelA.rvt).
        /// </summary>
        LocalFile,

        /// <summary>
        /// Network share RVT file (e.g. \\Server\Share\ModelA.rvt).
        /// </summary>
        NetworkFile,

        /// <summary>
        /// Regular RVT file placed in Autodesk Docs Desktop Connector workspace (Ordinary file, not a live cloud workshared model).
        /// </summary>
        DesktopConnectorFile,

        /// <summary>
        /// Published snapshot of a Revit Cloud Worksharing model accessed via Desktop Connector.
        /// STRICT RULE: CanRead=true, CanWriteLiveCloud=false, CanSynchronize=false, IsPublishedSnapshot=true.
        /// </summary>
        DesktopConnectorPublishedSnapshot,

        /// <summary>
        /// Live Revit Cloud Model (Single-user cloud model on Autodesk Docs / ACC).
        /// </summary>
        RevitCloudModel,

        /// <summary>
        /// Live Revit Cloud Worksharing Model (Multi-user workshared model on Autodesk Docs / ACC).
        /// </summary>
        RevitCloudWorksharedModel
    }
}
