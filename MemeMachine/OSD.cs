using System;
using System.Collections.ObjectModel;
using GameOverlay.Drawing;
using GameOverlay.Windows;

namespace MemeMachine
{
    class OSD
    {
        private readonly OverlayWindow overlayWindow;
        private readonly Graphics graphics;

        private Font font;

        private SolidBrush brushRed;
        private SolidBrush brushTransparent;

        private float testProgress = 0.0f;

        private ObservableCollection<MemeSound> daMemeList;

        public OSD(int left, int top, int right, int bottom)
        {
            overlayWindow = new OverlayWindow(left, top, right, bottom)
            {
                IsTopmost = true,
                IsVisible = true
            };

            graphics = new Graphics
            {
                MeasureFPS = false,
                Height = overlayWindow.Height,
                Width = overlayWindow.Width,
                PerPrimitiveAntiAliasing = true,
                TextAntiAliasing = true,
                UseMultiThreadedFactories = false,
                VSync = true,
                WindowHandle = IntPtr.Zero
            };
        }
        ~OSD()
        {
            graphics.Dispose();
            overlayWindow.Dispose();
        }

        public void Uninitialize()
        {
            if(graphics != null)
            {
                graphics.Dispose();
            }
            if(overlayWindow != null)
            {
                overlayWindow.Dispose();
            }
        }

        public void Initialize(ObservableCollection<MemeSound> memeList)
        {
            if(memeList.Count > 0)
            {
                daMemeList = memeList;

                overlayWindow.CreateWindow();

                graphics.WindowHandle = overlayWindow.Handle;
                graphics.Setup();

                // Create Fonts
                font = graphics.CreateFont("Verdana", 16);

                // Create Brushes
                brushRed = graphics.CreateSolidBrush(Color.Red);
                brushTransparent = graphics.CreateSolidBrush(Color.Transparent);
            }
        }

        public void Run()
        {
            while(true)
            {

                // Clear the Scene
                graphics.BeginScene();
                graphics.ClearScene(brushTransparent);

                for(int i=0; i < daMemeList.Count; i++)
                {
                    graphics.DrawTextWithLayout(font, 14.0f, graphics.CreateSolidBrush(225, 0, 0, 225), brushTransparent, 10, i*18, new Rectangle(10, i*18, 256, (i*18)+32), daMemeList[i].Name, 2);
                }
                //graphics.DrawVerticalProgressBar(graphics.CreateSolidBrush(180, 180, 180, 180), graphics.CreateSolidBrush(120, 120, 120, 120), Rectangle.Create(10, 10, 256, 32), 1.0f, testProgress);
                //graphics.DrawTextWithLayout(font, 14.0f, graphics.CreateSolidBrush(225, 225, 225, 225), brushTransparent, 10, 10, new Rectangle(10, 10, 256, 32), "Hello World", 2);

                graphics.EndScene();

                if (testProgress >= 0.0f && testProgress < 100.0f)
                {
                    testProgress = testProgress + 0.1f;
                }
            }
        }
    }
}
