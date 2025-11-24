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
            GeneratePixelShaderStub(typeBuilder);

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
    }
}
