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
        BaseColor = 1 << 0,
        NormalMap = 1 << 1,
        MetallicRoughness = 1 << 2,
        Emissive = 1 << 3,
        Skinning = 1 << 4,
    }
}
