using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Turns one PhysBone joint's settings into the force, damping and gravity
    // of a VRM spring joint. The two solvers state motion differently, so the
    // same numbers do not mean the same thing:
    //
    // - PhysBone pulls the bone's angle back by a fraction each step, whatever
    //   the bone's length. VRM pushes the tail by a distance per second, so the
    //   same bending needs a force proportional to the bone's length.
    // - VRM's drag removes velocity per frame and does not depend on length.
    //
    // The formulas come from running both solvers side by side over hundreds of
    // synthetic chains; see Compatibility/Evidence/SpringFidelity.
    internal static class PhysBoneMotionMapping
    {
        // VRM's drag and force are per frame and per second. The app steps
        // springs at 60 Hz or faster, so the mapping is stated at that rate.
        const float SolverRate = 60;
        // Above 98 % of the angle removed per step, the force only adds jitter.
        const float MinimumRetention = .02f;
        const int FalloffIterations = 32;
        // How far a typical swing leaves the point where the bone settles.
        const float TypicalSwing = 45 * Mathf.Deg2Rad;

        // With a high spring PhysBone still loses energy to the bones around it
        // while the VRM solver keeps nearly all of it and swings out of control.
        // The largest spring value that matched the recorded motion, by pull.
        static readonly float[] CapPull = { 0, .02f, .05f, .1f, .2f };
        static readonly float[] CapSpring = { 0, .45f, .68f, .8f, .85f };

        internal struct Settings
        {
            public float Pull, Spring, Stiffness, Gravity, Falloff;
            // The share of the parent's motion an All Motion chain ignores.
            public float Immobile;
            public bool Advanced;
            // PhysBone 1.1 gravity turns the rest direction at any pull; 1.0
            // gravity competes with pull, so it hangs fully when pull is zero.
            public bool GravityTurnsRest;
        }

        internal struct Forces
        {
            public float Stiffness, Drag, GravityPower;
            public Vector3 GravityDirection;
            // Where the bone comes to rest under gravity. Its children start
            // from there, because PhysBone measures their falloff that way.
            public Vector3 Settled;
        }

        // The force that keeps a bone on its rest direction every step.
        internal static float HoldingForce(float length) => length * SolverRate * (1 / MinimumRetention - 1);

        internal static float EffectiveSpring(float pull, float spring)
        {
            for (var i = 1; i < CapPull.Length; i++)
                if (pull < CapPull[i])
                    return Mathf.Min(spring, Mathf.Lerp(CapSpring[i - 1], CapSpring[i], Mathf.InverseLerp(CapPull[i - 1], CapPull[i], pull)));
            return Mathf.Min(spring, CapSpring[CapSpring.Length - 1]);
        }

        // length: the bone's length before the solver's scale, which multiplies
        // the force at run time. rest: the bone's direction before gravity.
        internal static Forces Map(in Settings settings, float length, Vector3 rest)
        {
            var pull = settings.Pull;
            float retained, keep, hanging;
            if (settings.Advanced)
            {
                // Advanced states pull and momentum directly. Stiffness holds
                // the previous pose, which VRM has no term for, so it can only
                // remove momentum.
                retained = Mathf.Max(1 - pull, MinimumRetention);
                keep = settings.Spring * (1 - settings.Stiffness);
                hanging = length * SolverRate / retained;
            }
            else
            {
                var spring = EffectiveSpring(pull, settings.Spring);
                retained = Mathf.Max(1 - pull * (1 - spring), MinimumRetention);
                keep = spring / retained;
                hanging = length * SolverRate * (1 - spring) / retained;
            }
            // VRM has no term that hides a share of the parent's motion. A bone
            // lags behind in proportion to motion over force, so hiding a share
            // of the motion and raising the force by the same share lag equally.
            var unhidden = Mathf.Max(1 - settings.Immobile, 1e-3f);
            var holding = HoldingForce(length);
            var force = Mathf.Min(length * SolverRate * (1 / retained - 1) / unhidden, holding);
            var result = new Forces
            {
                Stiffness = force,
                Drag = 1 - Mathf.Clamp01(keep),
                GravityDirection = settings.Gravity < 0 ? Vector3.up : Vector3.down,
                Settled = rest
            };
            var gravity = Mathf.Abs(settings.Gravity);
            if (gravity <= 0) return result;
            var cosine = Mathf.Clamp(Vector3.Dot(rest, result.GravityDirection), -1, 1);
            var sine = Mathf.Sqrt(1 - cosine * cosine);
            var effective = gravity;
            var angle = 0f;
            // Falloff weakens gravity by the bone's angle from rest, and that
            // angle depends on gravity. VRM's gravity is constant, so export
            // the value where the bone settles. Starting from rest finds the
            // same settling point the moving bone reaches first.
            for (var i = 0; i < FalloffIterations; i++)
            {
                effective = gravity * (1 - settings.Falloff * Mathf.Cos(angle));
                if (settings.GravityTurnsRest) effective = Mathf.Min(effective, 1);
                angle = settings.GravityTurnsRest
                    ? Mathf.Atan2(effective * sine, 1 - effective + effective * cosine)
                    : Mathf.Atan2(effective * sine, pull + effective * cosine);
                if (settings.Falloff <= 0) break;
            }
            var turns = settings.GravityTurnsRest;
            var hangingForce = turns ? force : hanging / unhidden;
            // Falloff changes gravity along the swing, which VRM's constant
            // gravity cannot. Raise force and gravity by the same share so the
            // settling point stays and the swing is pulled back as strongly.
            var share = settings.Falloff > 0 ? SwingShare(settings, force, hangingForce, effective, angle, Mathf.Atan2(sine, cosine)) : 1;
            var restForce = (turns ? force * (1 - effective) : force) * share;
            var gravityForce = hangingForce * effective * share;
            // Keep the balance between them when the force hits its ceiling.
            var largest = Mathf.Max(restForce, gravityForce);
            var fit = largest > holding && largest > 0 ? holding / largest : 1;
            result.Stiffness = restForce * fit;
            result.GravityPower = gravityForce * fit;
            result.Settled = Turn(rest, result.GravityDirection, angle);
            return result;
        }

        // The pull back toward the settling point at a typical swing, against
        // what constant gravity gives there. Angles are from rest toward gravity.
        static float SwingShare(Settings settings, float force, float hangingForce, float settledGravity, float settled, float toGravity)
        {
            var turns = settings.GravityTurnsRest;
            float Pull(float angle, float gravity) =>
                hangingForce * gravity * Mathf.Sin(toGravity - angle) - (turns ? force * (1 - gravity) : force) * Mathf.Sin(angle);
            float At(float angle)
            {
                var gravity = Mathf.Abs(settings.Gravity) * (1 - settings.Falloff * Mathf.Cos(angle));
                return Pull(angle, turns ? Mathf.Min(gravity, 1) : gravity);
            }
            var total = 0f;
            var count = 0;
            foreach (var swing in new[] { -TypicalSwing, TypicalSwing })
            {
                var constant = Pull(settled + swing, settledGravity);
                if (Mathf.Abs(constant) <= 1e-6f) continue;
                total += At(settled + swing) / constant;
                count++;
            }
            return count > 0 ? Mathf.Max(total / count, 1) : 1;
        }

        static Vector3 Turn(Vector3 from, Vector3 toward, float angle)
        {
            var side = toward - Vector3.Dot(from, toward) * from;
            if (side.sqrMagnitude <= 1e-12f) return from;
            return (Mathf.Cos(angle) * from + Mathf.Sin(angle) * side.normalized).normalized;
        }
    }
}
