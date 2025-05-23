using GraphicsLib.Types;
using System.Numerics;

namespace GraphicsLib.Types2
{
    public abstract class LightSource
    {
        private float shadowMapSize = 1024;

        public required Vector3 Color { get; set; }
        public required float Intensity { get; set; }
        public float Bias { get; set; } = 0.04f;
        public required float ShadowMapSize { get => shadowMapSize; set { shadowMapSize = value; UpdateShadowMaps(); } }
        public abstract float GetShadowCover(in Vector3 position);
        protected abstract void UpdateShadowMaps();
        public abstract void CalculateLightDirAndIntensity(in Vector3 position, out Vector3 lightDir, out float intensity);
    }
    public class PointLightSource : LightSource
    {
        private Vector3 position;
        public required Vector3 Position { get => position; set { position = value; UpdateShadowMaps(); } }
        private const int ShadowMapCount = 6;
        private const int ShadowPosX = 0;
        private const int ShadowNegX = 1;
        private const int ShadowPosY = 2;
        private const int ShadowNegY = 3;
        private const int ShadowPosZ = 4;
        private const int ShadowNegZ = 5;

        private static readonly Dictionary<int, (float Azimuth, float Polar, Vector3 TargetOffset)> shadowDirections = new Dictionary<int, (float Azimuth, float Polar, Vector3 TargetOffset)>
        {
            { ShadowNegX, (MathF.PI / 2, MathF.PI / 2, new Vector3(-1, 0, 0)) },
            { ShadowPosX, (3 * MathF.PI / 2, MathF.PI / 2, new Vector3(1, 0, 0)) },
            { ShadowNegY, (0, 0, new Vector3(0, -1, 0)) },
            { ShadowPosY, (0, MathF.PI, new Vector3(0, 1, 0)) },
            { ShadowNegZ, (0, MathF.PI / 2, new Vector3(0, 0, -1)) },
            { ShadowPosZ, (MathF.PI, MathF.PI / 2, new Vector3(0, 0, 1)) }
        };
        public ShadowMap[] ShadowMaps { get; private set; }
        public Camera[] ShadowMapViewports { get; private set; }
        public override float GetShadowCover(in Vector3 position)
        {
            Vector3 inputToLight = position - Position;
            Vector3 absInputToLight = Vector3.Abs(inputToLight);
            float maxComponent = float.Max(absInputToLight.X, float.Max(absInputToLight.Y, absInputToLight.Z));
            Vector3 projection = inputToLight / maxComponent;
            Vector2 uv;
            float depth = maxComponent;
            int bufferIndex;
#pragma warning disable S1244 // Floating point numbers should not be tested for equality
            if (maxComponent == absInputToLight.X && inputToLight.X > 0)
            {
                uv = new Vector2(projection.Z, -projection.Y) * 0.5f + new Vector2(0.5f);
                bufferIndex = ShadowPosX;
            }
            else if (maxComponent == absInputToLight.X && inputToLight.X <= 0)
            {
                uv = new Vector2(-projection.Z, -projection.Y) * 0.5f + new Vector2(0.5f);
                bufferIndex = ShadowNegX;
            }
            else if (maxComponent == absInputToLight.Y && inputToLight.Y > 0)
            {
                uv = new Vector2(projection.X, -projection.Z) * 0.5f + new Vector2(0.5f);
                bufferIndex = ShadowPosY;
            }
            else if (maxComponent == absInputToLight.Y && inputToLight.Y <= 0)
            {
                uv = new Vector2(projection.X, projection.Z) * 0.5f + new Vector2(0.5f);
                bufferIndex = ShadowNegY;
            }
            else if (maxComponent == absInputToLight.Z && inputToLight.Z > 0)
            {
                uv = new Vector2(-projection.X, -projection.Y) * 0.5f + new Vector2(0.5f);
                bufferIndex = ShadowPosZ;
            }
            else if (maxComponent == absInputToLight.Z && inputToLight.Z <= 0)
            {
                uv = new Vector2(projection.X, -projection.Y) * 0.5f + new Vector2(0.5f);
                bufferIndex = ShadowNegZ;
            }
            else
            {
                return 0f;
            }
#pragma warning restore S1244 // Floating point numbers should not be tested for equality
            uv = Vector2.Clamp(uv, Vector2.Zero, new Vector2(1));
            int centerX = (int)(uv.X * (int)(ShadowMapSize - 1));
            int centerY = (int)(uv.Y * (int)(ShadowMapSize - 1));
            float shadow = 0;
            //{
            //    float sampledDepth = 1f / ShadowMaps[bufferIndex][centerX, centerY];
            //    shadow += (depth - Bias) > -sampledDepth ? 1.0f : 0.0f;
            //    return shadow;
            //}
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = int.Clamp(centerX + dx, 0, (int)(ShadowMapSize - 1));
                    int y = int.Clamp(centerY + dy, 0, (int)(ShadowMapSize - 1));
                    float sampledDepth = 1f / ShadowMaps[bufferIndex][x, y];
                    shadow += (depth - Bias) > -sampledDepth ? 1.0f : 0.0f;
                }
            }
            return shadow / 9;
        }
        public override void CalculateLightDirAndIntensity(in Vector3 position, out Vector3 lightDir, out float intensity)
        {
            lightDir = Vector3.Normalize(position - Position);
            intensity = Intensity;
        }

        protected override void UpdateShadowMaps()
        {
            ShadowMaps = new ShadowMap[ShadowMapCount];
            ShadowMapViewports = new Camera[ShadowMapCount];
            for (int i = 0; i < ShadowMapCount; i++)
            {
                ShadowMaps[i] = new ShadowMap((int)ShadowMapSize);
            }
            foreach (var viewportParams in shadowDirections)
            {
                ShadowMapViewports[viewportParams.Key] = new Camera(viewportParams.Value.Azimuth, viewportParams.Value.Polar, 1f, Position + viewportParams.Value.TargetOffset)
                {
                    ScreenHeight = ShadowMapSize,
                    ScreenWidth = ShadowMapSize,
                    FieldOfView = MathF.PI / 2,
                    FarClipPlane = 1000f,
                };
            }
        }
    }
    public class AmbientLightSource : LightSource
    {
        public override void CalculateLightDirAndIntensity(in Vector3 position, out Vector3 lightDir, out float intensity)
        {
            lightDir = Vector3.Zero;
            intensity = Intensity;
        }

        public override float GetShadowCover(in Vector3 position)
        {
            throw new NotImplementedException();
        }

        protected override void UpdateShadowMaps()
        {
            throw new NotImplementedException();
        }
    }
    public class DirectionalLightSource : LightSource
    {
        private Vector3 direction;
        public required Vector3 Direction { get => direction; set { direction = Vector3.Normalize(value); UpdateShadowMaps(); } }
        public required float CoverSize { get; set; } = 1024;
        public ShadowMap ShadowMap { get; private set; }
        public Camera ShadowViewport { get; private set; }
        private Matrix4x4 WorldToProjection { get; set; }
        public override void CalculateLightDirAndIntensity(in Vector3 position, out Vector3 lightDir, out float intensity)
        {
            lightDir = Direction;
            intensity = Intensity;
        }

        public override float GetShadowCover(in Vector3 position)
        {
            Vector4 projection = Vector4.Transform(new Vector4(position, 1f), WorldToProjection);
            Vector2 uv = new Vector2(projection.X, -projection.Y) * 0.5f + new Vector2(0.5f);
            uv = Vector2.Clamp(uv, Vector2.Zero, new Vector2(1));
            int centerX = (int)(uv.X * (int)(ShadowMapSize - 1));
            int centerY = (int)(uv.Y * (int)(ShadowMapSize - 1));
            float depth = projection.Z;
            float shadow = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = int.Clamp(centerX + dx, 0, (int)(ShadowMapSize - 1));
                    int y = int.Clamp(centerY + dy, 0, (int)(ShadowMapSize - 1));
                    float sampledDepth = 1f / ShadowMap[x, y];
                    shadow += (depth - Bias) > -sampledDepth ? 1.0f : 0.0f;
                }
            }
            return shadow / 9;
        }

        protected override void UpdateShadowMaps()
        {
            ShadowMap = new ShadowMap((int)ShadowMapSize);
            float distanceXY = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
            float polar = MathF.PI - MathF.Atan2(distanceXY, direction.Y);
            float azimuth = MathF.Atan2(-direction.X, -direction.Z);
            ShadowViewport = new Camera(azimuth, polar, 1f, Vector3.Zero)
            {
                ScreenHeight = ShadowMapSize,
                ScreenWidth = ShadowMapSize,
                FarClipPlane = 10000f,
                IsPerspectiveCamera = false,
                OrthographicHeight = CoverSize,
                OrthographicWidth = CoverSize,
            };
            WorldToProjection = ShadowViewport.ViewMatrix * ShadowViewport.ProjectionMatrix;
        }
    }
    public class SpotLightSource : LightSource
    {
        private Vector3 position;
        private Vector3 direction;

        public required Vector3 Position { get => position; set { position = value; UpdateShadowMaps(); } }
        public required Vector3 Direction { get => direction; set { direction = Vector3.Normalize(value); UpdateShadowMaps();} }
        public required float CutOffCos { get; set; }
        public required float OuterCutCos { get; set; }
        public ShadowMap ShadowMap { get; private set; }
        public Camera ShadowViewport { get; private set; }
        private Matrix4x4 WorldToProjection { get; set; }

        public override void CalculateLightDirAndIntensity(in Vector3 position, out Vector3 lightDir, out float intensity)
        {
            lightDir = Vector3.Normalize(position - Position);
            float theta = Vector3.Dot(Direction, lightDir);
            if(OuterCutCos < CutOffCos)
            {
                intensity = float.Clamp(float.Lerp(Intensity, 0, (theta - CutOffCos) / (OuterCutCos - CutOffCos)), 0, Intensity);
            }
            else
            {
                intensity = theta < OuterCutCos ? 0 : Intensity;
            }
            
        }

        public override float GetShadowCover(in Vector3 position)
        {
            Vector4 projection = Vector4.Transform(new Vector4(position, 1f), WorldToProjection);
            Vector2 uv = new Vector2(projection.X / projection.W, -projection.Y / projection.W) * 0.5f + new Vector2(0.5f);
            uv = Vector2.Clamp(uv, Vector2.Zero, new Vector2(1));
            int centerX = (int)(uv.X * (int)(ShadowMapSize - 1));
            int centerY = (int)(uv.Y * (int)(ShadowMapSize - 1));
            float depth = projection.W;
            float shadow = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = int.Clamp(centerX + dx, 0, (int)(ShadowMapSize - 1));
                    int y = int.Clamp(centerY + dy, 0, (int)(ShadowMapSize - 1));
                    float sampledDepth = 1f / ShadowMap[x, y];
                    shadow += (depth - Bias) > -sampledDepth ? 1.0f : 0.0f;
                }
            }
            return shadow / 9;
        }

        protected override void UpdateShadowMaps()
        {
            ShadowMap = new ShadowMap((int)ShadowMapSize);
            float distanceXY = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
            float polar = MathF.PI - MathF.Atan2(distanceXY, direction.Y);
            float azimuth = MathF.Atan2(-direction.X, -direction.Z);
            ShadowViewport = new Camera(azimuth, polar, 1f, Position + Direction)
            {
                ScreenHeight = ShadowMapSize,
                ScreenWidth = ShadowMapSize,
                FieldOfView = MathF.Acos(OuterCutCos),
                FarClipPlane = 10000f,
            };
            WorldToProjection = ShadowViewport.ViewMatrix * ShadowViewport.ProjectionMatrix;
        }
    }
}
