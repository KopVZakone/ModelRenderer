using GraphicsLib.Primitives;
using GraphicsLib.Types.GltfTypes;
using GraphicsLib.Types.JsonConverters;
using Newtonsoft.Json;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
namespace GraphicsLib.Types
{
    public static class Parser
    {
        /// <summary>
        /// Загружает модель из текста формата Wavefront Obj
        /// </summary>
        /// <param name="source">Входной текст</param>
        /// <returns>Модель</returns>
        [Obsolete("not updated since 3 lab")]
        public static Obj ParseObjFile(string filePath)
        {
            Obj obj = new();
            using FileStream fileStream = new(filePath, FileMode.Open);
            using StreamReader sr = new(fileStream);
            List<Vector3> verticesList = [];
            List<Vector3> normalsList = [];
            List<Vector2> uvsList = [];
            List<Face> facesList = [];
            List<Material> materialsList = [Material.defaultMaterial];
            while (!sr.EndOfStream)
            {
                string? line = sr.ReadLine();
                if (line == null || line.Length == 0)
                {
                    continue;
                }
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0])
                {
                    case "v":
                        {
                            Vector3 newVertex;
                            newVertex.X = Single.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture);
                            newVertex.Y = Single.Parse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture);
                            newVertex.Z = Single.Parse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture);
                            if (parts.Length == 5)
                            {
                                Single w = Single.Parse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture);
                                newVertex /= w;
                            }
                            verticesList.Add(newVertex);
                        }
                        break;
                    case "vt":
                        {
                            Vector2 newTextureCoord;
                            newTextureCoord.X = Single.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture);
                            newTextureCoord.Y = Single.Parse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture);
                            uvsList.Add(newTextureCoord);
                        }
                        break;
                    case "vn":
                        {
                            Vector3 newNormal;
                            newNormal.X = Single.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture);
                            newNormal.Y = Single.Parse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture);
                            newNormal.Z = Single.Parse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture);
                            //нормаль может быть не нормализована. нормализуем
                            newNormal = Vector3.Normalize(newNormal);
                            normalsList.Add(newNormal);
                        }
                        break;
                    case "f":
                        {
                            int[,] indices = new int[parts.Length - 1, 3];
                            string[] faceParts = parts[1].Split('/');
                            bool hasTextureIndices = faceParts.Length > 1 && faceParts[1].Length > 0;
                            bool hasNormalIndices = faceParts.Length > 2 && faceParts[2].Length > 0;
                            int[] vertices = new int[(parts.Length - 1)];
                            int[]? textures = hasTextureIndices ? new int[(parts.Length - 1)] : null;
                            int[]? normals = hasNormalIndices ? new int[(parts.Length - 1)] : null;
                            for (int i = 1; i < parts.Length; i++)
                            {
                                faceParts = parts[i].Split('/');
                                vertices[i - 1] = int.Parse(faceParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture) - 1;
                                if (textures != null)
                                {
                                    textures[i - 1] = int.Parse(faceParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture) - 1;
                                }
                                if (normals != null)
                                {
                                    normals[i - 1] = int.Parse(faceParts[2], NumberStyles.Integer, CultureInfo.InvariantCulture) - 1;
                                }
                            }
                            for(int i = 0; i < vertices.Length - 2; i++)
                            {
                                int[] triangleVertices = [vertices[0], vertices[i + 1], vertices[i + 2]];
                                int[]? triangleTextures = hasTextureIndices ? [textures![0], textures[i + 1], textures[i + 2]] : null;
                                int[]? triangleNormals = hasNormalIndices ? [normals![0], normals[i + 1], normals[i + 2]] : null;
                                Face newFace = new(triangleVertices, 0)
                                {
                                    nIndices = triangleNormals,
                                    tIndices = triangleTextures,
                                };
                                facesList.Add(newFace);
                            }

                        }
                        break;
                    case "#":
                    default:
                        //skip
                        break;
                }
            }
            obj.faces = [.. facesList];
            obj.vertices = [.. verticesList];
            obj.normals = [.. normalsList];
            obj.uvs = [.. uvsList];
            obj.materials = [.. materialsList];
            StaticTriangle[] staticTriangles = new StaticTriangle[facesList.Count];
            for (int i = 0; i < facesList.Count; i++)
            {
                staticTriangles[i] = StaticTriangle.FromFace(obj, obj.faces[i]);
            }
            foreach (var face in obj.faces)
            {
                for (int i = 0; i < face.vIndices.Length; i++)
                {
                    int p = face.vIndices[i];
                    if (p < 0)
                        p = verticesList.Count + p + 1;
                    face.vIndices[i] = p;
                    if (face.nIndices != null)
                    {
                        int n = face.nIndices[i];
                        if (n < 0)
                            n = normalsList.Count + n + 1;
                        face.nIndices[i] = n;
                    }
                    if (face.tIndices != null)
                    {
                        int t = face.tIndices[i];
                        if (t < 0)
                            t = uvsList.Count + t + 1;
                        face.tIndices[i] = t;
                    }
                }
            }
            obj.triangles = [.. staticTriangles];
            
            return obj;
        }
        public static Obj ParseGltfFile(string filePath)
        {
            Obj obj = new();
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
            List<Vector3> verticesList = [];
            List<Vector3> normalsList = [];
            List<Vector2> uvsList = [];
            List<Vector4> tangentsList = [];
            List<Vector2> normalUvsList = [];
            List<Vector2> roughnessUvsList = [];
            List<Face> facesList = [];
            List<Material> materialsList = [];
            if (gltfRoot.Materials != null)
            {
                materialsList = new(gltfRoot.Materials.Count);
                for (int i = 0; i < gltfRoot.Materials.Count; i++)
                {
                    materialsList.Add(null!);//Lol
                }
                Parallel.For(0, gltfRoot.Materials.Count, i => 
                materialsList[i] = Material.FromGltfMaterial(gltfRoot.Materials[i])
                );
            }
            else
            {
                materialsList.Add(Material.defaultMaterial);
            }
            if (gltfRoot.Nodes != null)
            {
                foreach (var node in gltfRoot.Nodes)
                {
                    Matrix4x4 transform = node.GlobalTransform;
                    Matrix4x4 normalTransform = node.GlobalNormalTransform;
                    //Matrix4x4 normalTransform = node.GlobalTransform;
                    if (node.Mesh != null)
                    {
                        var mesh = node.Mesh;
                        foreach (var primitive in mesh.Primitives)
                        {
                            var mode = primitive.Mode;
                            //var attributes = primitive.Attributes;
                            int vIndexOffset = verticesList.Count;
                            int nIndexOffset = normalsList.Count;
                            int tIndexOffset = uvsList.Count;
                            int ntIndexOffset = normalUvsList.Count;
                            int rtIndexOffset = roughnessUvsList.Count;
                            int tangIndexOffset = tangentsList.Count;
                            Vector3[]? vertices = primitive.Position;
                            Vector3[]? normals = primitive.Normal;
                            Vector2[]? uvs = null;
                            Vector2[]? normalUvs = null;
                            Vector2[]? roughtnessUvs = null;
                            Vector4[]? tangents = primitive.Tangent;
                            int[]? indicies = primitive.PointIndices;
                            short materialIndex = (primitive.Material.HasValue) ? (short)primitive.Material.Value : (short)0;
                            var gltfMaterial = gltfRoot.Materials?[materialIndex];
                            if (gltfMaterial != null)
                            {
                                if (gltfMaterial.PbrMetallicRoughness != null)
                                {
                                    if (gltfMaterial.PbrMetallicRoughness.BaseColorTexture != null)
                                    {
                                        uvs = primitive.GetTextureCoords(gltfMaterial.PbrMetallicRoughness.BaseColorTexture.TexCoord);
                                    }
                                    if(gltfMaterial.PbrMetallicRoughness.MetallicRoughnessTexture != null)
                                    {
                                        roughtnessUvs = primitive.GetTextureCoords(gltfMaterial.PbrMetallicRoughness.MetallicRoughnessTexture.TexCoord);
                                    }
                                }
                                if (gltfMaterial.NormalTexture != null)
                                {
                                    normalUvs = primitive.GetTextureCoords(gltfMaterial.NormalTexture.TexCoord);
                                }
                            }
                            if (vertices != null)
                            {
                                for(int i = 0; i < vertices.Length; i++) 
                                {
                                    Vector3 transformed = Vector3.Transform(vertices[i], transform);
                                    verticesList.Add(transformed);
                                }
                            }
                            if (normals != null)
                            {

                                for(int i = 0; i < normals.Length; i++)
                                {
                                    Vector3 transformed = Vector3.Normalize(Vector3.TransformNormal(normals[i], normalTransform));
                                    normalsList.Add(transformed);
                                }
                            }
                            if (uvs != null)
                            {
                                for(int i = 0;i < uvs.Length; i++)
                                {
                                    uvsList.Add(uvs[i]);
                                }
                            }
                            if(normalUvs != null)
                            {
                                for(int i = 0; i < normalUvs.Length; i++)
                                {
                                    normalUvsList.Add(normalUvs[i]);
                                }
                            }
                            if(tangents != null)
                            {
                                for(int i = 0; i < tangents.Length; i++)
                                {
                                    tangentsList.Add(tangents[i]);
                                }
                            }
                            if(roughtnessUvs != null)
                            {
                                for(int i = 0; i < roughtnessUvs.Length; i++)
                                {
                                    roughnessUvsList.Add(roughtnessUvs[i]);
                                }
                            }
                            if (indicies != null)
                            {
                                Face AssembleTriangle(int index0, int index1, int index2, short materialIndex)
                                {
                                    int[] triangleVertices = [indicies[index0] + vIndexOffset,
                                            indicies[index1] + vIndexOffset,
                                            indicies[index2] + vIndexOffset];
                                    int[]? triangleNormals = null;
                                    if (normalsList.Count > nIndexOffset)
                                        triangleNormals = [indicies[index0] + nIndexOffset,
                                            indicies[index1] + nIndexOffset,
                                            indicies[index2] + nIndexOffset];
                                    int[]? triangleUvs = null;
                                    if (uvsList.Count > tIndexOffset)
                                        triangleUvs = [indicies[index0] + tIndexOffset,
                                            indicies[index1] + tIndexOffset,
                                            indicies[index2] + tIndexOffset];
                                    int[]? triangleTangents = null;
                                    if (tangentsList.Count > tangIndexOffset)
                                        triangleTangents = [indicies[index0] + tangIndexOffset,
                                            indicies[index1] + tangIndexOffset,
                                            indicies[index2] + tangIndexOffset];
                                    int[]? triangleNormalUvs = null;
                                    if (normalUvsList.Count > ntIndexOffset)
                                        triangleNormalUvs = [indicies[index0] + ntIndexOffset,
                                            indicies[index1] + ntIndexOffset,
                                            indicies[index2] + ntIndexOffset];
                                    int[]? triangleRoughnessUVs = null;
                                    if(roughnessUvsList.Count > rtIndexOffset)
                                        triangleRoughnessUVs = [indicies[index0] + rtIndexOffset,
                                            indicies[index1] + rtIndexOffset,
                                            indicies[index2] + rtIndexOffset];
                                    return new Face(triangleVertices, materialIndex)
                                    {
                                        nIndices = triangleNormals,
                                        tIndices = triangleUvs,
                                        tangentIndicies = triangleTangents,
                                        ntIndicies = triangleNormalUvs,
                                        rtIndicies = triangleRoughnessUVs
                                    };
                                }

                                if (mode == GltfMeshMode.TRIANGLES)
                                {
                                    for (int i = 0; i < indicies.Length; i += 3)
                                    {
                                        int index0 = i;
                                        int index1 = i + 1;
                                        int index2 = i + 2;
                                        Face newFace = AssembleTriangle(index0, index1, index2, materialIndex);
                                        facesList.Add(newFace);
                                    }
                                }
                                else if (mode == GltfMeshMode.TRIANGLE_STRIP)
                                {
                                    for (int i = 0; i < indicies.Length - 2; i++)
                                    {
                                        int index0 = i;
                                        int index1 = i + 1;
                                        int index2 = i + 2;
                                        Face newFace = AssembleTriangle(index0, index1, index2, materialIndex);
                                        facesList.Add(newFace);
                                    }
                                }
                                else if (mode == GltfMeshMode.TRIANGLE_FAN)
                                {
                                    for (int i = 0; i < indicies.Length - 2; i++)
                                    {
                                        int index0 = 0;
                                        int index1 = i + 1;
                                        int index2 = i + 2;
                                        Face newFace = AssembleTriangle(index0, index1, index2, materialIndex);
                                        facesList.Add(newFace);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            obj.materials = [.. materialsList];
            obj.faces = [.. facesList];
            obj.vertices = [.. verticesList];
            obj.normals = [.. normalsList];
            obj.tangents = [.. tangentsList];
            obj.normalUvs = [.. normalUvsList];
            obj.uvs = [.. uvsList];
            obj.roughnessUvs = [.. roughnessUvsList];
            StaticTriangle[] staticTriangles = new StaticTriangle[facesList.Count];
            for (int i = 0; i < facesList.Count; i++)
            {
                staticTriangles[i] = StaticTriangle.FromFace(obj, obj.faces[i]);
            }
            obj.triangles = [.. staticTriangles];
            return obj;
        }
    
    }
}


