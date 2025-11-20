using GraphicsLib.Types.GltfTypes;
using SixLabors.ImageSharp.PixelFormats;
using System.Configuration;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
using System.Runtime.CompilerServices;

namespace GraphicsLib.Types
{
    public class Sampler
    {
        private int width;
        private int height;
        private Rgba32[]? textureData;
        private readonly WrappingMode uWrappingMode;
        private readonly WrappingMode vWrappingMode;
        private readonly MagnificationFilterMode magnificationFilterMode;
        private readonly MinificationFilterMode minificationFilterMode;

        public Sampler(WrappingMode uWrappingMode = WrappingMode.ClampToEdge,
            WrappingMode vWrappingMode = WrappingMode.ClampToEdge,
            MagnificationFilterMode magnificationFilterMode = MagnificationFilterMode.Nearest,
            MinificationFilterMode minificationFilterMode = MinificationFilterMode.Nearest)
        {
            this.uWrappingMode = uWrappingMode;
            this.vWrappingMode = vWrappingMode;
            this.magnificationFilterMode = magnificationFilterMode;
            this.minificationFilterMode = minificationFilterMode;
        }
        public void BindTexture(Rgba32[] textureData, int width, int height)
        {
            this.textureData = textureData;
            this.width = width;
            this.height = height;
        }
        public void BindTexture(GltfImage gltfImage)
        {
            BindTexture(gltfImage.ImageData, gltfImage.Width, gltfImage.Height);
        }

        public Vector4 SampleNearest(Vector2 uv)
        {
            if (textureData == null)
            {
                throw new ConfigurationErrorsException("Texture data is not bound to sampler");
            }
            switch (uWrappingMode)
            {
                case WrappingMode.ClampToEdge:
                    uv.X = float.Clamp(uv.X, 0, 1);
                    break;
                case WrappingMode.Repeat:
                    uv.X = uv.X - MathF.Floor(uv.X);
                    break;
                case WrappingMode.MirroredRepeat:
                    uv.X = uv.X - uv.X % 2;
                    if (uv.X > 1)
                        uv.X = 2 - uv.X;
                    break;
            }
            switch (vWrappingMode)
            {
                case WrappingMode.ClampToEdge:
                    uv.Y = float.Clamp(uv.Y, 0, 1);
                    break;
                case WrappingMode.Repeat:
                    uv.Y = uv.Y - MathF.Floor(uv.Y);
                    break;
                case WrappingMode.MirroredRepeat:
                    uv.Y = uv.Y - uv.Y % 2;
                    if (uv.Y > 1)
                        uv.Y = 2 - uv.Y;
                    break;
            }
            int x = (int)MathF.Round((uv.X * (width - 1)));
            int y = (int)MathF.Round((uv.Y * (height - 1)));
            return textureData[y * width + x].ToScaledVector4();
        }
        public Vector4 Sample(Vector2 uv)
        {
            if (textureData == null)
            {
                throw new ConfigurationErrorsException("Texture data is not bound to sampler");
            }
            switch (uWrappingMode)
            {
                case WrappingMode.ClampToEdge:
                    uv.X = float.Clamp(uv.X, 0, 1);
                    break;
                case WrappingMode.Repeat:
                    uv.X = uv.X - MathF.Floor(uv.X);
                    break;
                case WrappingMode.MirroredRepeat:
                    uv.X = uv.X - uv.X % 2;
                    if (uv.X > 1)
                        uv.X = 2 - uv.X;
                    break;
            }
            switch (vWrappingMode)
            {
                case WrappingMode.ClampToEdge:
                    uv.Y = float.Clamp(uv.Y, 0, 1);
                    break;
                case WrappingMode.Repeat:
                    uv.Y = uv.Y - MathF.Floor(uv.Y);
                    break;
                case WrappingMode.MirroredRepeat:
                    uv.Y = uv.Y - uv.Y % 2;
                    if (uv.Y > 1)
                        uv.Y = 2 - uv.Y;
                    break;
            }
            if (width == 1024 && height == 1024)
            {
                return Sample1024x1024(uv);
            }
            else if (width == 2048 && height == 2048)
            {
                return Sample2048x2048(uv);
            }
            else if(width == 4096 && height == 4096)
            {
                return Sample4096x4096(uv);
            }
            switch (magnificationFilterMode)
            {
                case MagnificationFilterMode.Nearest:
                    {
                        int x = (int)MathF.Round((uv.X * (width - 1)));
                        int y = (int)MathF.Round((uv.Y * (height - 1)));
                        return textureData[y * width + x].ToScaledVector4();
                    }
                case MagnificationFilterMode.Linear:
                    {
                        int x0 = (int)(uv.X * (width - 1));
                        int x1 = int.Clamp(x0 + 1, 0, width - 1);
                        int y0 = (int)(uv.Y * (height - 1));
                        int y1 = int.Clamp(y0 + 1, 0, width - 1);
                        float dx = uv.X * (width - 1) - x0;
                        float dy = uv.Y * (height - 1) - y0;
                        if (Sse41.IsSupported)
                        {
                            Vector4 topLeft = ToUnscaledSse41(textureData[y0 * width + x0]);
                            Vector4 topRight = ToUnscaledSse41(textureData[y0 * width + x1]);
                            Vector4 bottomLeft = ToUnscaledSse41(textureData[y1 * width + x0]);
                            Vector4 bottomRight = ToUnscaledSse41(textureData[y1 * width + x1]);
                            Vector4 result = Vector4.Lerp(Vector4.Lerp(topLeft, topRight, dx), Vector4.Lerp(bottomLeft, bottomRight, dx), dy);
                            result *= (1 / 255f);
                            return result;
                        }
                        else
                        {
                            Vector4 topLeft = ToUnscaledVector4(textureData[y0 * width + x0]);
                            Vector4 topRight = ToUnscaledVector4(textureData[y0 * width + x1]);
                            Vector4 bottomLeft = ToUnscaledVector4(textureData[y1 * width + x0]);
                            Vector4 bottomRight = ToUnscaledVector4(textureData[y1 * width + x1]);
                            Vector4 result = Vector4.Lerp(Vector4.Lerp(topLeft, topRight, dx), Vector4.Lerp(bottomLeft, bottomRight, dx), dy);
                            result *= (1 / 255f);
                            return result;
                        }
                    }
            }
            int x2 = (int)(uv.X * (width - 1));
            int y2 = (int)(uv.Y * (height - 1));
            return textureData[y2 * width + x2].ToScaledVector4();
        }
        
        private Vector4 Sample1024x1024(Vector2 uv)
        {
            const int width1024 = 1024;
            const int height1024 = 1024;
            switch (magnificationFilterMode)
            {
                case MagnificationFilterMode.Nearest:
                    {
                        int x = (int)MathF.Round((uv.X * (width1024 - 1)));
                        int y = (int)MathF.Round((uv.Y * (height1024 - 1)));
                        return textureData![y * width1024 + x].ToScaledVector4();
                    }
                case MagnificationFilterMode.Linear:
                    {
                        int x0 = (int)(uv.X * (width1024 - 1));
                        int x1 = int.Clamp(x0 + 1, 0, width1024 - 1);
                        int y0 = (int)(uv.Y * (height1024 - 1));
                        int y1 = int.Clamp(y0 + 1, 0, width1024 - 1);
                        float dx = uv.X * (width1024 - 1) - x0;
                        float dy = uv.Y * (height1024 - 1) - y0;
                        if (Sse41.IsSupported)
                        {
                            Vector4 topLeft = ToUnscaledSse41(textureData![y0 * width1024 + x0]);
                            Vector4 topRight = ToUnscaledSse41(textureData[y0 * width1024 + x1]);
                            Vector4 bottomLeft = ToUnscaledSse41(textureData[y1 * width1024 + x0]);
                            Vector4 bottomRight = ToUnscaledSse41(textureData[y1 * width1024 + x1]);
                            Vector4 result = Vector4.Lerp(Vector4.Lerp(topLeft, topRight, dx), Vector4.Lerp(bottomLeft, bottomRight, dx), dy);
                            result *= (1 / 255f);
                            return result;
                        }
                        else
                        {
                            Vector4 topLeft = ToUnscaledVector4(textureData![y0 * width1024 + x0]);
                            Vector4 topRight = ToUnscaledVector4(textureData[y0 * width1024 + x1]);
                            Vector4 bottomLeft = ToUnscaledVector4(textureData[y1 * width1024 + x0]);
                            Vector4 bottomRight = ToUnscaledVector4(textureData[y1 * width1024 + x1]);
                            Vector4 result = Vector4.Lerp(Vector4.Lerp(topLeft, topRight, dx), Vector4.Lerp(bottomLeft, bottomRight, dx), dy);
                            result *= (1 / 255f);
                            return result;
                        }
                    }
            }
            int x2 = (int)(uv.X * (width1024 - 1));
            int y2 = (int)(uv.Y * (height1024 - 1));
            return textureData![y2 * width1024 + x2].ToScaledVector4();
        }
        private Vector4 Sample2048x2048(Vector2 uv)
        {
            const int width2048 = 2048;
            const int height2048 = 2048;
            switch (magnificationFilterMode)
            {
                case MagnificationFilterMode.Nearest:
                    {
                        int x = (int)MathF.Round((uv.X * (width2048 - 1)));
                        int y = (int)MathF.Round((uv.Y * (height2048 - 1)));
                        return textureData![y * width2048 + x].ToScaledVector4();
                    }
                case MagnificationFilterMode.Linear:
                    {
                        int x0 = (int)(uv.X * (width2048 - 1));
                        int x1 = int.Clamp(x0 + 1, 0, width2048 - 1);
                        int y0 = (int)(uv.Y * (height2048 - 1));
                        int y1 = int.Clamp(y0 + 1, 0, width2048 - 1);
                        float dx = uv.X * (width2048 - 1) - x0;
                        float dy = uv.Y * (height2048 - 1) - y0;
                        if (Sse41.IsSupported)
                        {
                            Vector4 topLeft = ToUnscaledSse41(textureData![y0 * width2048 + x0]);
                            Vector4 topRight = ToUnscaledSse41(textureData[y0 * width2048 + x1]);
                            Vector4 bottomLeft = ToUnscaledSse41(textureData[y1 * width2048 + x0]);
                            Vector4 bottomRight = ToUnscaledSse41(textureData[y1 * width2048 + x1]);
                            Vector4 result = Vector4.Lerp(Vector4.Lerp(topLeft, topRight, dx), Vector4.Lerp(bottomLeft, bottomRight, dx), dy);
                            result *= (1 / 255f);
                            return result;
                        }
                        else
                        {
                            Vector4 topLeft = ToUnscaledVector4(textureData![y0 * width2048 + x0]);
                            Vector4 topRight = ToUnscaledVector4(textureData[y0 * width2048 + x1]);
                            Vector4 bottomLeft = ToUnscaledVector4(textureData[y1 * width2048 + x0]);
                            Vector4 bottomRight = ToUnscaledVector4(textureData[y1 * width2048 + x1]);
                            Vector4 result = Vector4.Lerp(Vector4.Lerp(topLeft, topRight, dx), Vector4.Lerp(bottomLeft, bottomRight, dx), dy);
                            result *= (1 / 255f);
                            return result;
                        }
                    }
            }
            int x2 = (int)(uv.X * (width2048 - 1));
            int y2 = (int)(uv.Y * (height2048 - 1));
            return textureData![y2 * width2048 + x2].ToScaledVector4();
        }
        private Vector4 Sample4096x4096(Vector2 uv)
        {
            const int width4096 = 4096;
            const int height4096 = 4096;
            switch (magnificationFilterMode)
            {
                case MagnificationFilterMode.Nearest:
                    {
                        int x = (int)MathF.Round((uv.X * (width4096 - 1)));
                        int y = (int)MathF.Round((uv.Y * (height4096 - 1)));
                        return textureData![y * width4096 + x].ToScaledVector4();
                    }
                case MagnificationFilterMode.Linear:
                    {
                        int x0 = (int)(uv.X * (width4096 - 1));
                        int x1 = int.Clamp(x0 + 1, 0, width4096 - 1);
                        int y0 = (int)(uv.Y * (height4096 - 1));
                        int y1 = int.Clamp(y0 + 1, 0, width4096 - 1);
                        float dx = uv.X * (width4096 - 1) - x0;
                        float dy = uv.Y * (height4096 - 1) - y0;
                        if (Sse41.IsSupported)
                        {
                            Vector4 topLeft = ToUnscaledSse41(textureData![y0 * width4096 + x0]);
                            Vector4 topRight = ToUnscaledSse41(textureData[y0 * width4096 + x1]);
                            Vector4 bottomLeft = ToUnscaledSse41(textureData[y1 * width4096 + x0]);
                            Vector4 bottomRight = ToUnscaledSse41(textureData[y1 * width4096 + x1]);
                            Vector4 result = Vector4.Lerp(Vector4.Lerp(topLeft, topRight, dx), Vector4.Lerp(bottomLeft, bottomRight, dx), dy);
                            result *= (1 / 255f);
                            return result;
                        }
                        else
                        {
                            Vector4 topLeft = ToUnscaledVector4(textureData![y0 * width4096 + x0]);
                            Vector4 topRight = ToUnscaledVector4(textureData[y0 * width4096 + x1]);
                            Vector4 bottomLeft = ToUnscaledVector4(textureData[y1 * width4096 + x0]);
                            Vector4 bottomRight = ToUnscaledVector4(textureData[y1 * width4096 + x1]);
                            Vector4 result = Vector4.Lerp(Vector4.Lerp(topLeft, topRight, dx), Vector4.Lerp(bottomLeft, bottomRight, dx), dy);
                            result *= (1 / 255f);
                            return result;
                        }
                    }
            }
            int x2 = (int)(uv.X * (width4096 - 1));
            int y2 = (int)(uv.Y * (height4096 - 1));
            return textureData![y2 * width4096 + x2].ToScaledVector4();
        }
        static Vector4 ToUnscaledVector4(in Rgba32 rgba)
        {
            return new Vector4(rgba.R, rgba.G, rgba.B, rgba.A);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe Vector4 ToUnscaledSse41(Rgba32 rgba)
        {
            Vector128<byte> vector = Vector128.Load<byte>((byte*)(&rgba));
            return Sse42.ConvertToVector128Single(Sse42.ConvertToVector128Int32(vector)).AsVector4();
        }
    }
}
