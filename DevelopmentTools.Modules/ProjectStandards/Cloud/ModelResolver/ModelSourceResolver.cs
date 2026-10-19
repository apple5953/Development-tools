using System;
using System.IO;
using System.Threading.Tasks;
using DevelopmentTools.ProjectStandards.Domain.Models;

namespace DevelopmentTools.ProjectStandards.Cloud.ModelResolver
{
    /// <summary>
    /// Multi-condition resolver that strictly classifies Revit models into 6 distinct source types,
    /// avoiding common pitfalls such as misidentifying Desktop Connector published snapshots as Live Cloud Workshared central models.
    /// </summary>
    public class ModelSourceResolver : IModelSourceResolver
    {
        public Task<ModelDescriptor> ResolveAsync(ModelReference reference)
        {
            if (reference == null) throw new ArgumentNullException("reference");

            var descriptor = new ModelDescriptor
            {
                DisplayName = reference.DisplayName ?? Path.GetFileName(reference.RawPath) ?? "Unnamed Model",
                LocalPath = reference.RawPath,
                HubId = reference.HubId,
                ProjectId = reference.ProjectId,
                ItemId = reference.ItemId,
                VersionId = reference.VersionId,
                ProjectGuid = reference.ProjectGuid,
                ModelGuid = reference.ModelGuid,
                Region = reference.Region ?? "US",
                RevitVersion = "2024"
            };

            // Rule 1: Live Cloud Models are identified by ProjectGuid + ModelGuid AND explicit cloud flags
            if (reference.ProjectGuid.HasValue && reference.ModelGuid.HasValue &&
                reference.KnownIsCloudModel == true &&
                reference.KnownIsPublishedSnapshot != true)
            {
                descriptor.IsCloudModel = true;
                descriptor.IsPublishedSnapshot = false;

                if (reference.KnownIsWorkshared == true)
                {
                    descriptor.SourceType = ModelSourceType.RevitCloudWorksharedModel;
                    descriptor.IsWorkshared = true;
                    descriptor.CanRead = true;
                    descriptor.CanWrite = true;
                    descriptor.CanSynchronize = true;
                }
                else
                {
                    descriptor.SourceType = ModelSourceType.RevitCloudModel;
                    descriptor.IsWorkshared = false;
                    descriptor.CanRead = true;
                    descriptor.CanWrite = true;
                    descriptor.CanSynchronize = false;
                }

                var tcs = new TaskCompletionSource<ModelDescriptor>();
                tcs.SetResult(descriptor);
                return tcs.Task;
            }

            // Rule 2: Path-based classification
            string rawPath = reference.RawPath ?? string.Empty;

            if (IsDesktopConnectorPath(rawPath) || reference.KnownIsPublishedSnapshot == true)
            {
                bool isPublishedSnapshot = reference.KnownIsPublishedSnapshot == true ||
                                          (reference.KnownIsWorkshared == true && !descriptor.IsCloudModel);

                if (isPublishedSnapshot)
                {
                    descriptor.SourceType = ModelSourceType.DesktopConnectorPublishedSnapshot;
                    descriptor.IsCloudModel = false;
                    descriptor.IsWorkshared = true;
                    descriptor.IsPublishedSnapshot = true;
                    descriptor.CanRead = true;
                    descriptor.CanWrite = false;
                    descriptor.CanSynchronize = false;
                }
                else
                {
                    descriptor.SourceType = ModelSourceType.DesktopConnectorFile;
                    descriptor.IsCloudModel = false;
                    descriptor.IsWorkshared = reference.KnownIsWorkshared ?? false;
                    descriptor.IsPublishedSnapshot = false;
                    descriptor.CanRead = true;
                    descriptor.CanWrite = true;
                    descriptor.CanSynchronize = false;
                }

                var tcs = new TaskCompletionSource<ModelDescriptor>();
                tcs.SetResult(descriptor);
                return tcs.Task;
            }

            if (IsNetworkPath(rawPath))
            {
                descriptor.SourceType = ModelSourceType.NetworkFile;
                descriptor.IsCloudModel = false;
                descriptor.IsWorkshared = reference.KnownIsWorkshared ?? false;
                descriptor.IsPublishedSnapshot = false;
                descriptor.CanRead = true;
                descriptor.CanWrite = true;
                descriptor.CanSynchronize = descriptor.IsWorkshared;
                var tcs = new TaskCompletionSource<ModelDescriptor>();
                tcs.SetResult(descriptor);
                return tcs.Task;
            }

            // Default: Local File
            descriptor.SourceType = ModelSourceType.LocalFile;
            descriptor.IsCloudModel = false;
            descriptor.IsWorkshared = reference.KnownIsWorkshared ?? false;
            descriptor.IsPublishedSnapshot = false;
            descriptor.CanRead = true;
            descriptor.CanWrite = true;
            descriptor.CanSynchronize = descriptor.IsWorkshared;

            var defaultTcs = new TaskCompletionSource<ModelDescriptor>();
            defaultTcs.SetResult(descriptor);
            return defaultTcs.Task;
        }

        public static bool IsDesktopConnectorPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            return path.IndexOf("Autodesk Docs", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("ACCDocs", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("BIM 360", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("DC\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsNetworkPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return path.StartsWith(@"\\") || path.StartsWith("//");
        }
    }
}
