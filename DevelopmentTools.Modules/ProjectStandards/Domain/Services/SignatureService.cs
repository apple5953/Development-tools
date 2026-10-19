using System;
using System.Security.Cryptography;
using System.Text;
using DevelopmentTools.ProjectStandards.Domain.Snapshots;
using DevelopmentTools.ProjectStandards.Infrastructure.Json;

namespace DevelopmentTools.ProjectStandards.Domain.Services
{
    public class SignatureService : ISignatureService
    {
        private readonly ICanonicalizationService _canonicalizationService;

        public SignatureService(ICanonicalizationService canonicalizationService)
        {
            if (canonicalizationService == null) throw new ArgumentNullException("canonicalizationService");
            _canonicalizationService = canonicalizationService;
        }

        public string ComputeSnapshotSignature(ProjectStandardsSnapshot snapshot)
        {
            if (snapshot == null) return string.Empty;
            var canonical = _canonicalizationService.Canonicalize(snapshot);
            var signaturePayload = new
            {
                Worksets = canonical.Worksets,
                Parameters = canonical.Parameters,
                Levels = canonical.Levels,
                LinePatterns = canonical.LinePatterns,
                LineStyles = canonical.LineStyles,
                FillPatterns = canonical.FillPatterns,
                ObjectStyles = canonical.ObjectStyles,
                ViewFilters = canonical.ViewFilters,
                ViewTemplates = canonical.ViewTemplates,
                Schedules = canonical.Schedules,
                TextTypes = canonical.TextTypes,
                DimensionTypes = canonical.DimensionTypes,
                FamilyTypes = canonical.FamilyTypes,
                Legends = canonical.Legends,
                Sheets = canonical.Sheets,
                BrowserOrganizations = canonical.BrowserOrganizations,
                Phases = canonical.Phases,
                ScopeBoxes = canonical.ScopeBoxes,
                Coordinates = canonical.Coordinates
            };

            var json = SimpleJson.SerializeCanonical(signaturePayload);
            return ComputeSha256Hex(json);
        }

        public string ComputeItemSignature<T>(T item)
        {
            if (item == null) return string.Empty;
            var json = SimpleJson.SerializeCanonical(item);
            return ComputeSha256Hex(json);
        }

        private static string ComputeSha256Hex(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hash = sha256.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
