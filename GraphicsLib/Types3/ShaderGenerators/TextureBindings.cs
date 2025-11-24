using System;
using System.Collections.Generic;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public readonly struct TextureBindings : IEquatable<TextureBindings>
    {
        public int? BaseColorTexCoord { get; }
        public int? NormalTexCoord { get; }
        public int? MetallicRoughnessTexCoord { get; }
        public int? EmissiveTexCoord { get; }
        public int? OcclusionTexCoord { get; }

        public TextureBindings(
            int? baseColorTexCoord,
            int? normalTexCoord,
            int? metallicRoughnessTexCoord,
            int? emissiveTexCoord,
            int? occlusionTexCoord)
        {
            BaseColorTexCoord = baseColorTexCoord;
            NormalTexCoord = normalTexCoord;
            MetallicRoughnessTexCoord = metallicRoughnessTexCoord;
            EmissiveTexCoord = emissiveTexCoord;
            OcclusionTexCoord = occlusionTexCoord;
        }

        public static TextureBindings FromMaterial(MaterialV2 material, ShaderFeatures features)
        {
            return new TextureBindings(
                features.HasFlag(ShaderFeatures.BaseColorTexture) ? material.BaseColorTexture?.TexCoordIndex : null,
                features.HasFlag(ShaderFeatures.NormalMap) ? material.NormalTexture?.TexCoordIndex : null,
                features.HasFlag(ShaderFeatures.MetallicRoughnessTexture) ? material.MetallicRoughnessTexture?.TexCoordIndex : null,
                features.HasFlag(ShaderFeatures.EmissiveTexture) ? material.EmissiveTexture?.TexCoordIndex : null,
                features.HasFlag(ShaderFeatures.OcclusionTexture) ? material.OcclusionTexture?.TexCoordIndex : null
            );
        }

        public HashSet<int> GetUsedUVs()
        {
            var usedUVs = new HashSet<int>();
            if (BaseColorTexCoord.HasValue) usedUVs.Add(BaseColorTexCoord.Value);
            if (NormalTexCoord.HasValue) usedUVs.Add(NormalTexCoord.Value);
            if (MetallicRoughnessTexCoord.HasValue) usedUVs.Add(MetallicRoughnessTexCoord.Value);
            if (EmissiveTexCoord.HasValue) usedUVs.Add(EmissiveTexCoord.Value);
            if (OcclusionTexCoord.HasValue) usedUVs.Add(OcclusionTexCoord.Value);
            return usedUVs;
        }

        public bool Equals(TextureBindings other)
        {
            return BaseColorTexCoord == other.BaseColorTexCoord &&
                   NormalTexCoord == other.NormalTexCoord &&
                   MetallicRoughnessTexCoord == other.MetallicRoughnessTexCoord &&
                   EmissiveTexCoord == other.EmissiveTexCoord &&
                   OcclusionTexCoord == other.OcclusionTexCoord;
        }

        public override bool Equals(object? obj) => obj is TextureBindings other && Equals(other);

        public override int GetHashCode()
        {
            return HashCode.Combine(BaseColorTexCoord, NormalTexCoord, MetallicRoughnessTexCoord, EmissiveTexCoord, OcclusionTexCoord);
        }

        public static bool operator ==(TextureBindings left, TextureBindings right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TextureBindings left, TextureBindings right)
        {
            return !(left == right);
        }
    }
}
