using System;
using System.Collections.Generic;
using System.Numerics;

namespace JitterClipVisualizer;

internal readonly record struct ClipPoint(float X, float Y)
{
    public static ClipPoint operator +(ClipPoint left, ClipPoint right)
    {
        return new ClipPoint(left.X + right.X, left.Y + right.Y);
    }

    public static ClipPoint operator -(ClipPoint left, ClipPoint right)
    {
        return new ClipPoint(left.X - right.X, left.Y - right.Y);
    }

    public static ClipPoint operator *(float scale, ClipPoint point)
    {
        return new ClipPoint(scale * point.X, scale * point.Y);
    }

    public float LengthSquared()
    {
        return X * X + Y * Y;
    }

    public static float Dot(in ClipPoint left, in ClipPoint right)
    {
        return left.X * right.X + left.Y * right.Y;
    }
}

internal enum ClipDebugMode
{
    PolygonVsPolygon,
    PointVsPolygon,
    SegmentVsPolygon,
    SegmentVsSegment,
    SeedFallback,
    InvalidInput
}

internal sealed record ClipStep(
    string Title,
    string Summary,
    ClipPoint[] Probe,
    ClipPoint[] Accepted,
    ClipPoint[] ContactSet,
    bool ConnectAccepted = false,
    bool HasPrimaryEdge = false,
    ClipPoint PrimaryEdgeStart = default,
    ClipPoint PrimaryEdgeEnd = default,
    bool HasSecondaryEdge = false,
    ClipPoint SecondaryEdgeStart = default,
    ClipPoint SecondaryEdgeEnd = default);

internal sealed record ClipSnapshot(
    string PresetName,
    string Description,
    ClipDebugMode Mode,
    string Status,
    ClipPoint[] Left,
    ClipPoint[] Right,
    ClipPoint[] RawContacts,
    ClipPoint[] Result,
    ClipStep[] Steps);

internal sealed record ClipFrame(ClipPoint[] Left, ClipPoint[] Right);

internal sealed record ClipPreset(string Name, string Description, Func<float, ClipFrame> BuildFrame);

internal static class ContactManifoldClipDebugger
{
    private const int MaxSupportPoints = 6;
    private const int MaxManifoldPoints = 12;
    private const int SolverContactLimit = 4;
    private const float DuplicatePointDistanceSq = 0.001f;
    private const float Epsilon = 1e-3f;
    private const float AreaSignEpsilon = 1e-6f;

    private static readonly ClipPoint[] polygonA =
    [
        new ClipPoint(100.0f, 0.0f),
        new ClipPoint(50.0f, 86.0f),
        new ClipPoint(-50.0f, 86.0f),
        new ClipPoint(-100.0f, 0.0f),
        new ClipPoint(-50.0f, -86.0f),
        new ClipPoint(50.0f, -86.0f)
    ];

    private static readonly ClipPoint[] polygonB =
    [
        new ClipPoint(118.0f, 0.0f),
        new ClipPoint(36.0f, 110.0f),
        new ClipPoint(-96.0f, 68.0f),
        new ClipPoint(-96.0f, -68.0f),
        new ClipPoint(36.0f, -110.0f)
    ];

    private static readonly ClipPoint[] lineA =
    [
        new ClipPoint(-165.0f, 0.0f),
        new ClipPoint(165.0f, 0.0f)
    ];

    private static readonly ClipPoint[] lineB =
    [
        new ClipPoint(-130.0f, 0.0f),
        new ClipPoint(130.0f, 0.0f)
    ];

    public static IReadOnlyList<ClipPreset> CreatePresets()
    {
        return
        [
            new ClipPreset(
                "Polygon vs Polygon",
                "Shows inside-point checks plus explicit edge-edge intersections for two sampled face loops.",
                BuildPolygonVsPolygonFrame),
            new ClipPreset(
                "Segment vs Polygon",
                "Shows the segment-against-polygon fallback when one sampled feature collapses to a line.",
                BuildSegmentVsPolygonFrame),
            new ClipPreset(
                "Segment vs Segment",
                "Shows the segment helper, including the collinear overlap case used by the manifold builder.",
                BuildSegmentVsSegmentFrame)
        ];
    }

    public static ClipSnapshot BuildSnapshot(ClipPreset preset, float time)
    {
        ClipFrame frame = preset.BuildFrame(time);
        return BuildSnapshot(preset.Name, preset.Description, frame.Left, frame.Right);
    }

    public static ClipSnapshot BuildSnapshot(string presetName, string description, ClipPoint[] leftInput, ClipPoint[] rightInput)
    {
        if (leftInput.Length == 0 || rightInput.Length == 0)
        {
            return new ClipSnapshot(
                presetName,
                description,
                ClipDebugMode.InvalidInput,
                "Both sampled features need at least one point.",
                leftInput,
                rightInput,
                [],
                [],
                [
                    new ClipStep(
                        "Invalid input",
                        "At least one sampled feature was empty.",
                        [],
                        [],
                        [])
                ]);
        }

        if (leftInput.Length > MaxSupportPoints || rightInput.Length > MaxSupportPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(leftInput),
                $"This visualizer mirrors the manifold sampler and supports up to {MaxSupportPoints} support points per feature.");
        }

        ClipPoint[] left = Copy(leftInput, leftInput.Length);
        ClipPoint[] right = Copy(rightInput, rightInput.Length);
        int leftCount = left.Length;
        int rightCount = right.Length;

        List<ClipStep> steps = [];
        ClipPoint[] contacts = new ClipPoint[MaxManifoldPoints];
        int rawCount = 0;
        bool saturated = false;

        float leftSign = 0.0f;
        float rightSign = 0.0f;
        bool leftPolygon = leftCount > 2 && TryGetPolygonSign(left, leftCount, out leftSign);
        bool rightPolygon = rightCount > 2 && TryGetPolygonSign(right, rightCount, out rightSign);

        steps.Add(new ClipStep(
            "Classify sampled features",
            $"{DescribeFeature('A', leftCount, leftPolygon)} {DescribeFeature('B', rightCount, rightPolygon)}",
            [],
            [],
            []));

        int insideFromRight = 0;
        int insideFromLeft = 0;
        int clippedFromRight = 0;
        int clippedFromLeft = 0;
        int segmentHits = 0;
        int edgeHits = 0;

        if (leftPolygon)
        {
            ClipPoint[] added = CollectInsideContacts(right, rightCount, left, leftCount, leftSign,
                contacts, ref rawCount, out int candidateCount, out saturated);
            insideFromRight = added.Length;

            steps.Add(new ClipStep(
                "Check B points inside A",
                DescribeCollection("B-in-A point tests", candidateCount, added.Length),
                Copy(right, rightCount),
                added,
                Copy(contacts, rawCount)));

            if (saturated) goto Finalize;

            if (rightCount == 2)
            {
                added = CollectSegmentClipContacts(right[0], right[1], left, leftCount, leftSign,
                    contacts, ref rawCount, out candidateCount, out saturated);
                clippedFromRight = added.Length;

                steps.Add(new ClipStep(
                    "Clip B segment against A",
                    DescribeCollection("Segment-against-polygon fallback", candidateCount, added.Length),
                    Copy(right, rightCount),
                    added,
                    Copy(contacts, rawCount),
                    ConnectAccepted: added.Length == 2));

                if (saturated) goto Finalize;
            }
        }

        if (rightPolygon)
        {
            ClipPoint[] added = CollectInsideContacts(left, leftCount, right, rightCount, rightSign,
                contacts, ref rawCount, out int candidateCount, out saturated);
            insideFromLeft = added.Length;

            steps.Add(new ClipStep(
                "Check A points inside B",
                DescribeCollection("A-in-B point tests", candidateCount, added.Length),
                Copy(left, leftCount),
                added,
                Copy(contacts, rawCount)));

            if (saturated) goto Finalize;

            if (leftCount == 2)
            {
                added = CollectSegmentClipContacts(left[0], left[1], right, rightCount, rightSign,
                    contacts, ref rawCount, out candidateCount, out saturated);
                clippedFromLeft = added.Length;

                steps.Add(new ClipStep(
                    "Clip A segment against B",
                    DescribeCollection("Segment-against-polygon fallback", candidateCount, added.Length),
                    Copy(left, leftCount),
                    added,
                    Copy(contacts, rawCount),
                    ConnectAccepted: added.Length == 2));

                if (saturated) goto Finalize;
            }
        }

        if (leftCount == 2 && rightCount == 2)
        {
            ClipPoint[] added = CollectSegmentIntersectionContacts(left[0], left[1], right[0], right[1],
                contacts, ref rawCount, out int candidateCount, out saturated);
            segmentHits = added.Length;

            steps.Add(new ClipStep(
                "Intersect sampled segments",
                DescribeCollection("Segment-segment helper", candidateCount, added.Length),
                [],
                added,
                Copy(contacts, rawCount),
                ConnectAccepted: added.Length == 2,
                HasPrimaryEdge: true,
                PrimaryEdgeStart: left[0],
                PrimaryEdgeEnd: left[1],
                HasSecondaryEdge: true,
                SecondaryEdgeStart: right[0],
                SecondaryEdgeEnd: right[1]));

            if (saturated) goto Finalize;
        }

        if (leftPolygon && rightPolygon)
        {
            ClipPoint[] added = CollectPolygonEdgeIntersections(left, leftCount, right, rightCount,
                contacts, ref rawCount, out int candidateCount, out saturated);
            edgeHits = added.Length;

            steps.Add(new ClipStep(
                "Intersect polygon edges",
                DescribeCollection("Polygon edge checks", candidateCount, added.Length),
                [],
                added,
                Copy(contacts, rawCount)));

            if (saturated) goto Finalize;
        }

        Finalize:
        ClipPoint[] rawContacts = Copy(contacts, rawCount);

        if (saturated)
        {
            steps.Add(new ClipStep(
                "Raw manifold saturated",
                $"The raw manifold hit its {MaxManifoldPoints}-contact cap, so later feature checks were skipped.",
                [],
                [],
                rawContacts));
        }

        ClipPoint[] result = [];

        if (rawCount == 0)
        {
            steps.Add(new ClipStep(
                "Seed fallback",
                "No sampled feature test produced a contact in the plane. CollisionManifold would keep the original seed pair from the narrow phase.",
                [],
                [],
                []));
        }
        else
        {
            ClipPoint[] reduced = Copy(rawContacts, rawCount);
            int reducedCount = rawCount;
            ReduceManifold(reduced, ref reducedCount);
            result = Copy(reduced, reducedCount);

            steps.Add(new ClipStep(
                "Reduce to solver manifold",
                reducedCount == rawCount
                    ? $"The raw manifold already fit within the {SolverContactLimit}-contact solver limit."
                    : $"Sorted the {rawCount} raw contact(s) around their centroid and kept {reducedCount} evenly spaced solver contacts.",
                [],
                result,
                rawContacts,
                ConnectAccepted: result.Length > 1));
        }

        ClipDebugMode mode = DetermineMode(leftPolygon, rightPolygon, leftCount, rightCount, rawCount);
        string status = BuildStatus(rawCount, result.Length, saturated,
            insideFromRight, insideFromLeft, clippedFromRight, clippedFromLeft, segmentHits, edgeHits);

        return new ClipSnapshot(
            presetName,
            description,
            mode,
            status,
            left,
            right,
            rawContacts,
            result,
            steps.ToArray());
    }

    public static string GetModeLabel(ClipDebugMode mode)
    {
        return mode switch
        {
            ClipDebugMode.PolygonVsPolygon => "Polygon vs polygon",
            ClipDebugMode.PointVsPolygon => "Point vs polygon",
            ClipDebugMode.SegmentVsPolygon => "Segment vs polygon",
            ClipDebugMode.SegmentVsSegment => "Segment vs segment",
            ClipDebugMode.SeedFallback => "Seed fallback",
            _ => "Invalid input"
        };
    }

    private static ClipFrame BuildPolygonVsPolygonFrame(float time)
    {
        Vector2 drift = new(
            32.0f * MathF.Sin(time * 0.85f),
            26.0f * MathF.Cos(time * 0.55f));

        ClipPoint[] left = Transform(polygonA, drift, 0.45f + time * 0.55f, new Vector2(1.20f, 0.82f));
        ClipPoint[] right = Transform(polygonB, new Vector2(12.0f, -10.0f), -0.42f, new Vector2(1.00f, 0.96f));

        return new ClipFrame(left, right);
    }

    private static ClipFrame BuildSegmentVsPolygonFrame(float time)
    {
        Vector2 drift = new(
            48.0f * MathF.Sin(time * 1.15f),
            72.0f * MathF.Sin(time * 0.63f));

        ClipPoint[] left = Transform(lineA, drift, -0.22f + 0.35f * MathF.Sin(time * 0.7f), Vector2.One);
        ClipPoint[] right = Transform(polygonB, Vector2.Zero, -0.36f, new Vector2(0.95f, 0.78f));

        return new ClipFrame(left, right);
    }

    private static ClipFrame BuildSegmentVsSegmentFrame(float time)
    {
        Vector2 leftOffset = new(
            35.0f * MathF.Sin(time * 0.9f),
            25.0f * MathF.Sin(time * 0.5f));

        Vector2 rightOffset = new(
            85.0f * MathF.Sin(time * 0.55f),
            25.0f * MathF.Sin(time * 0.5f));

        ClipPoint[] left = Transform(lineA, leftOffset, 0.0f, Vector2.One);
        ClipPoint[] right = Transform(lineB, rightOffset, 0.0f, Vector2.One);

        return new ClipFrame(left, right);
    }

    private static ClipPoint[] Transform(ClipPoint[] points, Vector2 translation, float rotation, Vector2 scale)
    {
        ClipPoint[] transformed = new ClipPoint[points.Length];
        float cos = MathF.Cos(rotation);
        float sin = MathF.Sin(rotation);

        for (int i = 0; i < points.Length; i++)
        {
            float x = points[i].X * scale.X;
            float y = points[i].Y * scale.Y;

            transformed[i] = new ClipPoint(
                x * cos - y * sin + translation.X,
                x * sin + y * cos + translation.Y);
        }

        return transformed;
    }

    private static ClipDebugMode DetermineMode(bool leftPolygon, bool rightPolygon, int leftCount, int rightCount, int rawCount)
    {
        if (rawCount == 0) return ClipDebugMode.SeedFallback;
        if (leftPolygon && rightPolygon) return ClipDebugMode.PolygonVsPolygon;
        if ((leftPolygon && rightCount == 2) || (rightPolygon && leftCount == 2)) return ClipDebugMode.SegmentVsPolygon;
        if ((leftPolygon && rightCount == 1) || (rightPolygon && leftCount == 1) || leftPolygon || rightPolygon)
        {
            return ClipDebugMode.PointVsPolygon;
        }

        if (leftCount == 2 && rightCount == 2) return ClipDebugMode.SegmentVsSegment;
        return ClipDebugMode.SeedFallback;
    }

    private static string BuildStatus(int rawCount, int solverCount, bool saturated,
        int insideFromRight, int insideFromLeft, int clippedFromRight, int clippedFromLeft, int segmentHits, int edgeHits)
    {
        if (rawCount == 0)
        {
            return "No sampled feature produced a contact in the plane. The runtime manifold falls back to the original seed pair.";
        }

        List<string> parts = [];

        if (insideFromRight > 0) parts.Add($"{insideFromRight} B-in-A point(s)");
        if (insideFromLeft > 0) parts.Add($"{insideFromLeft} A-in-B point(s)");
        if (clippedFromRight > 0) parts.Add($"{clippedFromRight} clipped point(s) from B's segment");
        if (clippedFromLeft > 0) parts.Add($"{clippedFromLeft} clipped point(s) from A's segment");
        if (segmentHits > 0) parts.Add($"{segmentHits} segment intersection point(s)");
        if (edgeHits > 0) parts.Add($"{edgeHits} polygon edge hit(s)");
        if (saturated) parts.Add($"raw cap {MaxManifoldPoints} reached");

        string sourceSummary = parts.Count == 0
            ? "from sampled feature checks"
            : string.Join(", ", parts);

        string reduction = solverCount == rawCount
            ? "No reduction was needed."
            : $"Reduced to {solverCount} solver contact(s).";

        return $"{rawCount} raw contact(s): {sourceSummary}. {reduction}";
    }

    private static string DescribeFeature(char label, int count, bool polygon)
    {
        if (polygon)
        {
            return $"Feature {label} is a polygon with {count} sampled points.";
        }

        return count switch
        {
            1 => $"Feature {label} collapsed to a point.",
            2 => $"Feature {label} collapsed to a segment.",
            _ => $"Feature {label} is nearly linear, so it is treated as a non-polygon feature."
        };
    }

    private static string DescribeCollection(string label, int candidateCount, int addedCount)
    {
        if (candidateCount == 0)
        {
            return $"{label} produced no candidates.";
        }

        if (addedCount == candidateCount)
        {
            return $"{label} added {addedCount} contact(s).";
        }

        if (addedCount == 0)
        {
            return $"{label} found {candidateCount} candidate(s), but they were all duplicates.";
        }

        return $"{label} found {candidateCount} candidate(s); {addedCount} new contact(s) survived duplicate filtering.";
    }

    private static ClipPoint[] CollectInsideContacts(ClipPoint[] probe, int probeCount,
        ClipPoint[] polygon, int polygonCount, float sign,
        ClipPoint[] contacts, ref int rawCount, out int candidateCount, out bool saturated)
    {
        List<ClipPoint> added = [];
        candidateCount = 0;
        saturated = false;

        for (int i = 0; i < probeCount; i++)
        {
            ClipPoint point = probe[i];
            if (!ContainsPoint(polygon, polygonCount, point, sign)) continue;

            candidateCount += 1;

            if (TryAddContact(contacts, ref rawCount, point))
            {
                added.Add(point);
            }

            if (rawCount == MaxManifoldPoints)
            {
                saturated = true;
                break;
            }
        }

        return added.ToArray();
    }

    private static ClipPoint[] CollectSegmentClipContacts(in ClipPoint start, in ClipPoint end,
        ClipPoint[] polygon, int polygonCount, float sign,
        ClipPoint[] contacts, ref int rawCount, out int candidateCount, out bool saturated)
    {
        ClipPoint[] clipped = new ClipPoint[2];
        candidateCount = ClipSegmentAgainstPolygon(start, end, polygon, polygonCount, sign, clipped);
        saturated = false;

        List<ClipPoint> added = [];

        for (int i = 0; i < candidateCount; i++)
        {
            if (TryAddContact(contacts, ref rawCount, clipped[i]))
            {
                added.Add(clipped[i]);
            }

            if (rawCount == MaxManifoldPoints)
            {
                saturated = true;
                break;
            }
        }

        return added.ToArray();
    }

    private static ClipPoint[] CollectSegmentIntersectionContacts(in ClipPoint startA, in ClipPoint endA,
        in ClipPoint startB, in ClipPoint endB,
        ClipPoint[] contacts, ref int rawCount, out int candidateCount, out bool saturated)
    {
        ClipPoint[] clipped = new ClipPoint[2];
        candidateCount = IntersectSegments(startA, endA, startB, endB, clipped);
        saturated = false;

        List<ClipPoint> added = [];

        for (int i = 0; i < candidateCount; i++)
        {
            if (TryAddContact(contacts, ref rawCount, clipped[i]))
            {
                added.Add(clipped[i]);
            }

            if (rawCount == MaxManifoldPoints)
            {
                saturated = true;
                break;
            }
        }

        return added.ToArray();
    }

    private static ClipPoint[] CollectPolygonEdgeIntersections(ClipPoint[] left, int leftCount,
        ClipPoint[] right, int rightCount,
        ClipPoint[] contacts, ref int rawCount, out int candidateCount, out bool saturated)
    {
        List<ClipPoint> added = [];
        ClipPoint[] clipped = new ClipPoint[2];
        candidateCount = 0;
        saturated = false;

        for (int i = 0; i < leftCount; i++)
        {
            ClipPoint startA = left[i];
            ClipPoint endA = left[(i + 1) % leftCount];

            for (int j = 0; j < rightCount; j++)
            {
                ClipPoint startB = right[j];
                ClipPoint endB = right[(j + 1) % rightCount];

                int clippedCount = IntersectSegments(startA, endA, startB, endB, clipped);
                candidateCount += clippedCount;

                for (int k = 0; k < clippedCount; k++)
                {
                    if (TryAddContact(contacts, ref rawCount, clipped[k]))
                    {
                        added.Add(clipped[k]);
                    }

                    if (rawCount == MaxManifoldPoints)
                    {
                        saturated = true;
                        return added.ToArray();
                    }
                }
            }
        }

        return added.ToArray();
    }

    private static bool TryAddContact(ClipPoint[] contacts, ref int count, in ClipPoint point)
    {
        if (count == MaxManifoldPoints) return false;
        if (IsDuplicatePoint(contacts, count, point)) return false;

        contacts[count++] = point;
        return true;
    }

    private static bool IsDuplicatePoint(ClipPoint[] contacts, int count, in ClipPoint point)
    {
        for (int i = 0; i < count; i++)
        {
            if ((contacts[i] - point).LengthSquared() < DuplicatePointDistanceSq) return true;
        }

        return false;
    }

    private static ClipPoint[] Copy(ClipPoint[] source, int count)
    {
        ClipPoint[] copy = new ClipPoint[count];
        Array.Copy(source, copy, count);
        return copy;
    }

    private static bool TryGetPolygonSign(ClipPoint[] polygon, int count, out float sign)
    {
        float area = 0.0f;
        ClipPoint previous = polygon[count - 1];

        for (int i = 0; i < count; i++)
        {
            ClipPoint current = polygon[i];
            area += Cross2D(previous, current);
            previous = current;
        }

        if (MathF.Abs(area) <= AreaSignEpsilon)
        {
            sign = 0.0f;
            return false;
        }

        sign = area < 0.0f ? -1.0f : 1.0f;
        return true;
    }

    private static bool ContainsPoint(ClipPoint[] polygon, int count, in ClipPoint point, float sign)
    {
        ClipPoint previous = polygon[count - 1];

        for (int i = 0; i < count; i++)
        {
            ClipPoint current = polygon[i];
            float side = sign * Cross2D(current - previous, point - previous);

            if (side < -Epsilon) return false;

            previous = current;
        }

        return true;
    }

    private static int ClipSegmentAgainstPolygon(in ClipPoint start, in ClipPoint end,
        ClipPoint[] polygon, int polygonCount, float sign, ClipPoint[] clipped)
    {
        float enter = 0.0f;
        float exit = 1.0f;
        ClipPoint delta = end - start;

        for (int i = 0; i < polygonCount; i++)
        {
            ClipPoint edgeStart = polygon[i];
            ClipPoint edgeEnd = polygon[(i + 1) % polygonCount];

            float startSide = sign * Cross2D(edgeEnd - edgeStart, start - edgeStart);
            float endSide = sign * Cross2D(edgeEnd - edgeStart, end - edgeStart);

            bool startInside = startSide >= -Epsilon;
            bool endInside = endSide >= -Epsilon;

            if (!startInside && !endInside) return 0;
            if (startInside && endInside) continue;

            float denominator = startSide - endSide;
            if (MathF.Abs(denominator) <= Epsilon) return 0;

            float t = Math.Clamp(startSide / denominator, 0.0f, 1.0f);

            if (!startInside)
            {
                enter = MathF.Max(enter, t);
            }
            else
            {
                exit = MathF.Min(exit, t);
            }

            if (exit < enter) return 0;
        }

        clipped[0] = start + enter * delta;
        ClipPoint exitPoint = start + exit * delta;

        if ((exitPoint - clipped[0]).LengthSquared() < Epsilon)
        {
            return 1;
        }

        clipped[1] = exitPoint;
        return 2;
    }

    private static int IntersectSegments(in ClipPoint startA, in ClipPoint endA,
        in ClipPoint startB, in ClipPoint endB, ClipPoint[] clipped)
    {
        ClipPoint deltaA = endA - startA;
        ClipPoint deltaB = endB - startB;
        ClipPoint offset = startB - startA;

        float cross = Cross2D(deltaA, deltaB);

        if (MathF.Abs(cross) <= Epsilon)
        {
            if (MathF.Abs(Cross2D(offset, deltaA)) > Epsilon) return 0;

            float lengthASquared = deltaA.LengthSquared();
            float lengthBSquared = deltaB.LengthSquared();

            if (lengthASquared <= Epsilon || lengthBSquared <= Epsilon) return 0;

            float parameterB0 = ClipPoint.Dot(startB - startA, deltaA) / lengthASquared;
            float parameterB1 = ClipPoint.Dot(endB - startA, deltaA) / lengthASquared;

            float enter = MathF.Max(0.0f, MathF.Min(parameterB0, parameterB1));
            float exit = MathF.Min(1.0f, MathF.Max(parameterB0, parameterB1));

            if (exit < enter - Epsilon) return 0;

            clipped[0] = startA + enter * deltaA;
            ClipPoint exitPoint = startA + exit * deltaA;

            if ((exitPoint - clipped[0]).LengthSquared() <= Epsilon) return 1;

            clipped[1] = exitPoint;
            return 2;
        }

        float t = Cross2D(offset, deltaB) / cross;
        float u = Cross2D(offset, deltaA) / cross;

        if (t < -Epsilon || t > 1.0f + Epsilon ||
            u < -Epsilon || u > 1.0f + Epsilon)
        {
            return 0;
        }

        t = Math.Clamp(t, 0.0f, 1.0f);
        clipped[0] = startA + t * deltaA;

        return 1;
    }

    private static void ReduceManifold(ClipPoint[] contacts, ref int count)
    {
        if (count <= SolverContactLimit) return;

        ClipPoint centroid = new(0.0f, 0.0f);

        for (int i = 0; i < count; i++)
        {
            centroid += contacts[i];
        }

        centroid = (1.0f / count) * centroid;

        float[] angles = new float[count];
        int[] order = new int[count];

        for (int i = 0; i < count; i++)
        {
            ClipPoint delta = contacts[i] - centroid;
            angles[i] = MathF.Atan2(delta.Y, delta.X);
            order[i] = i;
        }

        for (int i = 1; i < count; i++)
        {
            int current = order[i];
            float currentAngle = angles[current];
            int j = i - 1;

            while (j >= 0 && angles[order[j]] > currentAngle)
            {
                order[j + 1] = order[j];
                j--;
            }

            order[j + 1] = current;
        }

        ClipPoint[] reduced = new ClipPoint[SolverContactLimit];

        for (int i = 0; i < SolverContactLimit; i++)
        {
            int selected = order[((2 * i + 1) * count) / (2 * SolverContactLimit)];
            reduced[i] = contacts[selected];
        }

        Array.Copy(reduced, contacts, SolverContactLimit);
        count = SolverContactLimit;
    }

    private static float Cross2D(in ClipPoint left, in ClipPoint right)
    {
        return left.X * right.Y - left.Y * right.X;
    }
}
