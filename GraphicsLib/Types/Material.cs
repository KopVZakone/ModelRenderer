using GraphicsLib.Types.GltfTypes;
using GraphicsLib.Types2.Shaders;
using System.Numerics;

namespace GraphicsLib.Types
{
    public class Material
    {
        public static readonly Material defaultMaterial = new();
        public string Name { get; set; }
        public GltfMaterialAlphaMode AlphaMode { get; set; } = GltfMaterialAlphaMode.OPAQUE;
        public Vector4 BaseColor { get; set; } = new(1, 1, 1, 1);
        public Vector3 EmissiveFactor { get; set; } = new(0, 0, 0);
        public Sampler? BaseColorTextureSampler { get; set; }
        public int BaseColorCoordsIndex { get; set; }
        public Sampler? NormalTextureSampler { get; set; }
        public int NormalCoordsIndex { get; set; }
        public Sampler? MetallicRoughnessTextureSampler { get; set; }
        public int MetallicRoughnessCoordsIndex { get; set; }
        public Sampler? OcclusionTextureSampler { get; set; }
        public int OcclusionCoordsIndex { get; set; }
        public Sampler? EmissiveTextureSampler { get; set; }
        public int EmissiveCoordsIndex { get; set; }
        public float Metallic { get; set; } = 1f;
        public float Roughness { get; set; } = 1f;


        public Material()
        {
            Name = string.Empty;
        }
        public static Material FromGltfMaterial(GltfMaterial material)
        {
            Material newMaterial = new();
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

                if(pbr.BaseColorTexture != null)
                {
                    newMaterial.BaseColorTextureSampler = pbr.BaseColorTexture.GetConvertedSampler();
                    newMaterial.BaseColorCoordsIndex = pbr.BaseColorTexture.TexCoord;
                }
                if(pbr.MetallicRoughnessTexture != null)
                {
                    newMaterial.MetallicRoughnessTextureSampler = pbr.MetallicRoughnessTexture.GetConvertedSampler();
                    newMaterial.MetallicRoughnessCoordsIndex = pbr.MetallicRoughnessTexture.TexCoord;
                }
            }
            //add samplers if they are present
            if(material.NormalTexture != null)
            {
                newMaterial.NormalTextureSampler = material.NormalTexture.GetConvertedSampler();
                newMaterial.NormalCoordsIndex = material.NormalTexture.TexCoord;
            }
            if(material.OcclusionTexture != null)
            {
                newMaterial.OcclusionTextureSampler = material.OcclusionTexture.GetConvertedSampler();
                newMaterial.OcclusionCoordsIndex = material.OcclusionTexture.TexCoord;
            }
            newMaterial.EmissiveFactor = material.EmissiveFactor;
            if (material.EmissiveTexture != null)
            {
                newMaterial.EmissiveTextureSampler = material.EmissiveTexture.GetConvertedSampler();
                newMaterial.EmissiveCoordsIndex = material.EmissiveTexture.TexCoord;
            }            
            return newMaterial;
        }

        public ShaderFeatures GetShaderFeatures()
        {
            ShaderFeatures f = ShaderFeatures.BaseColor;
            if (NormalTextureSampler != null) f |= ShaderFeatures.NormalMap;
            if (MetallicRoughnessTextureSampler != null) f |= ShaderFeatures.MetallicRoughness;
            if (EmissiveTextureSampler != null) f |= ShaderFeatures.Emissive;
            return f;
        }
        public HashSet<int> GetUsedUvs()
        {
            var set = new HashSet<int>();
            if (BaseColorTextureSampler != null) set.Add(BaseColorCoordsIndex);
            if (NormalTextureSampler != null) set.Add(NormalCoordsIndex);
            if (MetallicRoughnessTextureSampler != null) set.Add(MetallicRoughnessCoordsIndex);
            if (OcclusionTextureSampler != null) set.Add(OcclusionCoordsIndex);
            if (EmissiveTextureSampler != null) set.Add(EmissiveCoordsIndex);
            return set;
        }
    }
}