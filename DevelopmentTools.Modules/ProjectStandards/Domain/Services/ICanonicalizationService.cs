using DevelopmentTools.ProjectStandards.Domain.Snapshots;

namespace DevelopmentTools.ProjectStandards.Domain.Services
{
    public interface ICanonicalizationService
    {
        ProjectStandardsSnapshot Canonicalize(ProjectStandardsSnapshot snapshot);

        string ToCanonicalJson<T>(T obj);

        double NormalizeDouble(double value);

        string NormalizeColor(string colorRgb);
    }
}
