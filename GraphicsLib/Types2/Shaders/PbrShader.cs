using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading.Tasks;
using static GraphicsLib.Types2.Shaders.PbrShader;
using GraphicsLib.Types;
using System.Windows.Media.Media3D;
using Material = GraphicsLib.Types.Material;
using System.Drawing;

namespace GraphicsLib.Types2.Shaders
{
    public unsafe class PbrShader : IModelShader<PbrVertex>
    {
        static Material? currentMaterial = null;
        static ModelSkin? currentSkin = null;
        static LightSource[]? lightSources = null;
        static Matrix4x4 worldTransformation = Matrix4x4.Identity;
        static Matrix4x4 normalTransformation = Matrix4x4.Identity;
        static Vector3 cameraPosition;
        static Vector3 ambientLightColor;
        static float ambientLightIntensity;

        static Vector3* positionsArray = null;
        static Vector3* normalsArray = null;
        static Vector2* uvsArray = null;
        static Vector4* tangentsArray = null;
        static Vector2* normalUvsArray = null;
        static Vector2* roughnessMetallicUvsArray = null;
        static Vector2* emissiveUvsArray = null;
        static ushort* jointsArray = null;
        static float* weightsArray = null;

        public static void BindScene(in ModelScene scene)
        {
            cameraPosition = scene.Camera!.Position;
            ambientLightColor = new Vector3(1f);
            ambientLightIntensity = 0.5f;
            lightSources = scene.LightSources;
        }
        public static void UnbindScene()
        {
            lightSources = null;
            ambientLightColor = default;
            ambientLightColor = default;
            cameraPosition = default;
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
            Matrix4x4.Invert(transformation, out Matrix4x4 inverse);
            normalTransformation = Matrix4x4.Transpose(inverse);
            BindAttributes(primitive);
        }
        public static void UnbindPrimitive()
        {
            UnbindAttributes();
            normalTransformation = default;
            worldTransformation = default;
            currentMaterial = null;      
        }
        private static void BindAttributes(in ModelPrimitive primitive)
        {
            unsafe
            {
                positionsArray = ModelShaderUtils.GetAttributePointer<Vector3>(primitive, "POSITION");
                normalsArray = ModelShaderUtils.GetAttributePointer<Vector3>(primitive, "NORMAL");
                uvsArray = ModelShaderUtils.GetAttributePointer<Vector2>(primitive, $"TEXCOORD_{currentMaterial!.BaseColorCoordsIndex}");
                tangentsArray = ModelShaderUtils.GetAttributePointer<Vector4>(primitive, "TANGENT");
                normalUvsArray = ModelShaderUtils.GetAttributePointer<Vector2>(primitive, $"TEXCOORD_{currentMaterial!.NormalCoordsIndex}");
                roughnessMetallicUvsArray = ModelShaderUtils.GetAttributePointer<Vector2>(primitive, $"TEXCOORD_{currentMaterial!.MetallicRoughnessCoordsIndex}");
                jointsArray = ModelShaderUtils.GetJointsPointer(primitive);
                weightsArray = ModelShaderUtils.GetAttributePointer<float>(primitive, "WEIGHTS_0");
                emissiveUvsArray = ModelShaderUtils.GetAttributePointer<Vector2>(primitive, $"TEXCOORD_{currentMaterial!.EmissiveCoordsIndex}");
            }
        }
        private static void UnbindAttributes()
        {
            positionsArray = null;
            normalsArray = null;
            uvsArray = null;
            tangentsArray = null;
            normalUvsArray = null;
            roughnessMetallicUvsArray = null;
            jointsArray = null;
            weightsArray = null;
            emissiveUvsArray = null;
        }       

        public static Vector4 PixelShader(in PbrVertex input)
        {
            Vector4 diffuseColor = currentMaterial!.BaseColor;
            if (currentMaterial!.BaseColorTextureSampler != null)
            {
                diffuseColor *= currentMaterial!.BaseColorTextureSampler.Sample(input.Uv);
            }
            //transparent pixel
            if(diffuseColor.W < 0.0001f)
            {
                return new Vector4(0);
            }
            Vector3 normal = Vector3.Normalize(input.Normal);
            if (currentMaterial.NormalTextureSampler != null)
            {
                float sign = input.Tangent.W;
                Vector3 tangent = input.Tangent.AsVector3();
                Vector3 tangentSpaceNormal = currentMaterial.NormalTextureSampler.Sample(input.NormalUv).AsVector3();
                //decode
                tangentSpaceNormal = tangentSpaceNormal * 2 - new Vector3(1, 1, 1);
                Vector3 bitangent = sign * Vector3.Cross(normal, tangent);
                normal = Vector3.Normalize(tangent * tangentSpaceNormal.X + bitangent * tangentSpaceNormal.Y + normal * tangentSpaceNormal.Z);
            }
            
            //calculate roughness and metallic
            float roughness = currentMaterial.Roughness;
            float metallic = currentMaterial.Metallic;
            if (currentMaterial.MetallicRoughnessTextureSampler != null)
            {
                Vector4 metallicRoughness = currentMaterial.MetallicRoughnessTextureSampler.Sample(input.RoughnessMetallicUv);
                roughness *= metallicRoughness.Y;
                metallic *= metallicRoughness.Z;
            }
            Vector3 baseReflectivity = Vector3.Lerp(new Vector3(1.0f), diffuseColor.AsVector3(), metallic);
            Vector3 viewDir = Vector3.Normalize(cameraPosition - input.WorldPosition);
            float nDotV = Math.Max(Vector3.Dot(normal, viewDir), 0);
            float oneMinusNDotV = 1 - nDotV;
            Vector3 fresnel = baseReflectivity + (new Vector3(1) - baseReflectivity) * (oneMinusNDotV * oneMinusNDotV * oneMinusNDotV * oneMinusNDotV * oneMinusNDotV);
            Vector3 kSpecular = fresnel;
            Vector3 kDiffuse = new Vector3(1);
            float alpha = roughness;
            float alphaSqr = alpha * alpha;
            float k = (alpha + 1) * (alpha + 1) * 0.125f;
            float gv = MathF.ReciprocalEstimate(Math.Max(nDotV * (1 - k) + k, 0.001f));
            Vector3 ambient = Vector3.Clamp(diffuseColor.AsVector3() * ambientLightColor * ambientLightIntensity
                            , Vector3.Zero
                            , new Vector3(1));
            Vector3 finalColor = ambient;
            //calculate all lighting related vectors
            if (lightSources != null)
            {
                foreach (var lightSource in lightSources)
                {
                    lightSource.CalculateLightDirAndIntensity(input.WorldPosition, out Vector3 lightDir, out float intensity);
                    float nDotL = Math.Max(Vector3.Dot(normal, -lightDir), 0);
                    if (nDotL <= 0 || intensity < 0.00001f)
                    {
                        continue;
                    }
                    Vector3 halfWayDir = Vector3.Normalize(-lightDir + viewDir);
                    float nDotH = Math.Max(Vector3.Dot(normal, halfWayDir), 0);
                    float denomPart = (alphaSqr - 1) * nDotH * nDotH + 1;
                    float normalDistribution = alphaSqr / MathF.Max(MathF.PI * denomPart * denomPart, 0.0001f);
                    float gl = MathF.ReciprocalEstimate(Math.Max(nDotL * (1 - k) + k, 0.001f));
                    float geometryShading = gl * gv;
                    Vector3 cookTorrance = kSpecular * (normalDistribution * geometryShading * 0.25f);
                    Vector3 diffuse = diffuseColor.AsVector3();
                    Vector3 bdfs = cookTorrance + diffuse * kDiffuse;
                    finalColor += bdfs * lightSource.Color * (nDotL * intensity);
                }
            }
            finalColor = Vector3.Clamp(finalColor, Vector3.Zero, new Vector3(1));
            if (currentMaterial.EmissiveTextureSampler != null)
            {
                finalColor += currentMaterial.EmissiveTextureSampler.Sample(input.EmissiveUv).AsVector3() * currentMaterial.EmissiveFactor;
            }
            return new Vector4(finalColor, diffuseColor.W);
        }
        public static PbrVertex VertexShader(in ModelPrimitive primitive, in int vertexDataIndex)
        {
            Vector4 initialPosition = new Vector4(positionsArray[vertexDataIndex], 1);
            Vector3 initialNormal = normalsArray==null? Vector3.Zero : normalsArray[vertexDataIndex];
            Vector4 initialTangent = tangentsArray==null? Vector4.Zero : tangentsArray[vertexDataIndex];
            float tangentDir = initialTangent.W;
            if (currentSkin != null)
            {
                initialPosition = weightsArray[vertexDataIndex * 4] * Vector4.Transform(initialPosition, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4])) +
                    weightsArray[vertexDataIndex * 4 + 1] * Vector4.Transform(initialPosition, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 1])) +
                    weightsArray[vertexDataIndex * 4 + 2] * Vector4.Transform(initialPosition, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 2])) +
                    weightsArray[vertexDataIndex * 4 + 3] * Vector4.Transform(initialPosition, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 3]));
                initialNormal = Vector3.Normalize(Vector3.TransformNormal(initialNormal, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4])) * weightsArray[vertexDataIndex * 4] +
                    Vector3.TransformNormal(initialNormal, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 1])) * weightsArray[vertexDataIndex * 4 + 1] +
                    Vector3.TransformNormal(initialNormal, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 2])) * weightsArray[vertexDataIndex * 4 + 2] +
                    Vector3.TransformNormal(initialNormal, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 3])) * weightsArray[vertexDataIndex * 4 + 3]);
                Vector3 tangent = new Vector3(initialTangent.X, initialTangent.Y, initialTangent.Z);
                tangent = Vector3.Normalize(Vector3.TransformNormal(tangent, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4])) * weightsArray[vertexDataIndex * 4] +
                    Vector3.TransformNormal(tangent, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 1])) * weightsArray[vertexDataIndex * 4 + 1] +
                    Vector3.TransformNormal(tangent, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 2])) * weightsArray[vertexDataIndex * 4 + 2] +
                    Vector3.TransformNormal(tangent, GetInversedBoneTransform(jointsArray[vertexDataIndex * 4 + 3])) * weightsArray[vertexDataIndex * 4 + 3]);
                initialTangent = new Vector4(tangent.X, tangent.Y, tangent.Z, tangentDir);
            }
            Vector4 worldPosition = Vector4.Transform(initialPosition, worldTransformation);
            Vector3 worldNormal = Vector3.Transform(initialNormal, normalTransformation);
            Vector4 worldTangent = new Vector4(Vector3.TransformNormal(initialTangent.AsVector3(), worldTransformation), tangentDir);
            return new PbrVertex()
            {
                Position = worldPosition,
                Normal = worldNormal,
                WorldPosition = worldPosition.AsVector3(),
                Uv = uvsArray==null? Vector2.Zero : uvsArray[vertexDataIndex],
                Tangent = worldTangent,
                NormalUv = normalUvsArray==null? Vector2.Zero : normalUvsArray[vertexDataIndex],
                RoughnessMetallicUv = roughnessMetallicUvsArray==null? Vector2.Zero : roughnessMetallicUvsArray[vertexDataIndex],
                EmissiveUv = emissiveUvsArray==null? Vector2.Zero : emissiveUvsArray[vertexDataIndex]
            };
        }
        private static Matrix4x4 GetInversedBoneTransform(in int jointIndex)
        {
            return currentSkin!.CurrentFrameJointMatrices?[jointIndex] ?? Matrix4x4.Identity;
        }
        public struct PbrVertex : IModelVertex<PbrVertex>
        {
            public Vector4 Position { readonly get => position; set => position = value; }
            public Vector4 Tangent { readonly get => tangent; set => tangent = value; }
            public Vector3 Normal { readonly get => normal; set => normal = value; }
            public Vector3 WorldPosition { readonly get => worldPosition; set => worldPosition = value; }
            public Vector2 Uv { readonly get => uv; set => uv = value; }
            public Vector2 NormalUv { readonly get => normalUv; set => normalUv = value; }
            public Vector2 RoughnessMetallicUv { readonly get => roughnessMetallicUv; set => roughnessMetallicUv = value; }
            public Vector2 EmissiveUv { readonly get => emissiveUv; set => emissiveUv = value; }

            private Vector4 position;
            private Vector4 tangent;
            private Vector3 normal;
            private Vector3 worldPosition;
            private Vector2 uv;
            private Vector2 normalUv;
            private Vector2 roughnessMetallicUv;
            private Vector2 emissiveUv;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static PbrVertex Lerp(PbrVertex a, PbrVertex b, float t)
            {
                if (Avx2.IsSupported)
                {
                    return a * (1 - t) + b * t;
                }
                else
                {
                    return new PbrVertex
                    {
                        Position = Vector4.Lerp(a.Position, b.Position, t),
                        normal = Vector3.Lerp(a.normal, b.normal, t),
                        worldPosition = Vector3.Lerp(a.worldPosition, b.worldPosition, t),
                        uv = Vector2.Lerp(a.uv, b.uv, t),
                        tangent = Vector4.Lerp(a.tangent, b.tangent, t),
                        normalUv = Vector2.Lerp(a.normalUv, b.normalUv, t),
                        roughnessMetallicUv = Vector2.Lerp(a.roughnessMetallicUv, b.roughnessMetallicUv, t),
                        emissiveUv = Vector2.Lerp(a.emissiveUv, b.emissiveUv, t),
                    };
                }

            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static PbrVertex operator +(PbrVertex lhs, PbrVertex rhs)
            {
                if (Avx2.IsSupported)
                {
                    unsafe
                    {
                        PbrVertex vertex = default;
                        Avx.Store((float*)&vertex.position, Avx.Add(Avx.LoadVector256((float*)&lhs.position), Avx.LoadVector256((float*)&rhs.position)));
                        Avx.Store((float*)&vertex.normal, Avx.Add(Avx.LoadVector256((float*)&lhs.normal), Avx.LoadVector256((float*)&rhs.normal)));
                        Sse.Store((float*)&vertex.normalUv, Sse.Add(Sse.LoadVector128((float*)&lhs.normalUv), Sse.LoadVector128((float*)&rhs.normalUv)));
                        vertex.EmissiveUv = lhs.EmissiveUv + rhs.EmissiveUv;
                        return vertex;
                    }
                }
                else
                {
                    return new PbrVertex
                    {
                        Position = lhs.Position + rhs.Position,
                        normal = lhs.normal + rhs.normal,
                        worldPosition = lhs.worldPosition + rhs.worldPosition,
                        uv = lhs.uv + rhs.uv,
                        tangent = lhs.tangent + rhs.tangent,
                        normalUv = lhs.normalUv + rhs.normalUv,
                        roughnessMetallicUv = lhs.roughnessMetallicUv + rhs.roughnessMetallicUv,
                        emissiveUv = lhs.emissiveUv + rhs.emissiveUv,
                    };

                }

            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static PbrVertex operator -(PbrVertex lhs, PbrVertex rhs)
            {
                if (Avx2.IsSupported)
                {
                    unsafe
                    {
                        PbrVertex vertex = default;
                        Avx.Store((float*)&vertex.position, Avx.Subtract(Avx.LoadVector256((float*)&lhs.position), Avx.LoadVector256((float*)&rhs.position)));
                        Avx.Store((float*)&vertex.normal, Avx.Subtract(Avx.LoadVector256((float*)&lhs.normal), Avx.LoadVector256((float*)&rhs.normal)));
                        Sse.Store((float*)&vertex.normalUv, Sse.Subtract(Sse.LoadVector128((float*)&lhs.normalUv), Sse.LoadVector128((float*)&rhs.normalUv)));
                        vertex.emissiveUv = lhs.emissiveUv - rhs.emissiveUv;
                        return vertex;
                    }
                }
                else
                {
                    return new PbrVertex
                    {
                        Position = lhs.Position - rhs.Position,
                        normal = lhs.normal - rhs.normal,
                        worldPosition = lhs.worldPosition - rhs.worldPosition,
                        uv = lhs.uv - rhs.uv,
                        tangent = lhs.tangent - rhs.tangent,
                        normalUv = lhs.normalUv - rhs.normalUv,
                        roughnessMetallicUv = lhs.roughnessMetallicUv - rhs.roughnessMetallicUv,
                        emissiveUv = lhs.emissiveUv - rhs.emissiveUv,
                    };
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static PbrVertex operator *(PbrVertex lhs, float scalar)
            {
                if (Avx2.IsSupported)
                {
                    unsafe
                    {
                        PbrVertex vertex = default;
                        Vector256<float> multiplier = Avx.BroadcastScalarToVector256(&scalar);
                        Avx.Store((float*)&vertex.position, Avx.Multiply(Avx.LoadVector256((float*)&lhs.position), multiplier));
                        Avx.Store((float*)&vertex.normal, Avx.Multiply(Avx.LoadVector256((float*)&lhs.normal), multiplier));
                        Sse.Store((float*)&vertex.normalUv, Sse.Multiply(Sse.LoadVector128((float*)&lhs.normalUv), multiplier.GetLower()));
                        vertex.emissiveUv = lhs.emissiveUv * scalar;
                        return vertex;
                    }
                }
                else
                {
                    return new PbrVertex
                    {
                        Position = lhs.Position * scalar,
                        normal = lhs.normal * scalar,
                        worldPosition = lhs.worldPosition * scalar,
                        uv = lhs.uv * scalar,
                        tangent = lhs.tangent * scalar,
                        normalUv = lhs.normalUv * scalar,
                        roughnessMetallicUv = lhs.roughnessMetallicUv * scalar,
                        emissiveUv = lhs.emissiveUv * scalar,
                    };
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static PbrVertex operator *(float scalar, PbrVertex rhs)
            {
                return rhs * scalar;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static PbrVertex operator /(PbrVertex lhs, float scalar)
            {
                return lhs * (1 / scalar);
            }
        }
    }
    
}
