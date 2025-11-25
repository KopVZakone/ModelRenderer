using GraphicsLib.Types;
using GraphicsLib.Types2;
using GraphicsLib.Types2.Shaders;
using GraphicsLib.Types3.ShaderGenerators;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace GraphicsLib.Types3
{
    public class Renderer
    {
        private readonly Plane[] worldSpacevViewFrustumPlanes = new Plane[6];
        public ZBufferV3? Zbuffer { get; set; }
        public BloomProcessor? BloomProcessor { get; set; }
        public bool BloomEnabled { get; set; } = false;
        public static float TimeElapsed { get; set; } = 0;
        private static readonly Vector4 DefaultColor = new Vector4(0, 0.2f, 0.6f, 1f);
        private static readonly Queue<(Matrix4x4 Transform, ModelSkin? Skin, ModelPrimitive Primitive)> nonOpaqueQueue = [];
        private static readonly Queue<(Matrix4x4 Transform, ModelSkin? Skin, ModelPrimitive Primitive)> opaqueQueue = [];
        private void ExtractFrustumPlanes(Camera camera)
        {
            Matrix4x4 viewProjectionMatrix = camera.ViewMatrix * camera.ProjectionMatrix;
            // Left plane
            worldSpacevViewFrustumPlanes[0] = new Plane(
                viewProjectionMatrix.M14 + viewProjectionMatrix.M11,
                viewProjectionMatrix.M24 + viewProjectionMatrix.M21,
                viewProjectionMatrix.M34 + viewProjectionMatrix.M31,
                viewProjectionMatrix.M44 + viewProjectionMatrix.M41
            );

            // Right plane
            worldSpacevViewFrustumPlanes[1] = new Plane(
                viewProjectionMatrix.M14 - viewProjectionMatrix.M11,
                viewProjectionMatrix.M24 - viewProjectionMatrix.M21,
                viewProjectionMatrix.M34 - viewProjectionMatrix.M31,
                viewProjectionMatrix.M44 - viewProjectionMatrix.M41
            );

            // Bottom plane
            worldSpacevViewFrustumPlanes[2] = new Plane(
                viewProjectionMatrix.M14 + viewProjectionMatrix.M12,
                viewProjectionMatrix.M24 + viewProjectionMatrix.M22,
                viewProjectionMatrix.M34 + viewProjectionMatrix.M32,
                viewProjectionMatrix.M44 + viewProjectionMatrix.M42
            );

            // Top plane
            worldSpacevViewFrustumPlanes[3] = new Plane(
                viewProjectionMatrix.M14 - viewProjectionMatrix.M12,
                viewProjectionMatrix.M24 - viewProjectionMatrix.M22,
                viewProjectionMatrix.M34 - viewProjectionMatrix.M32,
                viewProjectionMatrix.M44 - viewProjectionMatrix.M42
            );

            // Near plane
            worldSpacevViewFrustumPlanes[4] = new Plane(
                viewProjectionMatrix.M14 + viewProjectionMatrix.M13,
                viewProjectionMatrix.M24 + viewProjectionMatrix.M23,
                viewProjectionMatrix.M34 + viewProjectionMatrix.M33,
                viewProjectionMatrix.M44 + viewProjectionMatrix.M43
            );

            // Far plane
            worldSpacevViewFrustumPlanes[5] = new Plane(
                viewProjectionMatrix.M14 - viewProjectionMatrix.M13,
                viewProjectionMatrix.M24 - viewProjectionMatrix.M23,
                viewProjectionMatrix.M34 - viewProjectionMatrix.M33,
                viewProjectionMatrix.M44 - viewProjectionMatrix.M43
            );
            // Normalize all planes
            for (int i = 0; i < 6; i++)
            {
                worldSpacevViewFrustumPlanes[i] = Plane.Normalize(worldSpacevViewFrustumPlanes[i]);
            }
        }
        private bool IsBoundingBoxWithinView(in BoundingBox boundingBox, in Matrix4x4 transform)
        {
            var (min, max) = BoundingBox.TransformBoundingBox(boundingBox, transform);
            for (int i = 0; i < 6; i++)
            {
                ref readonly Plane plane = ref worldSpacevViewFrustumPlanes[i];

                Vector3 pVertex;
                pVertex.X = plane.Normal.X > 0 ? max.X : min.X;
                pVertex.Y = plane.Normal.Y > 0 ? max.Y : min.Y;
                pVertex.Z = plane.Normal.Z > 0 ? max.Z : min.Z;
                if (Vector3.Dot(plane.Normal, pVertex) + plane.D < 0)
                    return false;
            }
            return true;
        }
        public void Render(in ModelScene scene, ShaderFeatures shaderFeatures)
        {
            if (scene.RootModelNodes == null || scene.RootModelNodes.Length == 0 || scene.Camera == null)
                return;
            if (Zbuffer == null)
            {
                Zbuffer = new((int)scene.Camera.ScreenWidth, (int)scene.Camera.ScreenHeight);
                Zbuffer.ChangeDefaultColor(DefaultColor);
            }
            Zbuffer.ResizeAndClear((int)scene.Camera.ScreenWidth, (int)scene.Camera.ScreenHeight);
            if (BloomProcessor == null)
            {
                BloomProcessor = new BloomProcessor((int)scene.Camera.ScreenWidth, (int)scene.Camera.ScreenHeight);
                BloomProcessor.PrepareGaussianKernel(10f);
            }
            if (scene.Skins != null)
            {
                foreach (var skin in scene.Skins)
                {
                    CalculateBindingMatrices(skin.Skeleton!, skin, Matrix4x4.Identity);
                }
            }
            RenderScene(scene, Zbuffer, shaderFeatures);
            if (BloomEnabled)
            {
                BloomProcessor.Process(Zbuffer, 2);
            }
        }
        private void RenderScene(in ModelScene scene, in ZBufferV3 zBuffer, ShaderFeatures shaderFeatures)
        {
            ExtractFrustumPlanes(scene.Camera!);
            foreach (var node in scene.RootModelNodes!)
            {
                EnqueuePrimitivesRecursive(node, Matrix4x4.Identity, shaderFeatures);
            }
            Vector3 cameraPosition = scene.Camera!.Position;
            foreach (var (Transform, Skin, Primitive) in opaqueQueue.OrderByDescending(x => (Vector3.Transform(x.Primitive.BoundingBox!.Value.Center, x.Transform)
                                    - cameraPosition).LengthSquared()))
            {
                var opaqueShaderFeatures = shaderFeatures & ~ShaderFeatures.BlendModeNonOpaque;
                var primitive = Primitive;
                var configuration = ShaderConfigurationFactory.Create(opaqueShaderFeatures, Primitive.MaterialV2, Skin != null);
                IPipeline pipeline = PipelineFactory.GetOrCreateInstance(configuration);
                pipeline.BindScene(scene);
                pipeline.BindZBuffer(zBuffer);
                pipeline.BindSkin(Skin);
                pipeline.Render(ref primitive, Transform);
                pipeline.UnbindSkin();
                pipeline.UnbindZBuffer();
                pipeline.UnbindScene();
            }
            opaqueQueue.Clear();
            foreach (var (Transform, Skin, Primitive) in nonOpaqueQueue
                    .OrderBy(x => (Vector3.Transform(x.Primitive.BoundingBox!.Value.Center, x.Transform)
                                    - cameraPosition).LengthSquared()))
            {
                var primitive = Primitive;
                var configuration = ShaderConfigurationFactory.Create(shaderFeatures, Primitive.MaterialV2, Skin != null);
                IPipeline pipeline = PipelineFactory.GetOrCreateInstance(configuration);
                pipeline.BindScene(scene);
                pipeline.BindZBuffer(zBuffer);
                pipeline.BindSkin(Skin);
                pipeline.Render(ref primitive, Transform);
                pipeline.UnbindSkin();
                pipeline.UnbindZBuffer();
                pipeline.UnbindScene();
            }
            nonOpaqueQueue.Clear();
        }
        private static void CalculateBindingMatrices(in ModelNode node, ModelSkin skin, in Matrix4x4 parentTransform)
        {
            Matrix4x4 currentTransformation = node.TransformationMatrix;
            if (node.Animations != null)
            {
                foreach (var animation in node.Animations)
                {
                    currentTransformation = animation.Apply(currentTransformation, TimeElapsed);
                }
            }
            currentTransformation *= parentTransform;
            if (node.InfluencedSkins != null)
            {
                var (jointIndex, influencedSkin) = node.InfluencedSkins.First(x => x.influencedSkin == skin);
                if (influencedSkin.CurrentFrameJointMatrices != null && influencedSkin.InverseBindMatrices != null)
                {
                    influencedSkin.CurrentFrameJointMatrices[jointIndex] = influencedSkin.InverseBindMatrices[jointIndex]
                                                                                    * currentTransformation;
                }
            }
            if (node.ChildNodes != null)
            {
                foreach (var child in node.ChildNodes)
                {
                    CalculateBindingMatrices(child, skin, currentTransformation);
                }
            }
        }

        private void EnqueuePrimitivesRecursive(in ModelNode node, in Matrix4x4 parentTransform, in ShaderFeatures features)
        {
            Matrix4x4 currentTransformation = node.TransformationMatrix;
            if (node.Animations != null)
            {
                foreach (var animation in node.Animations)
                {
                    currentTransformation = animation.Apply(currentTransformation, TimeElapsed);
                }
            }
            currentTransformation *= parentTransform;
            if (node.ChildNodes != null)
            {
                foreach (var child in node.ChildNodes)
                {
                    EnqueuePrimitivesRecursive(child, currentTransformation, features);
                }
            }
            if (node.Mesh != null)
            {
                ModelPrimitive[] primitives = node.Mesh.Primitives;

                // Filter primitives based on their bounding box visibility if no skin is applied to the node
                if (node.AppliedSkin == null)
                {
                    primitives = [.. primitives
                        .Where(primitive => IsBoundingBoxWithinView(
                            primitive.BoundingBox!.Value,
                            currentTransformation))];
                }

                foreach (var primitive in primitives)
                {
                    if (primitive.MaterialV2?.AlphaMode == Types.GltfTypes.GltfMaterialAlphaMode.OPAQUE)
                    {
                        opaqueQueue.Enqueue((currentTransformation, node.AppliedSkin, primitive));
                    }
                    else
                    {
                        nonOpaqueQueue.Enqueue((currentTransformation, node.AppliedSkin, primitive));
                    }
                }
            }

        }
    }
}
