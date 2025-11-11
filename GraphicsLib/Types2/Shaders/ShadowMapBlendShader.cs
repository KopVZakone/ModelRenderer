using GraphicsLib.Types;
using System.Numerics;
using System.Runtime.CompilerServices;
using static GraphicsLib.Types2.Shaders.ShadowMapBlendShader;

namespace GraphicsLib.Types2.Shaders
{        
    public unsafe class ShadowMapBlendShader : IModelShader<ShadowMapBlendVertex>
    {
        static Material? currentMaterial = null;
        static ModelSkin? currentSkin = null;
        static Matrix4x4 worldTransformation = Matrix4x4.Identity;
        static Vector3* positionsArray = null;
        static Vector2* uvArray = null;
        static ushort* jointsArray = null;
        static float* weightsArray = null;
        public static void BindScene(in ModelScene scene)
        {
            //no action needed
        }
        public static void UnbindScene()
        {
            // no action needed
        }
        public static void BindSkin(in ModelSkin skin)
        {
            currentSkin = skin;
        }
        public static void UnbindSkin()
        {
            currentSkin = null;
        }
        public static void BindPrimitive(in ModelPrimitive primitive, in Matrix4x4 transformation)
        {
            currentMaterial = primitive.Material;
            worldTransformation = transformation;
            BindAttributes(primitive);
        }
        public static void UnbindPrimitive()
        {
            UnbindAttributes();
            currentMaterial = null;
        }
        private static void BindAttributes(in ModelPrimitive primitive)
        {
            unsafe
            {
                positionsArray = ModelShaderUtils.GetAttributePointer<Vector3>(primitive, "POSITION");
                uvArray = ModelShaderUtils.GetAttributePointer<Vector2>(primitive, $"TEXCOORD_{currentMaterial!.BaseColorCoordsIndex}");
                jointsArray = ModelShaderUtils.GetJointsPointer(primitive);
                weightsArray = ModelShaderUtils.GetAttributePointer<float>(primitive, "WEIGHTS_0");
            }
        }
        private static void UnbindAttributes()
        {
            positionsArray = null;
            uvArray = null;
            jointsArray = null;
            weightsArray = null;
        }
        public static Vector4 PixelShader(in ShadowMapBlendVertex input)
        {
            Vector4 diffuseColor = currentMaterial!.BaseColor;
            if (currentMaterial!.BaseColorTextureSampler != null)
            {
                diffuseColor *= currentMaterial!.BaseColorTextureSampler.Sample(input.Uv);
            }
            if (diffuseColor.W < 0.0001f)
            {
                return new Vector4(0);
            }
            else
            {
                return new Vector4(1);
            }
        }

        public static ShadowMapBlendVertex VertexShader(in ModelPrimitive primitive, in int vertexDataIndex)
        {
            Vector4 initialPosition = new Vector4(positionsArray[vertexDataIndex], 1);
            if (currentSkin != null)
            {
                initialPosition = weightsArray[vertexDataIndex * 4] * Vector4.Transform(initialPosition, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4])) +
                    weightsArray[vertexDataIndex * 4 + 1] * Vector4.Transform(initialPosition, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 1])) +
                    weightsArray[vertexDataIndex * 4 + 2] * Vector4.Transform(initialPosition, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 2])) +
                    weightsArray[vertexDataIndex * 4 + 3] * Vector4.Transform(initialPosition, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 3]));
            }
            Vector4 worldPosition = Vector4.Transform(initialPosition, worldTransformation);
            return new ShadowMapBlendVertex()
            {
                Position = worldPosition,
                Uv = uvArray == null ? Vector2.Zero : uvArray[vertexDataIndex],
            };
        }
        private static Matrix4x4 GetInversedBoneTransform(in int jointIndex)
        {
            return currentSkin!.CurrentFrameJointMatrices?[jointIndex] ?? Matrix4x4.Identity;
        }
        public struct ShadowMapBlendVertex : IModelVertex<ShadowMapBlendVertex>
        {
            public Vector4 Position { readonly get => position; set => position = value; }
            public Vector2 Uv { readonly get => uv; set => uv = value; }
            private Vector4 position;
            private Vector2 uv;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ShadowMapBlendVertex Lerp(ShadowMapBlendVertex a, ShadowMapBlendVertex b, float t)
            {
                return new ShadowMapBlendVertex
                {
                    Position = Vector4.Lerp(a.Position, b.Position, t),
                    uv = Vector2.Lerp(a.uv, b.uv, t),
                };
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ShadowMapBlendVertex operator +(ShadowMapBlendVertex lhs, ShadowMapBlendVertex rhs)
            {
                return new ShadowMapBlendVertex
                {
                    Position = lhs.Position + rhs.Position,
                    uv = lhs.uv + rhs.uv,
                };
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ShadowMapBlendVertex operator -(ShadowMapBlendVertex lhs, ShadowMapBlendVertex rhs)
            {
                return new ShadowMapBlendVertex
                {
                    Position = lhs.Position - rhs.Position,
                    uv = lhs.uv - rhs.uv,
                };
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ShadowMapBlendVertex operator *(ShadowMapBlendVertex lhs, float scalar)
            {
                return new ShadowMapBlendVertex
                {
                    Position = lhs.Position * scalar,
                    uv = lhs.uv * scalar,
                };
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ShadowMapBlendVertex operator *(float scalar, ShadowMapBlendVertex rhs)
            {
                return rhs * scalar;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ShadowMapBlendVertex operator /(ShadowMapBlendVertex lhs, float scalar)
            {
                return lhs * (1 / scalar);
            }
        }
    }
}
