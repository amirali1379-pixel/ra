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
        internal Vector3 StartPoint { get; set; }
        internal Vector3 TargetPoint { get; set; }
        internal Vector3 CalibrationRight { get; set; } = Vector3.UnitX;
        internal Vector3 CalibrationUp { get; set; } = Vector3.UnitY;
        internal bool ShowStart { get; set; }
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

            DrawIndoorRange();
            if (ShowStart)
                DrawCalibrationCircle(StartPoint, CalibrationRight, CalibrationUp, 0.20f, 0.25f, 0.95f, 0.55f);
            if (ShowTarget)
                DrawCalibrationCircle(TargetPoint, CalibrationRight, CalibrationUp, 0.32f, 1.0f, 0.55f, 0.18f);
            DrawScreenCrosshair();
            SwapBuffers();
        }


        // Draw a fixed screen-space reticle after the perspective scene so it stays
        // centered regardless of camera rotation or the world-space cursor marker.
        private void DrawScreenCrosshair()
        {
            GL.Disable(EnableCap.DepthTest);
            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.Ortho(0, Width, Height, 0, -1, 1);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadIdentity();

            float cx = Width * 0.5f;
            float cy = Height * 0.5f;
            const float arm = 9f;
            const float gap = 4f;

            GL.LineWidth(2f);
            GL.Begin(PrimitiveType.Lines);
            GL.Color4(0f, 0f, 0f, 0.9f);
            GL.Vertex2(cx - arm - 1f, cy); GL.Vertex2(cx - gap + 1f, cy);
            GL.Vertex2(cx + gap - 1f, cy); GL.Vertex2(cx + arm + 1f, cy);
            GL.Vertex2(cx, cy - arm - 1f); GL.Vertex2(cx, cy - gap + 1f);
            GL.Vertex2(cx, cy + gap - 1f); GL.Vertex2(cx, cy + arm + 1f);
            GL.Color4(0.9f, 0.98f, 1f, 1f);
            GL.Vertex2(cx - arm, cy); GL.Vertex2(cx - gap, cy);
            GL.Vertex2(cx + gap, cy); GL.Vertex2(cx + arm, cy);
            GL.Vertex2(cx, cy - arm); GL.Vertex2(cx, cy - gap);
            GL.Vertex2(cx, cy + gap); GL.Vertex2(cx, cy + arm);
            GL.End();

            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Projection);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview);
            GL.Enable(EnableCap.DepthTest);
        }

        // Compact indoor FPS range: floor, side walls, ceiling beams and a target wall.
        // Coordinates are world-space; the camera remains at player eye height.
        private void DrawIndoorRange()
        {
            const float halfWidth = 18f, length = 42f, wallHeight = 8f;
            GL.Begin(PrimitiveType.Quads);
            GL.Color3(0.10f, 0.12f, 0.13f);
            GL.Vertex3(-halfWidth, 0f, 3f); GL.Vertex3(halfWidth, 0f, 3f);
            GL.Vertex3(halfWidth, 0f, -length); GL.Vertex3(-halfWidth, 0f, -length);
            GL.Color3(0.16f, 0.18f, 0.19f);
            GL.Vertex3(-halfWidth, wallHeight, 3f); GL.Vertex3(-halfWidth, wallHeight, -length);
            GL.Vertex3(-halfWidth, 0f, -length); GL.Vertex3(-halfWidth, 0f, 3f);
            GL.Vertex3(halfWidth, wallHeight, -length); GL.Vertex3(halfWidth, wallHeight, 3f);
            GL.Vertex3(halfWidth, 0f, 3f); GL.Vertex3(halfWidth, 0f, -length);
            GL.Color3(0.12f, 0.14f, 0.15f);
            GL.Vertex3(-halfWidth, wallHeight, 3f); GL.Vertex3(halfWidth, wallHeight, 3f);
            GL.Vertex3(halfWidth, wallHeight, -length); GL.Vertex3(-halfWidth, wallHeight, -length);
            GL.Color3(0.24f, 0.26f, 0.27f);
            GL.Vertex3(-halfWidth, 0f, -length); GL.Vertex3(halfWidth, 0f, -length);
            GL.Vertex3(halfWidth, wallHeight, -length); GL.Vertex3(-halfWidth, wallHeight, -length);
            GL.End();

            // Subtle lane seams and ceiling lights establish depth without a circular HUD/floor.
            GL.LineWidth(1f);
            GL.Begin(PrimitiveType.Lines);
            for (int z = 0; z >= -40; z -= 4)
            {
                GL.Color3(0.20f, 0.24f, 0.25f);
                GL.Vertex3(-halfWidth, 0.012f, z); GL.Vertex3(halfWidth, 0.012f, z);
            }
            for (int z = -4; z >= -36; z -= 8)
            {
                GL.Color3(0.34f, 0.37f, 0.37f);
                GL.Vertex3(-halfWidth, 7.8f, z); GL.Vertex3(halfWidth, 7.8f, z);
            }
            GL.End();

            GL.Begin(PrimitiveType.Quads);
            for (int z = -8; z >= -36; z -= 8)
            {
                GL.Color4(0.72f, 0.78f, 0.78f, 0.65f);
                GL.Vertex3(-4f, 7.72f, z); GL.Vertex3(4f, 7.72f, z);
                GL.Vertex3(4f, 7.72f, z - 0.45f); GL.Vertex3(-4f, 7.72f, z - 0.45f);
            }
            GL.End();
        }

        // A world-space ring on the original calibration plane. Both markers stay fixed
        // while the camera rotates, so the crosshair can move over them naturally.
        private static void DrawCalibrationCircle(Vector3 center, Vector3 right, Vector3 up,
            float radius, float red, float green, float blue)
        {
            Vector3 normal = Vector3.Normalize(Vector3.Cross(right, up));
            center += normal * 0.035f;
            GL.LineWidth(3f);
            GL.Begin(PrimitiveType.LineLoop);
            GL.Color3(red, green, blue);
            for (int i = 0; i < 64; i++)
            {
                float angle = (float)(i * Math.PI * 2.0 / 64.0);
                Vector3 p = center + right * ((float)Math.Cos(angle) * radius)
                    + up * ((float)Math.Sin(angle) * radius);
                GL.Vertex3(p);
            }
            GL.End();

            GL.PointSize(5f);
            GL.Begin(PrimitiveType.Points);
            GL.Color3(red, green, blue);
            GL.Vertex3(center);
            GL.End();
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