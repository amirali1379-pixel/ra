using System;
using System.Drawing;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace grapher.DesktopSens.Views
{
    // Real OpenGL scene: world-space floor, perspective projection, and a rotatable camera.
    internal sealed class DesktopSensArena3DControl : GLControl
    {
        internal float YawDegrees { get; set; }
        internal float PitchDegrees { get; set; } = 18f;
        internal float Radius { get; set; } = 10f;
        internal PointF Target { get; set; }
        internal PointF CursorWorld { get; set; }
        internal bool ShowTarget { get; set; }
        internal string StatusText { get; set; } = "";
        internal int TrialCount { get; set; }

        public DesktopSensArena3DControl() : base(new GraphicsMode(32, 24, 0, 4))
        {
            BackColor = Color.FromArgb(12, 16, 22);
            VSync = true;
            Paint += RenderScene;
            Resize += (s, e) => { MakeCurrent(); GL.Viewport(0, 0, Width, Height); Invalidate(); };
        }

        private void RenderScene(object sender, PaintEventArgs e)
        {
            if (Width < 1 || Height < 1 || IsDisposed) return;
            MakeCurrent();
            GL.Viewport(0, 0, Width, Height);
            GL.ClearColor(0.035f, 0.05f, 0.065f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(68f), (float)Width / Height, 0.05f, 500f);
            GL.LoadMatrix(ref projection);

            float yaw = MathHelper.DegreesToRadians(YawDegrees);
            float pitch = MathHelper.DegreesToRadians(Math.Max(-15f, Math.Min(65f, PitchDegrees)));
            Vector3 look = new Vector3((float)(Math.Sin(yaw) * Math.Cos(pitch)),
                (float)Math.Sin(pitch), (float)(-Math.Cos(yaw) * Math.Cos(pitch)));
            Vector3 eye = new Vector3(0f, 1.7f, 0f);
            Matrix4 view = Matrix4.LookAt(eye, eye + look, Vector3.UnitY);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadMatrix(ref view);

            DrawGround();
            DrawRings();
            DrawAxes();
            DrawMarker(new Vector3(Target.X, 0.05f, -Target.Y), 0.22f, 1f, 0.57f, 0.25f);
            if (ShowTarget) DrawLine(Vector3.Zero, new Vector3(Target.X, 0.05f, -Target.Y), 1f, 0.57f, 0.25f);
            DrawMarker(new Vector3(CursorWorld.X, 0.08f, -CursorWorld.Y), 0.13f, 0.25f, 0.95f, 0.72f);
            SwapBuffers();
        }

        private void DrawGround()
        {
            GL.Begin(PrimitiveType.Quads);
            GL.Color4(0.06f, 0.085f, 0.095f, 1f);
            GL.Vertex3(-Radius * 2, -0.03, -Radius * 2);
            GL.Vertex3(Radius * 2, -0.03, -Radius * 2);
            GL.Vertex3(Radius * 2, -0.03, Radius * 2);
            GL.Vertex3(-Radius * 2, -0.03, Radius * 2);
            GL.End();
            GL.LineWidth(1f);
            GL.Begin(PrimitiveType.Lines);
            for (int i = -20; i <= 20; i++)
            {
                float p = Radius * i / 10f;
                GL.Color4(0.18f, 0.29f, 0.31f, 0.38f);
                GL.Vertex3(p, 0f, -Radius * 2); GL.Vertex3(p, 0f, Radius * 2);
                GL.Vertex3(-Radius * 2, 0f, p); GL.Vertex3(Radius * 2, 0f, p);
            }
            GL.End();
        }

        private void DrawRings()
        {
            double[] fractions = { 0.25, 0.5, 0.75, 1.0 };
            foreach (double fraction in fractions)
            {
                float r = (float)(Radius * fraction);
                GL.Color4(0.40f, 0.67f, 0.69f, fraction == 1.0 ? 0.95f : 0.42f);
                GL.LineWidth(fraction == 1.0 ? 2.5f : 1f);
                GL.Begin(PrimitiveType.LineLoop);
                for (int i = 0; i < 160; i++)
                {
                    double a = i * Math.PI * 2 / 160.0;
                    GL.Vertex3(Math.Cos(a) * r, 0.025, Math.Sin(a) * r);
                }
                GL.End();
            }
        }

        private static void DrawAxes()
        {
            GL.LineWidth(2f);
            GL.Begin(PrimitiveType.Lines);
            GL.Color3(0.95f, 0.34f, 0.27f); GL.Vertex3(0, 0.04, 0); GL.Vertex3(2, 0.04, 0);
            GL.Color3(0.25f, 0.75f, 0.48f); GL.Vertex3(0, 0.04, 0); GL.Vertex3(0, 0.04, -2);
            GL.End();
            DrawMarker(Vector3.Zero, 0.16f, 0.25f, 0.85f, 0.68f);
        }

        private static void DrawMarker(Vector3 p, float size, float r, float g, float b)
        {
            GL.PointSize(Math.Max(5f, size * 30f));
            GL.Begin(PrimitiveType.Points);
            GL.Color3(r, g, b);
            GL.Vertex3(p.X, p.Y, p.Z);
            GL.End();
            GL.LineWidth(2f);
            GL.Begin(PrimitiveType.Lines);
            GL.Color3(r, g, b);
            GL.Vertex3(p.X - size, p.Y, p.Z); GL.Vertex3(p.X + size, p.Y, p.Z);
            GL.Vertex3(p.X, p.Y, p.Z - size); GL.Vertex3(p.X, p.Y, p.Z + size);
            GL.End();
        }

        private static void DrawLine(Vector3 a, Vector3 b, float r, float g, float blue)
        {
            GL.LineWidth(2f);
            GL.Begin(PrimitiveType.Lines);
            GL.Color3(r, g, blue);
            GL.Vertex3(a); GL.Vertex3(b);
            GL.End();
        }
    }
}