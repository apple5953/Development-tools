using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using DevelopmentTools.Modules.AdaptiveSectionBox.Models;
using DevelopmentTools.Modules.AdaptiveSectionBox.Models.Gizmo;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.Interaction
{
    public static class GizmoBuilder
    {
        private const double VERTEX_SIZE = 0.4; // 控制頂點立方體半寬 (英呎)

        public static List<GizmoElement> BuildGizmos(PolygonPrism prism)
        {
            var gizmos = new List<GizmoElement>();
            if (prism == null || prism.BasePoints.Count < 3) return gizmos;

            int n = prism.BasePoints.Count;

            // 建立 3D 控制點 (Vertices)
            for (int i = 0; i < n; i++)
            {
                var bp = prism.BasePoints[i];

                // 1. 底面控制點 (IsTop = false)
                var botPt = new XYZ(bp.X, bp.Y, prism.ZMin);
                var botGizmo = new GizmoElement(i, GizmoType.Vertex, isTop: false);
                BuildCubeMesh(botPt, VERTEX_SIZE, botGizmo);
                gizmos.Add(botGizmo);

                // 2. 頂面控制點 (IsTop = true)
                var topPt = new XYZ(bp.X, bp.Y, prism.ZMax);
                var topGizmo = new GizmoElement(i, GizmoType.Vertex, isTop: true);
                BuildCubeMesh(topPt, VERTEX_SIZE, topGizmo);
                gizmos.Add(topGizmo);
            }

            return gizmos;
        }

        private static void BuildCubeMesh(XYZ center, double halfSize, GizmoElement gizmo)
        {
            gizmo.Vertices.Clear();
            gizmo.Indices.Clear();

            // 8 個端頂點
            gizmo.Vertices.Add(center + new XYZ(-halfSize, -halfSize, -halfSize)); // 0
            gizmo.Vertices.Add(center + new XYZ(halfSize, -halfSize, -halfSize));  // 1
            gizmo.Vertices.Add(center + new XYZ(halfSize, halfSize, -halfSize));   // 2
            gizmo.Vertices.Add(center + new XYZ(-halfSize, halfSize, -halfSize));  // 3
            gizmo.Vertices.Add(center + new XYZ(-halfSize, -halfSize, halfSize));  // 4
            gizmo.Vertices.Add(center + new XYZ(halfSize, -halfSize, halfSize));   // 5
            gizmo.Vertices.Add(center + new XYZ(halfSize, halfSize, halfSize));    // 6
            gizmo.Vertices.Add(center + new XYZ(-halfSize, halfSize, halfSize));   // 7

            // 12 個面索引
            int[] tempIndices = new int[]
            {
                0, 2, 1,  0, 3, 2, // Bottom
                4, 5, 6,  4, 6, 7, // Top
                0, 1, 5,  0, 5, 4, // Front
                1, 2, 6,  1, 6, 5, // Right
                2, 3, 7,  2, 7, 6, // Back
                3, 0, 4,  3, 4, 7  // Left
            };
            gizmo.Indices.AddRange(tempIndices);

            // 設定 BoundingBox
            var box = new BoundingBoxXYZ();
            box.Min = center - new XYZ(halfSize, halfSize, halfSize);
            box.Max = center + new XYZ(halfSize, halfSize, halfSize);
            gizmo.BoundingBox = box;
        }
    }
}
