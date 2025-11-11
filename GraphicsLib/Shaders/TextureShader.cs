using GraphicsLib.Primitives;
using GraphicsLib.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static GraphicsLib.Shaders.TextureShader;

namespace GraphicsLib.Shaders
{
    public class TextureShader : IShader<Vertex>
    {
        private Scene scene;

        public Scene Scene { get => scene; set => SetSceneParams(value); }
        private Matrix4x4 worldTransform;
        private void SetSceneParams(Scene value)
        {
            scene = value;
            worldTransform = scene.Obj.transformation.Matrix;
        }

        public TextureShader()
        {
        }
        public TextureShader(Scene scene)
        {
            Scene = scene;
        }
        public Vertex GetVertexWithWorldPositionFromFace(Obj obj, int faceIndex, int vertexIndex)
        {
            Face face = obj.faces[faceIndex];
            Vertex vertex = default;
            vertex.Position = Vector4.Transform(new Vector4(obj.vertices[face.vIndices[vertexIndex]], 1), worldTransform);
            if (face.tIndices == null)
                throw new ArgumentException("Face has no uv indices, BRUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUH");
            vertex.Uv = obj.uvs[face.tIndices[vertexIndex]];
            vertex.Material = obj.materials[face.MaterialIndex];
            return vertex;
        }

        public Vertex GetVertexWithWorldPositionFromTriangle(Obj obj, int triangleIndex, int vertexIndex)
        {
            StaticTriangle triangle = obj.triangles[triangleIndex];
            Vertex vertex = default;
            switch (vertexIndex)
            {
                case 0:
                    vertex.Position = Vector4.Transform(new Vector4(triangle.position0, 1), worldTransform);
                    vertex.Uv = triangle.uvCoordinate0;
                    break;
                case 1:
                    vertex.Position = Vector4.Transform(new Vector4(triangle.position1, 1), worldTransform);
                    vertex.Uv = triangle.uvCoordinate1;
                    break;
                case 2:
                    vertex.Position = Vector4.Transform(new Vector4(triangle.position2, 1), worldTransform);
                    vertex.Uv = triangle.uvCoordinate2;
                    break;
                default:
                    throw new ArgumentException("Invalid vertex index");
            }
            vertex.Material = triangle.material;
            return vertex;
        }

        public uint PixelShader(in Vertex input)
        {
            Vector2 uv = input.Uv;
            Material material = input.Material;
            Vector4 finalARGBColor = material.BaseColor;
            if (material.BaseColorTextureSampler != null)
            {
                Vector4 textureColor = material.BaseColorTextureSampler.Sample(uv);               
                finalARGBColor *= textureColor;
            }
            uint color = (uint)(finalARGBColor.W * 0xFF) << 24
             | (uint)(finalARGBColor.X * 0xFF) << 16
             | (uint)(finalARGBColor.Y * 0xFF) << 8
             | (uint)(finalARGBColor.Z * 0xFF);
            return color;
        }

        public struct Vertex : IVertex<Vertex>
        {
            public Vector4 Position { get; set; }

            public Vector2 Uv { get; set; }
            public Material Material { get; set; }
            public static Vertex Lerp(Vertex a, Vertex b, float t)
            {
                return new Vertex
                {
                    Position = Vector4.Lerp(a.Position, b.Position, t),
                    Uv = Vector2.Lerp(a.Uv, b.Uv, t),
                    Material = a.Material
                };
            }
            public static Vertex operator +(Vertex lhs, Vertex rhs)
            {
                return new Vertex
                {
                    Position = lhs.Position + rhs.Position,
                    Uv = lhs.Uv + rhs.Uv,
                    Material = lhs.Material
                };
            }
            public static Vertex operator -(Vertex lhs, Vertex rhs)
            {
                return new Vertex
                {
                    Position = lhs.Position - rhs.Position,
                    Uv = lhs.Uv - rhs.Uv,
                    Material = lhs.Material
                };
            }
            public static Vertex operator *(Vertex lhs, float scalar)
            {
                return new Vertex
                {
                    Position = lhs.Position * scalar,
                    Uv = lhs.Uv * scalar,
                    Material = lhs.Material
                };
            }
            public static Vertex operator *(float scalar, Vertex rhs)
            {
                return new Vertex
                {
                    Position = rhs.Position * scalar,
                    Uv = rhs.Uv * scalar,
                    Material = rhs.Material
                };
            }
            public static Vertex operator /(Vertex lhs, float scalar)
            {
                return new Vertex
                {
                    Position = lhs.Position / scalar,
                    Uv = lhs.Uv / scalar,
                    Material = lhs.Material
                };
            }
        }
    }
}
