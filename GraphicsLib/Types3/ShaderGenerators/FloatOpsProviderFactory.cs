using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace GraphicsLib.Types3.ShaderGenerators
{
    public static class FloatOpsProviderFactory
    {

        private static readonly Dictionary<int, Type> CachedTypes = [];

        public static Type GetOrCreateInstanceStructByFlags(ShaderFeatures features, HashSet<int> usedUvs)
        {
            int length = ShaderFeaturesHelper.GetFloatOpsLength(features, usedUvs);
            return GetOrCreateInstanceStruct(length);
        }
        public static Type GetOrCreateInstanceStructForShadow(ShaderFeatures features)
        {
            int length = ShaderFeaturesHelper.GetIntOpsLengthForShadow(features);
            return GetOrCreateInstanceStruct(length);
        }
        public static Type GetOrCreateInstanceStruct(int length)
        {
            if (!CachedTypes.TryGetValue(length, out var type))
            {
                type = CreateInstanceStruct(length);
                CachedTypes[length] = type;
            }
            return type;
        }
        public static Type CreateInstanceStruct(int length, string? typeName = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

            typeName ??= $"RuntimeSpanFloatOpsInst_{length}";

            var asmName = new AssemblyName(typeName + "_Asm");
            var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
            var moduleBuilder = asmBuilder.DefineDynamicModule(asmName.Name);

            // public struct <typeName> : ISpanFloatOpsInstance
            var tb = moduleBuilder.DefineType(
                typeName,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                typeof(ValueType));

            // Реализуем интерфейс
            tb.AddInterfaceImplementation(typeof(IFloatOpsProvider));

            // Length(): int
            {
                var mb = tb.DefineMethod("Length",
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Final,
                    typeof(int), Type.EmptyTypes);
                var il = mb.GetILGenerator();
                il.Emit(OpCodes.Ldc_I4, length);
                il.Emit(OpCodes.Ret);
                ImplementInterfaceMethod(tb, mb, nameof(IFloatOpsProvider.Length));
            }

            EmitAdd(tb, length);
            EmitSub(tb, length);
            var mulScalar = EmitMulScalar(tb, length);
            EmitDivScalar(tb, length, mulScalar);
            EmitLerp(tb, length);

            return tb.CreateType();
        }

        static void EmitAdd(TypeBuilder tb, int length)
        {
            var mb = tb.DefineMethod("Add",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Final,
                typeof(void),
                [typeof(Span<float>), typeof(ReadOnlySpan<float>), typeof(ReadOnlySpan<float>)]);
            EmitAdd(mb, length);
            ImplementInterfaceMethod(tb, mb, nameof(IFloatOpsProvider.Add));
        }
        private static void EmitAdd(MethodBuilder mbAdd, int length)
        {
            var il = mbAdd.GetILGenerator();

            var dstPtr = il.DeclareLocal(typeof(IntPtr));
            var aPtr = il.DeclareLocal(typeof(IntPtr));
            var bPtr = il.DeclareLocal(typeof(IntPtr));
            // Подготовка указателей
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(SimdSpanPtr).GetMethod(nameof(SimdSpanPtr.Ptr), new[] { typeof(Span<float>) }));
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Stloc, dstPtr);

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, typeof(SimdSpanPtr).GetMethod(nameof(SimdSpanPtr.Ptr), new[] { typeof(ReadOnlySpan<float>) }));
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Stloc, aPtr);

            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, typeof(SimdSpanPtr).GetMethod(nameof(SimdSpanPtr.Ptr), new[] { typeof(ReadOnlySpan<float>) }));
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Stloc, bPtr);



            int availableAvxBlocks = Avx.IsSupported ? length / 8 : 0;
            int availableSseBlocks = Sse.IsSupported ? (length - availableAvxBlocks * 8) / 4 : 0;
            int remainingTail = length - (availableAvxBlocks * 8 + availableSseBlocks * 4);

            int offset = 0;
            for (int i = 0; i < availableAvxBlocks; i++)
            {
                EmitAvxAddBlock(il, dstPtr, aPtr, bPtr, offset);
                offset += 8 * sizeof(float);
            }
            for (int i = 0; i < availableSseBlocks; i++)
            {
                EmitSseAddBlock(il, dstPtr, aPtr, bPtr, offset);
                offset += 4 * sizeof(float);
            }
            EmitScalarTailAdd(il, dstPtr, aPtr, bPtr, offset, remainingTail);

            il.Emit(OpCodes.Ret);
        }

        private static void EmitAvxAddBlock(ILGenerator il, LocalBuilder dstPtr, LocalBuilder aPtr, LocalBuilder bPtr, int ofs)
        {
            var loadVec256Method = typeof(Avx).GetMethod(nameof(Avx.LoadVector256), [typeof(float*)])!;
            var addVec256Method = typeof(Avx).GetMethod(nameof(Avx.Add), [typeof(Vector256<float>), typeof(Vector256<float>)])!;
            var storeVec256Method = typeof(Avx).GetMethod(nameof(Avx.Store), [typeof(float*), typeof(Vector256<float>)])!;
            EmitLoadAddressWithOffset(il, dstPtr, ofs);
            EmitLoadAddressWithOffset(il, aPtr, ofs);
            il.Emit(OpCodes.Call, loadVec256Method);
            EmitLoadAddressWithOffset(il, bPtr, ofs);
            il.Emit(OpCodes.Call, loadVec256Method);
            il.Emit(OpCodes.Call, addVec256Method);
            il.Emit(OpCodes.Call, storeVec256Method);
        }
        private static void EmitLoadAddressWithOffset(ILGenerator il, LocalBuilder basePtr, int ofs)
        {
            il.Emit(OpCodes.Ldloc, basePtr);
            if (ofs == 0)
            {
                return;
            }
            il.Emit(OpCodes.Ldc_I4, ofs);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Conv_I);
        }
        private static void EmitSseAddBlock(ILGenerator il, LocalBuilder dstPtr, LocalBuilder aPtr, LocalBuilder bPtr, int ofs)
        {
            var loadVec128Method = typeof(Sse).GetMethod(nameof(Sse.LoadVector128), [typeof(float*)])!;
            var addVec128Method = typeof(Sse).GetMethod(nameof(Sse.Add), [typeof(Vector128<float>), typeof(Vector128<float>)])!;
            var storeVec128Method = typeof(Sse).GetMethod(nameof(Sse.Store), [typeof(float*), typeof(Vector128<float>)])!;
            EmitLoadAddressWithOffset(il, dstPtr, ofs);
            EmitLoadAddressWithOffset(il, aPtr, ofs);
            il.Emit(OpCodes.Call, loadVec128Method);
            EmitLoadAddressWithOffset(il, bPtr, ofs);
            il.Emit(OpCodes.Call, loadVec128Method);
            il.Emit(OpCodes.Call, addVec128Method);
            il.Emit(OpCodes.Call, storeVec128Method);
        }

        private static void EmitScalarTailAdd(ILGenerator il, LocalBuilder dstPtr, LocalBuilder aPtr, LocalBuilder bPtr, int startIdx, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int ofs = (startIdx + i) * sizeof(float);
                EmitLoadAddressWithOffset(il, aPtr, ofs);
                EmitLoadAddressWithOffset(il, aPtr, ofs);
                il.Emit(OpCodes.Ldind_R4);
                EmitLoadAddressWithOffset(il, bPtr, ofs);
                il.Emit(OpCodes.Ldind_R4);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stind_R4);
            }
        }

        static void EmitSub(TypeBuilder tb, int length)
        {
            var mb = tb.DefineMethod("Sub",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Final,
                typeof(void),
                [typeof(Span<float>), typeof(ReadOnlySpan<float>), typeof(ReadOnlySpan<float>)]);
            EmitSub(mb, length);
            ImplementInterfaceMethod(tb, mb, "Sub");
        }

        private static void EmitSub(MethodBuilder mbSub, int length)
        {
            var il = mbSub.GetILGenerator();

            var dstPtr = il.DeclareLocal(typeof(IntPtr));
            var aPtr = il.DeclareLocal(typeof(IntPtr));
            var bPtr = il.DeclareLocal(typeof(IntPtr));

            // pointers
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(SimdSpanPtr).GetMethod("Ptr", new[] { typeof(Span<float>) }));
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Stloc, dstPtr);

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, typeof(SimdSpanPtr).GetMethod("Ptr", new[] { typeof(ReadOnlySpan<float>) }));
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Stloc, aPtr);

            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, typeof(SimdSpanPtr).GetMethod("Ptr", new[] { typeof(ReadOnlySpan<float>) }));
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Stloc, bPtr);

            int avx = Avx.IsSupported ? length / 8 : 0;
            int sse = Sse.IsSupported ? (length - avx * 8) / 4 : 0;
            int tail = length - (avx * 8 + sse * 4);

            int ofs = 0;
            for (int i = 0; i < avx; i++)
            {
                EmitAvxSubBlock(il, dstPtr, aPtr, bPtr, ofs);
                ofs += 8 * 4;
            }
            for (int i = 0; i < sse; i++)
            {
                EmitSseSubBlock(il, dstPtr, aPtr, bPtr, ofs);
                ofs += 4 * 4;
            }
            EmitScalarTailSub(il, dstPtr, aPtr, bPtr, ofs, tail);

            il.Emit(OpCodes.Ret);
        }

        static void EmitAvxSubBlock(ILGenerator il, LocalBuilder dstPtr, LocalBuilder aPtr, LocalBuilder bPtr, int ofs)
        {
            var load = typeof(Avx).GetMethod("LoadVector256", new[] { typeof(float*) });
            var sub = typeof(Avx).GetMethod("Subtract", new[] { typeof(Vector256<float>), typeof(Vector256<float>) });
            var store = typeof(Avx).GetMethod("Store", new[] { typeof(float*), typeof(Vector256<float>) });

            EmitLoadAddressWithOffset(il, dstPtr, ofs);
            EmitLoadAddressWithOffset(il, aPtr, ofs);
            il.Emit(OpCodes.Call, load);
            EmitLoadAddressWithOffset(il, bPtr, ofs);
            il.Emit(OpCodes.Call, load);
            il.Emit(OpCodes.Call, sub);
            il.Emit(OpCodes.Call, store);
        }

        static void EmitSseSubBlock(ILGenerator il, LocalBuilder dstPtr, LocalBuilder aPtr, LocalBuilder bPtr, int ofs)
        {
            var load = typeof(Sse).GetMethod("LoadVector128", new[] { typeof(float*) });
            var sub = typeof(Sse).GetMethod("Subtract", new[] { typeof(Vector128<float>), typeof(Vector128<float>) });
            var store = typeof(Sse).GetMethod("Store", new[] { typeof(float*), typeof(Vector128<float>) });

            EmitLoadAddressWithOffset(il, dstPtr, ofs);
            EmitLoadAddressWithOffset(il, aPtr, ofs);
            il.Emit(OpCodes.Call, load);
            EmitLoadAddressWithOffset(il, bPtr, ofs);
            il.Emit(OpCodes.Call, load);
            il.Emit(OpCodes.Call, sub);
            il.Emit(OpCodes.Call, store);
        }

        static void EmitScalarTailSub(ILGenerator il, LocalBuilder dstPtr, LocalBuilder aPtr, LocalBuilder bPtr, int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int ofs = (start + i) * 4;

                EmitLoadAddressWithOffset(il, aPtr, ofs);
                il.Emit(OpCodes.Ldind_R4);

                EmitLoadAddressWithOffset(il, bPtr, ofs);
                il.Emit(OpCodes.Ldind_R4);

                il.Emit(OpCodes.Sub);

                EmitLoadAddressWithOffset(il, dstPtr, ofs);
                il.Emit(OpCodes.Stind_R4);
            }
        }
        static MethodBuilder EmitMulScalar(TypeBuilder tb, int length)
        {
            var mb = tb.DefineMethod("MulScalar",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Final,
                typeof(void),
                [typeof(Span<float>), typeof(ReadOnlySpan<float>), typeof(float)]);
            EmitMulScalar(mb, length);
            ImplementInterfaceMethod(tb, mb, "MulScalar");
            return mb;
        }

        private static void EmitMulScalar(MethodBuilder mb, int length)
        {
            var il = mb.GetILGenerator();

            var dstPtr = il.DeclareLocal(typeof(IntPtr));
            var aPtr = il.DeclareLocal(typeof(IntPtr));

            // pointers
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(SimdSpanPtr).GetMethod("Ptr", new[] { typeof(Span<float>) }));
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Stloc, dstPtr);

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, typeof(SimdSpanPtr).GetMethod("Ptr", new[] { typeof(ReadOnlySpan<float>) }));
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Stloc, aPtr);

            int avx = Avx.IsSupported ? length / 8 : 0;
            int sse = Sse.IsSupported ? (length - avx * 8) / 4 : 0;
            int tail = length - (avx * 8 + sse * 4);

            // scalar → vector
            var v256 = il.DeclareLocal(typeof(Vector256<float>));
            var v128 = il.DeclareLocal(typeof(Vector128<float>));

            if (Avx.IsSupported)
            {
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Call, typeof(Vector256).GetMethod("Create", new[] { typeof(float) }));
                il.Emit(OpCodes.Stloc, v256);
            }
            if (Sse.IsSupported)
            {
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Call, typeof(Vector128).GetMethod("Create", new[] { typeof(float) }));
                il.Emit(OpCodes.Stloc, v128);
            }

            int ofs = 0;
            for (int i = 0; i < avx; i++)
            {
                var load = typeof(Avx).GetMethod("LoadVector256", new[] { typeof(float*) });
                var mul = typeof(Avx).GetMethod("Multiply", new[] { typeof(Vector256<float>), typeof(Vector256<float>) });
                var store = typeof(Avx).GetMethod("Store", new[] { typeof(float*), typeof(Vector256<float>) });

                EmitLoadAddressWithOffset(il, dstPtr, ofs);
                EmitLoadAddressWithOffset(il, aPtr, ofs);
                il.Emit(OpCodes.Call, load);
                il.Emit(OpCodes.Ldloc, v256);
                il.Emit(OpCodes.Call, mul);
                il.Emit(OpCodes.Call, store);

                ofs += 8 * 4;
            }

            for (int i = 0; i < sse; i++)
            {
                var load = typeof(Sse).GetMethod("LoadVector128", new[] { typeof(float*) });
                var mul = typeof(Sse).GetMethod("Multiply", new[] { typeof(Vector128<float>), typeof(Vector128<float>) });
                var store = typeof(Sse).GetMethod("Store", new[] { typeof(float*), typeof(Vector128<float>) });

                EmitLoadAddressWithOffset(il, dstPtr, ofs);
                EmitLoadAddressWithOffset(il, aPtr, ofs);
                il.Emit(OpCodes.Call, load);
                il.Emit(OpCodes.Ldloc, v128);
                il.Emit(OpCodes.Call, mul);
                il.Emit(OpCodes.Call, store);

                ofs += 4 * 4;
            }

            // scalar tail
            for (int i = 0; i < tail; i++)
            {
                int o = (ofs + i * 4);
                EmitLoadAddressWithOffset(il, aPtr, o);
                il.Emit(OpCodes.Ldind_R4);
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Mul);
                EmitLoadAddressWithOffset(il, dstPtr, o);
                il.Emit(OpCodes.Stind_R4);
            }

            il.Emit(OpCodes.Ret);
        }
        static void EmitDivScalar(TypeBuilder tb, int length, MethodBuilder mulScalar)
        {
            var mb = tb.DefineMethod("DivScalar",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Final,
                typeof(void),
                [typeof(Span<float>), typeof(ReadOnlySpan<float>), typeof(float)]);
            EmitDivScalar(mb, length, mulScalar);
            ImplementInterfaceMethod(tb, mb, "DivScalar");
        }

        private static void EmitDivScalar(MethodBuilder mb, int length, MethodBuilder mulScalar)
        {
            var il = mb.GetILGenerator();

            // вычисляем inv = 1f / scalar
            //var inv = il.DeclareLocal(typeof(float));
            //il.Emit(OpCodes.Ldc_R4, 1f);
            //il.Emit(OpCodes.Ldarg_3);
            //il.Emit(OpCodes.Div);
            //il.Emit(OpCodes.Stloc, inv);

            //// вызываем MulScalar(dst, a, inv)
            //il.Emit(OpCodes.Ldarg_1);
            //il.Emit(OpCodes.Ldarg_2);
            //il.Emit(OpCodes.Ldloc, inv);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldc_R4, 1f);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Div);
            il.Emit(OpCodes.Call, mulScalar);
            il.Emit(OpCodes.Ret);
        }
        static void EmitLerp(TypeBuilder tb, int length)
        {
            var mb = tb.DefineMethod("Lerp",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Final,
                typeof(void),
                [typeof(Span<float>), typeof(ReadOnlySpan<float>), typeof(ReadOnlySpan<float>), typeof(float)]);
            EmitLerp(mb, length);
            ImplementInterfaceMethod(tb, mb, "Lerp");
        }

        private static void EmitLerp(MethodBuilder mb, int length)
        {
            var il = mb.GetILGenerator();

            var dstPtr = il.DeclareLocal(typeof(IntPtr));
            var aPtr = il.DeclareLocal(typeof(IntPtr));
            var bPtr = il.DeclareLocal(typeof(IntPtr));

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(SimdSpanPtr).GetMethod("Ptr", new[] { typeof(Span<float>) }));
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Stloc, dstPtr);

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, typeof(SimdSpanPtr).GetMethod("Ptr", new[] { typeof(ReadOnlySpan<float>) }));
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Stloc, aPtr);

            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, typeof(SimdSpanPtr).GetMethod("Ptr", new[] { typeof(ReadOnlySpan<float>) }));
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Stloc, bPtr);

            int avx = Avx.IsSupported && Fma.IsSupported ? length / 8 : 0;
            int sse = Sse.IsSupported ? (length - avx * 8) / 4 : 0;
            int tail = length - (avx * 8 + sse * 4);

            var t256 = il.DeclareLocal(typeof(Vector256<float>));
            var t128 = il.DeclareLocal(typeof(Vector128<float>));

            if (Avx.IsSupported && Fma.IsSupported)
            {
                il.Emit(OpCodes.Ldarg_S, 4);
                il.Emit(OpCodes.Call, typeof(Vector256).GetMethod("Create", new[] { typeof(float) }));
                il.Emit(OpCodes.Stloc, t256);
            }
            if (Sse.IsSupported)
            {
                il.Emit(OpCodes.Ldarg_S, 4);
                il.Emit(OpCodes.Call, typeof(Vector128).GetMethod("Create", new[] { typeof(float) }));
                il.Emit(OpCodes.Stloc, t128);
            }

            int ofs = 0;
            for (int i = 0; i < avx; i++)
            {
                EmitAvxLerpBlock(il, dstPtr, aPtr, bPtr, t256, ofs);
                ofs += 8 * 4;
            }
            for (int i = 0; i < sse; i++)
            {
                EmitSseLerpBlock(il, dstPtr, aPtr, bPtr, t128, ofs);
                ofs += 4 * 4;
            }

            EmitScalarTailLerp(il, dstPtr, aPtr, bPtr, ofs, tail);

            il.Emit(OpCodes.Ret);
        }

        private static void EmitAvxLerpBlock(
            ILGenerator il, LocalBuilder dstPtr, LocalBuilder aPtr, LocalBuilder bPtr,
            LocalBuilder tVec, int ofs)
        {
            var load = typeof(Avx).GetMethod("LoadVector256", new[] { typeof(float*) });
            var sub = typeof(Avx).GetMethod("Subtract", new[] { typeof(Vector256<float>), typeof(Vector256<float>) });
            var store = typeof(Avx).GetMethod("Store", new[] { typeof(float*), typeof(Vector256<float>) });

            var fma = typeof(Fma).GetMethod("MultiplyAdd", new[] {
                typeof(Vector256<float>), // diff
                typeof(Vector256<float>), // t
                typeof(Vector256<float>)  // a
            });

            // locals for a,b,diff,res
            var aReg = il.DeclareLocal(typeof(Vector256<float>));
            var bReg = il.DeclareLocal(typeof(Vector256<float>));
            var diffReg = il.DeclareLocal(typeof(Vector256<float>));
            var resReg = il.DeclareLocal(typeof(Vector256<float>));

            // aReg = load(a + ofs)
            EmitLoadAddressWithOffset(il, aPtr, ofs);
            il.Emit(OpCodes.Call, load);
            il.Emit(OpCodes.Stloc, aReg);

            // bReg = load(b + ofs)
            EmitLoadAddressWithOffset(il, bPtr, ofs);
            il.Emit(OpCodes.Call, load);
            il.Emit(OpCodes.Stloc, bReg);

            // diff = bReg - aReg
            il.Emit(OpCodes.Ldloc, bReg);
            il.Emit(OpCodes.Ldloc, aReg);
            il.Emit(OpCodes.Call, sub);
            il.Emit(OpCodes.Stloc, diffReg);

            // res = FMA(diff, t, a)
            il.Emit(OpCodes.Ldloc, diffReg); // (b-a)
            il.Emit(OpCodes.Ldloc, tVec);    // t
            il.Emit(OpCodes.Ldloc, aReg);    // a
            il.Emit(OpCodes.Call, fma);
            il.Emit(OpCodes.Stloc, resReg);

            // store(dst + ofs, res)
            EmitLoadAddressWithOffset(il, dstPtr, ofs);
            il.Emit(OpCodes.Ldloc, resReg);
            il.Emit(OpCodes.Call, store);
        }

        private static void EmitSseLerpBlock(
            ILGenerator il, LocalBuilder dstPtr, LocalBuilder aPtr, LocalBuilder bPtr,
            LocalBuilder tVec, int ofs)
        {
            var load = typeof(Sse).GetMethod("LoadVector128", new[] { typeof(float*) });
            var sub = typeof(Sse).GetMethod("Subtract", new[] { typeof(Vector128<float>), typeof(Vector128<float>) });
            var mul = typeof(Sse).GetMethod("Multiply", new[] { typeof(Vector128<float>), typeof(Vector128<float>) });
            var add = typeof(Sse).GetMethod("Add", new[] { typeof(Vector128<float>), typeof(Vector128<float>) });
            var store = typeof(Sse).GetMethod("Store", new[] { typeof(float*), typeof(Vector128<float>) });

            var aReg = il.DeclareLocal(typeof(Vector128<float>));
            var bReg = il.DeclareLocal(typeof(Vector128<float>));

            // a
            EmitLoadAddressWithOffset(il, aPtr, ofs);
            il.Emit(OpCodes.Call, load);
            il.Emit(OpCodes.Stloc, aReg);

            // b
            EmitLoadAddressWithOffset(il, bPtr, ofs);
            il.Emit(OpCodes.Call, load);
            il.Emit(OpCodes.Stloc, bReg);

            // b - a
            il.Emit(OpCodes.Ldloc, bReg);
            il.Emit(OpCodes.Ldloc, aReg);
            il.Emit(OpCodes.Call, sub);

            // * t
            il.Emit(OpCodes.Ldloc, tVec);
            il.Emit(OpCodes.Call, mul);

            // + a
            il.Emit(OpCodes.Ldloc, aReg);
            il.Emit(OpCodes.Call, add);

            // store
            EmitLoadAddressWithOffset(il, dstPtr, ofs);
            il.Emit(OpCodes.Call, store);
        }
        private static void EmitScalarTailLerp(
            ILGenerator il, LocalBuilder dstPtr, LocalBuilder aPtr, LocalBuilder bPtr,
            int start, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int ofs = (start + i) * 4;

                // a
                EmitLoadAddressWithOffset(il, aPtr, ofs);
                il.Emit(OpCodes.Ldind_R4);

                // b
                EmitLoadAddressWithOffset(il, bPtr, ofs);
                il.Emit(OpCodes.Ldind_R4);

                // (b-a)
                il.Emit(OpCodes.Sub);

                // * t
                il.Emit(OpCodes.Ldarg_S, 4);
                il.Emit(OpCodes.Mul);

                // + a
                EmitLoadAddressWithOffset(il, aPtr, ofs);
                il.Emit(OpCodes.Ldind_R4);
                il.Emit(OpCodes.Add);

                // store
                EmitLoadAddressWithOffset(il, dstPtr, ofs);
                il.Emit(OpCodes.Stind_R4);
            }
        }
        static void ImplementInterfaceMethod(TypeBuilder tb, MethodBuilder implMethod, string ifaceMethodName)
        {
            var iface = typeof(IFloatOpsProvider);
            var ifaceMethod = Array.Find(iface.GetMethods(), m => m.Name == ifaceMethodName)
                ?? throw new InvalidOperationException($"Interface method '{ifaceMethodName}' not found in interface '{iface.FullName}'");
            tb.DefineMethodOverride(implMethod, ifaceMethod);
        }
    }
    // Helpers для упрощения IL-вызовов
    public static class ReadOnlySpanHelpers
    {
        public static ReadOnlySpan<float> SliceR(this ReadOnlySpan<float> s, int offset, int length) => s.Slice(offset, length);
        public static float GetItem(this ReadOnlySpan<float> s, int index) => s[index];
    }
    public static class SpanHelpers
    {
        public static Span<float> SliceSpan(this Span<float> s, int offset, int length) => s.Slice(offset, length);
        public static ref float GetByRef(this Span<float> s, int index) => ref s[index];
    }
    public static class SimdSpanPtr
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe float* Ptr(Span<float> s)
        {
            ref float r = ref MemoryMarshal.GetReference(s);
            return (float*)Unsafe.AsPointer(ref r);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe float* Ptr(ReadOnlySpan<float> s)
        {
            ref float r = ref MemoryMarshal.GetReference(s);
            return (float*)Unsafe.AsPointer(ref r);
        }
    }
}
