using System.Threading.Tasks;
using DevelopmentTools.ProjectStandards.Domain.Models;

namespace DevelopmentTools.ProjectStandards.Cloud.ModelResolver
{
    public interface IModelSourceResolver
    {
        Task<ModelDescriptor> ResolveAsync(ModelReference reference);
    }
}
