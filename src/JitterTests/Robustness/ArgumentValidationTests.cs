namespace JitterTests.Robustness;

public class ArgumentValidationTests
{
    [Test]
    public void ConstraintInitialize_WithZeroAxis_Throws()
    {
        using World world = new();
        RigidBody body1 = world.CreateRigidBody();
        RigidBody body2 = world.CreateRigidBody();
        PointOnLine constraint = world.CreateConstraint<PointOnLine>(body1, body2);

        Assert.Throws<ArgumentException>(() => constraint.Initialize(JVector.Zero, JVector.Zero, JVector.Zero));
    }

    [Test]
    public void ShapeCreation_WithNaNDimension_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SphereShape(Real.NaN));
    }

    [Test]
    public void PointCloudShape_WithDegenerateVertices_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _ = new PointCloudShape([JVector.Zero]));

        Assert.Throws<InvalidOperationException>(() => _ = new PointCloudShape([
            JVector.Zero,
            JVector.UnitX,
            JVector.UnitY,
            JVector.UnitX + JVector.UnitY
        ]));

        Assert.Throws<InvalidOperationException>(() => _ = new PointCloudShape([
            new JVector(-1, -1, 1),
            new JVector(+1, -1, 1),
            new JVector(+1, +1, 1),
            new JVector(-1, +1, 1)
        ]));
    }

    [Test]
    public void SupportPrimitiveCreation_WithInfiniteDimension_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = SupportPrimitives.CreateSphere(Real.PositiveInfinity));
    }
}
