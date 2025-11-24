using GraphicsLib.Types;
using GraphicsLib.Types.GltfTypes;
using GraphicsLib.Types3.ShaderGenerators;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace GraphicsLib.Types3
{
    public class MaterialV2
    {
        public string Name { get; set; } = string.Empty;
        public GltfMaterialAlphaMode AlphaMode { get; set; } = GltfMaterialAlphaMode.OPAQUE;
        public Vector4 BaseColor { get; set; } = new(1, 1, 1, 1);
        public Vector3 EmissiveFactor { get; set; } = new(0, 0, 0);
        public float Metallic { get; set; } = 1f;
        public float Roughness { get; set; } = 1f;
        public TextureInfo? BaseColorTexture { get; set; }
        public NormalTextureInfo? NormalTexture { get; set; }
        public TextureInfo? MetallicRoughnessTexture { get; set; }
        public OcclusionTextureInfo? OcclusionTexture { get; set; }
        public TextureInfo? EmissiveTexture { get; set; }

        public static readonly MaterialV2 defaultMaterial = new();
        public MaterialV2() { }
        public static MaterialV2 FromGltfMaterial(GltfMaterial material)
        {
            MaterialV2 newMaterial = new();
            if (material.Name != null)
                newMaterial.Name = material.Name;
            newMaterial.AlphaMode = material.AlphaMode;
            if (material.PbrMetallicRoughness != null)
            {
                var pbr = material.PbrMetallicRoughness;
                newMaterial.Metallic = pbr.MetallicFactor;
                newMaterial.Roughness = pbr.RoughnessFactor;
                newMaterial.BaseColor = pbr.BaseColorFactor;
                //add samplers if they are present

                if (pbr.BaseColorTexture != null)
                {
                    newMaterial.BaseColorTexture = new TextureInfo
                    {
                        Sampler = pbr.BaseColorTexture.GetConvertedSampler(),
                        TexCoordIndex = pbr.BaseColorTexture.TexCoord
                    };
                }
                if (pbr.MetallicRoughnessTexture != null)
                {
                    newMaterial.MetallicRoughnessTexture = new TextureInfo
                    {
                        Sampler = pbr.MetallicRoughnessTexture.GetConvertedSampler(),
                        TexCoordIndex = pbr.MetallicRoughnessTexture.TexCoord
                    };
                }
            }
            //add samplers if they are present
            if (material.NormalTexture != null)
            {
                newMaterial.NormalTexture = new NormalTextureInfo
                {
                    Sampler = material.NormalTexture.GetConvertedSampler(),
                    TexCoordIndex = material.NormalTexture.TexCoord,
                    Scale = material.NormalTexture.Scale
                };
            }
            if (material.OcclusionTexture != null)
            {
                newMaterial.OcclusionTexture = new OcclusionTextureInfo()
                { Sampler = material.OcclusionTexture.GetConvertedSampler(),
                    TexCoordIndex = material.OcclusionTexture.TexCoord,
                    Strength = material.OcclusionTexture.Strength
                };
            }
            newMaterial.EmissiveFactor = material.EmissiveFactor;
            if (material.EmissiveTexture != null)
            {
                newMaterial.EmissiveTexture = new TextureInfo
                {
                    Sampler = material.EmissiveTexture.GetConvertedSampler(),
                    TexCoordIndex = material.EmissiveTexture.TexCoord
                };
            }
            return newMaterial;
        }
        private ShaderFeatures GetProvidedShaderFeatures(ShaderFeatures desiredFeatures)
        {
            ShaderFeatures available = new ShaderFeatures();
            if (AlphaMode != GltfMaterialAlphaMode.OPAQUE)
                available |= ShaderFeatures.BlendModeNonOpaque;
            if (BaseColorTexture != null)
                available |= ShaderFeatures.BaseColorTexture;
            if (NormalTexture != null)
                available |= ShaderFeatures.NormalMap;
            if (MetallicRoughnessTexture != null)
                available |= ShaderFeatures.MetallicRoughnessTexture;
            if (OcclusionTexture != null)
                available |= ShaderFeatures.OcclusionTexture;
            if (EmissiveTexture != null)
                available |= ShaderFeatures.EmissiveTexture;
            return available & desiredFeatures;
        }

        public (ShaderFeatures providedFeatures, TextureBindings textureBindings) GetShaderFeatures(ShaderFeatures desiredFeatures)
        {
            var providedFeatures = GetProvidedShaderFeatures(desiredFeatures);
            var textureBindings = TextureBindings.FromMaterial(this, providedFeatures);

            return (providedFeatures, textureBindings);
        }
    }
    public class TextureInfo
    {
        public required Sampler Sampler { get; set; }
        public int TexCoordIndex { get; set; }
    }
    public class NormalTextureInfo
    {
        public required Sampler Sampler { get; set; }
        public int TexCoordIndex { get; set; }
        public float Scale { get; set; } = 1;
    }
    public class OcclusionTextureInfo
    {
        public required Sampler Sampler { get; set; }
        public int TexCoordIndex { get; set; }
        public float Strength { get; set; } = 1;
    }
}
