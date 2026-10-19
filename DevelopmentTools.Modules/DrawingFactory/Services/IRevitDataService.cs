using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BIMDrawingFactory.UI.Models;

namespace BIMDrawingFactory.UI.Services
{
    public interface IRevitDataService
    {
        void InvalidateAnalysisCache();
        Task<List<string>> GetOpenProjectNamesAsync();
        Task<List<string>> GetOpenSourceProjectNamesAsync();
        Task<ProjectProfileDto> GetProjectProfileAsync();
        Task<List<BuildingCardDto>> GetBuildingsAsync(string projectName = null);
        Task<List<CustomDrawingPackageItemDto>> GetAvailablePackageRulesAsync(string buildingId);
        Task<List<IssueItemDto>> RunPreflightCheckAsync(GenerateRequestDto request);
        Task<List<SheetPreviewItemDto>> GetPreviewItemsAsync(GenerateRequestDto request);
        Task<GenerationResultDto> ExecuteGenerationAsync(GenerateRequestDto request, IProgress<GenerationProgressDto> progress, CancellationToken ct);
        Task<List<DrawingRuleDto>> GetDrawingRulesAsync();
        Task<List<SheetSeriesDto>> GetAvailableSheetSeriesAsync(string projectName = null);
        Task<bool> SaveDrawingRuleAsync(DrawingRuleDto rule);
        Task<List<ResourceMappingDto>> GetResourceMappingsAsync(ResourceMappingType type);
        Task<bool> SaveResourceMappingsAsync(List<ResourceMappingDto> mappings);
        Task<List<ResourceMappingDto>> RunAutoMappingAsync(ResourceMappingType type);
        Task<bool> OpenSheetInRevitAsync(string sheetNumber);
        Task<bool> OpenViewInRevitAsync(string viewName);
    }
}
