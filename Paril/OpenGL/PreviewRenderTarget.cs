using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using MCSkin3D;

namespace Paril.OpenGL
{
    // Scale only the 3D viewport. Editing coordinates, the 2D sheet and UI remain
    // at native resolution. Resolve MSAA before scaling (required by OpenGL).
    internal sealed class PreviewRenderTarget : IDisposable
    {
        private int framebuffer, color, depth, resolveFramebuffer, resolveColor;
        private int previousDraw, previousRead;
        private bool previousMultisample;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Samples { get; private set; }
        public bool Active { get; private set; }

        public void Begin(Size size, PreviewRenderOptions options)
        {
            options = options.Validated();
            if (options.Resolution == 100 && options.Samples == 0)
            {
                Dispose();
                Width = size.Width; Height = size.Height;
                return;
            }
            previousDraw = GL.GetInteger(GetPName.DrawFramebufferBinding);
            previousRead = GL.GetInteger(GetPName.ReadFramebufferBinding);
            previousMultisample = GL.IsEnabled(EnableCap.Multisample);
            int limit = GL.GetInteger(GetPName.MaxRenderbufferSize);
            double scale = Math.Min(options.Resolution / 100d, Math.Min(limit / (double)Math.Max(1, size.Width), limit / (double)Math.Max(1, size.Height)));
            int width = Math.Max(1, (int)Math.Round(size.Width * scale));
            int height = Math.Max(1, (int)Math.Round(size.Height * scale));
            int samples = Math.Min(options.Samples, GL.GetInteger(GetPName.MaxSamples));
            if (samples < 2) samples = 0;
            try
            {
                if (framebuffer == 0 || Width != width || Height != height || Samples != samples)
                {
                    Dispose();
                    Width = width; Height = height; Samples = samples;
                    framebuffer = CreateFramebuffer(ref color, ref depth, samples, true);
                    if (samples > 0) resolveFramebuffer = CreateFramebuffer(ref resolveColor, ref depth, 0, false);
                }
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
                if (samples > 0) GL.Enable(EnableCap.Multisample); else GL.Disable(EnableCap.Multisample);
                Active = true;
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            }
            catch
            {
                Dispose();
                Restore();
                throw;
            }
        }

        private int CreateFramebuffer(ref int colorBuffer, ref int depthBuffer, int samples, bool withDepth)
        {
            int result = GL.GenFramebuffer();
            try
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, result);
                colorBuffer = GL.GenRenderbuffer();
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colorBuffer);
                Storage(RenderbufferStorage.Rgba8, samples);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, colorBuffer);
                if (withDepth)
                {
                    depthBuffer = GL.GenRenderbuffer();
                    GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthBuffer);
                    Storage(RenderbufferStorage.DepthComponent24, samples);
                    GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthBuffer);
                }
                GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
                var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status != FramebufferErrorCode.FramebufferComplete)
                    throw new InvalidOperationException("Preview framebuffer: " + status);
                return result;
            }
            catch { GL.DeleteFramebuffer(result); throw; }
            finally { GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0); }
        }

        private void Storage(RenderbufferStorage format, int samples)
        {
            if (samples > 0) GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples, format, Width, Height);
            else GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, format, Width, Height);
        }

        public void End(Rectangle viewport)
        {
            if (!Active) return;
            try
            {
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
                if (Samples > 0)
                {
                    GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, resolveFramebuffer);
                    GL.BlitFramebuffer(0, 0, Width, Height, 0, 0, Width, Height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
                    GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, resolveFramebuffer);
                }
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousDraw);
                GL.BlitFramebuffer(0, 0, Width, Height, viewport.Left, viewport.Top, viewport.Right, viewport.Bottom,
                    ClearBufferMask.ColorBufferBit, Width == viewport.Width && Height == viewport.Height ? BlitFramebufferFilter.Nearest : BlitFramebufferFilter.Linear);
            }
            finally { Restore(); Active = false; }
        }

        private void Restore()
        {
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousDraw);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousRead);
            if (previousMultisample) GL.Enable(EnableCap.Multisample); else GL.Disable(EnableCap.Multisample);
        }

        public void Dispose()
        {
            if (framebuffer != 0) GL.DeleteFramebuffer(framebuffer);
            if (resolveFramebuffer != 0) GL.DeleteFramebuffer(resolveFramebuffer);
            if (color != 0) GL.DeleteRenderbuffer(color);
            if (depth != 0) GL.DeleteRenderbuffer(depth);
            if (resolveColor != 0) GL.DeleteRenderbuffer(resolveColor);
            framebuffer = resolveFramebuffer = color = depth = resolveColor = 0;
            Samples = 0; Active = false;
        }
    }
}
