using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.Models.Gizmo
{
    public class GizmoElement
    {
        public int Id { get; set; }
        public GizmoType Type { get; set; }
        public bool IsTop { get; set; }
        public bool IsHovered { get; set; }
        public bool IsDragging { get; set; }

        public XYZ P1 { get; set; }
        public XYZ P2 { get; set; }

        public BoundingBoxXYZ BoundingBox { get; set; }

        public List<XYZ> Vertices { get; } = new List<XYZ>();
        public List<int> Indices { get; } = new List<int>();

        public GizmoElement(int id, GizmoType type, bool isTop = false)
        {
            Id = id;
            Type = type;
            IsTop = isTop;
        }
    }
}
