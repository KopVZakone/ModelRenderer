using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GraphicsLib.Types2.Shaders
{
    [Flags]
    public enum ShaderFeatures
    {
        BaseColorTexture = 1 << 0,
        NormalMap = 1 << 1,
        MetallicRoughnessTexture = 1 << 2,
        EmissiveTexture = 1 << 3,
        OcclusionTexture = 1 << 4,
        BlendModeAlpha = 1 << 4,
    }
}
