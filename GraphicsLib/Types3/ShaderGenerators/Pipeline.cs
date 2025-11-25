using GraphicsLib.Types;
using GraphicsLib.Types.GltfTypes;
using GraphicsLib.Types2;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public class Pipeline<Shader, FloatOps> : IPipeline
        where Shader : ShaderBase, new()
        where FloatOps : struct, IFloatOpsProvider
    {
        private readonly Shader shaderProcessor = new();
        private readonly FloatOps floatOpsProcessor  = new();
        private readonly int FloatsPerVertexCount;
        private ModelScene? scene;
        private ModelPrimitive? currentPrimitive;
        private ZBufferV3? zBuffer;
        private Matrix4x4 cameraTransform;
        private Matrix4x4 projectionTransform;
        private Matrix4x4 viewPortTransform;
        
        private int screenHeight;
        private int screenWidth;
        private float[]? verticesBuffer = null;
        public Pipeline()
        {
            FloatsPerVertexCount = floatOpsProcessor.Length();
        }
        public void BindScene(in ModelScene scene)
        {
            this.scene = scene;
            cameraTransform = scene.Camera!.ViewMatrix;
            projectionTransform = scene.Camera.ProjectionMatrix;
            viewPortTransform = scene.Camera.ViewPortMatrix;
            screenHeight = (int)scene.Camera.ScreenHeight;
            screenWidth = (int)scene.Camera.ScreenWidth;
            shaderProcessor.BindScene(scene);
        }
       

        public void BindZBuffer(ZBufferV3 zBuffer)
        {
            this.zBuffer = zBuffer;
        }

        public void UnbindScene()
        {
            shaderProcessor.UnbindScene();
            scene = null;
        }

        public void BindSkin(in ModelSkin skin)
        {
            shaderProcessor.BindSkin(skin);
        }

        public void UnbindSkin()
        {
            shaderProcessor.UnbindSkin();
        }
        public void UnbindZBuffer()
        {
            zBuffer = null;
        }
        private ParallelOptions RenderLoopParallelOptions { get; set; } = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 };
        public void Render(ref ModelPrimitive primitive, in Matrix4x4 transformation)
        {
            shaderProcessor.BindPrimitive(ref primitive, transformation);
            PreprocessVertices(primitive);

            switch (primitive.Mode)
            {
                case Types.GltfTypes.GltfMeshMode.TRIANGLES:
                    {
                        Parallel.For(0, primitive.Indices!.Length / 3, RenderLoopParallelOptions, AssembleTriangle);
                        // for(int i = 0; i < primitive.Indices!.Length / 3; i++)
                                // AssembleTriangle(i);
                    }
                    break;
                case Types.GltfTypes.GltfMeshMode.TRIANGLE_STRIP:
                    {
                        Parallel.For(0, primitive.Indices!.Length - 2, RenderLoopParallelOptions, AssembleTriangleStrip);
                    }
                    break;
                case Types.GltfTypes.GltfMeshMode.TRIANGLE_FAN:
                    {
                        Parallel.For(2, primitive.Indices!.Length - 2, RenderLoopParallelOptions, AssembleTriangleFan);
                    }
                    break;
            }
            CleanUpVertices();
            shaderProcessor.UnbindPrimitive();
        }
        private ParallelOptions PreprocessLoopParallelOptions { get; set; } = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        private void PreprocessVertices(ModelPrimitive primitive)
        {
            currentPrimitive = primitive;
            ArrayPool<float> arrayPool = ArrayPool<float>.Shared;
            int verticesCount = currentPrimitive.VertexCount;
            verticesBuffer = arrayPool.Rent(verticesCount * FloatsPerVertexCount);
            PreprocessLoopParallelOptions.MaxDegreeOfParallelism = Math.Min(verticesCount / 128 + 1, Environment.ProcessorCount);

            if (verticesCount > 128)
            {
                Parallel.For(0, verticesCount, PreprocessLoopParallelOptions, i =>
                    shaderProcessor.VertexShader(i, verticesBuffer.SliceSpan(i * FloatsPerVertexCount, FloatsPerVertexCount))
                );
            }
            else
            {
                for (int i = 0; i < verticesCount; i++)
                {
                    shaderProcessor.VertexShader(i, verticesBuffer.SliceSpan(i * FloatsPerVertexCount, FloatsPerVertexCount));
                }
            }

        }
        private void CleanUpVertices()
        {
            currentPrimitive = null;
            ArrayPool<float> arrayPool = ArrayPool<float>.Shared;
            arrayPool.Return(verticesBuffer!);
            verticesBuffer = null;
        }
        private Span<float> GetVertexRefFromBuffer(int index) => verticesBuffer.AsSpan(index * FloatsPerVertexCount, FloatsPerVertexCount);
        private void CopyVertexFromBuffer(int index, Span<float> output) => GetVertexRefFromBuffer(index).CopyTo(output);
        private void AssembleTriangle(int i)
        {
            int index0 = currentPrimitive!.Indices![3 * i];
            int index1 = currentPrimitive!.Indices![3 * i + 1];
            int index2 = currentPrimitive!.Indices![3 * i + 2];
            Span<float> v0 = stackalloc float[FloatsPerVertexCount];
            Span<float> v1 = stackalloc float[FloatsPerVertexCount];
            Span<float> v2 = stackalloc float[FloatsPerVertexCount];
            CopyVertexFromBuffer(index0, v0);
            CopyVertexFromBuffer(index1, v1);
            CopyVertexFromBuffer(index2, v2);
            MoveTriangleToCameraSpace(v0, v1, v2);
        }

        private void AssembleTriangleStrip(int i)
        {
            Span<float> v0 = stackalloc float[FloatsPerVertexCount];
            Span<float> v1 = stackalloc float[FloatsPerVertexCount];
            Span<float> v2 = stackalloc float[FloatsPerVertexCount];
            if (i % 2 == 1)
            {
                CopyVertexFromBuffer(currentPrimitive!.Indices![i], v0);
                CopyVertexFromBuffer(currentPrimitive!.Indices![i + 1], v1);
                CopyVertexFromBuffer(currentPrimitive!.Indices![i + 2], v2);
                
            }
            else
            {
                CopyVertexFromBuffer(currentPrimitive!.Indices![i + 1], v0);
                CopyVertexFromBuffer(currentPrimitive!.Indices![i], v1);
                CopyVertexFromBuffer(currentPrimitive!.Indices![i + 2], v2);
            }
            MoveTriangleToCameraSpace(v0, v1, v2);
        }

        private void AssembleTriangleFan(int i)
        {
            int index0 = currentPrimitive!.Indices![0];
            int index1 = currentPrimitive!.Indices![i + 1];
            int index2 = currentPrimitive!.Indices![i + 2];
            Span<float> v0 = stackalloc float[FloatsPerVertexCount];
            Span<float> v1 = stackalloc float[FloatsPerVertexCount];
            Span<float> v2 = stackalloc float[FloatsPerVertexCount];
            CopyVertexFromBuffer(index0, v0);
            CopyVertexFromBuffer(index1, v1);
            CopyVertexFromBuffer(index2, v2);
            MoveTriangleToCameraSpace(v0, v1, v2);
        }

        private void MoveTriangleToCameraSpace(Span<float> v0, Span<float> v1, Span<float> v2)
        {
            TransformPosition(v0, cameraTransform);
            TransformPosition(v1, cameraTransform);
            TransformPosition(v2, cameraTransform);

            Vector4 p0 = ReadVector4(v0, 0);
            Vector4 p1 = ReadVector4(v1, 0);
            Vector4 p2 = ReadVector4(v2, 0);

            Vector3 edge1 = (p1 - p0).AsVector3();
            Vector3 edge2 = (p2 - p0).AsVector3();
            Vector3 normal = Vector3.Cross(edge2, edge1);
            float orientation = Vector4.Dot(normal.AsVector4(), p0);

            if (orientation <= 0)
                return;

            ProjectTriangle(v0, v1, v2);
        }

        private static void TransformPosition(Span<float> vertex, Matrix4x4 transform)
        {
            Vector4 position = ReadVector4(vertex, 0);
            position = Vector4.Transform(position, transform);
            WriteVector4(vertex, 0, position);
        }

        private void ProjectTriangle(Span<float> v0, Span<float> v1, Span<float> v2)
        {
            TransformPosition(v0, projectionTransform);
            TransformPosition(v1, projectionTransform);
            TransformPosition(v2, projectionTransform);
            CullAndClipTriangle(v0, v1, v2);
        }

        private void CullAndClipTriangle(Span<float> v0, Span<float> v1, Span<float> v2)
        {
            Vector4 p0 = ReadVector4(v0, 0);
            Vector4 p1 = ReadVector4(v1, 0);
            Vector4 p2 = ReadVector4(v2, 0);

            // Frustum culling checks (same logic)
            if (p0.X > p0.W && p1.X > p1.W && p2.X > p2.W) return;
            if (p0.X < -p0.W && p1.X < -p1.W && p2.X < -p2.W) return;
            if (p0.Y > p0.W && p1.Y > p1.W && p2.Y > p2.W) return;
            if (p0.Y < -p0.W && p1.Y < -p1.W && p2.Y < -p2.W) return;
            if (p0.Z > p0.W && p1.Z > p1.W && p2.Z > p2.W) return;
            if (p0.Z < 0 && p1.Z < 0 && p2.Z < 0) return;

            // Near plane clipping
            if (p0.Z < 0)
            {
                if (p1.Z < 0) ClipTriangleIntoOne(v0, v1, v2);
                else if (p2.Z < 0) ClipTriangleIntoOne(v0, v2, v1);
                else ClipTriangleIntoTwo(v0, v1, v2);
            }
            else if (p1.Z < 0)
            {
                if (p2.Z < 0) ClipTriangleIntoOne(v1, v2, v0);
                else ClipTriangleIntoTwo(v1, v0, v2);
            }
            else if (p2.Z < 0)
            {
                ClipTriangleIntoTwo(v2, v0, v1);
            }
            else
            {
                ProjectTriangleToViewPort(v0, v1, v2);
            }
        }

        private void ClipTriangleIntoTwo(Span<float> pointBehind, Span<float> point1, Span<float> point2)
        {
            Vector4 posBehind = ReadVector4(pointBehind, 0);
            Vector4 pos1 = ReadVector4(point1, 0);
            Vector4 pos2 = ReadVector4(point2, 0);

            float c0 = (-posBehind.Z) / (pos1.Z - posBehind.Z);
            float c1 = (-posBehind.Z) / (pos2.Z - posBehind.Z);

            Span<float> leftInterpolant = stackalloc float[FloatsPerVertexCount];
            Span<float> rightInterpolant = stackalloc float[FloatsPerVertexCount];
            
            floatOpsProcessor.Lerp(leftInterpolant, pointBehind, point1, c0);
            floatOpsProcessor.Lerp(rightInterpolant, pointBehind, point2, c1);

            // Make copies to avoid modifying points before rendering second triangle
            Span<float> point2Copy = stackalloc float[FloatsPerVertexCount];
            point2.CopyTo(point2Copy); 
            Span<float> leftInterpolantCopy = stackalloc float[FloatsPerVertexCount];
            leftInterpolant.CopyTo(leftInterpolantCopy);


            ProjectTriangleToViewPort(leftInterpolantCopy, point1, point2Copy);
            ProjectTriangleToViewPort(rightInterpolant, leftInterpolant, point2);
        }

        private void ClipTriangleIntoOne(Span<float> leftPointBehind, Span<float> rightPointBehind, Span<float> point2)
        {
            Vector4 posLeft = ReadVector4(leftPointBehind, 0);
            Vector4 posRight = ReadVector4(rightPointBehind, 0);
            Vector4 pos2 = ReadVector4(point2, 0);

            float c0 = (-posLeft.Z) / (pos2.Z - posLeft.Z);
            float c1 = (-posRight.Z) / (pos2.Z - posRight.Z);

            Span<float> leftInterpolant = stackalloc float[FloatsPerVertexCount];
            Span<float> rightInterpolant = stackalloc float[FloatsPerVertexCount];

            floatOpsProcessor.Lerp(leftInterpolant, leftPointBehind, point2, c0);
            floatOpsProcessor.Lerp(rightInterpolant, rightPointBehind, point2, c1);

            ProjectTriangleToViewPort(leftInterpolant, point2, rightInterpolant);
        }

        private void ProjectTriangleToViewPort(Span<float> v0, Span<float> v1, Span<float> v2)
        {
            TransformToViewPort(v0);
            TransformToViewPort(v1);
            TransformToViewPort(v2);
            DrawTriangle(v0, v1, v2);
        }

        private void TransformToViewPort(Span<float> vertex)
        {
            Vector4 position = ReadVector4(vertex, 0);
            float invZ = 1 / position.W;

            // Perspective correction: multiply all vertex attributes by 1/W
            floatOpsProcessor.MulScalar(vertex, vertex, invZ);

            // Transform to viewport
            position = ReadVector4(vertex, 0);
            Vector4 ndcPosition = Vector4.Transform(position, viewPortTransform);
            ndcPosition.W = invZ; // Store 1/W for later correction
            WriteVector4(vertex, 0, ndcPosition);
        }

        private void DrawTriangle(Span<float> v0, Span<float> v1, Span<float> v2)
        {
            Vector4 p0 = ReadVector4(v0, 0);
            Vector4 p1 = ReadVector4(v1, 0);
            Vector4 p2 = ReadVector4(v2, 0);

            // Sort vertices by Y (using Span references)
            Span<float> min = v0, mid = v1, max = v2;
            Vector4 posMin = p0, posMid = p1, posMax = p2;

            // Sorting logic (similar to original but with Span)
            if (posMid.Y < posMin.Y) { Swap(ref min, ref mid); Swap(ref posMin, ref posMid); }
            if (posMax.Y < posMin.Y) { Swap(ref min, ref max); Swap(ref posMin, ref posMax); }
            if (posMax.Y < posMid.Y) { Swap(ref mid, ref max); Swap(ref posMid, ref posMax); }

            if (posMin.Y == posMid.Y)
            {
                if (posMid.X < posMin.X) { Swap(ref min, ref mid); }
                DrawFlatTopTriangle(min, mid, max);
            }
            else if (posMax.Y == posMid.Y)
            {
                if (posMax.X > posMid.X) { Swap(ref mid, ref max); }
                DrawFlatBottomTriangle(min, mid, max);
            }
            else
            {
                float c = (posMid.Y - posMin.Y) / (posMax.Y - posMin.Y);
                Span<float> interpolant = stackalloc float[FloatsPerVertexCount];
                floatOpsProcessor.Lerp(interpolant, min, max, c);

                // Make copies to avoid modifying points before rendering second triangle
                Span<float> midPointCopy = stackalloc float[FloatsPerVertexCount];
                mid.CopyTo(midPointCopy);
                Span<float> interpolantCopy = stackalloc float[FloatsPerVertexCount];
                interpolant.CopyTo(interpolantCopy);

                Vector4 posInterpolant = ReadVector4(interpolant, 0);
                if (posInterpolant.X > posMid.X)
                {
                    DrawFlatBottomTriangle(min, interpolantCopy, midPointCopy);
                    DrawFlatTopTriangle(mid, interpolant, max);
                }
                else
                {
                    DrawFlatBottomTriangle(min, midPointCopy, interpolantCopy);
                    DrawFlatTopTriangle(interpolant, mid, max);
                }
            }
        }
        private void DrawFlatTopTriangle(Span<float> leftTopPoint, Span<float> rightTopPoint, Span<float> bottomPoint)
        {
            Vector4 posLeftTop = ReadVector4(leftTopPoint, 0);
            Vector4 posRightTop = ReadVector4(rightTopPoint, 0);
            Vector4 posBottom = ReadVector4(bottomPoint, 0);

            float dy = posBottom.Y - posLeftTop.Y;
            if (Math.Abs(dy) < float.Epsilon) return;

            // Вычисляем дельты для левого и правого краев
            Span<float> dLeftPoint = stackalloc float[FloatsPerVertexCount];
            Span<float> dRightPoint = stackalloc float[FloatsPerVertexCount];

            // dLeftPoint = (bottomPoint - leftTopPoint) / dy
            floatOpsProcessor.Sub(dLeftPoint, bottomPoint, leftTopPoint);
            floatOpsProcessor.DivScalar(dLeftPoint, dLeftPoint, dy);

            // dRightPoint = (bottomPoint - rightTopPoint) / dy
            floatOpsProcessor.Sub(dRightPoint, bottomPoint, rightTopPoint);
            floatOpsProcessor.DivScalar(dRightPoint, dRightPoint, dy);

            // Интерполянт для горизонтальной линии
            Span<float> dLineInterpolant = stackalloc float[FloatsPerVertexCount];
            float dx = posRightTop.X - posLeftTop.X;
            if (Math.Abs(dx) < float.Epsilon) return;

            // dLineInterpolant = (rightTopPoint - leftTopPoint) / dx
            floatOpsProcessor.Sub(dLineInterpolant, rightTopPoint, leftTopPoint);
            floatOpsProcessor.DivScalar(dLineInterpolant, dLineInterpolant, dx);

            DrawFlatTriangle(leftTopPoint, rightTopPoint, posBottom.Y, dLeftPoint, dRightPoint, dLineInterpolant);
        }

        private void DrawFlatBottomTriangle(Span<float> topPoint, Span<float> rightBottomPoint, Span<float> leftBottomPoint)
        {
            Vector4 posTop = ReadVector4(topPoint, 0);
            Vector4 posRightBottom = ReadVector4(rightBottomPoint, 0);
            Vector4 posLeftBottom = ReadVector4(leftBottomPoint, 0);

            float dy = posRightBottom.Y - posTop.Y;
            if (Math.Abs(dy) < float.Epsilon) return;

            // Вычисляем дельты для левого и правого краев
            Span<float> dLeftPoint = stackalloc float[FloatsPerVertexCount];
            Span<float> dRightPoint = stackalloc float[FloatsPerVertexCount];

            // dLeftPoint = (leftBottomPoint - topPoint) / dy
            floatOpsProcessor.Sub(dLeftPoint, leftBottomPoint, topPoint);
            floatOpsProcessor.DivScalar(dLeftPoint, dLeftPoint, dy);

            // dRightPoint = (rightBottomPoint - topPoint) / dy
            floatOpsProcessor.Sub(dRightPoint, rightBottomPoint, topPoint);
            floatOpsProcessor.DivScalar(dRightPoint, dRightPoint, dy);

            // Интерполянт для горизонтальной линии между нижними точками
            Span<float> dLineInterpolant = stackalloc float[FloatsPerVertexCount];
            float dx = posRightBottom.X - posLeftBottom.X;
            if (Math.Abs(dx) < float.Epsilon) return;

            // dLineInterpolant = (rightBottomPoint - leftBottomPoint) / dx
            floatOpsProcessor.Sub(dLineInterpolant, rightBottomPoint, leftBottomPoint);
            floatOpsProcessor.DivScalar(dLineInterpolant, dLineInterpolant, dx);

            DrawFlatTriangle(topPoint, topPoint, posRightBottom.Y, dLeftPoint, dRightPoint, dLineInterpolant);
        }

        private void DrawFlatTriangle(Span<float> leftPoint, Span<float> rightPoint, float yMax,
            Span<float> dLeftPoint, Span<float> dRightPoint, Span<float> dLineInterpolant)
        {
            Vector4 posLeft = ReadVector4(leftPoint, 0);
            Vector4 posRight = ReadVector4(rightPoint, 0);

            int yStart = Math.Max((int)MathF.Ceiling(posLeft.Y), 0);
            int yEnd = Math.Min((int)MathF.Ceiling(yMax), screenHeight);

            if (yStart >= yEnd) return;

            // Пред-шаг по Y
            float yPrestep = yStart - posLeft.Y;

            // Создаем копии для текущих левой и правой точек
            Span<float> currentLeft = stackalloc float[FloatsPerVertexCount];
            Span<float> currentRight = stackalloc float[FloatsPerVertexCount];

            leftPoint.CopyTo(currentLeft);
            rightPoint.CopyTo(currentRight);

            // Применяем пред-шаг
            Span<float> tempLeft = stackalloc float[FloatsPerVertexCount];
            Span<float> tempRight = stackalloc float[FloatsPerVertexCount];

            floatOpsProcessor.MulScalar(tempLeft, dLeftPoint, yPrestep);
            floatOpsProcessor.Add(currentLeft, currentLeft, tempLeft);

            floatOpsProcessor.MulScalar(tempRight, dRightPoint, yPrestep);
            floatOpsProcessor.Add(currentRight, currentRight, tempRight);

            // Буферы для интерполянта линии, скорректированной вершины
            Span<float> lineInterpolant = stackalloc float[FloatsPerVertexCount];
            Span<float> tempLine = stackalloc float[FloatsPerVertexCount];
            Span<float> correctedPoint = stackalloc float[FloatsPerVertexCount];
            for (int y = yStart; y < yEnd; y++)
            {
                Vector4 posCurLeft = ReadVector4(currentLeft, 0);
                Vector4 posCurRight = ReadVector4(currentRight, 0);

                int xStart = Math.Max((int)MathF.Ceiling(posCurLeft.X), 0);
                int xEnd = Math.Min((int)MathF.Ceiling(posCurRight.X), screenWidth);

                if (xStart < xEnd)
                {
                    float xPrestep = xStart - posCurLeft.X;

                    // Копируем currentLeft в lineInterpolant и применяем пред-шаг по X
                    currentLeft.CopyTo(lineInterpolant);

                    
                    floatOpsProcessor.MulScalar(tempLine, dLineInterpolant, xPrestep);
                    floatOpsProcessor.Add(lineInterpolant, lineInterpolant, tempLine);

                    for (int x = xStart; x < xEnd; x++)
                    {
                        Vector4 posLine = ReadVector4(lineInterpolant, 0);

                        if (zBuffer!.Test(x, y, posLine.Z))
                        {
                            // Применяем коррекцию перспективы (умножаем на W)
                            
                            float invW = 1 / posLine.W;
                            floatOpsProcessor.MulScalar(correctedPoint, lineInterpolant, invW);

                            // Вызываем пиксельный шейдер
                            Vector4 color = shaderProcessor.PixelShader(correctedPoint);

                            if (currentPrimitive!.MaterialV2?.AlphaMode == GltfMaterialAlphaMode.BLEND)
                            {
                                if (color.W <= 0)
                                {
                                    // Переходим к следующему пикселю
                                    floatOpsProcessor.Add(lineInterpolant, lineInterpolant, dLineInterpolant);
                                    continue;
                                }

                                Vector4 prevColor = zBuffer[x, y].color;
                                Vector4 finalColor = Vector4.Lerp(prevColor, color, color.W);
                                zBuffer.TestAndSet(x, y, posLine.Z, finalColor);
                            }
                            else
                            {
                                zBuffer.TestAndSet(x, y, posLine.Z, color);
                            }
                        }

                        // Переходим к следующему пикселю по X
                        floatOpsProcessor.Add(lineInterpolant, lineInterpolant, dLineInterpolant);
                    }
                }

                // Переходим к следующей строке по Y
                floatOpsProcessor.Add(currentLeft, currentLeft, dLeftPoint);
                floatOpsProcessor.Add(currentRight, currentRight, dRightPoint);
            }
        }

        // Вспомогательные методы для чтения/записи векторов
        private static Vector4 ReadVector4(Span<float> span, int offset) =>
            new Vector4(span[offset], span[offset + 1], span[offset + 2], span[offset + 3]);

        private static Vector3 ReadVector3(Span<float> span, int offset) =>
            new Vector3(span[offset], span[offset + 1], span[offset + 2]);

        private static void WriteVector4(Span<float> span, int offset, Vector4 value)
        {
            span[offset] = value.X;
            span[offset + 1] = value.Y;
            span[offset + 2] = value.Z;
            span[offset + 3] = value.W;
        }

        private static void Swap(ref Span<float> a, ref Span<float> b)
        {
            Span<float> temp = a;
            a = b; b = temp;
        }

        private static void Swap(ref Vector4 a, ref Vector4 b) => (a, b) = (b, a);
    }
}
