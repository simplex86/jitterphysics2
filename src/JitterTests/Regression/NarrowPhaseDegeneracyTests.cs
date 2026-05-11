namespace JitterTests.Regression;

public class NarrowPhaseDegeneracyTests
{
    private static VertexSupportMap CreateTriangle()
    {
        return new VertexSupportMap([
            new JVector((Real)(-1.0), (Real)0.0, (Real)(-1.0)),
            new JVector((Real)1.0, (Real)0.0, (Real)(-1.0)),
            new JVector((Real)0.0, (Real)0.0, (Real)1.0)
        ]);
    }

    [TestCase]
    public void MprEpa_CoincidentPoints_ReportOverlap()
    {
        var point = SupportPrimitives.CreatePoint();

        NarrowPhaseResult result = NarrowPhase.MprEpa(point, point, JQuaternion.Identity, JVector.Zero,
            out JVector pointA, out JVector pointB, out JVector normal, out Real penetration);

        Assert.That(result, Is.EqualTo(NarrowPhaseResult.Hit));
        Assert.That(pointA, Is.EqualTo(JVector.Zero));
        Assert.That(pointB, Is.EqualTo(JVector.Zero));
        Assert.That(normal.LengthSquared(), Is.GreaterThan((Real)0.0));
        Assert.That(penetration, Is.EqualTo((Real)0.0).Within((Real)1e-6));
    }

    [TestCase]
    public void MprEpa_SeparatedPoints_ReturnSeparatedWithDefaultOutputs()
    {
        var point = SupportPrimitives.CreatePoint();

        NarrowPhaseResult result = NarrowPhase.MprEpa(point, point, JQuaternion.Identity, JVector.UnitX,
            out JVector pointA, out JVector pointB, out JVector normal, out Real penetration);

        Assert.That(result, Is.EqualTo(NarrowPhaseResult.Separated));
        Assert.That(pointA, Is.EqualTo(JVector.Zero));
        Assert.That(pointB, Is.EqualTo(JVector.Zero));
        Assert.That(normal, Is.EqualTo(JVector.Zero));
        Assert.That(penetration, Is.EqualTo((Real)0.0));
    }

    [TestCase]
    public void MprEpa_SeparatedTransformedPoints_ReturnSeparatedWithDefaultOutputs()
    {
        var point = SupportPrimitives.CreatePoint();

        NarrowPhaseResult result = NarrowPhase.MprEpa(point, point,
            JQuaternion.Identity, JQuaternion.Identity,
            new JVector((Real)10.0, (Real)20.0, (Real)30.0),
            new JVector((Real)11.0, (Real)20.0, (Real)30.0),
            out JVector pointA, out JVector pointB, out JVector normal, out Real penetration);

        Assert.That(result, Is.EqualTo(NarrowPhaseResult.Separated));
        Assert.That(pointA, Is.EqualTo(JVector.Zero));
        Assert.That(pointB, Is.EqualTo(JVector.Zero));
        Assert.That(normal, Is.EqualTo(JVector.Zero));
        Assert.That(penetration, Is.EqualTo((Real)0.0));
    }

    [TestCase]
    public void MprEpa_CoplanarOverlappingTriangles_ReportOverlap()
    {
        var triangle = CreateTriangle();

        NarrowPhaseResult result = NarrowPhase.MprEpa(triangle, triangle, JQuaternion.Identity, JVector.Zero,
            out JVector pointA, out JVector pointB, out JVector normal, out Real penetration);

        Assert.That(result, Is.EqualTo(NarrowPhaseResult.Hit));
        Assert.That(Real.IsFinite(pointA.LengthSquared()), Is.True);
        Assert.That(Real.IsFinite(pointB.LengthSquared()), Is.True);
        Assert.That(normal.LengthSquared(), Is.GreaterThan((Real)0.0));
        Assert.That(Real.IsFinite(penetration), Is.True);
        Assert.That(penetration, Is.GreaterThanOrEqualTo((Real)0.0));
    }
}
