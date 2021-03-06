﻿using System;
using System.Runtime.InteropServices;
using PepperSharp;

namespace Graphics_2D
{
    public class Graphics_2D : PPInstance
    {
        PP_Instance instance = new PP_Instance();

        PP_Resource context;
        PP_Resource flushContext;
        PP_Size size;
        PP_Point mouse;
        bool mouseDown;
        byte[] buffer;
        uint[] palette = new uint[256];
        float deviceScale;
        Random random;
        const int mouseRadius = 15;

        public Graphics_2D(IntPtr handle) : base(handle) { }

        ~Graphics_2D() { System.Console.WriteLine("Graphics_2D destructed"); }

        public override bool Init(int argc, string[] argn, string[] argv)
        {
            instance.pp_instance = Handle.ToInt32();
            PPB_Console.Log(instance, PP_LogLevel.Log, "Hello from PepperSharp using C#");
            PPB_InputEvent.RequestInputEvents(instance, (int)PP_InputEvent_Class.Mouse);
            int seed = 1;
            random = new Random(seed);
            CreatePalette();

            return true;
        }

        public override void DidChangeView(PP_Resource view)
        {
            var viewRect = new PP_Rect();
            var result = PPB_View.GetRect(view, out viewRect);

            deviceScale = PPB_View.GetDeviceScale(view);
            PP_Size newSize = new PP_Size();
            newSize.width = (int)(viewRect.size.width * deviceScale);
            newSize.height = (int)(viewRect.size.height * deviceScale);

            if (!CreateContext(newSize))
                return;

            // When flush_context_ is null, it means there is no Flush callback in
            // flight. This may have happened if the context was not created
            // successfully, or if this is the first call to DidChangeView (when the
            // module first starts). In either case, start the main loop.
            if (flushContext.pp_resource == 0)
                MainLoop(0);
        }

        public override void DidChangeFocus(bool hasFocus)
        {
            //Console.WriteLine($"Graphics_2D DidChangeFocus: {hasFocus}");
        }

        public override bool HandleInputEvent(PP_Resource inputEvent)
        {
            if (buffer == null)
                return true;

            
            var eventType = PPB_InputEvent.GetType(inputEvent);
            if (eventType == PP_InputEvent_Type.Mousedown ||
                eventType == PP_InputEvent_Type.Mousemove)
            {
                var mouseButton = PPB_MouseInputEvent.GetButton(inputEvent);
                if (mouseButton == PP_InputEvent_MouseButton.None)
                    return true;
                if (PPB_MouseInputEvent.IsMouseInputEvent(inputEvent) == PP_Bool.PP_TRUE)
                {
                    
                    var pos = PPB_MouseInputEvent.GetPosition(inputEvent);
                    //Console.WriteLine($"yes it is {pos.x}");
                    mouse.x = (int)(pos.x * deviceScale);
                    mouse.y = (int)(pos.y * deviceScale);

                    mouseDown = true;
                }
            }

            if (eventType == PP_InputEvent_Type.Mouseup)
                mouseDown = false;
            
            return true;
        }

        bool CreateContext(PP_Size new_size)
        {
            bool kIsAlwaysOpaque = true;
            var isAlwaysOpaque = new PP_Bool();
            isAlwaysOpaque = kIsAlwaysOpaque ? PP_Bool.PP_TRUE : PP_Bool.PP_FALSE;
            context = PPB_Graphics2D.Create(instance, ref new_size, isAlwaysOpaque);

            // Call SetScale before BindGraphics so the image is scaled correctly on
            // HiDPI displays.
            PPB_Graphics2D.SetScale(context, (1.0f / deviceScale));

            var osize = new PP_Size();
            var oopaque = new PP_Bool();

            PPB_Graphics2D.Describe(context, out osize, out oopaque);

            if (!BindGraphics(context))
            {
                Console.WriteLine("Unable to bind 2d context!");
                return false;
            }

            // Allocate a buffer of palette entries of the same size as the new context.
            buffer = new byte[new_size.width * new_size.height];
            size = new_size;

            return true;
        }

        void CreatePalette()
        {
            for (int i = 0; i < 64; ++i)
            {
                // Black -> Red
                palette[i] = MakeColor((byte)(i * 2), 0, 0);
                palette[i + 64] = MakeColor((byte)(128 + i * 2), 0, 0);
                // Red -> Yellow
                palette[i + 128] = MakeColor(255, (byte)(i * 4), 0);
                // Yellow -> White
                palette[i + 192] = MakeColor(255, 255, (byte)(i * 4));
            }

        }

        uint MakeColor(byte r, byte g, byte b)
        {
            byte a = 255;
            var format = PPB_ImageData.GetNativeImageDataFormat();

            if (format == PP_ImageDataFormat.Bgra_premul)
            {
                return (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
            else
            {
                return (uint)((a << 24) | (b << 16) | (g << 8) | r);
            }

        }

        const uint RAND_MAX = 0x7fff;
        byte RandUint8(byte min, byte max)
        {
            var r = random.Next();
            var result = (byte)((r * (max - min + 1) / RAND_MAX) + min);
            return result;
        }

        void Update()
        {
            // Old-school fire technique cribbed from
            // http://ionicsolutions.net/2011/12/30/demo-fire-effect/
            UpdateCoals();
            DrawMouse();
            UpdateFlames();
        }

        void UpdateCoals()
        {
            int width = size.width;
            int height = size.height;
            uint span = 0;

            // Draw two rows of random values at the bottom.
            for (int y = height - 2; y < height; ++y)
            {
                var offset = y * width;
                for (int x = 0; x < width; ++x)
                {
                    // On a random chance, draw some longer strips of brighter colors.
                    if (span == 1 || RandUint8(1, 4) == 1)
                    {
                        if (span == 0)
                            span = RandUint8(10, 20);
                        buffer[offset + x] = RandUint8(128, 255);
                        span--;
                    }
                    else
                    {
                        buffer[offset + x] = RandUint8(32, 96);
                    }
                }
            }
        }

        void UpdateFlames()
        {
            int width = size.width;
            int height = size.height;
            for (int y = 1; y < height - 1; ++y)
            {
                uint offset = (uint)(y * width);
                for (int x = 1; x < width - 1; ++x)
                {
                    int sum = 0;
                    sum += buffer[offset - width + x - 1];
                    sum += buffer[offset - width + x + 1];
                    sum += buffer[offset + x - 1];
                    sum += buffer[offset + x + 1];
                    sum += buffer[offset + width + x - 1];
                    sum += buffer[offset + width + x];
                    sum += buffer[offset + width + x + 1];
                    buffer[offset - width + x] = (byte)(sum / 7);
                }
            }
        }

        void DrawMouse()
        {
            if (!mouseDown)
                return;

            int width = size.width;
            int height = size.height;

            // Draw a circle at the mouse position.
            int radius = (int)(mouseRadius * deviceScale);
            int cx = mouse.x;
            int cy = mouse.y;
            int minx = cx - radius <= 0 ? 1 : cx - radius;
            int maxx = cx + radius >= width ? width - 1 : cx + radius;
            int miny = cy - radius <= 0 ? 1 : cy - radius;
            int maxy = cy + radius >= height ? height - 1 : cy + radius;
            for (int y = miny; y < maxy; ++y)
            {
                for (int x = minx; x < maxx; ++x)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) < radius * radius)
                        buffer[y * width + x] = RandUint8(192, 255);
                }
            }
        }

        void Paint()
        {
            // See the comment above the call to ReplaceContents below.
            var format = PPB_ImageData.GetNativeImageDataFormat();
            bool kDontInitToZero = false;
            var dontInitToZero = kDontInitToZero ? PP_Bool.PP_TRUE : PP_Bool.PP_FALSE;
            var image_data = PPB_ImageData.Create(instance, format, ref size, dontInitToZero);
            var desc = new PP_ImageDataDesc();

            int[] data = null;
            IntPtr dataPtr = IntPtr.Zero;
            if (PPB_ImageData.Describe(image_data, out desc) == PP_Bool.PP_TRUE)
            {
                dataPtr = PPB_ImageData.Map(image_data);
                if (dataPtr == IntPtr.Zero)
                    return;
                data = new int[(desc.size.width * desc.size.height)];

                Marshal.Copy(dataPtr, data, 0, data.Length);

            }


            var num_pixels = (size.width * size.height);
            uint offset = 0;

            for (uint i = 0; i < num_pixels; ++i)
            {
                data[offset] = (int)palette[buffer[offset]];
                offset++;
            }

            Marshal.Copy(data, 0, dataPtr, data.Length);

            // Using Graphics2D::ReplaceContents is the fastest way to update the
            // entire canvas every frame. According to the documentation:
            //
            //   Normally, calling PaintImageData() requires that the browser copy
            //   the pixels out of the image and into the graphics context's backing
            //   store. This function replaces the graphics context's backing store
            //   with the given image, avoiding the copy.
            //
            //   In the case of an animation, you will want to allocate a new image for
            //   the next frame. It is best if you wait until the flush callback has
            //   executed before allocating this bitmap. This gives the browser the
            //   option of caching the previous backing store and handing it back to
            //   you (assuming the sizes match). In the optimal case, this means no
            //   bitmaps are allocated during the animation, and the backing store and
            //   "front buffer" (which the module is painting into) are just being
            //   swapped back and forth.
            //
            PPB_Graphics2D.ReplaceContents(context, image_data);
        }

        void MainLoop(int dt)
        {
            if (context.pp_resource == 0)
            {
                // The current Graphics2D context is null, so updating and rendering is
                // pointless. Set flush_context_ to null as well, so if we get another
                // DidChangeView call, the main loop is started again.
                flushContext.pp_resource = context.pp_resource;
                return;
            }

            Update();
            Paint();
            // Store a reference to the context that is being flushed; this ensures
            // the callback is called, even if context_ changes before the flush
            // completes.
            flushContext.pp_resource = context.pp_resource;

            PP_CompletionCallback_Func callback = 
                (IntPtr callbackUserData, int result) =>
                   {
                        MainLoop(result);
                    };


            var completionCallback = new PP_CompletionCallback();
            completionCallback.func = callback;
            completionCallback.flags = (int)PP_CompletionCallback_Flag.None;

            var flushResult = PPB_Graphics2D.Flush(context, completionCallback);
        }

    }
}
