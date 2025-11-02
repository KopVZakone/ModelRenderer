using GraphicsLib.Types.GltfTypes;
using System.Numerics;

namespace GraphicsLib.Types
{
    public class Material
    {
        public static readonly Material defaultMaterial = new();


        public string name;
        public GltfMaterialAlphaMode alphaMode = GltfMaterialAlphaMode.OPAQUE;

        //also known as albedo
        public Vector4 baseColor = new(1, 1, 1, 1);
        public Vector3 emissiveFactor = new(0, 0, 0);
        public Sampler? baseColorTextureSampler;
        public int baseColorCoordsIndex = 0;
        public Sampler? normalTextureSampler;
        public int normalCoordsIndex = 0;
        public Sampler? metallicRoughnessTextureSampler;
        public int metallicRoughnessCoordsIndex = 0;
        public Sampler? occlusionTextureSampler;
        public int occlusionCoordsIndex = 0;
        public Sampler? emissiveTextureSampler;
        public int emissiveCoordsIndex = 0;
        public float metallic = 1f;
        public float roughness = 1f;

        public Material()
        {
            name = string.Empty;
        }
        public static Material FromGltfMaterial(GltfMaterial material)
        {
            Material newMaterial = new();
            if (material.Name != null)
                newMaterial.name = material.Name;
            newMaterial.alphaMode = material.AlphaMode;
            if (material.PbrMetallicRoughness != null)
            {
                var pbr = material.PbrMetallicRoughness;
                newMaterial.metallic = pbr.MetallicFactor;
                newMaterial.roughness = pbr.RoughnessFactor;
                newMaterial.baseColor = pbr.BaseColorFactor;
                //add samplers if they are present

                if(pbr.BaseColorTexture != null)
                {
                    newMaterial.baseColorTextureSampler = pbr.BaseColorTexture.GetConvertedSampler();
                    newMaterial.baseColorCoordsIndex = pbr.BaseColorTexture.TexCoord;
                }
                if(pbr.MetallicRoughnessTexture != null)
                {
                    newMaterial.metallicRoughnessTextureSampler = pbr.MetallicRoughnessTexture.GetConvertedSampler();
                    newMaterial.metallicRoughnessCoordsIndex = pbr.MetallicRoughnessTexture.TexCoord;
                }
            }
            //add samplers if they are present
            if(material.NormalTexture != null)
            {
                newMaterial.normalTextureSampler = material.NormalTexture.GetConvertedSampler();
                newMaterial.normalCoordsIndex = material.NormalTexture.TexCoord;
            }
            if(material.OcclusionTexture != null)
            {
                newMaterial.occlusionTextureSampler = material.OcclusionTexture.GetConvertedSampler();
                newMaterial.occlusionCoordsIndex = material.OcclusionTexture.TexCoord;
            }
            newMaterial.emissiveFactor = material.EmissiveFactor;
            if (material.EmissiveTexture != null)
            {
                newMaterial.emissiveTextureSampler = material.EmissiveTexture.GetConvertedSampler();
                newMaterial.emissiveCoordsIndex = material.EmissiveTexture.TexCoord;
            }            
            return newMaterial;
        }
    }
}