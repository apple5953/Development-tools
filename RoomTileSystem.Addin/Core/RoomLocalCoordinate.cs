using Autodesk.Revit.DB;

namespace RoomTileSystem.Core
{
    public class RoomLocalCoordinate
    {
        public XYZ Origin { get; private set; }
        public XYZ BasisX { get; private set; } // Local U
        public XYZ BasisY { get; private set; } // Local V
        public XYZ BasisZ { get; private set; } // Face Normal

        // 用於 Revit 的建構子
        public RoomLocalCoordinate(FamilyInstance anchorInstance)
        {
            Transform transform = anchorInstance.GetTransform();
            Origin = transform.Origin;
            BasisX = transform.BasisX.Normalize();
            BasisY = transform.BasisY.Normalize();
            BasisZ = transform.BasisZ.Normalize();
        }

        // 用於手動設定或測試的建構子
        public RoomLocalCoordinate(XYZ origin, XYZ basisX, XYZ basisY)
        {
            Origin = origin;
            BasisX = basisX.Normalize();
            BasisY = basisY.Normalize();
            BasisZ = basisX.CrossProduct(basisY).Normalize();
        }

        // 將 Revit 3D 座標轉成局部 2D UV
        public UV GlobalToLocal(XYZ point)
        {
            XYZ relative = point - Origin;
            double u = relative.DotProduct(BasisX);
            double v = relative.DotProduct(BasisY);
            return new UV(u, v);
        }

        // 將局部 2D UV 轉回 Revit 3D 座標
        public XYZ LocalToGlobal(UV uv, double offsetZ = 0)
        {
            return Origin + (uv.U * BasisX) + (uv.V * BasisY) + (offsetZ * BasisZ);
        }

        // 轉換為自訂的幾何類別
        public UVPoint GlobalToLocalPoint(XYZ point)
        {
            UV uv = GlobalToLocal(point);
            return new UVPoint(uv.U, uv.V);
        }

        public XYZ LocalToGlobalXYZ(UVPoint uv, double offsetZ = 0)
        {
            UV revitUv = new UV(uv.U, uv.V);
            return LocalToGlobal(revitUv, offsetZ);
        }
    }
}
