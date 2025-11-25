using System;
using System.Collections.Generic;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public static class PipelineFactory
    {
        private static readonly Dictionary<ShaderConfiguration, IPipeline> CachedTypes = [];

        public static IPipeline GetOrCreateInstance(ShaderConfiguration shaderConfiguration)
        {
            if(!CachedTypes.TryGetValue(shaderConfiguration, out IPipeline? pipeline)) 
            {
                pipeline = CreateInstance(shaderConfiguration);
                CachedTypes.Add(shaderConfiguration, pipeline);
            }
            return pipeline;

        }
        private static IPipeline CreateInstance(ShaderConfiguration shaderConfiguration)
        {
            Type floatOps = FloatOpsProviderFactory.GetOrCreateInstanceStruct(shaderConfiguration.InterpolatedDataSize);
            Type shader = ShaderFactory.CreateShader(shaderConfiguration);
            var pipelineType = typeof(Pipeline<,>).MakeGenericType(shader, floatOps);
            return (IPipeline)(Activator.CreateInstance(pipelineType) ?? throw new Exception("Failed to create pipeline"));
        }
    }
}
