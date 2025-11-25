using GraphicsLib.Types2;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public interface IPipeline
    {
        public void BindScene(in ModelScene scene);
        public void UnbindScene();
        public void BindSkin(in ModelSkin skin);
        public void UnbindSkin();
        public void BindZBuffer(ZBufferV3 buffer);
        public void UnbindZBuffer();
        void Render(ref ModelPrimitive primitive,in Matrix4x4 transformation);

    }
}
