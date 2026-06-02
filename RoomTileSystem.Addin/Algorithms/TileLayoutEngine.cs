using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RoomTileSystem.Core;

namespace RoomTileSystem.Algorithms
{
    public class TileLayoutEngine
    {
        // 鋪貼地坪磁磚
        public SurfaceTileLayoutData LayoutFloor(
            List<CurveLoop> floorLoops,
            RoomLocalCoordinate localCoord,
            TilePatternParams patParams,
            string anchorId,
            string roomId,
            string hostId,
            double uOffset = 0,
            double vOffset = 0)
        {
            SurfaceTileLayoutData layout = new SurfaceTileLayoutData
            {
                Surface_ID = "Floor",
                Tile_Width = patParams.TileWidth,
                Tile_Height = patParams.TileHeight,
                Joint_Width = patParams.JointWidth,
                Rotation_Angle = 0
            };

            if (floorLoops == null || floorLoops.Count == 0) return layout;

            // 1. 將地板外圍與內洞轉換為 UV 空間的點集 (使用 RoomLocalCoordinate，轉為 mm 單位)
            List<List<UVPoint>> localLoops = new List<List<UVPoint>>();
            foreach (CurveLoop loop in floorLoops)
            {
                List<UVPoint> loopPts = new List<UVPoint>();
                foreach (Curve curve in loop)
                {
                    XYZ pt = curve.GetEndPoint(0);
                    UVPoint feetPt = localCoord.GlobalToLocalPoint(pt);
                    loopPts.Add(new UVPoint(feetPt.U * 304.8, feetPt.V * 304.8));
                }
                localLoops.Add(loopPts);
            }

            List<UVPoint> outerBoundary = localLoops[0];
            List<List<UVPoint>> innerBoundaries = new List<List<UVPoint>>();
            for (int k = 1; k < localLoops.Count; k++)
            {
                innerBoundaries.Add(localLoops[k]);
            }

            // 2. 計算包圍盒以涵蓋所有磁磚
            double minU = double.MaxValue, maxU = double.MinValue;
            double minV = double.MaxValue, maxV = double.MinValue;
            foreach (var p in outerBoundary)
            {
                if (p.U < minU) minU = p.U; if (p.U > maxU) maxU = p.U;
                if (p.V < minV) minV = p.V; if (p.V > maxV) maxV = p.V;
            }

            // 生成並裁剪磁磚
            List<TileData> generatedTiles = GenerateTilesForSurface(
                minU, maxU, minV, maxV, uOffset, vOffset, patParams, 
                anchorId, roomId, "Floor", hostId, outerBoundary, innerBoundaries, localCoord, null);

            layout.Tiles.AddRange(generatedTiles);
            return layout;
        }

        // 鋪貼牆面磁磚
        public SurfaceTileLayoutData LayoutWall(
            WallSurfaceGeometry wallGeo,
            RoomLocalCoordinate localCoord,
            TilePatternParams patParams,
            string anchorId,
            string roomId,
            int wallIndex,
            double uOffset = 0,
            double vOffset = 0)
        {
            string surfaceId = $"Wall_{wallIndex}";
            SurfaceTileLayoutData layout = new SurfaceTileLayoutData
            {
                Surface_ID = surfaceId,
                Tile_Width = patParams.TileWidth,
                Tile_Height = patParams.TileHeight,
                Joint_Width = patParams.JointWidth,
                Rotation_Angle = 0
            };

            if (wallGeo == null || wallGeo.FaceObject == null) return layout;

            // 強制使用「水平BasisX + 世界向上BasisY」建立牆面座標系
            // 不能直接用 wallGeo.YVector (pf.YVector)，因為 Revit 的 Face YVector
            // 不保證朝上，可能是 (0,0,-1)，導致磁磚水平縫高度全部倒反
            XYZ wallNorm = wallGeo.Normal.Normalize();
            XYZ worldUp = XYZ.BasisZ; // (0, 0, 1)
            // BasisX = 水平軸 = 世界向上 × 牆法向量，確保垂直於法向量且水平
            XYZ wallBasisX = worldUp.CrossProduct(wallNorm).Normalize();
            // 如果牆是水平的（極少情況），fallback 用 XVector
            if (wallBasisX.GetLength() < 0.01)
                wallBasisX = wallGeo.XVector.Normalize();
            XYZ wallBasisY = worldUp; // V 軸 = 世界向上，確保高度正確
            RoomLocalCoordinate wLocalCoord = new RoomLocalCoordinate(wallGeo.Origin, wallBasisX, wallBasisY);
            IList<CurveLoop> faceLoops = wallGeo.FaceObject.GetEdgesAsCurveLoops();
            List<List<UVPoint>> localLoops = new List<List<UVPoint>>();
            foreach (CurveLoop loop in faceLoops)
            {
                List<UVPoint> loopPts = new List<UVPoint>();
                foreach (Curve curve in loop)
                {
                    XYZ pt = curve.GetEndPoint(0);
                    UVPoint feetPt = wLocalCoord.GlobalToLocalPoint(pt);
                    loopPts.Add(new UVPoint(feetPt.U * 304.8, feetPt.V * 304.8));
                }
                localLoops.Add(loopPts);
            }

            if (localLoops.Count == 0) return layout;

            List<UVPoint> outerBoundary = localLoops[0];
            List<List<UVPoint>> innerBoundaries = new List<List<UVPoint>>();
            for (int k = 1; k < localLoops.Count; k++)
            {
                innerBoundaries.Add(localLoops[k]);
            }

            // 計算牆面外輪廓包圍盒
            double minU = double.MaxValue, maxU = double.MinValue;
            double minV = double.MaxValue, maxV = double.MinValue;
            foreach (var p in outerBoundary)
            {
                if (p.U < minU) minU = p.U; if (p.U > maxU) maxU = p.U;
                if (p.V < minV) minV = p.V; if (p.V > maxV) maxV = p.V;
            }

            List<TileData> generatedTiles = GenerateTilesForSurface(
                minU, maxU, minV, maxV, uOffset, vOffset, patParams,
                anchorId, roomId, surfaceId, wallGeo.HostElementId, outerBoundary, innerBoundaries, 
                wLocalCoord, wallGeo);

            layout.Tiles.AddRange(generatedTiles);
            return layout;
        }

        // 核心排版引擎分支生成器
        private List<TileData> GenerateTilesForSurface(
            double minU, double maxU, double minV, double maxV,
            double uOffset, double vOffset,
            TilePatternParams patParams,
            string anchorId, string roomId, string surfaceId, string hostId,
            List<UVPoint> outerBoundary,
            List<List<UVPoint>> innerBoundaries,
            RoomLocalCoordinate localCoord,
            WallSurfaceGeometry wallGeo)
        {
            List<TileData> tiles = new List<TileData>();

            double w = patParams.TileWidth;
            double h = patParams.TileHeight;
            double j = patParams.JointWidth;
            double t = patParams.Thickness;
            TilePatternStyle style = patParams.Style;
            double offsetPercent = patParams.OffsetPercent;

            double pitchX = w + j;
            double pitchY = h + j;

            bool isFloor = (localCoord != null);

            // 1. 水平縫與垂直縫特殊間距處理
            if (style == TilePatternStyle.HorizontalJoint)
            {
                w = (maxU - minU) + 2000.0;
                pitchX = w + j;
            }
            else if (style == TilePatternStyle.VerticalJoint)
            {
                h = (maxV - minV) + 2000.0;
                pitchY = h + j;
            }

            // 包圍網格計算
            int startI = (int)Math.Floor((minU - uOffset) / pitchX) - 3;
            int endI = (int)Math.Ceiling((maxU - uOffset) / pitchX) + 3;
            int startJ = (int)Math.Floor((minV - vOffset) / pitchY) - 3;
            int endJ = (int)Math.Ceiling((maxV - vOffset) / pitchY) + 3;

            // 如果是人字拼，使用獨立的人字拼生成法
            if (style == TilePatternStyle.Herringbone)
            {
                return GenerateHerringbone(minU, maxU, minV, maxV, uOffset, vOffset, patParams, anchorId, roomId, surfaceId, hostId, outerBoundary, innerBoundaries, localCoord, wallGeo);
            }

            for (int i = startI; i <= endI; i++)
            {
                for (int jIndex = startJ; jIndex <= endJ; jIndex++)
                {
                    List<List<UVPoint>> tilePolygons = new List<List<UVPoint>>();

                    // 依樣式產生多邊形頂點
                    if (style == TilePatternStyle.DoubleHeightStack)
                    {
                        double h1 = patParams.TileHeight;
                        double h2 = patParams.TileHeight2;
                        double pitch = h1 + h2 + 2 * j;
                        int cycle = (int)Math.Floor(jIndex / 2.0);
                        int rem = ((jIndex % 2) + 2) % 2;
                        
                        double v0 = cycle * pitch + vOffset;
                        double currH = h1;
                        if (rem == 1)
                        {
                            v0 += h1 + j;
                            currH = h2;
                        }
                        double u0 = i * pitchX + uOffset;

                        tilePolygons.Add(CreateRectUVs(u0, v0, w, currH));
                    }
                    else if (style == TilePatternStyle.DoubleWidthStack)
                    {
                        double w1 = patParams.TileWidth;
                        double w2 = patParams.TileWidth2;
                        double pitch = w1 + w2 + 2 * j;
                        int cycle = (int)Math.Floor(i / 2.0);
                        int rem = ((i % 2) + 2) % 2;

                        double u0 = cycle * pitch + uOffset;
                        double currW = w1;
                        if (rem == 1)
                        {
                            u0 += w1 + j;
                            currW = w2;
                        }
                        double v0 = jIndex * pitchY + vOffset;

                        tilePolygons.Add(CreateRectUVs(u0, v0, currW, h));
                    }
                    else if (style == TilePatternStyle.HexagonSeamless || style == TilePatternStyle.HexagonJoint)
                    {
                        double sideL = patParams.TileWidth; // 邊長以 TileWidth 代表
                        double hexJ = (style == TilePatternStyle.HexagonSeamless) ? 0.0 : j;
                        double lv = sideL + hexJ / Math.Sqrt(3.0);
                        double pX = Math.Sqrt(3.0) * lv;
                        double pY = 1.5 * lv;

                        double vc = jIndex * pY + vOffset;
                        double uc = i * pX + uOffset;
                        if (Math.Abs(jIndex % 2) == 1)
                        {
                            uc += 0.5 * pX;
                        }

                        tilePolygons.Add(CreateHexagonUVs(uc, vc, sideL));
                    }
                    else if (style == TilePatternStyle.FourTriangles || style == TilePatternStyle.TwoTriangles)
                    {
                        double size = patParams.TileWidth; // 方形邊長
                        double u0 = i * (size + j) + uOffset;
                        double v0 = jIndex * (size + j) + vOffset;

                        // 收縮內嵌正方形以留縫
                        double margin = 0.5 * j;
                        UVPoint A = new UVPoint(u0 + margin, v0 + margin);
                        UVPoint B = new UVPoint(u0 + size - margin, v0 + margin);
                        UVPoint C = new UVPoint(u0 + size - margin, v0 + size - margin);
                        UVPoint D = new UVPoint(u0 + margin, v0 + size - margin);
                        UVPoint M = new UVPoint(u0 + 0.5 * size, v0 + 0.5 * size);

                        if (style == TilePatternStyle.FourTriangles)
                        {
                            tilePolygons.Add(ShrinkTriangle(A, B, M, margin));
                            tilePolygons.Add(ShrinkTriangle(B, C, M, margin));
                            tilePolygons.Add(ShrinkTriangle(C, D, M, margin));
                            tilePolygons.Add(ShrinkTriangle(D, A, M, margin));
                        }
                        else
                        {
                            tilePolygons.Add(ShrinkTriangle(A, B, C, margin));
                            tilePolygons.Add(ShrinkTriangle(A, C, D, margin));
                        }
                    }
                    else
                    {
                        // 堆疊 (Stack) 或 錯位 (RunningBond)
                        double rowShift = 0.0;
                        if (style == TilePatternStyle.RunningBond || style == TilePatternStyle.StackOffset)
                        {
                            double shiftRatio = offsetPercent / 100.0;
                            double totalShift = jIndex * shiftRatio;
                            double fraction = totalShift - Math.Floor(totalShift);
                            rowShift = fraction * pitchX;
                        }

                        double u0 = i * pitchX + uOffset + rowShift;
                        double v0 = jIndex * pitchY + vOffset;

                        tilePolygons.Add(CreateRectUVs(u0, v0, w, h));
                    }

                    // 處理生成的所有多邊形磚
                    int subIndex = 0;
                    foreach (var poly in tilePolygons)
                    {
                        foreach (var clipped in PolygonClipper.ClipPolygonRegions(poly, outerBoundary))
                        {
                            if (clipped.Count < 3) continue;

                        double clippedArea = PolygonClipper.CalculatePolygonArea(clipped);
                        if (clippedArea < 100.0) continue;

                        PolygonClipper.EnsureCounterClockwise(clipped);

                        // 計算 clipped 的中心點 (重心)
                        double sumU = 0, sumV = 0;
                        foreach (var pt in clipped)
                        {
                            sumU += pt.U;
                            sumV += pt.V;
                        }
                        UVPoint centerUv = new UVPoint(sumU / clipped.Count, sumV / clipped.Count);

                        // 檢查重心是否在任何開口內部
                        bool isInsideOpening = false;
                        if (innerBoundaries != null)
                        {
                            foreach (var inner in innerBoundaries)
                            {
                                if (PolygonClipper.IsPointInPolygon(centerUv, inner))
                                {
                                    isInsideOpening = true;
                                    break;
                                }
                            }
                        }

                        if (isInsideOpening) continue; // 重心在開口中，直接丟棄

                        double fullArea = PolygonClipper.CalculatePolygonArea(poly);
                        string cutStatus = "Full";
                        string tileType = "Full";
                        if (Math.Abs(clippedArea - fullArea) > 1.0)
                        {
                            cutStatus = "PartiallyCut";
                            tileType = (clippedArea < fullArea * 0.15) ? "Border" : "Cut";
                        }

                        // 轉回 3D 空間座標 (使用統一且 100% 精確的 LocalToGlobalXYZ 方法)
                        List<XYZPoint> xyzBoundary = new List<XYZPoint>();
                        double sumX = 0, sumY = 0, sumZ = 0;
                        foreach (var uv in clipped)
                        {
                            UVPoint feetUv = new UVPoint(uv.U / 304.8, uv.V / 304.8);
                            XYZ gPt = localCoord.LocalToGlobalXYZ(feetUv, 0);
                            xyzBoundary.Add(new XYZPoint(gPt.X, gPt.Y, gPt.Z));
                            sumX += gPt.X; sumY += gPt.Y; sumZ += gPt.Z;
                        }
                        XYZPoint center = new XYZPoint(sumX / clipped.Count, sumY / clipped.Count, sumZ / clipped.Count);
                        XYZ normal = isFloor ? localCoord.BasisZ : wallGeo.Normal;

                        tiles.Add(new TileData
                        {
                            Tile_ID = $"{anchorId}_{surfaceId}_{i}_{jIndex}_{subIndex++}",
                            Anchor_ID = anchorId,
                            Room_ID = roomId,
                            Surface_ID = surfaceId,
                            Tile_Type = tileType,
                            Width = w,
                            Height = h,
                            Thickness = t,
                            Area = clippedArea / 1000000.0,
                            Cut_Status = cutStatus,
                            UVBoundary = clipped,
                            XYZBoundary = xyzBoundary,
                            CenterPoint = center,
                            Normal = new XYZPoint(normal.X, normal.Y, normal.Z),
                            Rotation = 0,
                            Material = isFloor ? "Tile_Floor_Default" : "Tile_Wall_Default",
                            Host_ID = hostId
                        });
                        }
                    }
                }
            }

            return tiles;
        }

        // 人字拼獨立排列生成
        private List<TileData> GenerateHerringbone(
            double minU, double maxU, double minV, double maxV,
            double uOffset, double vOffset,
            TilePatternParams patParams,
            string anchorId, string roomId, string surfaceId, string hostId,
            List<UVPoint> outerBoundary,
            List<List<UVPoint>> innerBoundaries,
            RoomLocalCoordinate localCoord,
            WallSurfaceGeometry wallGeo)
        {
            List<TileData> tiles = new List<TileData>();

            double w = patParams.TileWidth;
            double l = patParams.TileHeight; // 長度以 TileHeight 代表
            double jVal = patParams.JointWidth;
            double t = patParams.Thickness;
            bool isFloor = (localCoord != null);

            // 雙向 45 度人字拼週期排列
            // 平移步長加上縫寬
            double lp = l + jVal;
            double wp = w + jVal;

            // 為了包覆整個區域，我們估算網格索引範圍
            double boundsWidth = maxU - minU;
            double boundsHeight = maxV - minV;
            double maxDim = Math.Max(boundsWidth, boundsHeight);
            int range = (int)Math.Ceiling(maxDim / Math.Min(w, l)) + 10;

            int tileCounter = 0;

            for (int i = -range; i <= range; i++)
            {
                for (int j = -range; j <= range; j++)
                {
                    // 1. 水平磚 (磚 A)
                    // 幾何起點
                    double ua = i * lp + j * (wp - lp) + uOffset;
                    double va = i * (lp - wp) + j * lp + vOffset;

                    // 若超出外圍包圍盒太遠則跳過
                    if (ua < minU - lp - 500 || ua > maxU + lp + 500 || va < minV - lp - 500 || va > maxV + lp + 500)
                        continue;

                    List<UVPoint> polyA = CreateRectUVs(ua, va, l, w);

                    // 2. 垂直磚 (磚 B)
                    double ub = ua + lp - wp;
                    double vb = va + wp;
                    List<UVPoint> polyB = CreateRectUVs(ub, vb, w, l);

                    List<List<UVPoint>> components = new List<List<UVPoint>> { polyA, polyB };
                    int compIdx = 0;

                    foreach (var poly in components)
                    {
                        int currentComp = compIdx++; // 儲存當前組件索引 (0: A, 1: B)
                        foreach (var clipped in PolygonClipper.ClipPolygonRegions(poly, outerBoundary))
                        {
                            if (clipped.Count < 3) continue;

                        double clippedArea = PolygonClipper.CalculatePolygonArea(clipped);
                        if (clippedArea < 100.0) continue;

                        PolygonClipper.EnsureCounterClockwise(clipped);

                        // 計算重心點
                        double sumU = 0, sumV = 0;
                        foreach (var pt in clipped)
                        {
                            sumU += pt.U;
                            sumV += pt.V;
                        }
                        UVPoint centerUv = new UVPoint(sumU / clipped.Count, sumV / clipped.Count);

                        // 檢查是否在任何開口內部
                        bool isInsideOpening = false;
                        if (innerBoundaries != null)
                        {
                            foreach (var inner in innerBoundaries)
                            {
                                if (PolygonClipper.IsPointInPolygon(centerUv, inner))
                                {
                                    isInsideOpening = true;
                                    break;
                                }
                            }
                        }

                        if (isInsideOpening) continue;

                        double fullArea = PolygonClipper.CalculatePolygonArea(poly);
                        string cutStatus = "Full";
                        string tileType = "Full";
                        if (Math.Abs(clippedArea - fullArea) > 1.0)
                        {
                            cutStatus = "PartiallyCut";
                            tileType = (clippedArea < fullArea * 0.15) ? "Border" : "Cut";
                        }

                        List<XYZPoint> xyzBoundary = new List<XYZPoint>();
                        double sumX = 0, sumY = 0, sumZ = 0;
                        foreach (var uv in clipped)
                        {
                            UVPoint feetUv = new UVPoint(uv.U / 304.8, uv.V / 304.8);
                            XYZ gPt = localCoord.LocalToGlobalXYZ(feetUv, 0);
                            xyzBoundary.Add(new XYZPoint(gPt.X, gPt.Y, gPt.Z));
                            sumX += gPt.X; sumY += gPt.Y; sumZ += gPt.Z;
                        }
                        XYZPoint center = new XYZPoint(sumX / clipped.Count, sumY / clipped.Count, sumZ / clipped.Count);
                        XYZ normal = isFloor ? localCoord.BasisZ : wallGeo.Normal;

                        tiles.Add(new TileData
                        {
                            Tile_ID = $"{anchorId}_{surfaceId}_HB_{i}_{j}_{currentComp}_{tileCounter++}",
                            Anchor_ID = anchorId,
                            Room_ID = roomId,
                            Surface_ID = surfaceId,
                            Tile_Type = tileType,
                            Width = w,
                            Height = l,
                            Thickness = t,
                            Area = clippedArea / 1000000.0,
                            Cut_Status = cutStatus,
                            UVBoundary = clipped,
                            XYZBoundary = xyzBoundary,
                            CenterPoint = center,
                            Normal = new XYZPoint(normal.X, normal.Y, normal.Z),
                            Rotation = (currentComp == 0) ? 0.0 : 90.0,
                            Material = isFloor ? "Tile_Floor_Default" : "Tile_Wall_Default",
                            Host_ID = hostId
                        });
                        }
                    }
                }
            }

            return tiles;
        }

        // 幾何輔助: 建立矩形多邊形頂點
        private List<UVPoint> CreateRectUVs(double u0, double v0, double width, double height)
        {
            return new List<UVPoint>
            {
                new UVPoint(u0, v0),
                new UVPoint(u0 + width, v0),
                new UVPoint(u0 + width, v0 + height),
                new UVPoint(u0, v0 + height)
            };
        }

        // 幾何輔助: 建立正六邊形多邊形頂點 (頂角朝上)
        private List<UVPoint> CreateHexagonUVs(double uc, double vc, double sideL)
        {
            List<UVPoint> pts = new List<UVPoint>();
            for (int k = 0; k < 6; k++)
            {
                double angle = k * Math.PI / 3.0 + Math.PI / 6.0; // 旋轉 30 度以使一對邊平行於水平
                pts.Add(new UVPoint(uc + sideL * Math.Cos(angle), vc + sideL * Math.Sin(angle)));
            }
            return pts;
        }

        // 幾何輔助: 將三角形頂點向其重心收縮留縫
        private List<UVPoint> ShrinkTriangle(UVPoint a, UVPoint b, UVPoint c, double margin)
        {
            // 計算重心
            double cgU = (a.U + b.U + c.U) / 3.0;
            double cgV = (a.V + b.V + c.V) / 3.0;
            UVPoint cg = new UVPoint(cgU, cgV);

            // 頂點收縮
            Func<UVPoint, UVPoint> shrink = (p) =>
            {
                UVPoint dir = cg - p;
                double len = dir.Length();
                if (len < 0.001) return p;
                // 向內平移收縮 margin 長度
                return p + (dir * (margin / len));
            };

            return new List<UVPoint> { shrink(a), shrink(b), shrink(c) };
        }
    }
}
