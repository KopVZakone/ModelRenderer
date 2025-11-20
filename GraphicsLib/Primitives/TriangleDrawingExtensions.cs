using GraphicsLib.Types;
using GraphicsLib.Types2;
using System.Numerics;
using System.Windows.Media.Imaging;

namespace GraphicsLib.Primitives
{
    public static class TriangleDrawingExtensions
    {

        /// <summary>
        ///  Draws triangle of any size
        ///  points order is not important
        ///  W-coordinate is used for Z-buffer
        /// </summary>
        public static void DrawTriangleWithZBuffer(
            this WriteableBitmap bitmap, int bitmapWidth, int bitmapHeight,
            Vector4 p0, Vector4 p1, Vector4 p2,
            uint color, Zbuffer zbuffer
        )
        {
            Vector4 min = p0;
            Vector4 mid = p1;
            Vector4 max = p2;
            // Correct min, mid and max
            if (mid.Y < min.Y)
            {
                (min, mid) = (mid, min);
            }
            if (max.Y < min.Y)
            {
                (min, max) = (max, min);
            }
            if (max.Y < mid.Y)
            {
                (mid, max) = (max, mid);
            }

            if (min.Y == mid.Y)
            {
                //flat top
                //   ----
                //   \  /
                //    \/
                if (mid.X < min.X)
                {
                    (min, mid) = (mid, min);
                }
                bitmap.DrawFlatTopTriangleWithZBuffer(bitmapWidth, bitmapHeight, min, mid, max, color, zbuffer);
            }
            else if (max.Y == mid.Y)
            {
                //flat bottom
                //    /\
                //   /  \
                //   ----
                if (max.X > mid.X)
                {
                    (mid, max) = (max, mid);
                }
                bitmap.DrawFlatBottomTriangleWithZBuffer(bitmapWidth, bitmapHeight, min, mid, max, color, zbuffer);
            }
            else
            {
                float c = (mid.Y - min.Y) / (max.Y - min.Y);
                Vector4 interpolant = Vector4.Lerp(min, max, c);
                if (interpolant.X > mid.X)
                {
                    //right major
                    //    min
                    //       
                    // mid     interpolant
                    //                    
                    //  
                    //                       max

                    bitmap.DrawFlatBottomTriangleWithZBuffer(bitmapWidth, bitmapHeight, min, interpolant, mid, color, zbuffer);
                    bitmap.DrawFlatTopTriangleWithZBuffer(bitmapWidth, bitmapHeight, mid, interpolant, max, color, zbuffer);
                }
                else
                {
                    //left major
                    //                  min
                    //       
                    //      interpolant     mid
                    //                    
                    //  
                    // max                      
                    bitmap.DrawFlatBottomTriangleWithZBuffer(bitmapWidth, bitmapHeight, min, mid, interpolant, color, zbuffer);
                    bitmap.DrawFlatTopTriangleWithZBuffer(bitmapWidth, bitmapHeight, interpolant, mid, max, color, zbuffer);
                }
            }

        }

        private static void DrawFlatTopTriangleWithZBuffer(this WriteableBitmap bitmap, int bitmapWidth, int bitmapHeight,
                    Vector4 leftTopPoint, Vector4 rightTopPoint, Vector4 bottomPoint, uint color, Zbuffer zbuffer)
        {
            float dy = bottomPoint.Y - leftTopPoint.Y;
            Vector4 dLeftPoint = (bottomPoint - leftTopPoint) / dy;
            Vector4 dRightPoint = (bottomPoint - rightTopPoint) / dy;
            float dzdx = (rightTopPoint.W - leftTopPoint.W) / (rightTopPoint.X - leftTopPoint.X);
            Vector4 rightPoint = rightTopPoint;
            bitmap.DrawFlatTriangleWithZBuffer(bitmapWidth, bitmapHeight, leftTopPoint, rightPoint, bottomPoint.Y,
                                                dLeftPoint, dRightPoint, dzdx, color, zbuffer);
        }
        private static void DrawFlatBottomTriangleWithZBuffer(this WriteableBitmap bitmap, int bitmapWidth, int bitmapHeight,
            Vector4 topPoint, Vector4 rightBottomPoint, Vector4 leftBottomPoint, uint color, Zbuffer zbuffer)
        {
            float dy = rightBottomPoint.Y - topPoint.Y;
            Vector4 dRightPoint = (rightBottomPoint - topPoint) / dy;
            Vector4 dLeftPoint = (leftBottomPoint - topPoint) / dy;
            float dzdx = (rightBottomPoint.W - leftBottomPoint.W) / (rightBottomPoint.X - leftBottomPoint.X);
            Vector4 rightPoint = topPoint;
            bitmap.DrawFlatTriangleWithZBuffer(bitmapWidth, bitmapHeight, topPoint, rightPoint,
                rightBottomPoint.Y, dLeftPoint, dRightPoint, dzdx, color, zbuffer);
        }
        /// <summary>
        /// draws triangle with 1 side parallel to x-axis
        /// </summary>
        /// <param name="bitmap"></param>
        /// <param name="bitmapWidth">used to avoid calling bitmap.PixelWidth</param>
        /// <param name="bitmapHeight">used to avoid calling bitmap.PixelWidth</param>
        /// <param name="leftPoint">left starting point</param>
        /// <param name="rightPoint">right starting point</param>
        /// <param name="yMax">max y of triangle</param>
        /// <param name="dLeftPoint"> interpolation delta for left point, y must be 1</param>
        /// <param name="dRightPoint">interpolation delta for right point, y must be 1</param>
        /// <param name="dzdx">interpolation delta for z</param>
        /// <param name="color">color of triangle</param>
        /// <param name="zbuffer"></param>
        private static void DrawFlatTriangleWithZBuffer(this WriteableBitmap bitmap, int bitmapWidth, int bitmapHeight,
            Vector4 leftPoint, Vector4 rightPoint, float yMax, Vector4 dLeftPoint, Vector4 dRightPoint, float dzdx, uint color, Zbuffer zbuffer)
        {
            int yStart = Math.Max((int)Math.Ceiling(leftPoint.Y), 0);
            int yEnd = Math.Min((int)Math.Ceiling(yMax), bitmapHeight);
            float yPrestep = yStart - leftPoint.Y;
            leftPoint += dLeftPoint * yPrestep;
            rightPoint += dRightPoint * yPrestep;
            for (int y = yStart; y < yEnd; y++)
            {
                int xStart = Math.Max((int)Math.Ceiling(leftPoint.X), 0);
                int xEnd = Math.Min((int)Math.Ceiling(rightPoint.X), bitmapWidth);
                float xPrestep = xStart - leftPoint.X;
                float z = leftPoint.W + dzdx * xPrestep;
                for (int x = xStart; x < xEnd; x++)
                {
                    if (zbuffer.TestAndSet(x, y, z))
                    {
                        unsafe
                        {
                            uint* ptr = (uint*)bitmap.BackBuffer;
                            ptr[y * bitmapWidth + x] = color;
                        }
                    }
                    z += dzdx;
                }
                leftPoint += dLeftPoint;
                rightPoint += dRightPoint;
            }

        }
        public static void FlushZBufferV2(this WriteableBitmap bitmap, ZBufferV2 zbufferV2)
        {
            unsafe
            {
                uint* ptr = (uint*)bitmap.BackBuffer;
                int x = bitmap.PixelWidth;
                int y = bitmap.PixelHeight;
                int length = x * y;
                for (int i = 0; i < length; i++)
                {
                    ptr[i] = zbufferV2.At(i).color;
                }
            }
        }
        public static void FlushZBufferV3(this WriteableBitmap bitmap, ZBufferV3 zbufferV2)
        {
            unsafe
            {
                uint* ptr = (uint*)bitmap.BackBuffer;
                int x = bitmap.PixelWidth;
                int y = bitmap.PixelHeight;
                int length = x * y;
                for (int i = 0; i < length; i++)
                {
                    Vector4 color = zbufferV2.At(i).color;
                    Vector3 corrected = Vector3.SquareRoot(Vector3.SquareRoot(color.AsVector3()));
                    ////gamma correction
                    //if (color.X < 0.04045f)
                    //{
                    //    color.X /= 12.92f;
                    //}
                    //else
                    //{
                    //    color.X = MathF.Pow((color.X + 0.055f) / 1.055f, 2.4f);
                    //}
                    //if (color.Y < 0.04045f)
                    //{
                    //    color.Y /= 12.92f;
                    //}
                    //else
                    //{
                    //    color.Y = MathF.Pow((color.Y + 0.055f) / 1.055f, 2.4f);
                    //}
                    //if (color.Z < 0.04045f)
                    //{
                    //    color.Z /= 12.92f;
                    //}
                    //else
                    //{
                    //    color.Z = MathF.Pow((color.Z + 0.055f) / 1.055f, 2.4f);
                    //}
                    Vector3 clampedColor = Vector3.Clamp(corrected, Vector3.Zero, new Vector3(1));
                    uint c = (uint)(clampedColor.X * 255) << 16 | (uint)(clampedColor.Y * 255) << 8 | (uint)(clampedColor.Z * 255) | (uint)(color.W * 255) << 24;
                    ptr[i] = c;
                }
            }
        }
        [Obsolete("use gbuffer method instead")]
        public static void FlushZGBuffer(this WriteableBitmap bitmap, GBuffer gbuffer)
        {
            unsafe
            {
                uint* ptr = (uint*)bitmap.BackBuffer;
                int x = bitmap.PixelWidth;
                int y = bitmap.PixelHeight;
                int length = x * y;
                for (int i = 0; i < length; i++)
                {
                    ptr[i] = gbuffer.GetColor(i % x, i / x);
                }
            }
        }
    }
}
