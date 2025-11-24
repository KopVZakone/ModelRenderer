using GraphicsLib.Types.GltfTypes;
using GraphicsLib.Types.JsonConverters;
using GraphicsLib.Types;
using Newtonsoft.Json;
using System.IO;
using System.Windows;
using System.Xml.Linq;
using System.Linq;
using System;
using System.Numerics;
using System.Collections.Concurrent;
using GraphicsLib.Types3;

namespace GraphicsLib.Types2
{
    public static class ModelParser
    {
        public static ModelScene ParseGltfFile(string filePath)
        {
            try
            {

                using FileStream fileStream = new(filePath, FileMode.Open);
                using StreamReader sr = new(fileStream);
                string data = sr.ReadToEnd();
                string sourceDirectory = Path.GetDirectoryName(filePath)!;
                GltfRoot gltfRoot = JsonConvert.DeserializeObject<GltfRoot>(data, GltfSerializerSettings.GetSettings(sourceDirectory))
                        ?? throw new FormatException("invalid json gltf");
                if (gltfRoot.ExtensionsRequired != null && gltfRoot.ExtensionsRequired.Count != 0)
                {
                    MessageBox.Show($"Model requires extensions : {gltfRoot.ExtensionsRequired.Aggregate((a, b) => a + ", " + b)}.");
                }
                Material[] materialsList;
                MaterialV2[] materialsV2List;
                if (gltfRoot.Materials != null)
                {
                    materialsList = new Material[gltfRoot.Materials.Count];
                    materialsV2List = new MaterialV2[gltfRoot.Materials.Count];
                    Parallel.For(0, gltfRoot.Materials.Count, i =>
                        {
                            materialsList[i] = Material.FromGltfMaterial(gltfRoot.Materials[i]);
                            materialsV2List[i] = MaterialV2.FromGltfMaterial(gltfRoot.Materials[i]);
                        }  
                    );
                }
                else
                {
                    materialsList = [Material.defaultMaterial];
                    materialsV2List = [MaterialV2.defaultMaterial];
                }
                if (gltfRoot.Skins != null)
                {
                    foreach (var skin in gltfRoot.Skins)
                    {
                        ModelSkin modelSkin = ModelSkin.FromGltfSkin(skin);
                        skinCache.TryAdd(skin, modelSkin);
                    }
                }
                if (gltfRoot.Scenes != null)
                {
                    ModelScene scene = new();
                    GltfScene gltfScene = gltfRoot.Scenes[0];
                    if (gltfScene.Nodes != null)
                    {
                        scene.RootModelNodes = new ModelNode[gltfScene.Nodes.Length];
                        for(int i = 0; i < gltfScene.Nodes.Length; i++)
                        {
                            scene.RootModelNodes[i] = ParseNode(gltfRoot.Nodes![gltfScene.Nodes[i]], materialsList, materialsV2List).Result;
                        }
                    }
                    if(!skinCache.IsEmpty)
                    {
                        scene.Skins = [.. skinCache.Values];
                    }
                    return scene;
                }
            return new ModelScene();
            }
            finally
            {
                skinCache.Clear();
            }
        }
        private static readonly ConcurrentDictionary<GltfSkin, ModelSkin> skinCache = [];
        private static ModelMesh ParseMesh(GltfMesh gltfMesh, Material[] materialsList, MaterialV2[] materialsV2List)
        {
            var primitives = new ModelPrimitive[gltfMesh.Primitives.Length];
            Parallel.For(0, gltfMesh.Primitives.Length, i =>
                {
                    var p = gltfMesh.Primitives[i];
                    var attributes = p.Attributes.Select<KeyValuePair<string, int>, KeyValuePair<string, GltfAccessor>>(a => new(a.Key, p.Root!.Accessors![a.Value])).ToDictionary();
                    Dictionary<string, short> attributesOffsets = [];
                    List<float[]> floatData = [];
                    List<ushort[]> weightsData = [];
                    int vertexCount = 0;
                    foreach (var attribute in attributes)
                    {
                        if (attribute.Key.StartsWith("JOINTS_"))
                        {
                            weightsData.Add(GltfUtils.GetAccessorData<ushort>(attribute.Value));
                        }
                        else
                        {
                            floatData.Add(GltfUtils.GetAccessorData<float>(attribute.Value));
                            vertexCount = Math.Max(vertexCount, attribute.Value.Count);
                            attributesOffsets.Add(attribute.Key, (short)(floatData.Count - 1));
                        }

                    }
                    primitives[i] = new ModelPrimitive()
                    {
                        VertexCount = vertexCount,
                        Indices = p.PointIndices,
                        Material = p.Material.HasValue ? materialsList[p.Material.Value] : Material.defaultMaterial,
                        MaterialV2 = p.Material.HasValue ? materialsV2List[p.Material.Value] : MaterialV2.defaultMaterial,
                        Joints = [.. weightsData],
                        Mode = p.Mode,
                        AttributesOffsets = attributesOffsets,
                        FloatData = [.. floatData],
                        BoundingBox = GltfUtils.GetBoundingBox(attributes, floatData[attributesOffsets["POSITION"]])
                    };
                });
            var mesh = new ModelMesh()
            {
                Primitives = primitives,
                BoundingBox = primitives.Select(p => p.BoundingBox).Aggregate((a, b) =>
                    new(Vector3.Min(a!.Value.Min, b!.Value.Min), Vector3.Max(a.Value.Max, b.Value.Max)))
            };
            return mesh;
        }
        private static async Task<ModelNode> ParseNode(GltfNode gltfNode, Material[] materialsList, MaterialV2[] materialsV2List)
        {
            var childrenTasks = gltfNode.ChildrenNodes?
            .Select(child => ParseNode(child, materialsList, materialsV2List))
                .ToArray() ?? [];
            ModelMesh? mesh = gltfNode.Mesh != null ? ParseMesh(gltfNode.Mesh, materialsList, materialsV2List) : null;
            ModelNode[]? childrenNodes = await Task.WhenAll(childrenTasks);
            if(childrenNodes.Length == 0)
            {
                childrenNodes = null;
            }
            ModelNode modelNode = new()
            {
                ChildNodes = childrenNodes,
                Mesh = mesh,
            };
            if (gltfNode.Matrix.HasValue)
            {
                modelNode.TransformationMatrix = gltfNode.Matrix.Value;
            }
            else
            {
                if (gltfNode.Translation.HasValue)
                {
                    modelNode.Translation = gltfNode.Translation.Value;
                }
                if (gltfNode.Rotation.HasValue)
                {
                    modelNode.Rotation = gltfNode.Rotation.Value;
                }
                if (gltfNode.Scale.HasValue)
                {
                    modelNode.Scale = gltfNode.Scale.Value;
                }
            }
            if(gltfNode.Animations != null)
            {
                modelNode.Animations = [.. gltfNode.Animations.Select(a => ModelAnimation.FromGltfAnimation(a))];
            }
            if(gltfNode.AppliedSkin != null)
            {
                modelNode.AppliedSkin = skinCache[gltfNode.AppliedSkin];
            }
            if(gltfNode.SkeletonForSkins != null)
            {
                foreach (var gltfSkin in gltfNode.SkeletonForSkins)
                {
                    skinCache[gltfSkin].Skeleton = modelNode;
                }
            }
            if (gltfNode.InfluencedSkins != null)
            {
                modelNode.InfluencedSkins = [.. gltfNode.InfluencedSkins.Select((s) => (s.jointIndex, skinCache[s.influencedSkin]))];
            }
            return modelNode;
        }
    }
}
