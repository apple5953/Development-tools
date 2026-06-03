using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.TileElevationGenerator
{
    public static class FloorBoundaryExtractor
    {
        public static IList<CurveLoop> GetFloorBoundaryLoops(Floor floor)
        {
            var loops = new List<CurveLoop>();
            Options opt = new Options { DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geomElem = floor.get_Geometry(opt);
            
            PlanarFace topFace = null;
            double maxZ = -double.MaxValue;
            
            foreach (GeometryObject geomObj in geomElem)
            {
                if (geomObj is Solid solid && solid.Volume > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        // 尋找朝上 (Normal = (0, 0, 1)) 的 PlanarFace
                        if (face is PlanarFace planarFace && planarFace.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ))
                        {
                            double z = planarFace.Origin.Z;
                            if (z > maxZ)
                            {
                                maxZ = z;
                                topFace = planarFace;
                            }
                        }
                    }
                }
            }
            
            if (topFace != null)
            {
                foreach (var loop in topFace.GetEdgesAsCurveLoops())
                {
                    loops.Add(loop);
                }
            }
            
            return loops;
        }
    }
}
