using System;
using System.Collections.Generic;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public readonly struct ShaderConfiguration : IEquatable<ShaderConfiguration>
    {
        public ShaderFeatures Features { get; }
        public int InterpolatedDataSize { get; }
        public bool HasSkin { get; }
        public VertexAttributeOffsets Offsets { get; }
        public TextureBindings TextureBindings { get; }

        public ShaderConfiguration(
            ShaderFeatures features,
            int interpolatedDataSize,
            bool hasSkin,
            VertexAttributeOffsets offsets,
            TextureBindings textureBindings)
        {
            Features = features;
            InterpolatedDataSize = interpolatedDataSize;
            HasSkin = hasSkin;
            Offsets = offsets;
            TextureBindings = textureBindings;
        }

        public bool Equals(ShaderConfiguration other)
        {
            return Features == other.Features &&
                   InterpolatedDataSize == other.InterpolatedDataSize &&
                   HasSkin == other.HasSkin &&
                   Offsets.Equals(other.Offsets) &&
                   TextureBindings.Equals(other.TextureBindings);
        }

        public override bool Equals(object? obj) => obj is ShaderConfiguration other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Features;
                hash = (hash * 397) ^ InterpolatedDataSize;
                hash = (hash * 397) ^ HasSkin.GetHashCode();
                hash = (hash * 397) ^ Offsets.GetHashCode();
                hash = (hash * 397) ^ TextureBindings.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(ShaderConfiguration left, ShaderConfiguration right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ShaderConfiguration left, ShaderConfiguration right)
        {
            return !(left == right);
        }
    }
}
