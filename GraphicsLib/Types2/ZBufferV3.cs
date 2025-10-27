using GraphicsLib.Types;
using SixLabors.ImageSharp.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace GraphicsLib.Types2
{
    public struct VectorPixelData
    {
        public float depth = float.PositiveInfinity;
        public Vector4 color;

        public VectorPixelData()
        {
        }
        public VectorPixelData(float depth, Vector4 color)
        {
            this.depth = depth;
            this.color = color;
        }
    }
    public class ZBufferV3
    {

        private int width;
        private int height;
        private VectorPixelData[] buffer;
        private VectorPixelData filler = new();
        public int Width { get => width; private set => width = value; }
        public int Height { get => height; private set => height = value; }
        public ZBufferV3(int width, int height)
        {
            this.Width = width;
            this.Height = height;
            buffer = new VectorPixelData[width * height];
            Clear();
        }
        public void ChangeDefaultColor(Vector4 color)
        {
            filler.color = color;
        }
        public void Clear()
        {
            Array.Fill<VectorPixelData>(buffer, filler);
        }
        public void ResizeAndClear(int width, int height)
        {
            if (width != this.width || height != this.height)
            {
                Width = width;
                Height = height;
                buffer = new VectorPixelData[width * height];
            }
            Clear();
        }
        public VectorPixelData At(int pos)
        {
            return buffer[pos];
        }
        public VectorPixelData this[int x, int y]
        {
            get
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    throw new ArgumentOutOfRangeException("x or y out of buffer range");
                }
                return buffer[y * width + x];
            }
            set
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    throw new ArgumentOutOfRangeException("x or y out of buffer range");
                }
                buffer[y * width + x] = value;
            }
        }
        public bool Test(int x, int y, float depth)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                throw new ArgumentOutOfRangeException("x or y out of buffer range");
            }
            int pos = y * width + x;
            return depth <= buffer[pos].depth;
        }
        public bool TestAndSet(int x, int y, float depth, Vector4 color)
        {
            if (color.W == 0)
                return false;
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                throw new ArgumentOutOfRangeException("x or y out of buffer range");
            }
            int pos = y * width + x;
            VectorPixelData pixelData = default;
            pixelData.color = color;
            pixelData.depth = depth;
            VectorPixelData currentPixel = buffer[pos];
            if (currentPixel.depth < depth)
            {
                return false;
            }
            buffer[pos] = pixelData;
            return true;
        }
    }
}
