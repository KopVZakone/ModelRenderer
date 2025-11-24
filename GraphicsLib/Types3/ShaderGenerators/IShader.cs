using GraphicsLib.Types2;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public interface IShader
    {
        public void BindScene(in ModelScene scene);
        public void UnbindScene();
        public void BindPrimitive(ref ModelPrimitive primitive, in Matrix4x4 transformation);
        public void UnbindPrimitive();
        public void BindSkin(in ModelSkin skin);
        public void UnbindSkin();

        public Vector4 PixelShader(ReadOnlySpan<float> input);
        public void VertexShader(int vertexDataIndex, Span<float> output);
    }
}
