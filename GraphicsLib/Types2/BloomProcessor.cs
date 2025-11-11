using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace GraphicsLib.Types2
{
    public sealed class BloomProcessor
    {
        private readonly int maxWidth;
        private readonly int maxHeight;
        private int padW, padH, padArea;

        private Complex32[] spectrumRB; // 2-for-1 packed: [0..padArea-1] = R+iG, [padArea..2*padArea-1] = B+iA
        private Complex32[] spectrumBA;
        private Complex32[] temp1D;   // temp buffer for 1D FFT
        private Complex32[] kernelSpec; // pre-transformed kernel (shared across both planes)
        private float gaussianKernelRadius = 1.5f;
        private bool customKernel = false;
        public BloomProcessor(int maxWidth, int maxHeight)
        {
            this.maxWidth = maxWidth;
            this.maxHeight = maxHeight;
            ResizeIfNeeded(maxWidth, maxHeight);
        }
        [MemberNotNull(nameof(padW))]
        [MemberNotNull(nameof(padH))]
        [MemberNotNull(nameof(padArea))]
        [MemberNotNull(nameof(spectrumRB))]
        [MemberNotNull(nameof(spectrumBA))]
        [MemberNotNull(nameof(temp1D))]
        [MemberNotNull(nameof(kernelSpec))]
        public void ResizeIfNeeded(int width, int height)
        {
            padW = NextPowerOfTwo(width);
            padH = NextPowerOfTwo(height);
            padArea = padW * padH;

            if (spectrumRB == null || spectrumRB.Length < padArea)
                spectrumRB = new Complex32[padArea];
            if (spectrumBA == null || spectrumBA.Length < padArea)
                spectrumBA = new Complex32[padArea];

            if (temp1D == null || temp1D.Length < Math.Max(padW, padH))
                temp1D = new Complex32[Math.Max(padW, padH)];

            if (kernelSpec == null || kernelSpec.Length < padArea)
            {
                kernelSpec = new Complex32[padArea];
                if (!customKernel)
                {
                    PrepareGaussianKernel(gaussianKernelRadius);
                }
            }

        }

        public void PrepareGaussianKernel(float sigma)
        {
            //gaussianKernelRadius = radius;
            for (int y = 0; y < padH; y++)
            {
                float fy = (y <= padH / 2 ? y : y - padH) / (float)padH;
                for (int x = 0; x < padW; x++)
                {
                    float fx = (x <= padW / 2 ? x : x - padW) / (float)padW;
                    float val = MathF.Exp(-2f * MathF.PI * MathF.PI * sigma * sigma * (fx * fx + fy * fy));
                    kernelSpec[y * padW + x] = new Complex32(val, 0f);
                }
            }
            // 1. Создаем гауссово ядро в пространственной области
            //Complex32[] kernelSpatial = new Complex32[padArea];

            //int centerX = padW / 2;
            //int centerY = padH / 2;
            //float sum = 0;

            //// Заполняем гауссово ядро
            //for (int y = 0; y < padH; y++)
            //{
            //    for (int x = 0; x < padW; x++)
            //    {
            //        float dx = x - centerX;
            //        float dy = y - centerY;
            //        float value = Gaussian2D(dx, dy, sigma);
            //        kernelSpatial[y * padW + x] = new Complex32(value, 0);
            //        sum += value;
            //    }
            //}
            ////fft shift

            //for (int y = 0; y < padH; y++)
            //{
            //    for (int x = 0; x < padW; x++)
            //    {
            //        if (((x + y) & 1) != 0)
            //            kernelSpatial[y * padW + x] = -kernelSpatial[y * padW + x];
            //    }
            //}
            //for (int i = 0; i < padArea; i++)
            //{
            //    kernelSpatial[i] /= sum;
            //}

            //Forward2DFFT(kernelSpatial, 0, padW, padH);

            //Array.Copy(kernelSpatial, kernelSpec, padArea);
        }
        public void LoadKernelFromImage(string path, bool normalize = true)
        {
            using var img = Image.Load<Rgba32>(path);
            
            int kw = img.Width;
            int kh = img.Height;

            if (kw > padW || kh > padH)
                throw new InvalidOperationException($"Kernel image ({kw}x{kh}) is larger than pad area ({padW}x{padH}).");

            // Конвертация в яркость (0..1)
            float[,] kernel = new float[kh, kw];
            float sum = 0f;
            var data = new Rgba32[img.Width * img.Height];
            img.CopyPixelDataTo(data);

            for (int y = 0; y < kh; y++)
            {
                var row = y * kw;
                for (int x = 0; x < kw; x++)
                {
                    Rgba32 px = data[row + x];
                    float v = (px.R + px.G + px.B) / (3f * 255f);
                    kernel[y, x] = v;
                    sum += v;
                }
            }

            // Нормализация
            if (normalize && sum > 0)
            {
                float inv = 1f / sum;
                for (int y = 0; y < kh; y++)
                    for (int x = 0; x < kw; x++)
                        kernel[y, x] *= inv;
            }


            Complex32[] padded = new Complex32[padArea];

            // Центр ядра
            int cx = kw / 2;
            int cy = kh / 2;

            // --- копируем четыре четверти, как unwrap fftshift ---
            for (int y = 0; y < kh; y++)
            {
                int dy = (y < cy) ? (y + padH - cy) : (y - cy);
                if (dy < 0 || dy >= padH) continue;

                for (int x = 0; x < kw; x++)
                {
                    int dx = (x < cx) ? (x + padW - cx) : (x - cx);
                    if (dx < 0 || dx >= padW) continue;

                    padded[dy * padW + dx] = new Complex32(kernel[y, x], 0f);
                }
            }

            // FFT ядра (теперь в частотную область)
            Forward2DFFT(padded, 0, padW, padH);

            // Сохраняем спектр
            Array.Copy(padded, kernelSpec, padArea);
        }
        public void Process(ZBufferV3 zbuf, float intensity = 1f, float threshold = 0.8f)
        {
            int w = zbuf.Width, h = zbuf.Height;
            ResizeIfNeeded(w, h);
            // 1. Pack R+iG and B+iA into spectrum
            Array.Clear(spectrumRB, 0, padArea);
            Array.Clear(spectrumBA, 0, padArea);
            Parallel.For(0, h, y =>
            {
                int rowSrc = y * w;
                int rowDst = y * padW;
                for (int x = 0; x < w; x++)
                {
                    var c = zbuf.At(rowSrc + x).color;
                    var r = c.X > threshold ? c.X : 0f;
                    var g = c.Y > threshold ? c.Y : 0f;
                    var b = c.Z > threshold ? c.Z : 0f;
                    var a = c.W;
                    spectrumRB[rowDst + x] = new Complex32(r, g);
                    spectrumBA[rowDst + x] = new Complex32(b, a);
                }
            });

            // 2. Forward horizontal FFT
            // 3. Forward vertical FFT
            Forward2DFFT(spectrumRB, 0, padW, padH);
            Forward2DFFT(spectrumBA, 0, padW, padH);
            //// 4. Multiply by kernel
            Parallel.For(0, padArea, i =>
            {
                spectrumRB[i] *= kernelSpec[i];
                spectrumBA[i] *= kernelSpec[i];
            });

            // 5. Inverse vertical FFT
            // 6. Inverse horizontal FFT
            Inverse2DFFT(spectrumRB, 0, padW, padH);
            Inverse2DFFT(spectrumBA, 0, padW, padH);
            // 7. Unpack and blend
            Parallel.For(0, h, y =>
            {
                int row = y * w;
                int rowPad = y * padW;
                for (int x = 0; x < w; x++)
                {
                    int idx = rowPad + x;
                    var c0 = spectrumRB[idx];
                    var c1 = spectrumBA[idx];

                    float addR = c0.Real * intensity;
                    float addG = c0.Imaginary * intensity;
                    float addB = c1.Real * intensity;

                    var src = zbuf.At(row + x).color;
                    var hdrColor = src.AsVector3() + new Vector3(addR, addG, addB);

                    zbuf[x, y] = new VectorPixelData(
                        zbuf.At(row + x).depth,
                        new Vector4(hdrColor.X, hdrColor.Y, hdrColor.Z, src.W)
                    );
                }
            });
        }
        private void Forward2DFFT(Complex32[] buffer, int offset, int width, int height)
        {
            // Горизонтальные
            Parallel.For(0, height, y =>
            {
                FFT1D(buffer, offset + y * width, width, true);
            });

            // Вертикальные
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                    temp1D[y] = buffer[offset + y * width + x];
                FFT1D(temp1D, 0, height, true);
                for (int y = 0; y < height; y++)
                    buffer[offset + y * width + x] = temp1D[y];
            }
        }
        void FFTShiftResult(Complex32[] data, int width, int height)
        {
            int halfW = width / 2;
            int halfH = height / 2;
            for (int y = 0; y < halfH; y++)
            {
                for (int x = 0; x < halfW; x++)
                {
                    int a = y * width + x;
                    int b = (y + halfH) * width + (x + halfW);
                    (data[a], data[b]) = (data[b], data[a]);
                }
            }
        }
        private static float Gaussian2D(float x, float y, float sigma)
        {
            return MathF.Exp(-(x * x + y * y) / (2 * sigma * sigma));
        }

        private void Inverse2DFFT(Complex32[] buffer, int offset, int width, int height)
        {
            // Вертикальные
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                    temp1D[y] = buffer[offset + y * width + x];
                FFT1D(temp1D, 0, height, false);
                for (int y = 0; y < height; y++)
                    buffer[offset + y * width + x] = temp1D[y];
            }

            // Горизонтальные
            Parallel.For(0, height, y =>
            {
                FFT1D(buffer, offset + y * width, width, false);
            });

            // Глобальное масштабирование
            float scale = 1f / (width * height);
            for (int i = 0; i < width * height; i++)
                buffer[offset + i] *= scale;
        }
        private static void FFT1D(Complex32[] buffer, int offset, int n, bool forward)
        {
            // Bit reversal
            for (int j = 1, i = 0; j < n; j++)
            {
                int bit = n >> 1;
                for (; (i & bit) != 0; bit >>= 1) i ^= bit;
                i ^= bit;
                if (j < i)
                {
                    (buffer[offset + i], buffer[offset + j]) = (buffer[offset + j], buffer[offset + i]);
                }
            }

            // Cooley–Tukey
            for (int len = 2; len <= n; len <<= 1)
            {
                float ang = 2 * MathF.PI / len * (forward ? -1 : 1);
                Complex32 wlen = Complex32.FromPolarCoordinates(1.0f, ang);
                for (int i = 0; i < n; i += len)
                {
                    Complex32 w = Complex32.One;
                    for (int j = 0; j < len / 2; j++)
                    {
                        Complex32 u = buffer[offset + i + j];
                        Complex32 v = buffer[offset + i + j + len / 2] * w;
                        buffer[offset + i + j] = u + v;
                        buffer[offset + i + j + len / 2] = u - v;
                        w *= wlen;
                    }
                }
            }
        }

        private static int NextPowerOfTwo(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }
    }
}