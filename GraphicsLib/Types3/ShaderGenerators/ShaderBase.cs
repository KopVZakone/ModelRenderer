using GraphicsLib.Types2;
using GraphicsLib.Types2.Shaders;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public unsafe abstract class ShaderBase : IShader
    {
        protected MaterialV2? currentMaterial = null;
        protected ModelSkin? currentSkin = null;
        protected Vector3 cameraPosition;
        protected Vector3 ambientLightColor;
        protected float ambientLightIntensity;
        protected LightSource[]? lightSources = null;
        protected Matrix4x4 worldTransformation = Matrix4x4.Identity;
        protected Matrix4x4 normalTransformation = Matrix4x4.Identity;
        protected Vector3* positionsArray = null;
        public void BindPrimitive(ref ModelPrimitive primitive, in Matrix4x4 transformation)
        {
            currentMaterial = primitive.MaterialV2;
            worldTransformation = transformation;
            Matrix4x4.Invert(transformation, out Matrix4x4 inverse);
            normalTransformation = Matrix4x4.Transpose(inverse);
            BindAllAttributes(ref primitive);
        }

        private void BindAllAttributes(ref ModelPrimitive primitive)
        {
            unsafe
            {
                positionsArray = ModelShaderUtils.GetAttributePointer<Vector3>(primitive, "POSITION");
            }
            BindAttributes(ref primitive);
        }
        protected abstract void BindAttributes(ref ModelPrimitive primitive);

        public void BindScene(in ModelScene scene)
        {
            cameraPosition = scene.Camera!.Position;
            ambientLightColor = new Vector3(1f);
            ambientLightIntensity = 0.5f;
            lightSources = scene.LightSources;
        }

        public void BindSkin(in ModelSkin skin)
        {
            currentSkin = skin;
        }

        protected abstract void UnbindAttributes();
        private void UnbindAllAttributes()
        {
            UnbindAttributes();
            positionsArray = null;
        }


        public void UnbindPrimitive()
        {
            UnbindAllAttributes();
            normalTransformation = default;
            worldTransformation = default;
            currentMaterial = null;
        }

        public void UnbindScene()
        {
            lightSources = null;
            ambientLightColor = default;
            ambientLightColor = default;
            cameraPosition = default;
        }

        public void UnbindSkin()
        {
            currentSkin = null;
        }

        public abstract void VertexShader(int vertexDataIndex, Span<float> output);

        public abstract Vector4 PixelShader(ReadOnlySpan<float> input);
    }
}
