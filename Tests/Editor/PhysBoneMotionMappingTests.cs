using NUnit.Framework;
using UnityEngine;
using Settings = VRVlog.LilToonExporter.PhysBoneMotionMapping.Settings;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class PhysBoneMotionMappingTests
    {
        static float Angle(Vector3 a, Vector3 b) => Vector3.Angle(a, b);

        [Test]
        public void ForceGrowsWithTheBoneLengthAndDragDoesNot()
        {
            var settings = new Settings { Pull = .4f, Spring = .2f };
            var short_ = PhysBoneMotionMapping.Map(settings, .1f, Vector3.forward);
            var long_ = PhysBoneMotionMapping.Map(settings, .3f, Vector3.forward);
            Assert.That(short_.Stiffness, Is.EqualTo(.1f * 60 * (1 / .68f - 1)).Within(1e-4));
            Assert.That(long_.Stiffness, Is.EqualTo(short_.Stiffness * 3).Within(1e-4));
            Assert.That(short_.Drag, Is.EqualTo(1 - .2f / .68f).Within(1e-4));
            Assert.That(long_.Drag, Is.EqualTo(short_.Drag).Within(1e-6));
        }

        [Test]
        public void ZeroPullHasNoForceAndNoMomentum()
        {
            var forces = PhysBoneMotionMapping.Map(new Settings { Pull = 0, Spring = .9f }, .1f, Vector3.forward);
            Assert.That(forces.Stiffness, Is.EqualTo(0).Within(1e-6));
            Assert.That(forces.Drag, Is.EqualTo(1).Within(1e-6));
        }

        [TestCase(0f, 0f)]
        [TestCase(.02f, .45f)]
        [TestCase(.2f, .85f)]
        [TestCase(.9f, .85f)]
        public void HighSpringIsLimitedByPull(float pull, float limit)
        {
            Assert.That(PhysBoneMotionMapping.EffectiveSpring(pull, 1), Is.EqualTo(limit).Within(1e-5));
            Assert.That(PhysBoneMotionMapping.EffectiveSpring(pull, limit / 2), Is.EqualTo(limit / 2).Within(1e-5));
        }

        [Test]
        public void AdvancedStiffnessOnlyRemovesMomentum()
        {
            var free = PhysBoneMotionMapping.Map(new Settings { Advanced = true, Pull = .5f, Spring = .8f }, .1f, Vector3.forward);
            var held = PhysBoneMotionMapping.Map(new Settings { Advanced = true, Pull = .5f, Spring = .8f, Stiffness = .5f }, .1f, Vector3.forward);
            Assert.That(free.Stiffness, Is.EqualTo(.1f * 60).Within(1e-4));
            Assert.That(held.Stiffness, Is.EqualTo(free.Stiffness).Within(1e-6));
            Assert.That(free.Drag, Is.EqualTo(.2f).Within(1e-5));
            Assert.That(held.Drag, Is.EqualTo(.6f).Within(1e-5));
        }

        [Test]
        public void NewGravityTurnsTheRestDirectionWhateverThePull()
        {
            foreach (var pull in new[] { .1f, .8f })
            {
                var forces = PhysBoneMotionMapping.Map(new Settings { Pull = pull, Gravity = .5f, GravityTurnsRest = true }, .1f, Vector3.forward);
                var total = forces.Stiffness + forces.GravityPower;
                Assert.That(forces.Stiffness / total, Is.EqualTo(.5f).Within(1e-4));
                Assert.That(Angle(forces.Settled, Vector3.forward), Is.EqualTo(45).Within(.01));
                Assert.That(forces.GravityDirection, Is.EqualTo(Vector3.down));
            }
        }

        [TestCase(.5f, .5f, 45f)]
        [TestCase(.1f, .5f, 78.69f)]
        public void OldGravityCompetesWithPull(float pull, float gravity, float degrees)
        {
            var forces = PhysBoneMotionMapping.Map(new Settings { Pull = pull, Gravity = gravity }, .1f, Vector3.forward);
            Assert.That(Angle(forces.Settled, Vector3.forward), Is.EqualTo(degrees).Within(.01));
        }

        [Test]
        public void NegativeGravityPullsUp()
        {
            var forces = PhysBoneMotionMapping.Map(new Settings { Pull = .4f, Gravity = -.5f, GravityTurnsRest = true }, .1f, Vector3.forward);
            Assert.That(forces.GravityDirection, Is.EqualTo(Vector3.up));
            Assert.That(forces.GravityPower, Is.GreaterThan(0));
        }

        [Test]
        public void FullFalloffLeavesABoneHangingAtRestUntouched()
        {
            var forces = PhysBoneMotionMapping.Map(new Settings { Pull = .4f, Gravity = 1, Falloff = 1, GravityTurnsRest = true }, .1f, Vector3.down);
            Assert.That(forces.GravityPower, Is.EqualTo(0).Within(1e-4));
            Assert.That(Angle(forces.Settled, Vector3.down), Is.EqualTo(0).Within(.01));
        }

        // VRM balances when the force back to rest equals gravity's pull away from it.
        [TestCase(true)]
        [TestCase(false)]
        public void FalloffRaisesTheForceWithoutMovingWhereTheBoneSettles(bool turnsRest)
        {
            var settings = new Settings { Pull = .3f, Spring = .2f, Gravity = .6f, Falloff = .7f, GravityTurnsRest = turnsRest };
            var forces = PhysBoneMotionMapping.Map(settings, .1f, Vector3.forward);
            var angle = Angle(forces.Settled, Vector3.forward) * Mathf.Deg2Rad;
            Assert.That(angle, Is.GreaterThan(0));
            Assert.That(forces.Stiffness * Mathf.Sin(angle),
                Is.EqualTo(forces.GravityPower * Mathf.Sin(Mathf.PI / 2 - angle)).Within(1e-3));
        }

        // Hanging at rest the old gravity adds nothing, but a swing meets a
        // gravity that grows with the angle, so the pull back is stronger.
        [Test]
        public void OldGravityFalloffStrengthensAHangingBoneWithoutMovingIt()
        {
            var plain = PhysBoneMotionMapping.Map(new Settings { Pull = .3f, Spring = .2f }, .1f, Vector3.down);
            var forces = PhysBoneMotionMapping.Map(new Settings { Pull = .3f, Spring = .2f, Gravity = .6f, Falloff = 1 }, .1f, Vector3.down);
            Assert.That(forces.Stiffness, Is.GreaterThan(plain.Stiffness * 1.1f));
            Assert.That(Angle(forces.Settled, Vector3.down), Is.EqualTo(0).Within(.01));
        }

        [Test]
        public void AllMotionImmobileRaisesForceByTheHiddenShareUpToTheHoldingForce()
        {
            var free = PhysBoneMotionMapping.Map(new Settings { Pull = .4f, Spring = .2f }, .1f, Vector3.forward);
            var half = PhysBoneMotionMapping.Map(new Settings { Pull = .4f, Spring = .2f, Immobile = .5f }, .1f, Vector3.forward);
            var full = PhysBoneMotionMapping.Map(new Settings { Pull = .4f, Spring = .2f, Immobile = 1 }, .1f, Vector3.forward);
            Assert.That(half.Stiffness, Is.EqualTo(free.Stiffness * 2).Within(1e-4));
            Assert.That(half.Drag, Is.EqualTo(free.Drag).Within(1e-6));
            Assert.That(full.Stiffness, Is.EqualTo(PhysBoneMotionMapping.HoldingForce(.1f)).Within(1e-3));
        }

        [Test]
        public void ForceAtItsCeilingKeepsTheBalanceWithGravity()
        {
            var settings = new Settings { Pull = 1, Spring = 0, Gravity = .5f, Immobile = .99f, GravityTurnsRest = true };
            var forces = PhysBoneMotionMapping.Map(settings, .1f, Vector3.forward);
            var holding = PhysBoneMotionMapping.HoldingForce(.1f);
            Assert.That(Mathf.Max(forces.Stiffness, forces.GravityPower), Is.LessThanOrEqualTo(holding + 1e-3f));
            Assert.That(forces.GravityPower / forces.Stiffness, Is.EqualTo(1).Within(1e-3));
        }
    }
}
