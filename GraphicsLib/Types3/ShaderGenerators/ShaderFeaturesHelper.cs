using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public class ShaderFeaturesHelper
    {
        public static int GetFloatOpsLength(ShaderFeatures features, HashSet<int> usedUvs)
        {
            int result = 0;
            result += 4; // Position
            result += 3; // Normal
            result += 3; // World Position
            if (features.HasFlag(ShaderFeatures.NormalMap))
            {
                result += 4; // Tangent
            }
            foreach (int index in usedUvs)
            {
                result += 2; // UV
            }
            return result;
        }
        public static int GetIntOpsLengthForShadow(ShaderFeatures features)
        {
            int result = 0;
            result += 4; // Position
            if (features.HasFlag(ShaderFeatures.BlendModeNonOpaque) && features.HasFlag(ShaderFeatures.BaseColorTexture))
            {
                result += 2; // Texture UV to check for transparency
            }
            return result;
        }
    }
}