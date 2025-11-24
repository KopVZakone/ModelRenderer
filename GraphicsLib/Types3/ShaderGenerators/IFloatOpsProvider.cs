using System;
using System.Collections.Generic;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public interface IFloatOpsProvider
    {
        int Length();
        void Add(Span<float> dst, ReadOnlySpan<float> a, ReadOnlySpan<float> b);
        void Sub(Span<float> dst, ReadOnlySpan<float> a, ReadOnlySpan<float> b);
        void MulScalar(Span<float> dst, ReadOnlySpan<float> a, float scalar);
        void DivScalar(Span<float> dst, ReadOnlySpan<float> a, float scalar);
        void Lerp(Span<float> dst, ReadOnlySpan<float> a, ReadOnlySpan<float> b, float t);
    }
}
