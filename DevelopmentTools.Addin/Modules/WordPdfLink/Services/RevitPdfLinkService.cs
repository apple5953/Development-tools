using System;
using Autodesk.Revit.DB;
using DT_WordPdfLink.Utils;

namespace DT_WordPdfLink.Services
{
    public static class RevitPdfLinkService
    {
        public static ImageType GetOrCreatePdfLink(Document doc, string pdfPath, int pageNumber, out bool isNew)
        {
            isNew = false;
            
            // Check if already loaded in Revit under the same path and page number
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            foreach (Element elem in collector.OfClass(typeof(ImageType)).ToElements())
            {
                ImageType it = elem as ImageType;
                if (it != null && it.Source == ImageTypeSource.Link)
                {
                    if (string.Equals(it.Path, pdfPath, StringComparison.OrdinalIgnoreCase) && it.PageNumber == pageNumber)
                    {
                        return it;
                    }
                }
            }

            // Create new Link
            ImageTypeOptions options = new ImageTypeOptions(pdfPath, false, ImageTypeSource.Link);
            options.PageNumber = pageNumber;
            options.Resolution = 300; // 300 DPI

            isNew = true;
            return ImageType.Create(doc, options);
        }

        public static ImageInstance PlacePdfOnSheet(Document doc, ViewSheet sheet, ImageType imageType, XYZ point, double widthMm)
        {
            // Pick BoxPlacement
            ImagePlacementOptions placementOpts = new ImagePlacementOptions(point, BoxPlacement.TopLeft);
            ImageInstance instance = ImageInstance.Create(doc, sheet, imageType.Id, placementOpts);

            // Scale to A4/source size
            instance.LockProportions = true;
            double internalWidth = RevitUnitUtils.MmToInternal(widthMm);
            instance.Width = internalWidth;

            Logger.Info(string.Format("Placed ImageInstance: UniqueId={0}, Sheet={1}, Width={2} mm", 
                instance.UniqueId, sheet.SheetNumber, widthMm));

            return instance;
        }

        public static void ScaleInstance(ImageInstance instance, double widthMm)
        {
            instance.LockProportions = true;
            double internalWidth = RevitUnitUtils.MmToInternal(widthMm);
            instance.Width = internalWidth;
        }
    }
}
