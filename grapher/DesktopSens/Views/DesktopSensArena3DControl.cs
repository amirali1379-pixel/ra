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
        internal Vector3 ViewDirection { get; set; } = new Vector3(0f, 0f, -1f);
        internal float Radius { get; set; } = 10f;
        internal Vector3 StartPoint { get; set; }
        internal Vector3 TargetPoint { get; set; }
        internal Vector3 CalibrationRight { get; set; } = Vector3.UnitX;
        internal Vector3 CalibrationForward { get; set; } = new Vector3(0f, 0f, -1f);
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

            // Use the same calibration-relative direction as target construction.
            // Rebuilding look from absolute world yaw caused the reticle to point away
            // from the recorded calibration ray after the first mouse movement.
            Vector3 look = ViewDirection.LengthSquared > 0.0001f
                ? Vector3.Normalize(ViewDirection)
                : new Vector3(0f, 0f, -1f);
            Vector3 eye = new Vector3(0f, 1.7f, 0f);

            // FPS camera must stay upright in world space. Project world-up onto
            // the plane perpendicular to look so yaw never introduces camera roll.
            Vector3 worldUp = Vector3.UnitY;
            Vector3 up = worldUp - look * Vector3.Dot(worldUp, look);
            if (up.LengthSquared < 0.0001f)
            {
                Vector3 fallback = Vector3.UnitZ;
                up = fallback - look * Vector3.Dot(fallback, look);
            }
            up = Vector3.Normalize(up);
            Matrix4 view = Matrix4.LookAt(eye, eye + look, up);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadMatrix(ref view);

            DrawIndoorRange();
            DrawAzimuthTicks();
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

        // Open 360-degree training world: no finite wall or ceiling edge.
        // The floor is a large, seamless circular grid; a sky dome supplies a stable
        // horizon in every direction without creating a box-room cutoff.
        private void DrawIndoorRange()
        {
            const float floorRadius = 180f;
            const int segments = 180;

            GL.Begin(PrimitiveType.TriangleFan);
            GL.Color3(0.075f, 0.09f, 0.10f);
            GL.Vertex3(0f, 0f, 0f);
            for (int i = 0; i <= segments; i++)
            {
                double a = i * Math.PI * 2.0 / segments;
                GL.Vertex3((float)Math.Cos(a) * floorRadius, 0f,
                    (float)Math.Sin(a) * floorRadius);
            }
            GL.End();

            GL.LineWidth(1f);
            GL.Begin(PrimitiveType.Lines);
            // Ground rings continue far beyond the visible training range.
            for (int ring = 4; ring <= 176; ring += 4)
            {
                for (int i = 0; i < segments; i++)
                {
                    double a1 = i * Math.PI * 2.0 / segments;
                    double a2 = (i + 1) * Math.PI * 2.0 / segments;
                    GL.Color3(ring % 20 == 0 ? 0.22f : 0.13f,
                        ring % 20 == 0 ? 0.27f : 0.17f, 0.18f);
                    GL.Vertex3((float)Math.Cos(a1) * ring, 0.012f,
                        (float)Math.Sin(a1) * ring);
                    GL.Vertex3((float)Math.Cos(a2) * ring, 0.012f,
                        (float)Math.Sin(a2) * ring);
                }
            }
            // Radial guides provide orientation without enclosing the player.
            for (int degree = 0; degree < 360; degree += 15)
            {
                double a = degree * Math.PI / 180.0;
                GL.Color3(degree % 45 == 0 ? 0.23f : 0.14f,
                    degree % 45 == 0 ? 0.28f : 0.18f, 0.20f);
                GL.Vertex3(0f, 0.018f, 0f);
                GL.Vertex3((float)Math.Cos(a) * floorRadius, 0.018f,
                    (float)Math.Sin(a) * floorRadius);
            }
            GL.End();

            // Spherical sky grid: it has no top edge, so looking up never reveals
            // the end of a short room. The lower hemisphere is omitted intentionally.
            const float skyRadius = 150f;
            const int azimuthSegments = 72;
            const int elevationSegments = 9;
            GL.Begin(PrimitiveType.Lines);
            for (int elevation = 1; elevation <= elevationSegments; elevation++)
            {
                double elev = elevation * (Math.PI * 0.5) / (elevationSegments + 1);
                for (int i = 0; i < azimuthSegments; i += 1)
                {
                    double a1 = i * Math.PI * 2.0 / azimuthSegments;
                    double a2 = (i + 1) * Math.PI * 2.0 / azimuthSegments;
                    float y = (float)(Math.Sin(elev) * skyRadius);
                    float r = (float)(Math.Cos(elev) * skyRadius);
                    GL.Color3(0.12f, 0.17f, 0.20f);
                    GL.Vertex3((float)Math.Cos(a1) * r, y, (float)Math.Sin(a1) * r);
                    GL.Vertex3((float)Math.Cos(a2) * r, y, (float)Math.Sin(a2) * r);
                }
            }
            for (int degree = 0; degree < 360; degree += 15)
            {
                double a = degree * Math.PI / 180.0;
                for (int elevation = 0; elevation < elevationSegments; elevation++)
                {
                    double e1 = elevation * (Math.PI * 0.5) / elevationSegments;
                    double e2 = (elevation + 1) * (Math.PI * 0.5) / elevationSegments;
                    GL.Color3(0.12f, 0.17f, 0.20f);
                    GL.Vertex3((float)(Math.Cos(a) * Math.Cos(e1) * skyRadius),
                        (float)(Math.Sin(e1) * skyRadius),
                        (float)(Math.Sin(a) * Math.Cos(e1) * skyRadius));
                    GL.Vertex3((float)(Math.Cos(a) * Math.Cos(e2) * skyRadius),
                        (float)(Math.Sin(e2) * skyRadius),
                        (float)(Math.Sin(a) * Math.Cos(e2) * skyRadius));
                }
            }
            GL.End();
        }

        // Azimuth graduations around the cylindrical arena. 0° is the calibration
        // forward direction; major marks every 30° make the full 360° reference visible.
        private void DrawAzimuthTicks()
        {
            // Azimuth is a horizontal world-space reference, not part of the pitched
            // calibration plane. Using CalibrationForward/Right directly here carried
            // their Y component into the floor ring, making the degree axis appear tilted
            // whenever calibration was captured while looking up/down.
            Vector3 forward = new Vector3(CalibrationForward.X, 0f, CalibrationForward.Z);
            if (forward.LengthSquared < 0.0001f)
                forward = new Vector3(0f, 0f, -1f);
            forward = Vector3.Normalize(forward);

            // Build a level right vector from the horizontal forward direction.
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            const float innerRadius = 20.5f;
            const float outerMinor = 21.0f;
            const float outerMajor = 21.8f;
            GL.LineWidth(2f);
            GL.Begin(PrimitiveType.Lines);
            for (int degree = 0; degree < 360; degree += 10)
            {
                double radians = degree * Math.PI / 180.0;
                Vector3 direction = Vector3.Normalize(
                    forward * (float)Math.Cos(radians) +
                    right * (float)Math.Sin(radians));
                float outer = degree % 30 == 0 ? outerMajor : outerMinor;
                if (degree == 0) GL.Color3(0.25f, 0.95f, 0.62f);
                else if (degree % 30 == 0) GL.Color3(0.95f, 0.72f, 0.25f);
                else GL.Color3(0.36f, 0.48f, 0.53f);
                GL.Vertex3(direction.X * innerRadius, 0.03f, direction.Z * innerRadius);
                GL.Vertex3(direction.X * outer, 0.03f, direction.Z * outer);
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