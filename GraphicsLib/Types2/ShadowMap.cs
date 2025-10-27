using GraphicsLib.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace GraphicsLib.Types2
{
    public class ShadowMap
    {
        public int Size { get; private set; }
        public ZBufferV3 DepthMap { get; private set; }
        public ShadowMap()
        {
            Size = 1024;
            DepthMap = new ZBufferV3(Size, Size);
        }
        public ShadowMap(int size)
        {
            Size = size;
            DepthMap = new ZBufferV3(Size, Size);
        }
        public void Resize(int size)
        {
            Size = size;
            DepthMap = new ZBufferV3(Size, Size);
        }
        public float this[int x, int y]
        {
            get { return DepthMap[x, y].depth; }
        }
        public float Sample(Vector2 uv)
        {
            int x = int.Clamp((int)(uv.X * (Size - 1)), 0, Size - 1);
            int y = int.Clamp((int)(uv.Y * (Size - 1)), 0, Size - 1);
            return DepthMap[x, y].depth;
        }
    }
}
