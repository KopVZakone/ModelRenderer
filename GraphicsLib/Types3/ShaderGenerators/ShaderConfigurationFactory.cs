using GraphicsLib.Types2;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public static class ShaderConfigurationFactory
    {
        public static ShaderConfiguration Create(
            ShaderFeatures desiredFeatures,
            MaterialV2 material,
            bool hasSkin)
        {
            // Получаем фичи, которые материал поддерживает из желаемых
            var (providedFeatures, textureBindings) = material.GetShaderFeatures(desiredFeatures);

            // Получаем используемые UV из привязок текстур
            var usedUVs = textureBindings.GetUsedUVs();

            // Определяем размер данных
            int interpolatedSize = GetInterpolatedDataSize(providedFeatures, usedUVs);

            // Создаем смещения
            var offsets = CalculateOffsets(providedFeatures, usedUVs);

            return new ShaderConfiguration(providedFeatures, interpolatedSize, hasSkin, offsets, textureBindings);
        }
        private static int GetInterpolatedDataSize(ShaderFeatures features, HashSet<int> usedUVs)
        {
            int size = 0;
            size += 4; // Position
            size += 3; // WorldPosition
            size += 3; // Normal

            if (features.HasFlag(ShaderFeatures.NormalMap))
            {
                size += 4; // Tangent
            }

            size += 2 * usedUVs.Count; // UV координаты

            return size;
        }

        private static VertexAttributeOffsets CalculateOffsets(ShaderFeatures features, HashSet<int> usedUVs)
        {
            int currentOffset = 0;

            // Фиксированный порядок атрибутов
            int positionOffset = currentOffset; // 0
            currentOffset += 4; // Position

            int worldPositionOffset = currentOffset;
            currentOffset += 3;

            int normalOffset = currentOffset;
            currentOffset += 3;

            int? tangentOffset = null;
            if (features.HasFlag(ShaderFeatures.NormalMap))
            {
                tangentOffset = currentOffset;
                currentOffset += 4;
            }

            // UV offsets (сортируем для детерминированности)
            var uvOffsets = new Dictionary<int, int>();
            var sortedUVs = usedUVs.OrderBy(x => x).ToList();
            foreach (int uvIndex in sortedUVs)
            {
                uvOffsets[uvIndex] = currentOffset;
                currentOffset += 2;
            }

            return new VertexAttributeOffsets(
                positionOffset, worldPositionOffset, normalOffset,
                tangentOffset, uvOffsets);
        }
    }

}
