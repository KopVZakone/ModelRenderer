using GraphicsLib.Types;
using GraphicsLib.Types.GltfTypes;
using GraphicsLib.Types3;
using System.Numerics;

namespace GraphicsLib.Types2
{
    public class ModelPrimitive
    {
        public required Dictionary<string, short> AttributesOffsets { get; set; }
        public int[]? Indices { get; set; }
        public float[][] FloatData { get; set; } = [];
        public ushort[][] Joints { get; set; } = [];
        public int VertexCount { get; set; }
        public Material Material { get; set; } = Material.defaultMaterial;
        public MaterialV2 MaterialV2 { get; set; } = MaterialV2.defaultMaterial;
        public GltfMeshMode Mode { get; set; } = GltfMeshMode.TRIANGLES;
        public BoundingBox? BoundingBox { get; set; }
    }
}