using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitPerspectiveMinimap.Models;

namespace RevitPerspectiveMinimap.Core
{
    public static class CameraManager
    {
        private const double FeetToMm = 304.8;
        private const double MmToFeet = 1.0 / 304.8;

        public static List<PerspectiveCameraModel> GetAllPerspectiveViews(Document doc, View3D activeView)
        {
            List<PerspectiveCameraModel> result = new List<PerspectiveCameraModel>();
            if (doc == null) return result;

            List<View3D> views = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(v => !v.IsTemplate && v.IsPerspective)
                .OrderBy(v => v.Name)
                .ToList();

            string[] colorPalette = new string[] { "#E53935", "#1E88E5", "#43A047", "#FB8C00", "#8E24AA", "#00ACC1", "#3949AB", "#D81B60" };
            int colorIndex = 0;

            foreach (View3D view in views)
            {
                try
                {
                    ViewOrientation3D orientation = view.GetOrientation();
                    XYZ eye = orientation.EyePosition;
                    XYZ forward = orientation.ForwardDirection;
                    XYZ up = orientation.UpDirection;
                    XYZ target = eye + forward.Multiply(25.0);

                    DetectedLevelResult detected = SmartLevelDetector.DetectLevel(doc, eye, null);

                    PerspectiveCameraModel model = new PerspectiveCameraModel();
                    model.ViewId = view.Id;
                    model.ViewName = view.Name;
                    model.EyePosition = eye;
                    model.TargetPosition = target;
                    model.ForwardDirection = forward;
                    model.UpDirection = up;
                    model.AbsoluteElevationMm = eye.Z * FeetToMm;
                    model.EyeHeightAboveFloorMm = (eye.Z - detected.BaseElevationFeet) * FeetToMm;
                    model.AssociatedLevelId = (detected.Level != null) ? detected.Level.Id : null;
                    model.AssociatedLevelName = (detected.Level != null) ? detected.Level.Name : "未指定";
                    model.DetectedSource = detected.DetectionMethod;
                    model.IsActive = (activeView != null && activeView.Id == view.Id);
                    model.ColorHex = colorPalette[colorIndex % colorPalette.Length];

                    colorIndex++;
                    result.Add(model);
                }
                catch
                {
                }
            }

            return result;
        }

        public static void SetCameraEyePosition(Document doc, ElementId viewId, XYZ newEye)
        {
            if (doc == null || viewId == null) return;
            View3D view = doc.GetElement(viewId) as View3D;
            if (view == null || !view.IsPerspective) return;

            using (Transaction tx = new Transaction(doc, "更新透視圖站點位置"))
            {
                tx.Start();
                ViewOrientation3D orient = view.GetOrientation();
                UpdateViewCropBoxAndOrientation(view, newEye, orient.UpDirection, orient.ForwardDirection);
                tx.Commit();
            }
        }

        public static void SetCameraDirection(Document doc, ElementId viewId, double yawDeg, double pitchDeg)
        {
            if (doc == null || viewId == null) return;
            View3D view = doc.GetElement(viewId) as View3D;
            if (view == null || !view.IsPerspective) return;

            using (Transaction tx = new Transaction(doc, "更新透視圖視角方向"))
            {
                tx.Start();
                ViewOrientation3D orient = view.GetOrientation();
                XYZ eye = orient.EyePosition;

                double yawRad = yawDeg * (Math.PI / 180.0);
                double pitchRad = pitchDeg * (Math.PI / 180.0);

                double cosPitch = Math.Cos(pitchRad);
                double sinPitch = Math.Sin(pitchRad);

                double forwardX = Math.Cos(yawRad) * cosPitch;
                double forwardY = Math.Sin(yawRad) * cosPitch;
                double forwardZ = sinPitch;

                XYZ forward = new XYZ(forwardX, forwardY, forwardZ).Normalize();
                XYZ worldUp = XYZ.BasisZ;
                XYZ right = (Math.Abs(forward.DotProduct(worldUp)) > 0.99) ? XYZ.BasisX : forward.CrossProduct(worldUp).Normalize();
                XYZ up = right.CrossProduct(forward).Normalize();

                UpdateViewCropBoxAndOrientation(view, eye, up, forward);
                tx.Commit();
            }
        }

        public static void SetEyeHeightAboveFloor(Document doc, ElementId viewId, ElementId explicitLevelId, double heightMm)
        {
            if (doc == null || viewId == null) return;
            View3D view = doc.GetElement(viewId) as View3D;
            if (view == null || !view.IsPerspective) return;

            ViewOrientation3D orient = view.GetOrientation();
            XYZ eye = orient.EyePosition;

            double baseElevationFeet = 0;
            if (explicitLevelId != null)
            {
                Level lvl = doc.GetElement(explicitLevelId) as Level;
                if (lvl != null)
                {
                    baseElevationFeet = lvl.Elevation;
                }
                else
                {
                    DetectedLevelResult detected = SmartLevelDetector.DetectLevel(doc, eye, null);
                    baseElevationFeet = detected.BaseElevationFeet;
                }
            }
            else
            {
                DetectedLevelResult detected = SmartLevelDetector.DetectLevel(doc, eye, null);
                baseElevationFeet = detected.BaseElevationFeet;
            }

            double targetZ = baseElevationFeet + (heightMm * MmToFeet);
            XYZ newEye = new XYZ(eye.X, eye.Y, targetZ);

            using (Transaction tx = new Transaction(doc, "調整透視圖視線高度"))
            {
                tx.Start();
                UpdateViewCropBoxAndOrientation(view, newEye, orient.UpDirection, orient.ForwardDirection);
                tx.Commit();
            }
        }

        public static void SetCameraAbsoluteZ(Document doc, ElementId viewId, double zFeet)
        {
            if (doc == null || viewId == null) return;
            View3D view = doc.GetElement(viewId) as View3D;
            if (view == null || !view.IsPerspective) return;

            ViewOrientation3D orient = view.GetOrientation();
            XYZ eye = orient.EyePosition;
            XYZ newEye = new XYZ(eye.X, eye.Y, zFeet);

            using (Transaction tx = new Transaction(doc, "調整透視圖絕對高程"))
            {
                tx.Start();
                UpdateViewCropBoxAndOrientation(view, newEye, orient.UpDirection, orient.ForwardDirection);
                tx.Commit();
            }
        }

        public static View3D CreatePerspectiveView(Document doc, string name, XYZ eye, XYZ target, Level level)
        {
            ViewFamilyType viewFamilyType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

            if (viewFamilyType == null) return null;

            View3D newView = null;
            using (Transaction tx = new Transaction(doc, "新建透視圖"))
            {
                tx.Start();
                newView = View3D.CreatePerspective(doc, viewFamilyType.Id);
                newView.Name = GetUniqueViewName(doc, name);

                XYZ forward = (target - eye).Normalize();
                XYZ right = (Math.Abs(forward.DotProduct(XYZ.BasisZ)) > 0.99) ? XYZ.BasisX : forward.CrossProduct(XYZ.BasisZ).Normalize();
                XYZ up = right.CrossProduct(forward).Normalize();

                UpdateViewCropBoxAndOrientation(newView, eye, up, forward);
                tx.Commit();
            }

            return newView;
        }

        private static void UpdateViewCropBoxAndOrientation(View3D view, XYZ eye, XYZ up, XYZ forward)
        {
            if (view == null || eye == null || up == null || forward == null) return;

            ViewOrientation3D orient = new ViewOrientation3D(eye, up, forward);
            view.SetOrientation(orient);

            try
            {
                XYZ worldUp = XYZ.BasisZ;
                XYZ right = (Math.Abs(forward.DotProduct(worldUp)) > 0.99) ? XYZ.BasisX : forward.CrossProduct(worldUp).Normalize();
                XYZ realUp = right.CrossProduct(forward).Normalize();

                BoundingBoxXYZ box = view.CropBox;
                if (box != null)
                {
                    Transform trf = Transform.Identity;
                    trf.Origin = eye;
                    trf.BasisX = right;
                    trf.BasisY = realUp;
                    trf.BasisZ = -forward;
                    box.Transform = trf;
                    view.CropBox = box;
                    view.CropBoxActive = true;
                    view.CropBoxVisible = true;
                }
            }
            catch { }
        }

        private static string GetUniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int counter = 1;
            while (new FilteredElementCollector(doc).OfClass(typeof(View)).Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                name = string.Format("{0} ({1})", baseName, counter++);
            }
            return name;
        }
    }
}