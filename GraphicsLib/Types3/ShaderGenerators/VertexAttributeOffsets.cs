using System;
using System.Collections.Generic;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public readonly struct VertexAttributeOffsets : IEquatable<VertexAttributeOffsets>
    {
        public int Position { get; }
        public int WorldPosition { get; }
        public int Normal { get; }
        public int? Tangent { get; }
        public IReadOnlyDictionary<int, int> UVOffsets { get; }

        public VertexAttributeOffsets(int position, int worldPosition, int normal, int? tangent, Dictionary<int, int> uvOffsets)
        {
            Position = position;
            WorldPosition = worldPosition;
            Normal = normal;
            Tangent = tangent;
            UVOffsets = uvOffsets;
        }

        public bool HasUV(int uvIndex) => UVOffsets.ContainsKey(uvIndex);
        public int GetUVOffset(int uvIndex) => UVOffsets[uvIndex];

        public bool Equals(VertexAttributeOffsets other)
        {
            if (Position != other.Position) return false;
            if (WorldPosition != other.WorldPosition) return false;
            if (Normal != other.Normal) return false;
            if (Tangent != other.Tangent) return false;
            if (UVOffsets.Count != other.UVOffsets.Count) return false;

            foreach (var kvp in UVOffsets)
            {
                if (!other.UVOffsets.TryGetValue(kvp.Key, out int otherValue) || kvp.Value != otherValue)
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is VertexAttributeOffsets other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Position;
                hash = (hash * 397) ^ WorldPosition;
                hash = (hash * 397) ^ Normal;
                hash = (hash * 397) ^ (Tangent?.GetHashCode() ?? 0);

                foreach (var kvp in UVOffsets.OrderBy(x => x.Key))
                {
                    hash = (hash * 397) ^ kvp.Key;
                    hash = (hash * 397) ^ kvp.Value;
                }

                return hash;
            }
        }

        public static bool operator ==(VertexAttributeOffsets left, VertexAttributeOffsets right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VertexAttributeOffsets left, VertexAttributeOffsets right)
        {
            return !(left == right);
        }
    }
}
