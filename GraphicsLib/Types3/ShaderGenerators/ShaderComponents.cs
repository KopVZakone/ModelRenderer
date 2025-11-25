using GraphicsLib.Types;
using GraphicsLib.Types2;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.Providers.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public static class ShaderComponents
    {
        public static void CalculateTextureNormal(ref Vector3 normal, in Vector4 tangent, in Vector2 normalUv, Sampler NormalTextureSampler)
        {
            Vector3 tangentSpaceNormal = NormalTextureSampler.Sample(normalUv).AsVector3();
            Vector3 tangent3 = tangent.AsVector3();
            //decode
            tangentSpaceNormal = tangentSpaceNormal * 2 - Vector3.One;
            float sign = tangent.W;
            Vector3 bitangent = sign * Vector3.Cross(normal, tangent3);
            normal = Vector3.Normalize(tangent3 * tangentSpaceNormal.X + bitangent * tangentSpaceNormal.Y + normal * tangentSpaceNormal.Z);
        }
        public static void CalculateTextureMetallicRoughness(ref float roughness, ref float metallic, in Vector2 metallicRoughnessUV, Sampler metallicRoughnessTextureSampler)
        {
            Vector4 metallicRoughness = metallicRoughnessTextureSampler.Sample(metallicRoughnessUV);
            roughness *= metallicRoughness.Y;
            metallic *= metallicRoughness.Z;
        }
        public static void CalculateTextureDiffuseColor(ref Vector4 diffuseColor, in Vector2 uv, Sampler baseColorTextureSampler)
        {
            diffuseColor *= baseColorTextureSampler.Sample(uv);
        }
        public static void CalculateAmbient(ref Vector3 color, in Vector3 diffuseColor, in Vector3 ambientLightColor, in float ambientLightIntensity)
        {
            color += diffuseColor * ambientLightColor * ambientLightIntensity;
        }
        public static void CalculateTextureEmissive(ref Vector3 emissive, in Vector2 emissiveUv, Sampler emissiveTextureSampler)
        {
            emissive *= emissiveTextureSampler.Sample(emissiveUv).AsVector3();
        }
        public static void CalculateViewDir(out Vector3 viewDir, in Vector3 cameraPosition, in Vector3 worldPosition)
        {
            viewDir = Vector3.Normalize(cameraPosition - worldPosition);
        }
        public static void GetBasePbrParameters(in MaterialV2 material, out Vector4 diffuseColorFull, out Vector3 diffuseColor, out Vector3 emissive, out float metallic, out float roughness)
        {
            diffuseColorFull = material.BaseColor;
            diffuseColor = diffuseColorFull.AsVector3();
            metallic = material.Metallic;
            roughness = material.Roughness;
            emissive = material.EmissiveFactor;
        }
        public static void CalculatePbr(ref Vector3 color, in LightSource[]? lightSources, in Vector3 diffuseColor, in Vector3 worldPosition, in Vector3 normal, in Vector3 viewDir, in float metallic, in float roughness)
        {
            if (lightSources is null || lightSources.Length == 0)
                return;
            Vector3 baseReflectivity = Vector3.Lerp(Vector3.One, diffuseColor, metallic);
            float nDotV = Math.Max(Vector3.Dot(normal, viewDir), 0);
            float oneMinusNDotV = 1 - nDotV;
            Vector3 fresnel = baseReflectivity + (Vector3.One - baseReflectivity) * (oneMinusNDotV * oneMinusNDotV * oneMinusNDotV * oneMinusNDotV * oneMinusNDotV);
            float alpha = roughness;
            float alphaSqr = alpha * alpha;
            float k = (alpha + 1) * (alpha + 1) * 0.125f;
            float gv = MathF.ReciprocalEstimate(Math.Max(nDotV * (1 - k) + k, 0.001f));
            Vector3 kSpecular = fresnel;
            Vector3 kDiffuse = Vector3.One;
            foreach (LightSource lightSource in lightSources)
            {

                lightSource.CalculateLightDirAndIntensity(worldPosition, out Vector3 lightDir, out float intensity);
                float nDotL = Math.Max(Vector3.Dot(normal, -lightDir), 0);
                if (nDotL <= 0 || intensity < 0.00001f)
                {
                    return;
                }
                Vector3 halfWayDir = Vector3.Normalize(-lightDir + viewDir);
                float nDotH = Math.Max(Vector3.Dot(normal, halfWayDir), 0);
                float denomPart = (alphaSqr - 1) * nDotH * nDotH + 1;
                float normalDistribution = alphaSqr / MathF.Max(MathF.PI * denomPart * denomPart, 0.0001f);
                float gl = MathF.ReciprocalEstimate(Math.Max(nDotL * (1 - k) + k, 0.001f));
                float geometryShading = gl * gv;
                Vector3 cookTorrance = kSpecular * (normalDistribution * geometryShading * 0.25f);
                Vector3 bdfs = cookTorrance + diffuseColor * kDiffuse;
                color += bdfs * lightSource.Color * (nDotL * intensity);
            }
        }
        public unsafe static void CalculateInverseBoneTransforms(in ushort* jointsArray, in float* weightsArray, in ModelSkin skin, in int vertexDataIndex,
            Span<Matrix4x4> boneMatrices)
        {
            for(int i = 0; i < 4; i++)
            {
                ushort jointIndex = jointsArray[vertexDataIndex * 4 + i];
                Matrix4x4 initial = skin.CurrentFrameJointMatrices?[jointIndex] ?? Matrix4x4.Identity;
                boneMatrices[i] = initial * weightsArray[vertexDataIndex + i];
            }
        }
        public static void TransformPositionByBone(ref Vector4 position, Span<Matrix4x4> boneMatrices)
        {
            Vector4 newPosition = default;
            for (int i = 0; i < 4; i++)
            {
                newPosition += Vector4.Transform(position, boneMatrices[i]);
            }
            position = newPosition;
        }
        public static void TransformNormalByBone(ref Vector3 normal, Span<Matrix4x4> boneMatrices)
        {
            Vector3 newNormal = default;
            for (int i = 0; i < 4; i++)
            {
                newNormal += Vector3.TransformNormal(normal, boneMatrices[i]);
            }
            normal = Vector3.Normalize(newNormal);
        }
        public static void TransformTangentByBone(ref Vector4 tangent, Span<Matrix4x4> boneMatrices)
        {
            Vector3 newTangent = default;
            Vector3 tangent3 = tangent.AsVector3();
            for (int i = 0; i < 4; i++)
            {
                newTangent += Vector3.TransformNormal(tangent3, boneMatrices[i]);
            }
            tangent = new Vector4(Vector3.Normalize(tangent3), tangent.W);
        }
        public static void WriteVector4(Span<float> span, int offset, Vector4 value)
        {
            ref float target = ref span[offset];

            Unsafe.WriteUnaligned(
                ref Unsafe.As<float, byte>(ref target),
                value
            );
        }
        public static void WriteVector3(Span<float> span, int offset, Vector3 value)
        {
            ref float target = ref span[offset];

            Unsafe.WriteUnaligned(
                ref Unsafe.As<float, byte>(ref target),
                value
            );
        }
        public static void WriteVector2(Span<float> span, int offset, Vector2 value)
        {
            ref float target = ref span[offset];

            Unsafe.WriteUnaligned(
                ref Unsafe.As<float, byte>(ref target),
                value
            );
        }
        public static Vector4 ReadVector4(Span<float> span, int offset)
        {
            ref float target = ref span[offset];
            return Unsafe.ReadUnaligned<Vector4>(
                ref Unsafe.As<float, byte>(ref target)
            );
        }
        public static Vector3 ReadVector3(Span<float> span, int offset)
        {
            ref float target = ref span[offset];
            return Unsafe.ReadUnaligned<Vector3>(
                ref Unsafe.As<float, byte>(ref target)
            );
        }
        public static Vector2 ReadVector2(Span<float> span, int offset)
        {
            ref float target = ref span[offset];
            return Unsafe.ReadUnaligned<Vector2>(
                ref Unsafe.As<float, byte>(ref target)
            );
        }
    }
}
