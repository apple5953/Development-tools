using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using DevelopmentTools.Modules.AdaptiveSectionBox.Models;
using DevelopmentTools.Modules.AdaptiveSectionBox.Models.Gizmo;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.Rendering
{
    public class DirectContext3DServer : IDirectContext3DServer
    {
        private readonly Guid _serverId = new Guid("98b5843b-4171-460d-9b59-e932454a88bb");
        
        private PolygonPrism _cachedPrism;
        private List<GizmoElement> _cachedGizmos = new List<GizmoElement>();
        private bool _isEditModeActive;
        private bool _isDirty = true;

        // 面 (Triangles) 繪製 Buffers
        private VertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;
        private int _numVertices;
        private int _numIndices;
        private int _numTriangles;

        // 線框 (Lines) 繪製 Buffers
        private VertexBuffer _lineVertexBuffer;
        private IndexBuffer _lineIndexBuffer;
        private int _numLineVertices;
        private int _numLineIndices;
        private int _numLines;

        private VertexFormat _vertexFormat;

        public void UpdateData(PolygonPrism prism, List<GizmoElement> gizmos, bool isEditModeActive)
        {
            _cachedPrism = prism;
            _cachedGizmos = gizmos ?? new List<GizmoElement>();
            _isEditModeActive = isEditModeActive;
            _isDirty = true;
        }

        #region IExternalServer Members

        public Guid GetServerId() => _serverId;
        public string GetName() => "Adaptive Region 3D Rendering Server";
        public string GetDescription() => "用於在 Revit 3D 視圖中動態預覽與編輯 Adaptive Region。";
        public string GetApplicationId() => "c2d5d85c-4d33-4f9e-a89e-21ef1ea3b361";
        public string GetSourceId() => string.Empty;
        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public string GetVendorId() => "DeepMind";

        #endregion

        #region IDirectContext3DServer Members

        public bool CanExecute(View view)
        {
            if (view == null || view.ViewType != ViewType.ThreeD) return false;
            return _isEditModeActive && _cachedPrism != null;
        }

        public Outline GetBoundingBox(View view)
        {
            // 返還一個超大 Outline (正負兩百萬英呎) 繞過 Revit Frustum Culling 裁剪，確保每幀都呼叫 RenderScene
            var min = new XYZ(-2000000.0, -2000000.0, -2000000.0);
            var max = new XYZ(2000000.0, 2000000.0, 2000000.0);
            return new Outline(min, max);
        }

        public bool UseInView(View view) => CanExecute(view);
        public bool UsesHandles() => false;
        public bool UseInTransparentPass(View view) => true;

        public void RenderScene(View view, DisplayStyle displayStyle)
        {
            try
            {
                if (_vertexFormat == null)
                {
                    _vertexFormat = new VertexFormat(VertexFormatBits.PositionColored);
                }

                if (_isDirty)
                {
                    RebuildBuffers();
                    _isDirty = false;
                }

                // 1. 繪製面 (半透明量體與控制點)
                if (_vertexBuffer != null && _indexBuffer != null && _numTriangles > 0)
                {
                    if (DrawContext.IsAvailable())
                    {
                        var effectInstance = new EffectInstance(VertexFormatBits.PositionColored);
                        DrawContext.FlushBuffer(_vertexBuffer, _numVertices, _indexBuffer, _numIndices, _vertexFormat, effectInstance, PrimitiveType.TriangleList, 0, _numTriangles);
                    }
                }

                // 2. 繪製亮邊邊線段
                if (_lineVertexBuffer != null && _lineIndexBuffer != null && _numLines > 0)
                {
                    if (DrawContext.IsAvailable())
                    {
                        var effectInstance = new EffectInstance(VertexFormatBits.PositionColored);
                        DrawContext.FlushBuffer(_lineVertexBuffer, _numLineVertices, _lineIndexBuffer, _numLineIndices, _vertexFormat, effectInstance, PrimitiveType.LineList, 0, _numLines);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DirectContext3D] RenderScene 錯誤: {ex.Message}");
            }
        }

        #endregion

        private void RebuildBuffers()
        {
            // 釋放舊 Buffer
            _vertexBuffer?.Dispose();
            _vertexBuffer = null;
            _indexBuffer?.Dispose();
            _indexBuffer = null;

            _lineVertexBuffer?.Dispose();
            _lineVertexBuffer = null;
            _lineIndexBuffer?.Dispose();
            _lineIndexBuffer = null;

            if (_cachedPrism == null) return;

            // 1. 構造面 Buffers (TriangleList)
            var vertices = new List<VertexPositionColored>();
            var indices = new List<int>();

            BuildPrismMesh(_cachedPrism, vertices, indices);
            BuildGizmosMesh(_cachedGizmos, vertices, indices);

            if (vertices.Count > 0 && indices.Count > 0)
            {
                _numVertices = vertices.Count;
                _numIndices = indices.Count;
                _numTriangles = _numIndices / 3;

                int vertexSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(VertexPositionColored));
                int vertexBufferSize = _numVertices * vertexSize;
                _vertexBuffer = new VertexBuffer(vertexBufferSize);
                _vertexBuffer.Map(vertexBufferSize);
                var bufferStream = _vertexBuffer.GetVertexStreamPositionColored();
                foreach (var v in vertices) bufferStream.AddVertex(v);
                _vertexBuffer.Unmap();

                int triangleSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(IndexTriangle));
                int indexBufferSize = _numTriangles * triangleSize;
                _indexBuffer = new IndexBuffer(indexBufferSize);
                _indexBuffer.Map(indexBufferSize);
                var indexStream = _indexBuffer.GetIndexStreamTriangle();
                for (int i = 0; i < _numIndices; i += 3)
                {
                    indexStream.AddTriangle(new IndexTriangle(indices[i], indices[i + 1], indices[i + 2]));
                }
                _indexBuffer.Unmap();
            }

            // 2. 構造線 Buffers (LineList)
            var lineVertices = new List<VertexPositionColored>();
            var lineIndices = new List<int>();

            BuildPrismWireframe(_cachedPrism, lineVertices, lineIndices);

            if (lineVertices.Count > 0 && lineIndices.Count > 0)
            {
                _numLineVertices = lineVertices.Count;
                _numLineIndices = lineIndices.Count;
                _numLines = _numLineIndices / 2;

                int vertexSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(VertexPositionColored));
                int lineVSize = _numLineVertices * vertexSize;
                _lineVertexBuffer = new VertexBuffer(lineVSize);
                _lineVertexBuffer.Map(lineVSize);
                var stream = _lineVertexBuffer.GetVertexStreamPositionColored();
                foreach (var v in lineVertices) stream.AddVertex(v);
                _lineVertexBuffer.Unmap();

                int lineSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(IndexLine));
                int lineISize = _numLines * lineSize;
                _lineIndexBuffer = new IndexBuffer(lineISize);
                _lineIndexBuffer.Map(lineISize);
                var streamIdx = _lineIndexBuffer.GetIndexStreamLine();
                for (int i = 0; i < _numLineIndices; i += 2)
                {
                    streamIdx.AddLine(new IndexLine(lineIndices[i], lineIndices[i + 1]));
                }
                _lineIndexBuffer.Unmap();
            }
        }

        private void BuildPrismMesh(PolygonPrism prism, List<VertexPositionColored> vertices, List<int> indices)
        {
            if (prism == null || prism.BasePoints.Count < 3) return;

            int n = prism.BasePoints.Count;
            // 藍色半透明面 (60% 透明度，Revit 透明度 0~100 範圍下 60 剛好適中)
            var color = new ColorWithTransparency(30, 144, 255, 60);

            int startIdx = vertices.Count;
            var pts = prism.GetVertices();
            foreach (var p in pts)
            {
                vertices.Add(new VertexPositionColored(p, color));
            }

            // 底面三角形索引 (順時針)
            for (int i = 1; i < n - 1; i++)
            {
                indices.Add(startIdx + 0);
                indices.Add(startIdx + i + 1);
                indices.Add(startIdx + i);
            }

            // 頂面三角形索引 (逆時針)
            int topOffset = n;
            for (int i = 1; i < n - 1; i++)
            {
                indices.Add(startIdx + topOffset + 0);
                indices.Add(startIdx + topOffset + i);
                indices.Add(startIdx + topOffset + i + 1);
            }

            // 側面上色
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                int b1 = startIdx + i;
                int b2 = startIdx + next;
                int t1 = startIdx + topOffset + i;
                int t2 = startIdx + topOffset + next;

                indices.Add(b1);
                indices.Add(b2);
                indices.Add(t2);

                indices.Add(b1);
                indices.Add(t2);
                indices.Add(t1);
            }
        }

        private void BuildGizmosMesh(List<GizmoElement> gizmos, List<VertexPositionColored> vertices, List<int> indices)
        {
            foreach (var gizmo in gizmos)
            {
                if (gizmo.Type != GizmoType.Vertex) continue;

                // 依據狀態決定控制點顏色：Hovered (紅色), Dragging (黃色), 平常 (亮藍色不透明)
                ColorWithTransparency color;
                if (gizmo.IsDragging)
                {
                    color = new ColorWithTransparency(255, 215, 0, 0); // 亮黃
                }
                else if (gizmo.IsHovered)
                {
                    color = new ColorWithTransparency(255, 0, 0, 0);   // 亮紅
                }
                else
                {
                    color = new ColorWithTransparency(0, 191, 255, 0); // 亮藍
                }

                int startIdx = vertices.Count;
                foreach (var v in gizmo.Vertices)
                {
                    vertices.Add(new VertexPositionColored(v, color));
                }

                foreach (var idx in gizmo.Indices)
                {
                    indices.Add(startIdx + idx);
                }
            }
        }

        private void BuildPrismWireframe(PolygonPrism prism, List<VertexPositionColored> vertices, List<int> indices)
        {
            if (prism == null || prism.BasePoints.Count < 3) return;

            int n = prism.BasePoints.Count;
            // 亮藍色邊線段 (0% 透明度，實體邊框)
            var color = new ColorWithTransparency(0, 191, 255, 0);

            int startIdx = vertices.Count;
            var pts = prism.GetVertices();
            foreach (var p in pts)
            {
                vertices.Add(new VertexPositionColored(p, color));
            }

            // 1. 底面多邊形邊
            for (int i = 0; i < n; i++)
            {
                indices.Add(startIdx + i);
                indices.Add(startIdx + ((i + 1) % n));
            }

            // 2. 頂面多邊形邊
            int topOffset = n;
            for (int i = 0; i < n; i++)
            {
                indices.Add(startIdx + topOffset + i);
                indices.Add(startIdx + topOffset + ((i + 1) % n));
            }

            // 3. 垂直立柱邊
            for (int i = 0; i < n; i++)
            {
                indices.Add(startIdx + i);
                indices.Add(startIdx + topOffset + i);
            }
        }

        public void Dispose()
        {
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _lineVertexBuffer?.Dispose();
            _lineIndexBuffer?.Dispose();
        }
    }
}
