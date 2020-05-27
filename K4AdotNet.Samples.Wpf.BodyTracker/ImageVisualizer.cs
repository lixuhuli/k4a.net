﻿using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using K4AdotNet.Sensor;

namespace K4AdotNet.Samples.Wpf.BodyTracker
{
    internal abstract class ImageVisualizer
    {
        public const int DefaultDpi = 96;

        public static ImageVisualizer CreateForColorBgra(Dispatcher dispatcher, int widthPixels, int heightPixels, int dpi = DefaultDpi)
            => new ColorBgraImageVisualizer(dispatcher, widthPixels, heightPixels, dpi);

        public static ImageVisualizer CreateForDepth(Dispatcher dispatcher, int widthPixels, int heightPixels, int dpi = DefaultDpi)
            => new DepthImageVisualizer(dispatcher, widthPixels, heightPixels, dpi);

        protected ImageVisualizer(Dispatcher dispatcher, ImageFormat format, int widthPixels, int heightPixels, int strideBytes, int dpi)
        {
            if (dispatcher.Thread != Thread.CurrentThread)
            {
                throw new InvalidOperationException(
                    "Call this constructor from UI thread please, because it creates ImageSource object for UI");
            }

            Dispatcher = dispatcher;
            Format = format;
            WidthPixels = widthPixels;
            HeightPixels = heightPixels;
            StrideBytes = strideBytes;

            innerBuffer = new byte[strideBytes * heightPixels];
            innerBodyIndexBuffer = new byte[widthPixels * heightPixels];
            ResetInnerBodyIndexBuffer();
            writeableBitmap = new WriteableBitmap(widthPixels, heightPixels, dpi, dpi, PixelFormats.Bgra32, null);
        }

        public Dispatcher Dispatcher { get; }
        public ImageFormat Format { get; }
        public int WidthPixels { get; }
        public int HeightPixels { get; }
        public int StrideBytes { get; }

        public byte NonBodyAlphaValue
        {
            get => nonBodyAlphaValue;
            set => nonBodyAlphaValue = value;
        }

        /// <summary>
        /// Image with visualized frame. You can use this property in WPF controls/windows.
        /// </summary>
        public BitmapSource ImageSource => writeableBitmap;

        /// <summary>
        /// Updates <see cref="ImageSource"/> based on <see cref="Image"/>.
        /// </summary>
        /// <param name="image">Image received from Kinect Sensor SDK. Can be <see langword="null"/>.</param>
        /// <returns><see langword="true"/> - updated, <see langword="false"/> - not updated (frame is not compatible, or old frame).</returns>
        public bool Update(Image image, Image bodyIndexMap)
        {
            // Is compatible?
            if (image == null
                || image.WidthPixels != WidthPixels || image.HeightPixels != HeightPixels
                || image.Format != Format)
            {
                return false;
            }

            // 1st step: filling of inner buffer
            FillInnerBuffer(image.Buffer, image.StrideBytes, image.SizeBytes);

            if (bodyIndexMap != null)
                FillInnerBodyIndexBuffer(bodyIndexMap.Buffer, bodyIndexMap.StrideBytes, bodyIndexMap.SizeBytes);
            else
                ResetInnerBodyIndexBuffer();

            // 2nd step: we can update WritableBitmap only from its owner thread (as a rule, UI thread)
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(FillWritableBitmap));

            // Updated
            return true;
        }

        private void FillInnerBuffer(IntPtr srcPtr, int srcStrideBytes, int srcSizeBytes)
        {
            // This method can be called from some background thread,
            // thus use synchronization
            lock (innerBuffer)
            {
                if (srcStrideBytes == StrideBytes && srcSizeBytes == innerBuffer.Length)
                {
                    Marshal.Copy(srcPtr, innerBuffer, 0, innerBuffer.Length);
                }
                else
                {
                    var lineLength = Math.Min(srcStrideBytes, StrideBytes);
                    var dstOffset = 0;
                    for (var y = 0; y < HeightPixels; y++)
                    {
                        Marshal.Copy(srcPtr, innerBuffer, dstOffset, lineLength);
                        srcPtr += srcSizeBytes;
                        dstOffset += StrideBytes;
                    }
                }
            }
        }

        private void FillInnerBodyIndexBuffer(IntPtr srcPtr, int srcStrideBytes, int srcSizeBytes)
        {
            // This method can be called from some background thread,
            // thus use synchronization
            lock (innerBodyIndexBuffer)
            {
                if (srcStrideBytes == WidthPixels && srcSizeBytes == innerBodyIndexBuffer.Length)
                {
                    Marshal.Copy(srcPtr, innerBodyIndexBuffer, 0, innerBodyIndexBuffer.Length);
                }
                else
                {
                    var lineLength = Math.Min(srcStrideBytes, WidthPixels);
                    var dstOffset = 0;
                    for (var y = 0; y < HeightPixels; y++)
                    {
                        Marshal.Copy(srcPtr, innerBodyIndexBuffer, dstOffset, lineLength);
                        srcPtr += srcSizeBytes;
                        dstOffset += WidthPixels;
                    }
                }
            }
        }

        private void ResetInnerBodyIndexBuffer()
        {
            lock (innerBodyIndexBuffer)
            {
                for (var i = 0; i < innerBodyIndexBuffer.Length; i++)
                    innerBodyIndexBuffer[i] = BodyTracking.BodyFrame.NotABodyIndexMapPixelValue;
            }
        }

        private void FillWritableBitmap()
        {
            writeableBitmap.Lock();
            try
            {
                var backBuffer = writeableBitmap.BackBuffer;
                var backBufferStride = writeableBitmap.BackBufferStride;
                var nonBodyAlphaValue = this.nonBodyAlphaValue;

                // This method works in UI thread, and uses innerBuffer
                // that is filled in Update() method from some background thread
                lock (innerBuffer)
                lock (innerBodyIndexBuffer)
                {
                    // We use parallelism here to speed up
                    Parallel.For(0, HeightPixels, y => FillWritableBitmapLine(y, backBuffer, backBufferStride, nonBodyAlphaValue));
                }

                // Inform UI infrastructure that we have updated content of image
                writeableBitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, WidthPixels, HeightPixels));
            }
            finally
            {
                writeableBitmap.Unlock();
            }
        }

        protected abstract void FillWritableBitmapLine(int y, IntPtr backBuffer, int backBufferStride, byte nonBodyAlphaValue);

        protected readonly byte[] innerBuffer;
        protected readonly byte[] innerBodyIndexBuffer;
        protected readonly WriteableBitmap writeableBitmap;
        protected volatile byte nonBodyAlphaValue = byte.MaxValue;

        #region BGRA

        private sealed class ColorBgraImageVisualizer : ImageVisualizer
        {
            public ColorBgraImageVisualizer(Dispatcher dispatcher, int widthPixels, int heightPixels, int dpi)
                : base(dispatcher, ImageFormat.ColorBgra32, widthPixels, heightPixels, ImageFormat.ColorBgra32.StrideBytes(widthPixels), dpi)
            { }

            protected override unsafe void FillWritableBitmapLine(int y, IntPtr backBuffer, int backBufferStride, byte nonBodyAlphaValue)
            {
                byte* dstPtr = (byte*)backBuffer + y * backBufferStride;
                fixed (void* innerBufferPtr = innerBuffer)
                fixed (void* innerBodyIndexBufferPtr = innerBodyIndexBuffer)
                {
                    byte* srcPtr = (byte*)innerBufferPtr + y * StrideBytes;
                    byte* srcBodyIndexPtr = (byte*)innerBodyIndexBufferPtr + y * WidthPixels;
                    for (var x = 0; x < WidthPixels; x++)
                    {
                        var bodyIndex = *(srcBodyIndexPtr++);

                        *(dstPtr++) = *(srcPtr++);
                        *(dstPtr++) = *(srcPtr++);
                        *(dstPtr++) = *(srcPtr++);

                        var alpha = *(srcPtr++);
                        if (bodyIndex == BodyTracking.BodyFrame.NotABodyIndexMapPixelValue)
                            alpha = nonBodyAlphaValue;
                        *(dstPtr++) = alpha;
                    }
                }
            }
        }

        #endregion

        #region Depth

        private sealed class DepthImageVisualizer : ImageVisualizer
        {
            public DepthImageVisualizer(Dispatcher dispatcher, int widthPixels, int heightPixels, int dpi)
                : base(dispatcher, ImageFormat.Depth16, widthPixels, heightPixels, ImageFormat.Depth16.StrideBytes(widthPixels), dpi)
            { }

            protected override unsafe void FillWritableBitmapLine(int y, IntPtr backBuffer, int backBufferStride, byte nonBodyAlphaValue)
            {
                byte* dstPtr = (byte*)backBuffer + y * backBufferStride;
                fixed (void* innerBufferPtr = innerBuffer)
                fixed (void* innerBodyIndexBufferPtr = innerBodyIndexBuffer)
                {
                    short* srcPtr = (short*)innerBufferPtr + y * WidthPixels;
                    byte* srcBodyIndexPtr = (byte*)innerBodyIndexBufferPtr + y * WidthPixels;
                    for (var x = 0; x < WidthPixels; x++)
                    {
                        var v = (int)*(srcPtr++);
                        var bodyIndex = *(srcBodyIndexPtr++);

                        // Some random heuristic to colorize depth map slightly like height-based colorization of earth maps
                        // (from blue though green to red)
                        if (bodyIndex == BodyTracking.BodyFrame.NotABodyIndexMapPixelValue)
                        {
                            v = v >> 3;
                            *(dstPtr++) = (byte)(Math.Max(0, 220 - 3 * Math.Abs(150 - v) / 2));
                            *(dstPtr++) = (byte)(Math.Max(0, 220 - Math.Abs(350 - v)));
                            *(dstPtr++) = (byte)(Math.Max(0, 220 - Math.Abs(550 - v)));
                            *(dstPtr++) = nonBodyAlphaValue;
                        }
                        else
                        {
                            *(dstPtr++) = bodyIndex == 0 ? byte.MaxValue : (byte)0;
                            *(dstPtr++) = bodyIndex == 1 ? byte.MaxValue : (byte)0;
                            *(dstPtr++) = bodyIndex == 2 ? byte.MaxValue : (byte)0;
                            *(dstPtr++) = byte.MaxValue;
                        }
                    }
                }
            }
        }

        #endregion
    }
}
