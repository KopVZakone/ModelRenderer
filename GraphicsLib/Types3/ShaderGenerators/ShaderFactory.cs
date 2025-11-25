using GraphicsLib.Types2;
using GraphicsLib.Types2.Shaders;
using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public static class ShaderFactory
    {
        private static readonly AssemblyBuilder AssemblyBuilder;
        private static readonly ModuleBuilder ModuleBuilder;
        private static readonly Dictionary<ShaderConfiguration, Type> ShaderCache = [];

        static ShaderFactory()
        {
            AssemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("DynamicShaders"),
                AssemblyBuilderAccess.Run);

            ModuleBuilder = AssemblyBuilder.DefineDynamicModule("MainModule");
        }

        public static Type CreateShader(ShaderConfiguration config)
        {
            if (ShaderCache.TryGetValue(config, out Type? shaderType))
            {
                return shaderType;
            }

            shaderType = BuildShaderType(config);
            ShaderCache[config] = shaderType;
            return shaderType;
        }

        private static Type BuildShaderType(ShaderConfiguration config)
        {
            string typeName = $"DynamicShader_{Guid.NewGuid():N}";
            TypeBuilder typeBuilder = ModuleBuilder.DefineType(
                typeName,
                TypeAttributes.Public | TypeAttributes.Class,
                typeof(ShaderBase));

            // Создаем поля для хранения указателей на атрибуты
            var attributeFields = CreateAttributeFields(typeBuilder, config);

            // Генерируем методы
            var bindAttributesMethod = GenerateBindAttributesMethod(typeBuilder, config, attributeFields);
            var unbindAttributesMethod = GenerateUnbindAttributesMethod(typeBuilder, attributeFields);
            GenerateVertexShaderMethod(typeBuilder, config, attributeFields);
            GeneratePixelShaderMethod(typeBuilder, config, attributeFields);

            // Явно переопределяем базовые методы
            var baseBindAttributes = typeof(ShaderBase).GetMethod("BindAttributes",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var baseUnbindAttributes = typeof(ShaderBase).GetMethod("UnbindAttributes",
                BindingFlags.NonPublic | BindingFlags.Instance);

            typeBuilder.DefineMethodOverride(bindAttributesMethod, baseBindAttributes);
            typeBuilder.DefineMethodOverride(unbindAttributesMethod, baseUnbindAttributes);

            return typeBuilder.CreateType();
        }

        private static Dictionary<string, FieldBuilder> CreateAttributeFields(TypeBuilder typeBuilder, ShaderConfiguration config)
        {
            var fields = new Dictionary<string, FieldBuilder>();

            // Базовые атрибуты (всегда есть)
            fields["NORMAL"] = typeBuilder.DefineField(
                "normalsArray", typeof(Vector3*), FieldAttributes.Private);

            // Тангенты (только если есть нормальная карта)
            if (config.Features.HasFlag(ShaderFeatures.NormalMap))
            {
                fields["TANGENT"] = typeBuilder.DefineField(
                    "tangentsArray", typeof(Vector4*), FieldAttributes.Private);
            }

            // UV координаты для каждого используемого слоя
            var usedUVs = config.TextureBindings.GetUsedUVs();
            foreach (int uvIndex in usedUVs)
            {
                string fieldName = $"uvArray_{uvIndex}";
                fields[$"TEXCOORD_{uvIndex}"] = typeBuilder.DefineField(
                    fieldName, typeof(Vector2*), FieldAttributes.Private);
            }

            // Атрибуты для скининга (если есть)
            if (config.HasSkin)
            {
                fields["JOINTS_0"] = typeBuilder.DefineField(
                    "jointsArray", typeof(Vector4*), FieldAttributes.Private);

                fields["WEIGHTS_0"] = typeBuilder.DefineField(
                    "weightsArray", typeof(Vector4*), FieldAttributes.Private);
            }

            return fields;
        }

        private static MethodBuilder GenerateBindAttributesMethod(
            TypeBuilder typeBuilder,
            ShaderConfiguration config,
            Dictionary<string, FieldBuilder> attributeFields)
        {   
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(
                "BindAttributes",
                MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                null,
                new Type[] { typeof(ModelPrimitive).MakeByRefType() });
            ILGenerator il = methodBuilder.GetILGenerator();

            // Генерация вызовов GetAttributePointer для каждого атрибута
            foreach (var (attributeName, field) in attributeFields)
            {
                Type attributeType = GetAttributeType(attributeName);
                GenerateGetAttributePointerCall(il, typeBuilder, attributeName, attributeType, field);
            }

            il.Emit(OpCodes.Ret);

            return methodBuilder;
        }

        private static void GenerateGetAttributePointerCall(
            ILGenerator il,
            TypeBuilder typeBuilder,
            string attributeName,
            Type attributeType,
            FieldBuilder field)
        {
            // this.field = ModelShaderUtils.GetAttributePointer<attributeType>(primitive, "ATTRIBUTE_NAME")
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldarg_1); // primitive

            // Создаем обобщенный метод GetAttributePointer
            MethodInfo genericMethod = typeof(ModelShaderUtils)
                .GetMethod("GetAttributePointer", BindingFlags.Public | BindingFlags.Static)
                .MakeGenericMethod(attributeType);

            il.Emit(OpCodes.Ldstr, attributeName);
            il.Emit(OpCodes.Call, genericMethod);
            il.Emit(OpCodes.Stfld, field);
        }

        private static Type GetAttributeType(string attributeName)
        {
            return attributeName switch
            {
                "POSITION" => typeof(Vector3),
                "NORMAL" => typeof(Vector3),
                "TANGENT" => typeof(Vector4),
                "JOINTS_0" => typeof(Vector4),
                "WEIGHTS_0" => typeof(Vector4),
                string s when s.StartsWith("TEXCOORD_") => typeof(Vector2),
                _ => throw new ArgumentException($"Unknown attribute: {attributeName}")
            };
        }

        private static MethodBuilder GenerateUnbindAttributesMethod(
            TypeBuilder typeBuilder,
            Dictionary<string, FieldBuilder> attributeFields)
        {
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(
                "UnbindAttributes",
                MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                null,
                Type.EmptyTypes);

            ILGenerator il = methodBuilder.GetILGenerator();

            // Устанавливаем все поля в null
            foreach (var field in attributeFields.Values)
            {
                il.Emit(OpCodes.Ldarg_0); // this
                il.Emit(OpCodes.Ldc_I4_0); // 0 (эквивалент null для указателей)
                il.Emit(OpCodes.Conv_U);   // преобразуем в указатель
                il.Emit(OpCodes.Stfld, field);
            }

            il.Emit(OpCodes.Ret);

            return methodBuilder;
        }

        private static void GenerateVertexShaderStub(TypeBuilder typeBuilder)
        {
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(
                "VertexShader",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                null,
                new Type[] { typeof(int), typeof(Span<float>) });

            // Получаем базовый метод для переопределения
            var baseVertexShader = typeof(ShaderBase).GetMethod("VertexShader");

            ILGenerator il = methodBuilder.GetILGenerator();
            // Заглушка - просто выходим
            il.Emit(OpCodes.Ret);

            typeBuilder.DefineMethodOverride(methodBuilder, baseVertexShader);
        }
        
        private static MethodBuilder GenerateVertexShaderMethod(
    TypeBuilder typeBuilder,
    ShaderConfiguration config,
    Dictionary<string, FieldBuilder> attributeFields)
        {
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(
                "VertexShader",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                null,
                new Type[] { typeof(int), typeof(Span<float>) });

            ILGenerator il = methodBuilder.GetILGenerator();

            // Получаем все необходимые MethodInfo и FieldInfo
            var methodRefs = CollectMethodReferences();
            var fieldRefs = CollectFieldReferences();

            // Объявляем локальные переменные
            var locals = DeclareLocalVariables(il, config);

            // Генерируем код получения атрибутов вершины
            GenerateVertexAttributesLoading(il, config, attributeFields, fieldRefs, locals);

            // Генерируем код скининга (если есть)
            if (config.HasSkin)
            {
                GenerateSkinningCode(il, config, attributeFields, fieldRefs, methodRefs, locals);
            }

            // Генерируем код мировых трансформаций
            GenerateWorldTransformations(il, config, fieldRefs, methodRefs, locals);

            // Генерируем код записи результатов в Span
            GenerateOutputWriting(il, config, methodRefs, locals);

            il.Emit(OpCodes.Ret);
            return methodBuilder;
        }

        // Сбор всех используемых MethodInfo
        private static VertexShaderMethodReferences CollectMethodReferences()
        {
            return new VertexShaderMethodReferences
            {
                // Методы ShaderComponents
                CalculateInverseBoneTransforms = typeof(ShaderComponents).GetMethod(
                    "CalculateInverseBoneTransforms", BindingFlags.Public | BindingFlags.Static),
                TransformPositionByBone = typeof(ShaderComponents).GetMethod(
                    "TransformPositionByBone", BindingFlags.Public | BindingFlags.Static),
                TransformNormalByBone = typeof(ShaderComponents).GetMethod(
                    "TransformNormalByBone", BindingFlags.Public | BindingFlags.Static),
                TransformTangentByBone = typeof(ShaderComponents).GetMethod(
                    "TransformTangentByBone", BindingFlags.Public | BindingFlags.Static),
                WriteVector4 = typeof(ShaderComponents).GetMethod("WriteVector4"),
                WriteVector3 = typeof(ShaderComponents).GetMethod("WriteVector3"),
                WriteVector2 = typeof(ShaderComponents).GetMethod("WriteVector2"),

                // Методы Vector4
                Vector4CtorVector3Float = typeof(Vector4).GetConstructor(new[] { typeof(Vector3), typeof(float) }),
                Vector4Transform = typeof(Vector4).GetMethod("Transform", new[] { typeof(Vector4), typeof(Matrix4x4) }),
                Vector4AsVector3 = typeof(Vector).GetMethod("AsVector3", [typeof(Vector4)]),

                // Методы Vector3
                Vector3TransformNormal = typeof(Vector3).GetMethod("TransformNormal", new[] { typeof(Vector3), typeof(Matrix4x4) })
            };
        }

        // Сбор всех используемых FieldInfo
        private static VertexShaderFieldReferences CollectFieldReferences()
        {
            return new VertexShaderFieldReferences
            {
                // Поля ShaderBase
                PositionsArray = typeof(ShaderBase).GetField("positionsArray", BindingFlags.NonPublic | BindingFlags.Instance),
                WorldTransformation = typeof(ShaderBase).GetField("worldTransformation", BindingFlags.NonPublic | BindingFlags.Instance),
                NormalTransformation = typeof(ShaderBase).GetField("normalTransformation", BindingFlags.NonPublic | BindingFlags.Instance),
                CurrentSkin = typeof(ShaderBase).GetField("currentSkin", BindingFlags.NonPublic | BindingFlags.Instance),

                // Поля Vector4
                Vector4W = typeof(Vector4).GetField("W")
            };
        }

        // Объявление локальных переменных
        private static VertexShaderLocals DeclareLocalVariables(ILGenerator il, ShaderConfiguration config)
        {
            var locals = new VertexShaderLocals
            {
                InitialPosition = il.DeclareLocal(typeof(Vector4)),
                InitialNormal = il.DeclareLocal(typeof(Vector3)),
                InitialTangent = config.Features.HasFlag(ShaderFeatures.NormalMap) ?
                    il.DeclareLocal(typeof(Vector4)) : null,
                WorldPosition = il.DeclareLocal(typeof(Vector4)),
                WorldNormal = il.DeclareLocal(typeof(Vector3)),
                WorldTangent = config.Features.HasFlag(ShaderFeatures.NormalMap) ?
                    il.DeclareLocal(typeof(Vector4)) : null,
                BoneMatrices = config.HasSkin ?
                    il.DeclareLocal(typeof(Matrix4x4).MakeArrayType()) : null
            };

            // UV координаты
            locals.UVLocals = new Dictionary<int, LocalBuilder>();
            foreach (var uvIndex in config.TextureBindings.GetUsedUVs())
            {
                locals.UVLocals[uvIndex] = il.DeclareLocal(typeof(Vector2));
            }

            return locals;
        }

        // Генерация кода загрузки атрибутов вершины
        private static void GenerateVertexAttributesLoading(
            ILGenerator il,
            ShaderConfiguration config,
            Dictionary<string, FieldBuilder> attributeFields,
            VertexShaderFieldReferences fieldRefs,
            VertexShaderLocals locals)
        {
            // Загрузка позиции
            LoadVector3Attribute(il, fieldRefs.PositionsArray, locals.InitialPosition, 1.0f); // w = 1.0

            // Загрузка нормали
            LoadVector3Attribute(il, attributeFields["NORMAL"], locals.InitialNormal);

            // Загрузка тангента (если есть)
            if (config.Features.HasFlag(ShaderFeatures.NormalMap))
            {
                LoadVector4Attribute(il, attributeFields["TANGENT"], locals.InitialTangent);
            }

            // Загрузка UV координат
            foreach (var uvIndex in config.TextureBindings.GetUsedUVs())
            {
                LoadVector2Attribute(il, attributeFields[$"TEXCOORD_{uvIndex}"], locals.UVLocals[uvIndex]);
            }
        }

        // Генерация кода скининга
        private static void GenerateSkinningCode(
            ILGenerator il,
            ShaderConfiguration config,
            Dictionary<string, FieldBuilder> attributeFields,
            VertexShaderFieldReferences fieldRefs,
            VertexShaderMethodReferences methodRefs,
            VertexShaderLocals locals)
        {
            // Создаем массив для матриц костей
            il.Emit(OpCodes.Ldc_I4_4); // 4 матрицы
            il.Emit(OpCodes.Newarr, typeof(Matrix4x4));
            il.Emit(OpCodes.Stloc, locals.BoneMatrices);

            // Вызываем CalculateInverseBoneTransforms
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, attributeFields["JOINTS_0"]); // jointsArray

            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, attributeFields["WEIGHTS_0"]); // weightsArray

            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, fieldRefs.CurrentSkin);

            il.Emit(OpCodes.Ldarg_1); // vertexDataIndex

            il.Emit(OpCodes.Ldloc, locals.BoneMatrices); // boneMatrices array

            il.Emit(OpCodes.Call, methodRefs.CalculateInverseBoneTransforms);

            // Трансформируем позицию
            il.Emit(OpCodes.Ldloca, locals.InitialPosition);
            il.Emit(OpCodes.Ldloc, locals.BoneMatrices);
            il.Emit(OpCodes.Call, methodRefs.TransformPositionByBone);

            // Трансформируем нормаль
            il.Emit(OpCodes.Ldloca, locals.InitialNormal);
            il.Emit(OpCodes.Ldloc, locals.BoneMatrices);
            il.Emit(OpCodes.Call, methodRefs.TransformNormalByBone);

            // Трансформируем тангент (если есть)
            if (config.Features.HasFlag(ShaderFeatures.NormalMap))
            {
                il.Emit(OpCodes.Ldloca, locals.InitialTangent);
                il.Emit(OpCodes.Ldloc, locals.BoneMatrices);
                il.Emit(OpCodes.Call, methodRefs.TransformTangentByBone);
            }
        }

        // Генерация кода мировых трансформаций
        private static void GenerateWorldTransformations(
            ILGenerator il,
            ShaderConfiguration config,
            VertexShaderFieldReferences fieldRefs,
            VertexShaderMethodReferences methodRefs,
            VertexShaderLocals locals)
        {
            // World position
            il.Emit(OpCodes.Ldloc, locals.InitialPosition);
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, fieldRefs.WorldTransformation);
            il.Emit(OpCodes.Call, methodRefs.Vector4Transform);
            il.Emit(OpCodes.Stloc, locals.WorldPosition);

            // World normal
            il.Emit(OpCodes.Ldloc, locals.InitialNormal);
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, fieldRefs.NormalTransformation);
            il.Emit(OpCodes.Call, methodRefs.Vector3TransformNormal);
            il.Emit(OpCodes.Stloc, locals.WorldNormal);

            // World tangent (если есть)
            if (config.Features.HasFlag(ShaderFeatures.NormalMap))
            {
                il.Emit(OpCodes.Ldloc, locals.InitialTangent);
                il.Emit(OpCodes.Call, methodRefs.Vector4AsVector3);
                il.Emit(OpCodes.Ldarg_0); // this
                il.Emit(OpCodes.Ldfld, fieldRefs.WorldTransformation);
                il.Emit(OpCodes.Call, methodRefs.Vector3TransformNormal);
                il.Emit(OpCodes.Ldloc, locals.InitialTangent);
                il.Emit(OpCodes.Ldfld, fieldRefs.Vector4W);
                il.Emit(OpCodes.Newobj, methodRefs.Vector4CtorVector3Float);
                il.Emit(OpCodes.Stloc, locals.WorldTangent);
            }
        }

        // Генерация кода записи результатов в Span
        private static void GenerateOutputWriting(
            ILGenerator il,
            ShaderConfiguration config,
            VertexShaderMethodReferences methodRefs,
            VertexShaderLocals locals)
        {
            VertexAttributeOffsets offsets = config.Offsets;

            // Position (Vector4) - всегда по offset 0
            WriteVector4ToSpan(il, locals.WorldPosition, offsets.Position, methodRefs.WriteVector4);

            // World Position (Vector3)
            WriteVector3ToSpan(il, locals.WorldPosition, offsets.WorldPosition,
                methodRefs.Vector4AsVector3, methodRefs.WriteVector3);

            // Normal
            WriteVector3ToSpan(il, locals.WorldNormal, offsets.Normal, methodRefs.WriteVector3);

            // Tangent (если есть)
            if (config.Features.HasFlag(ShaderFeatures.NormalMap))
            {
                WriteVector4ToSpan(il, locals.WorldTangent, offsets.Tangent.Value, methodRefs.WriteVector4);
            }

            // UV координаты
            foreach (var uvIndex in config.TextureBindings.GetUsedUVs())
            {
                if (offsets.UVOffsets.TryGetValue(uvIndex, out int uvOffset))
                {
                    WriteVector2ToSpan(il, locals.UVLocals[uvIndex], uvOffset, methodRefs.WriteVector2);
                }
            }
        }

        // Вспомогательные методы для загрузки атрибутов
        private static void LoadVector3Attribute(ILGenerator il, FieldInfo field, LocalBuilder local, float? wComponent = null)
        {
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ldarg_1); // vertexDataIndex
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Sizeof, typeof(Vector3));
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldobj, typeof(Vector3));

            if (wComponent.HasValue)
            {
                il.Emit(OpCodes.Ldc_R4, wComponent.Value);
                il.Emit(OpCodes.Newobj, typeof(Vector4).GetConstructor(new[] { typeof(Vector3), typeof(float) }));
            }

            il.Emit(OpCodes.Stloc, local);
        }

        private static void LoadVector4Attribute(ILGenerator il, FieldInfo field, LocalBuilder local)
        {
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ldarg_1); // vertexDataIndex
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Sizeof, typeof(Vector4));
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldobj, typeof(Vector4));
            il.Emit(OpCodes.Stloc, local);
        }

        private static void LoadVector2Attribute(ILGenerator il, FieldInfo field, LocalBuilder local)
        {
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ldarg_1); // vertexDataIndex
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Sizeof, typeof(Vector2));
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldobj, typeof(Vector2));
            il.Emit(OpCodes.Stloc, local);
        }

        // Вспомогательные методы для записи в Span
        private static void WriteVector4ToSpan(ILGenerator il, LocalBuilder vector, int offset, MethodInfo writeMethod)
        {
            il.Emit(OpCodes.Ldarg_2); // output span
            il.Emit(OpCodes.Ldc_I4, offset);
            il.Emit(OpCodes.Ldloc, vector);
            il.Emit(OpCodes.Call, writeMethod);
        }

        private static void WriteVector3ToSpan(ILGenerator il, LocalBuilder vector, int offset, MethodInfo writeMethod)
        {
            il.Emit(OpCodes.Ldarg_2); // output span
            il.Emit(OpCodes.Ldc_I4, offset);
            il.Emit(OpCodes.Ldloc, vector);
            il.Emit(OpCodes.Call, writeMethod);
        }

        private static void WriteVector3ToSpan(ILGenerator il, LocalBuilder vector, int offset,
            MethodInfo convertMethod, MethodInfo writeMethod)
        {
            il.Emit(OpCodes.Ldarg_2); // output span
            il.Emit(OpCodes.Ldc_I4, offset);
            il.Emit(OpCodes.Ldloc, vector);
            il.Emit(OpCodes.Call, convertMethod); // Конвертируем Vector4 в Vector3
            il.Emit(OpCodes.Call, writeMethod);
        }

        private static void WriteVector2ToSpan(ILGenerator il, LocalBuilder vector, int offset, MethodInfo writeMethod)
        {
            il.Emit(OpCodes.Ldarg_2); // output span
            il.Emit(OpCodes.Ldc_I4, offset);
            il.Emit(OpCodes.Ldloc, vector);
            il.Emit(OpCodes.Call, writeMethod);
        }

        // Вспомогательные классы для хранения ссылок
        private class VertexShaderMethodReferences
        {
            public MethodInfo CalculateInverseBoneTransforms { get; set; }
            public MethodInfo TransformPositionByBone { get; set; }
            public MethodInfo TransformNormalByBone { get; set; }
            public MethodInfo TransformTangentByBone { get; set; }
            public MethodInfo WriteVector4 { get; set; }
            public MethodInfo WriteVector3 { get; set; }
            public MethodInfo WriteVector2 { get; set; }
            public ConstructorInfo Vector4CtorVector3Float { get; set; }
            public MethodInfo Vector4Transform { get; set; }
            public MethodInfo Vector4AsVector3 { get; set; }
            public MethodInfo Vector3TransformNormal { get; set; }
        }

        private class VertexShaderFieldReferences
        {
            public FieldInfo PositionsArray { get; set; }
            public FieldInfo WorldTransformation { get; set; }
            public FieldInfo NormalTransformation { get; set; }
            public FieldInfo CurrentSkin { get; set; }
            public FieldInfo Vector4W { get; set; }
        }

        private class VertexShaderLocals
        {
            public LocalBuilder InitialPosition { get; set; }
            public LocalBuilder InitialNormal { get; set; }
            public LocalBuilder InitialTangent { get; set; }
            public LocalBuilder WorldPosition { get; set; }
            public LocalBuilder WorldNormal { get; set; }
            public LocalBuilder WorldTangent { get; set; }
            public LocalBuilder BoneMatrices { get; set; }
            public Dictionary<int, LocalBuilder> UVLocals { get; set; }
        }
        private static void GeneratePixelShaderStub(TypeBuilder typeBuilder)
        {
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(
                "PixelShader",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(Vector4),
                new Type[] { typeof(ReadOnlySpan<float>) });

            // Получаем базовый метод для переопределения
            var basePixelShader = typeof(ShaderBase).GetMethod("PixelShader");

            ILGenerator il = methodBuilder.GetILGenerator();
            // Заглушка - возвращаем черный цвет
            il.Emit(OpCodes.Ldloca_S, 0); // Создаем локальную переменную для Vector4
            il.Emit(OpCodes.Initobj, typeof(Vector4)); // Инициализируем как (0,0,0,0)
            il.Emit(OpCodes.Ldloc_0); // Загружаем значение
            il.Emit(OpCodes.Ret);

            typeBuilder.DefineMethodOverride(methodBuilder, basePixelShader);
        }
        private static MethodBuilder GeneratePixelShaderMethod(
            TypeBuilder typeBuilder,
            ShaderConfiguration config,
            Dictionary<string, FieldBuilder> attributeFields)
        {
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(
                "PixelShader",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(Vector4),
                new Type[] { typeof(ReadOnlySpan<float>) });

            ILGenerator il = methodBuilder.GetILGenerator();

            var methodRefs = CollectPixelShaderMethodReferences();
            var fieldRefs = CollectPixelShaderFieldReferences();
            var locals = DeclarePixelShaderLocalVariables(il, config);

            // Основной поток пиксельного шейдера
            ReadInputAttributes(il, config, methodRefs, locals);
            LoadBaseMaterialParameters(il, methodRefs, fieldRefs, locals);
            ProcessDiffuseTexture(il, config, methodRefs, fieldRefs, locals);
            //if (!PerformAlphaTest(il, locals))
            //{
                    ProcessTextures(il, config, methodRefs, fieldRefs, locals);
                    CalculateLighting(il, methodRefs, fieldRefs, locals);
                    AssembleFinalColor(il, methodRefs, locals);
            //}
            //ReturnTransparentBlack(il);
            il.Emit(OpCodes.Ret);
            return methodBuilder;
        }

        // === ЧТЕНИЕ ВХОДНЫХ ДАННЫХ ===

        private static void ReadInputAttributes(
            ILGenerator il,
            ShaderConfiguration config,
            PixelShaderMethodReferences methodRefs,
            PixelShaderLocals locals)
        {
            VertexAttributeOffsets offsets = config.Offsets;

            ReadWorldPosition(il, offsets, methodRefs, locals);
            ReadNormal(il, offsets, methodRefs, locals);

            if (config.Features.HasFlag(ShaderFeatures.NormalMap))
            {
                ReadTangent(il, offsets, methodRefs, locals);
            }

            ReadUVCoordinates(il, config, offsets, methodRefs, locals);
        }

        private static void ReadWorldPosition(
            ILGenerator il,
            VertexAttributeOffsets offsets,
            PixelShaderMethodReferences methodRefs,
            PixelShaderLocals locals)
        {
            ReadVector3FromSpan(il, locals.WorldPosition, offsets.WorldPosition, methodRefs.ReadVector3);
        }

        private static void ReadNormal(
            ILGenerator il,
            VertexAttributeOffsets offsets,
            PixelShaderMethodReferences methodRefs,
            PixelShaderLocals locals)
        {
            ReadVector3FromSpan(il, locals.Normal, offsets.Normal, methodRefs.ReadVector3);
            il.Emit(OpCodes.Ldloc, locals.Normal);
            il.Emit(OpCodes.Call, methodRefs.Vector3Normalize);
            il.Emit(OpCodes.Stloc, locals.Normal);
        }

        private static void ReadTangent(
            ILGenerator il,
            VertexAttributeOffsets offsets,
            PixelShaderMethodReferences methodRefs,
            PixelShaderLocals locals)
        {
            ReadVector4FromSpan(il, locals.Tangent, offsets.Tangent.Value, methodRefs.ReadVector4);
        }

        private static void ReadUVCoordinates(
            ILGenerator il,
            ShaderConfiguration config,
            VertexAttributeOffsets offsets,
            PixelShaderMethodReferences methodRefs,
            PixelShaderLocals locals)
        {
            foreach (var uvIndex in config.TextureBindings.GetUsedUVs())
            {
                if (offsets.UVOffsets.TryGetValue(uvIndex, out int uvOffset))
                {
                    ReadVector2FromSpan(il, locals.UVLocals[uvIndex], uvOffset, methodRefs.ReadVector2);
                }
            }
        }

        // === ЗАГРУЗКА ПАРАМЕТРОВ МАТЕРИАЛА ===

        private static void LoadBaseMaterialParameters(
            ILGenerator il,
            PixelShaderMethodReferences methodRefs,
            PixelShaderFieldReferences fieldRefs,
            PixelShaderLocals locals)
        {
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldflda, fieldRefs.CurrentMaterial);
            il.Emit(OpCodes.Ldloca, locals.DiffuseColorFull);
            il.Emit(OpCodes.Ldloca, locals.Emissive);
            il.Emit(OpCodes.Ldloca, locals.Metallic);
            il.Emit(OpCodes.Ldloca, locals.Roughness);
            il.Emit(OpCodes.Call, methodRefs.GetBasePbrParameters);
        }

        // === АЛЬФА-ТЕСТ ===

        private static bool PerformAlphaTest(ILGenerator il, PixelShaderLocals locals)
        {
            Label skipPixelLabel = il.DefineLabel();
            Label returnTransparentLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldloca, locals.DiffuseColorFull);
            il.Emit(OpCodes.Ldfld, typeof(Vector4).GetField("W"));
            il.Emit(OpCodes.Ldc_R4, 0.0001f);
            il.Emit(OpCodes.Bge_Un, skipPixelLabel);

            // Возвращаем прозрачный черный цвет
            ReturnTransparentBlack(il);
            il.MarkLabel(returnTransparentLabel);

            il.MarkLabel(skipPixelLabel);
            return false;
        }

        private static void ReturnTransparentBlack(ILGenerator il)
        {
            il.Emit(OpCodes.Ldloca_S, 0);
            il.Emit(OpCodes.Initobj, typeof(Vector4));
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ret);
        }

        // === ОБРАБОТКА ТЕКСТУР ===

        private static void ProcessTextures(
            ILGenerator il,
            ShaderConfiguration config,
            PixelShaderMethodReferences methodRefs,
            PixelShaderFieldReferences fieldRefs,
            PixelShaderLocals locals)
        {
            ProcessNormalTexture(il, config, methodRefs, fieldRefs, locals);
            ProcessMetallicRoughnessTexture(il, config, methodRefs, fieldRefs, locals);
            ProcessEmissiveTexture(il, config, methodRefs, fieldRefs, locals);
        }

        private static void ProcessDiffuseTexture(
            ILGenerator il,
            ShaderConfiguration config,
            PixelShaderMethodReferences methodRefs,
            PixelShaderFieldReferences fieldRefs,
            PixelShaderLocals locals)
        {
            if (!config.Features.HasFlag(ShaderFeatures.BaseColorTexture)) return;

            var uvIndex = config.TextureBindings.BaseColorTexCoord.Value;
            il.Emit(OpCodes.Ldloca, locals.DiffuseColorFull);
            il.Emit(OpCodes.Ldloca, locals.UVLocals[uvIndex]);
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, fieldRefs.CurrentMaterial);
            il.Emit(OpCodes.Call, methodRefs.GetBaseColorTextureSampler);
            il.Emit(OpCodes.Call, methodRefs.CalculateTextureDiffuseColor);

        }

        private static void ProcessNormalTexture(
            ILGenerator il,
            ShaderConfiguration config,
            PixelShaderMethodReferences methodRefs,
            PixelShaderFieldReferences fieldRefs,
            PixelShaderLocals locals)
        {
            if (!config.Features.HasFlag(ShaderFeatures.NormalMap)) return;

            var uvIndex = config.TextureBindings.NormalTexCoord.Value;
            il.Emit(OpCodes.Ldloca, locals.Normal);
            il.Emit(OpCodes.Ldloca, locals.Tangent);
            il.Emit(OpCodes.Ldloca, locals.UVLocals[uvIndex]);
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, fieldRefs.CurrentMaterial);
            il.Emit(OpCodes.Call, methodRefs.GetNormalTextureSampler);
            il.Emit(OpCodes.Call, methodRefs.CalculateTextureNormal);
        }

        private static void ProcessMetallicRoughnessTexture(
            ILGenerator il,
            ShaderConfiguration config,
            PixelShaderMethodReferences methodRefs,
            PixelShaderFieldReferences fieldRefs,
            PixelShaderLocals locals)
        {
            if (!config.Features.HasFlag(ShaderFeatures.MetallicRoughnessTexture)) return;

            var uvIndex = config.TextureBindings.MetallicRoughnessTexCoord.Value;
            il.Emit(OpCodes.Ldloca, locals.Roughness);
            il.Emit(OpCodes.Ldloca, locals.Metallic);
            il.Emit(OpCodes.Ldloca, locals.UVLocals[uvIndex]);
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, fieldRefs.CurrentMaterial);
            il.Emit(OpCodes.Call, methodRefs.GetMetallicRoughnessTextureSampler);
            il.Emit(OpCodes.Call, methodRefs.CalculateTextureMetallicRoughness);
        }

        private static void ProcessEmissiveTexture(
            ILGenerator il,
            ShaderConfiguration config,
            PixelShaderMethodReferences methodRefs,
            PixelShaderFieldReferences fieldRefs,
            PixelShaderLocals locals)
        {
            if (!config.Features.HasFlag(ShaderFeatures.EmissiveTexture)) return;

            var uvIndex = config.TextureBindings.EmissiveTexCoord.Value;
            il.Emit(OpCodes.Ldloca, locals.Emissive);
            il.Emit(OpCodes.Ldloca, locals.UVLocals[uvIndex]);
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, fieldRefs.CurrentMaterial);
            il.Emit(OpCodes.Call, methodRefs.GetEmissiveTextureSampler);
            il.Emit(OpCodes.Call, methodRefs.CalculateTextureEmissive);
        }

        // === РАСЧЕТ ОСВЕЩЕНИЯ ===

        private static void CalculateLighting(
            ILGenerator il,
            PixelShaderMethodReferences methodRefs,
            PixelShaderFieldReferences fieldRefs,
            PixelShaderLocals locals)
        {
            CalculateViewDirection(il, methodRefs, fieldRefs, locals);
            InitializeFinalColor(il, locals);
            CalculateAmbientLighting(il, methodRefs, fieldRefs, locals);
            CalculatePbrLighting(il, methodRefs, fieldRefs, locals);
            AddEmissiveContribution(il, locals);
        }

        private static void CalculateViewDirection(
            ILGenerator il,
            PixelShaderMethodReferences methodRefs,
            PixelShaderFieldReferences fieldRefs,
            PixelShaderLocals locals)
        {
            il.Emit(OpCodes.Ldloca, locals.ViewDir);
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldflda, fieldRefs.CameraPosition);
            il.Emit(OpCodes.Ldloca, locals.WorldPosition);
            il.Emit(OpCodes.Call, methodRefs.CalculateViewDir);
        }

        private static void InitializeFinalColor(ILGenerator il, PixelShaderLocals locals)
        {
            il.Emit(OpCodes.Ldloca, locals.FinalColor);
            il.Emit(OpCodes.Initobj, typeof(Vector3));
        }

        private static void CalculateAmbientLighting(
            ILGenerator il,
            PixelShaderMethodReferences methodRefs,
            PixelShaderFieldReferences fieldRefs,
            PixelShaderLocals locals)
        {
            il.Emit(OpCodes.Ldloca, locals.FinalColor);
            il.Emit(OpCodes.Ldloca, locals.DiffuseColorFull);
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldflda, fieldRefs.AmbientLightColor);
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldflda, fieldRefs.AmbientLightIntensity);
            il.Emit(OpCodes.Call, methodRefs.CalculateAmbient);
        }

        private static void CalculatePbrLighting(
            ILGenerator il,
            PixelShaderMethodReferences methodRefs,
            PixelShaderFieldReferences fieldRefs,
            PixelShaderLocals locals)
        {
            il.Emit(OpCodes.Ldloca, locals.FinalColor);
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldflda, fieldRefs.LightSources);
            il.Emit(OpCodes.Ldloca, locals.DiffuseColorFull);
            il.Emit(OpCodes.Ldloca, locals.WorldPosition);
            il.Emit(OpCodes.Ldloca, locals.Normal);
            il.Emit(OpCodes.Ldloca, locals.ViewDir);
            il.Emit(OpCodes.Ldloca, locals.Metallic);
            il.Emit(OpCodes.Ldloca, locals.Roughness);
            il.Emit(OpCodes.Call, methodRefs.CalculatePbr);
        }

        private static void AddEmissiveContribution(ILGenerator il, PixelShaderLocals locals)
        {
            il.Emit(OpCodes.Ldloc, locals.FinalColor);
            il.Emit(OpCodes.Ldloc, locals.Emissive);
            il.Emit(OpCodes.Call, typeof(Vector3).GetMethod("op_Addition"));
            il.Emit(OpCodes.Stloc, locals.FinalColor);
        }

        // === ФОРМИРОВАНИЕ ФИНАЛЬНОГО ЦВЕТА ===

        private static void AssembleFinalColor(
            ILGenerator il,
            PixelShaderMethodReferences methodRefs,
            PixelShaderLocals locals)
        {
            il.Emit(OpCodes.Ldloc, locals.FinalColor);
            il.Emit(OpCodes.Ldloc, locals.DiffuseColorFull);
            il.Emit(OpCodes.Ldfld, typeof(Vector4).GetField("W"));
            il.Emit(OpCodes.Newobj, typeof(Vector4).GetConstructor(new[] { typeof(Vector3), typeof(float) }));
        }

        // === ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ЧТЕНИЯ ===

        private static void ReadVector4FromSpan(ILGenerator il, LocalBuilder local, int offset, MethodInfo readMethod)
        {
            il.Emit(OpCodes.Ldarg_1); // input span
            il.Emit(OpCodes.Ldc_I4, offset);
            il.Emit(OpCodes.Call, readMethod);
            il.Emit(OpCodes.Stloc, local);
        }

        private static void ReadVector3FromSpan(ILGenerator il, LocalBuilder local, int offset, MethodInfo readMethod)
        {
            il.Emit(OpCodes.Ldarg_1); // input span
            il.Emit(OpCodes.Ldc_I4, offset);
            il.Emit(OpCodes.Call, readMethod);
            il.Emit(OpCodes.Stloc, local);
        }

        private static void ReadVector2FromSpan(ILGenerator il, LocalBuilder local, int offset, MethodInfo readMethod)
        {
            il.Emit(OpCodes.Ldarg_1); // input span
            il.Emit(OpCodes.Ldc_I4, offset);
            il.Emit(OpCodes.Call, readMethod);
            il.Emit(OpCodes.Stloc, local);
        }

        // Сбор всех используемых MethodInfo для пиксельного шейдера
        private static PixelShaderMethodReferences CollectPixelShaderMethodReferences()
        {
            return new PixelShaderMethodReferences
            {
                // Методы ShaderComponents для PBR
                CalculateTextureNormal = typeof(ShaderComponents).GetMethod(
                    "CalculateTextureNormal", BindingFlags.Public | BindingFlags.Static),
                CalculateTextureMetallicRoughness = typeof(ShaderComponents).GetMethod(
                    "CalculateTextureMetallicRoughness", BindingFlags.Public | BindingFlags.Static),
                CalculateTextureDiffuseColor = typeof(ShaderComponents).GetMethod(
                    "CalculateTextureDiffuseColor", BindingFlags.Public | BindingFlags.Static),
                CalculateTextureEmissive = typeof(ShaderComponents).GetMethod(
                    "CalculateTextureEmissive", BindingFlags.Public | BindingFlags.Static),
                CalculateAmbient = typeof(ShaderComponents).GetMethod(
                    "CalculateAmbient", BindingFlags.Public | BindingFlags.Static),
                CalculateViewDir = typeof(ShaderComponents).GetMethod(
                    "CalculateViewDir", BindingFlags.Public | BindingFlags.Static),
                GetBasePbrParameters = typeof(ShaderComponents).GetMethod(
                    "GetBasePbrParameters", BindingFlags.Public | BindingFlags.Static),
                CalculatePbr = typeof(ShaderComponents).GetMethod(
                    "CalculatePbr", BindingFlags.Public | BindingFlags.Static),

                // Методы чтения из Span
                ReadVector4 = typeof(ShaderComponents).GetMethod("ReadVector4"),
                ReadVector3 = typeof(ShaderComponents).GetMethod("ReadVector3"),
                ReadVector2 = typeof(ShaderComponents).GetMethod("ReadVector2"),

                // Методы Vector
                Vector4AsVector3 = typeof(Vector).GetMethod("AsVector3", [typeof(Vector4)]),
                Vector3Normalize = typeof(Vector3).GetMethod("Normalize", new[] { typeof(Vector3) }),
                Vector3Dot = typeof(Vector3).GetMethod("Dot", new[] { typeof(Vector3), typeof(Vector3) }),
                Vector3Cross = typeof(Vector3).GetMethod("Cross", new[] { typeof(Vector3), typeof(Vector3) }),

                // Методы MaterialV2 для получения сэмплеров
                GetBaseColorTextureSampler = typeof(MaterialV2).GetProperty("BaseColorTexture")?.GetGetMethod(),
                GetNormalTextureSampler = typeof(MaterialV2).GetProperty("NormalTexture")?.GetGetMethod(),
                GetMetallicRoughnessTextureSampler = typeof(MaterialV2).GetProperty("MetallicRoughnessTexture")?
                    .GetGetMethod(),
                GetEmissiveTextureSampler = typeof(MaterialV2).GetProperty("EmissiveTexture")?.GetGetMethod(),
                GetOcclusionTextureSampler = typeof(MaterialV2).GetProperty("OcclusionTexture")?.GetGetMethod()
            };
        }

        // Сбор всех используемых FieldInfo для пиксельного шейдера
        private static PixelShaderFieldReferences CollectPixelShaderFieldReferences()
        {
            return new PixelShaderFieldReferences
            {
                // Поля ShaderBase
                CurrentMaterial = typeof(ShaderBase).GetField("currentMaterial", BindingFlags.NonPublic | BindingFlags.Instance),
                CameraPosition = typeof(ShaderBase).GetField("cameraPosition", BindingFlags.NonPublic | BindingFlags.Instance),
                AmbientLightColor = typeof(ShaderBase).GetField("ambientLightColor", BindingFlags.NonPublic | BindingFlags.Instance),
                AmbientLightIntensity = typeof(ShaderBase).GetField("ambientLightIntensity", BindingFlags.NonPublic | BindingFlags.Instance),
                LightSources = typeof(ShaderBase).GetField("lightSources", BindingFlags.NonPublic | BindingFlags.Instance)
            };
        }

        // Объявление локальных переменных для пиксельного шейдера
        private static PixelShaderLocals DeclarePixelShaderLocalVariables(ILGenerator il, ShaderConfiguration config)
        {
            var locals = new PixelShaderLocals
            {
                DiffuseColorFull = il.DeclareLocal(typeof(Vector4)),
                Normal = il.DeclareLocal(typeof(Vector3)),
                WorldPosition = il.DeclareLocal(typeof(Vector3)),
                Tangent = config.Features.HasFlag(ShaderFeatures.NormalMap) ?
                    il.DeclareLocal(typeof(Vector4)) : null,
                Metallic = il.DeclareLocal(typeof(float)),
                Roughness = il.DeclareLocal(typeof(float)),
                Emissive = il.DeclareLocal(typeof(Vector3)),
                ViewDir = il.DeclareLocal(typeof(Vector3)),
                FinalColor = il.DeclareLocal(typeof(Vector3))
            };

            // UV координаты
            locals.UVLocals = new Dictionary<int, LocalBuilder>();
            foreach (var uvIndex in config.TextureBindings.GetUsedUVs())
            {
                locals.UVLocals[uvIndex] = il.DeclareLocal(typeof(Vector2));
            }

            return locals;
        }

        // Вспомогательные классы для хранения ссылок пиксельного шейдера
        private class PixelShaderMethodReferences
        {
            public MethodInfo CalculateTextureNormal { get; set; }
            public MethodInfo CalculateTextureMetallicRoughness { get; set; }
            public MethodInfo CalculateTextureDiffuseColor { get; set; }
            public MethodInfo CalculateTextureEmissive { get; set; }
            public MethodInfo CalculateAmbient { get; set; }
            public MethodInfo CalculateViewDir { get; set; }
            public MethodInfo GetBasePbrParameters { get; set; }
            public MethodInfo CalculatePbr { get; set; }
            public MethodInfo ReadVector4 { get; set; }
            public MethodInfo ReadVector3 { get; set; }
            public MethodInfo ReadVector2 { get; set; }
            public MethodInfo Vector4AsVector3 { get; set; }
            public MethodInfo Vector3Normalize { get; set; }
            public MethodInfo Vector3Dot { get; set; }
            public MethodInfo Vector3Cross { get; set; }
            public MethodInfo GetBaseColorTextureSampler { get; set; }
            public MethodInfo GetNormalTextureSampler { get; set; }
            public MethodInfo GetMetallicRoughnessTextureSampler { get; set; }
            public MethodInfo GetEmissiveTextureSampler { get; set; }
            public MethodInfo GetOcclusionTextureSampler { get; set; }
        }

        private class PixelShaderFieldReferences
        {
            public FieldInfo CurrentMaterial { get; set; }
            public FieldInfo CameraPosition { get; set; }
            public FieldInfo AmbientLightColor { get; set; }
            public FieldInfo AmbientLightIntensity { get; set; }
            public FieldInfo LightSources { get; set; }
        }

        private class PixelShaderLocals
        {
            public LocalBuilder DiffuseColorFull { get; set; }
            public LocalBuilder Normal { get; set; }
            public LocalBuilder WorldPosition { get; set; }
            public LocalBuilder Tangent { get; set; }
            public LocalBuilder Metallic { get; set; }
            public LocalBuilder Roughness { get; set; }
            public LocalBuilder Emissive { get; set; }
            public LocalBuilder ViewDir { get; set; }
            public LocalBuilder FinalColor { get; set; }
            public Dictionary<int, LocalBuilder> UVLocals { get; set; }
        }
    }
}
