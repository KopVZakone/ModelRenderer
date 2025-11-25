using System.Numerics;

namespace GraphicsLib.Types2
{
    public record struct BoundingBox(Vector3 Min, Vector3 Max)
    {
        public readonly Vector3 Center => (Min + Max) / 2;
        public static BoundingBox TransformBoundingBox(in BoundingBox localBox, in Matrix4x4 worldMatrix)
        {
            // Get all 8 corners in model space
            Span<Vector3> corners =
            [
                new Vector3(localBox.Min.X, localBox.Min.Y, localBox.Min.Z),
                    new Vector3(localBox.Min.X, localBox.Min.Y, localBox.Max.Z),
                    new Vector3(localBox.Min.X, localBox.Max.Y, localBox.Min.Z),
                    new Vector3(localBox.Min.X, localBox.Max.Y, localBox.Max.Z),
                    new Vector3(localBox.Max.X, localBox.Min.Y, localBox.Min.Z),
                    new Vector3(localBox.Max.X, localBox.Min.Y, localBox.Max.Z),
                    new Vector3(localBox.Max.X, localBox.Max.Y, localBox.Min.Z),
                    new Vector3(localBox.Max.X, localBox.Max.Y, localBox.Max.Z),
                ];

            // Transform all corners to world space
            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);

            for (int i = 0; i < 8; i++)
            {
                Vector3 worldCorner = Vector3.Transform(corners[i], worldMatrix);
                min = Vector3.Min(min, worldCorner);
                max = Vector3.Max(max, worldCorner);
            }

            return new(min, max);
        }
    }
}