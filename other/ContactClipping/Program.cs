using System;
using System.Numerics;
using Raylib_cs;

namespace JitterClipVisualizer;

internal static class Program
{
    private static readonly Color Background = new(246, 241, 233, 255);
    private static readonly Color PanelFill = new(255, 252, 246, 255);
    private static readonly Color PanelBorder = new(205, 191, 170, 255);
    private static readonly Color Ink = new(37, 45, 57, 255);
    private static readonly Color Muted = new(110, 120, 132, 255);
    private static readonly Color SubjectFill = new(35, 101, 176, 64);
    private static readonly Color SubjectStroke = new(35, 101, 176, 255);
    private static readonly Color ClipFill = new(230, 142, 33, 64);
    private static readonly Color ClipStroke = new(230, 142, 33, 255);
    private static readonly Color RawContactFill = new(61, 129, 96, 84);
    private static readonly Color RawContactStroke = new(61, 129, 96, 255);
    private static readonly Color OutputFill = new(37, 151, 92, 88);
    private static readonly Color OutputStroke = new(37, 151, 92, 255);
    private static readonly Color InputStroke = new(118, 128, 140, 255);
    private static readonly Color CurrentEdge = new(198, 61, 63, 255);
    private static readonly Color SecondaryEdge = new(157, 108, 38, 255);

    private static int presetIndex;
    private static int stepIndex;
    private static float sceneTime;
    private static float stepTimer;
    private static bool animateGeometry = true;
    private static bool animateSteps = true;

    private static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(1500, 940, "Jitter2 ContactManifold Visualizer");
        Raylib.SetTargetFPS(60);

        var presets = ContactManifoldClipDebugger.CreatePresets();

        while (!Raylib.WindowShouldClose())
        {
            float deltaTime = Raylib.GetFrameTime();

            HandleInput(presets.Count);

            if (animateGeometry)
            {
                sceneTime += deltaTime;
            }

            ClipSnapshot snapshot = ContactManifoldClipDebugger.BuildSnapshot(presets[presetIndex], sceneTime);
            UpdateCurrentStep(snapshot, deltaTime);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Background);

            DrawScene(snapshot);

            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }

    private static void HandleInput(int presetCount)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            animateSteps = !animateSteps;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.A))
        {
            animateGeometry = !animateGeometry;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.R))
        {
            sceneTime = 0.0f;
            stepIndex = 0;
            stepTimer = 0.0f;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            presetIndex = (presetIndex + 1) % presetCount;
            stepIndex = 0;
            stepTimer = 0.0f;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.One))
        {
            presetIndex = 0;
            stepIndex = 0;
            stepTimer = 0.0f;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Two) && presetCount > 1)
        {
            presetIndex = 1;
            stepIndex = 0;
            stepTimer = 0.0f;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Three) && presetCount > 2)
        {
            presetIndex = 2;
            stepIndex = 0;
            stepTimer = 0.0f;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Right))
        {
            animateSteps = false;
            stepIndex += 1;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Left))
        {
            animateSteps = false;
            stepIndex -= 1;
        }
    }

    private static void UpdateCurrentStep(ClipSnapshot snapshot, float deltaTime)
    {
        int stepCount = Math.Max(snapshot.Steps.Length, 1);

        if (animateSteps && stepCount > 1)
        {
            stepTimer += deltaTime;

            if (stepTimer >= 0.95f)
            {
                stepTimer -= 0.95f;
                stepIndex = (stepIndex + 1) % stepCount;
            }
        }

        if (stepIndex < 0)
        {
            stepIndex = stepCount - 1;
        }

        if (stepIndex >= stepCount)
        {
            stepIndex = 0;
        }
    }

    private static void DrawScene(ClipSnapshot snapshot)
    {
        int width = Raylib.GetScreenWidth();
        int height = Raylib.GetScreenHeight();

        const float outerMargin = 24.0f;
        const float columnGap = 18.0f;
        const float panelTop = 124.0f;
        const float panelBottom = 88.0f;
        float panelWidth = (width - outerMargin * 2.0f - columnGap * 2.0f) / 3.0f;
        float panelHeight = height - panelTop - panelBottom;

        Rectangle leftPanel = new(outerMargin, panelTop, panelWidth, panelHeight);
        Rectangle centerPanel = new(outerMargin + panelWidth + columnGap, panelTop, panelWidth, panelHeight);
        Rectangle rightPanel = new(outerMargin + (panelWidth + columnGap) * 2.0f, panelTop, panelWidth, panelHeight);

        ClipStep step = snapshot.Steps[stepIndex];
        WorldBounds bounds = CollectBounds(snapshot, step);

        DrawHeader(snapshot);

        DrawPanel(leftPanel, "Sampled Features");
        DrawPanel(centerPanel, "Current Step");
        DrawPanel(rightPanel, "Solver Manifold");

        DrawInputPanel(leftPanel, bounds, snapshot);
        DrawStepPanel(centerPanel, bounds, snapshot, step);
        DrawResultPanel(rightPanel, bounds, snapshot);

        DrawFooter(snapshot);
    }

    private static void DrawHeader(ClipSnapshot snapshot)
    {
        Raylib.DrawText(snapshot.PresetName, 28, 18, 34, Ink);
        Raylib.DrawText(snapshot.Description, 28, 57, 22, Muted);

        string source = "Mirrors the current sampled-feature manifold builder in src/Jitter2/Collision/NarrowPhase/CollisionManifold.cs";
        Raylib.DrawText(source, 28, 85, 18, Muted);

        string status = $"{ContactManifoldClipDebugger.GetModeLabel(snapshot.Mode)} | {snapshot.Status}";
        Raylib.DrawText(status, 28, 106, 18, Ink);
    }

    private static void DrawFooter(ClipSnapshot snapshot)
    {
        int y = Raylib.GetScreenHeight() - 54;
        string controls = "Tab/1-3 preset   Left/Right step   Space autoplay steps   A animate geometry   R reset time   Esc quit";
        string step = $"Step {stepIndex + 1}/{Math.Max(snapshot.Steps.Length, 1)}";

        Raylib.DrawText(controls, 28, y, 18, Muted);
        Raylib.DrawText(step, Raylib.GetScreenWidth() - 170, y, 18, Ink);
    }

    private static void DrawPanel(Rectangle rect, string title)
    {
        Raylib.DrawRectangleRounded(rect, 0.03f, 8, PanelFill);
        Raylib.DrawRectangleRoundedLinesEx(rect, 0.03f, 8, 1.4f, PanelBorder);
        Raylib.DrawText(title, (int)rect.X + 16, (int)rect.Y + 12, 24, Ink);
    }

    private static void DrawInputPanel(Rectangle rect, WorldBounds bounds, ClipSnapshot snapshot)
    {
        Rectangle plot = GetPlotRect(rect);

        DrawGrid(plot, bounds);
        DrawFeature(plot, bounds, snapshot.Left, snapshot.Left.Length > 2, SubjectFill, SubjectStroke, 2.4f);
        DrawFeature(plot, bounds, snapshot.Right, snapshot.Right.Length > 2, ClipFill, ClipStroke, 2.4f);
        DrawContacts(plot, bounds, snapshot.RawContacts, RawContactFill, RawContactStroke, false, 5.6f, 2.4f);

        DrawLegend((int)rect.X + 16, (int)rect.Y + 46, SubjectStroke, "shape A");
        DrawLegend((int)rect.X + 126, (int)rect.Y + 46, ClipStroke, "shape B");
        DrawLegend((int)rect.X + 236, (int)rect.Y + 46, RawContactStroke, "raw contacts");
    }

    private static void DrawStepPanel(Rectangle rect, WorldBounds bounds, ClipSnapshot snapshot, ClipStep step)
    {
        Rectangle plot = GetPlotRect(rect);
        DrawGrid(plot, bounds);

        DrawFeature(plot, bounds, snapshot.Left, snapshot.Left.Length > 2,
            new Color(0, 0, 0, 0), Raylib.Fade(SubjectStroke, 0.55f), 2.0f);
        DrawFeature(plot, bounds, snapshot.Right, snapshot.Right.Length > 2,
            new Color(0, 0, 0, 0), Raylib.Fade(ClipStroke, 0.55f), 2.0f);

        if (step.HasSecondaryEdge)
        {
            DrawEdge(plot, bounds, step.SecondaryEdgeStart, step.SecondaryEdgeEnd, SecondaryEdge, 4.0f);
        }

        if (step.HasPrimaryEdge)
        {
            DrawEdge(plot, bounds, step.PrimaryEdgeStart, step.PrimaryEdgeEnd, CurrentEdge, 4.0f);
        }

        if (step.ContactSet.Length > 0)
        {
            DrawContacts(plot, bounds, step.ContactSet,
                Raylib.Fade(RawContactFill, 0.95f), RawContactStroke, false, 5.4f, 2.0f);
        }

        if (step.Probe.Length > 0)
        {
            DrawFeature(plot, bounds, step.Probe, step.Probe.Length > 2,
                new Color(0, 0, 0, 0), InputStroke, 2.6f);
        }

        if (step.Accepted.Length > 0)
        {
            DrawContacts(plot, bounds, step.Accepted, OutputFill, OutputStroke,
                step.ConnectAccepted, 6.4f, 3.0f);
        }

        int textX = (int)rect.X + 16;
        int textY = (int)(rect.Y + rect.Height) - 104;
        Raylib.DrawText(step.Title, textX, textY, 22, Ink);
        Raylib.DrawText(step.Summary, textX, textY + 28, 18, Muted);
    }

    private static void DrawResultPanel(Rectangle rect, WorldBounds bounds, ClipSnapshot snapshot)
    {
        Rectangle plot = GetPlotRect(rect);
        DrawGrid(plot, bounds);

        DrawFeature(plot, bounds, snapshot.Left, snapshot.Left.Length > 2,
            new Color(0, 0, 0, 0), Raylib.Fade(SubjectStroke, 0.5f), 1.8f);
        DrawFeature(plot, bounds, snapshot.Right, snapshot.Right.Length > 2,
            new Color(0, 0, 0, 0), Raylib.Fade(ClipStroke, 0.5f), 1.8f);
        DrawContacts(plot, bounds, snapshot.RawContacts,
            Raylib.Fade(RawContactFill, 0.95f), RawContactStroke, false, 5.4f, 2.0f);
        DrawContacts(plot, bounds, snapshot.Result, OutputFill, OutputStroke, snapshot.Result.Length > 1, 6.8f, 3.0f);

        string label = snapshot.Result.Length switch
        {
            0 => "runtime uses the seed pair",
            1 => "1 solver contact",
            _ => $"{snapshot.Result.Length} solver contacts"
        };

        string detail = $"{snapshot.RawContacts.Length} raw candidate(s)";

        Raylib.DrawText(detail, (int)rect.X + 16, (int)(rect.Y + rect.Height) - 82, 18, Muted);
        Raylib.DrawText(label, (int)rect.X + 16, (int)(rect.Y + rect.Height) - 54, 20, Ink);
    }

    private static void DrawGrid(Rectangle plot, WorldBounds bounds)
    {
        Raylib.DrawRectangleRounded(plot, 0.02f, 6, new Color(250, 247, 241, 255));

        Vector2 left = WorldToScreen(plot, bounds, new ClipPoint(bounds.MinX, 0.0f));
        Vector2 right = WorldToScreen(plot, bounds, new ClipPoint(bounds.MaxX, 0.0f));
        Vector2 top = WorldToScreen(plot, bounds, new ClipPoint(0.0f, bounds.MaxY));
        Vector2 bottom = WorldToScreen(plot, bounds, new ClipPoint(0.0f, bounds.MinY));

        Raylib.DrawLineEx(left, right, 1.0f, new Color(226, 219, 206, 255));
        Raylib.DrawLineEx(top, bottom, 1.0f, new Color(226, 219, 206, 255));
    }

    private static void DrawLegend(int x, int y, Color color, string text)
    {
        Raylib.DrawRectangle(x, y + 3, 16, 10, color);
        Raylib.DrawText(text, x + 22, y, 18, Muted);
    }

    private static Rectangle GetPlotRect(Rectangle panel)
    {
        return new Rectangle(panel.X + 14.0f, panel.Y + 78.0f, panel.Width - 28.0f, panel.Height - 182.0f);
    }

    private static void DrawFeature(Rectangle plot, WorldBounds bounds, ClipPoint[] feature,
        bool closeLoop, Color fill, Color stroke, float thickness)
    {
        if (feature.Length == 0) return;

        Vector2[] vertices = new Vector2[feature.Length];

        for (int i = 0; i < feature.Length; i++)
        {
            vertices[i] = WorldToScreen(plot, bounds, feature[i]);
        }

        if (closeLoop && feature.Length >= 3 && fill.A > 0)
        {
            for (int i = 1; i < vertices.Length - 1; i++)
            {
                Raylib.DrawTriangle(vertices[0], vertices[i], vertices[i + 1], fill);
            }
        }

        if (feature.Length == 1)
        {
            Raylib.DrawCircleV(vertices[0], 6.0f, stroke);
            return;
        }

        for (int i = 0; i < vertices.Length - 1; i++)
        {
            Raylib.DrawLineEx(vertices[i], vertices[i + 1], thickness, stroke);
        }

        if (closeLoop && feature.Length >= 3)
        {
            Raylib.DrawLineEx(vertices[^1], vertices[0], thickness, stroke);
        }

        foreach (Vector2 vertex in vertices)
        {
            Raylib.DrawCircleV(vertex, 4.5f, stroke);
            Raylib.DrawCircleV(vertex, 2.2f, PanelFill);
        }
    }

    private static void DrawContacts(Rectangle plot, WorldBounds bounds, ClipPoint[] contacts,
        Color fill, Color stroke, bool connect, float radius, float thickness)
    {
        if (contacts.Length == 0) return;

        Vector2[] vertices = new Vector2[contacts.Length];

        for (int i = 0; i < contacts.Length; i++)
        {
            vertices[i] = WorldToScreen(plot, bounds, contacts[i]);
        }

        if (connect && contacts.Length > 1)
        {
            for (int i = 0; i < vertices.Length - 1; i++)
            {
                Raylib.DrawLineEx(vertices[i], vertices[i + 1], thickness, stroke);
            }

            if (contacts.Length > 2)
            {
                Raylib.DrawLineEx(vertices[^1], vertices[0], thickness, stroke);
            }
        }

        foreach (Vector2 vertex in vertices)
        {
            Raylib.DrawCircleV(vertex, radius, fill);
            Raylib.DrawCircleV(vertex, radius - 1.8f, stroke);
            Raylib.DrawCircleV(vertex, radius - 3.4f, PanelFill);
        }
    }

    private static void DrawEdge(Rectangle plot, WorldBounds bounds, ClipPoint start, ClipPoint end, Color color, float thickness)
    {
        Vector2 a = WorldToScreen(plot, bounds, start);
        Vector2 b = WorldToScreen(plot, bounds, end);
        Raylib.DrawLineEx(a, b, thickness, color);
        Raylib.DrawCircleV(a, 5.0f, color);
        Raylib.DrawCircleV(b, 5.0f, color);
    }

    private static Vector2 WorldToScreen(Rectangle plot, WorldBounds bounds, ClipPoint point)
    {
        float rangeX = MathF.Max(bounds.MaxX - bounds.MinX, 1.0f);
        float rangeY = MathF.Max(bounds.MaxY - bounds.MinY, 1.0f);
        float scale = MathF.Min((plot.Width - 36.0f) / rangeX, (plot.Height - 36.0f) / rangeY);

        float centerX = 0.5f * (bounds.MinX + bounds.MaxX);
        float centerY = 0.5f * (bounds.MinY + bounds.MaxY);

        return new Vector2(
            plot.X + plot.Width * 0.5f + (point.X - centerX) * scale,
            plot.Y + plot.Height * 0.5f - (point.Y - centerY) * scale);
    }

    private static WorldBounds CollectBounds(ClipSnapshot snapshot, ClipStep step)
    {
        WorldBounds bounds = new(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);

        Expand(ref bounds, snapshot.Left);
        Expand(ref bounds, snapshot.Right);
        Expand(ref bounds, snapshot.RawContacts);
        Expand(ref bounds, snapshot.Result);
        Expand(ref bounds, step.Probe);
        Expand(ref bounds, step.Accepted);
        Expand(ref bounds, step.ContactSet);

        if (step.HasPrimaryEdge)
        {
            Expand(ref bounds, step.PrimaryEdgeStart);
            Expand(ref bounds, step.PrimaryEdgeEnd);
        }

        if (step.HasSecondaryEdge)
        {
            Expand(ref bounds, step.SecondaryEdgeStart);
            Expand(ref bounds, step.SecondaryEdgeEnd);
        }

        if (bounds.MinX == float.MaxValue)
        {
            bounds = new WorldBounds(-120.0f, -120.0f, 120.0f, 120.0f);
        }

        float sizeX = bounds.MaxX - bounds.MinX;
        float sizeY = bounds.MaxY - bounds.MinY;
        float pad = MathF.Max(MathF.Max(sizeX, sizeY) * 0.16f, 24.0f);

        return new WorldBounds(bounds.MinX - pad, bounds.MinY - pad, bounds.MaxX + pad, bounds.MaxY + pad);
    }

    private static void Expand(ref WorldBounds bounds, ClipPoint point)
    {
        bounds.MinX = MathF.Min(bounds.MinX, point.X);
        bounds.MinY = MathF.Min(bounds.MinY, point.Y);
        bounds.MaxX = MathF.Max(bounds.MaxX, point.X);
        bounds.MaxY = MathF.Max(bounds.MaxY, point.Y);
    }

    private static void Expand(ref WorldBounds bounds, ClipPoint[] points)
    {
        foreach (ClipPoint point in points)
        {
            Expand(ref bounds, point);
        }
    }

    private struct WorldBounds(float minX, float minY, float maxX, float maxY)
    {
        public float MinX = minX;
        public float MinY = minY;
        public float MaxX = maxX;
        public float MaxY = maxY;
    }
}
