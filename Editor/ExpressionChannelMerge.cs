using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter
{
    // One menu entry's blend-shape values and animation, merged first by the
    // authoring (path, shape) and then by the final (renderer, shape) that
    // NDMF left. Every collision must agree. Callers resolve paths and word
    // the conflict, so their messages and where an error lands stay theirs.
    internal static class ExpressionChannelMerge
    {
        internal sealed class Channel
        {
            internal string Path, Shape;
            internal SkinnedMeshRenderer Renderer;
            internal Mesh Mesh;
            internal float[] Rest;
            internal bool HasValue;
            internal float Weight;
            internal ExpressionAnimationData.Curve Curve;
        }

        // resolve runs once per new (path, shape) and sets Renderer, plus Mesh
        // and Rest when the caller needs them. skipUnresolved keeps a channel
        // whose renderer is null out of every comparison and group. Groups are
        // yielded lazily: a check the caller makes per group (sameGroup, or
        // after each yield) runs in the same order as the merge's own checks.
        internal static IEnumerable<Channel> Groups(VrChatExpressionMenu.Entry entry, Func<string, Channel> resolve,
            Func<Channel, Exception> conflict, bool skipUnresolved = false, Action<Channel, Channel> sameGroup = null)
        {
            var channels = new Dictionary<(string Path, string Shape), Channel>();
            Channel Get(string path, string shape)
            {
                var key = (path, shape);
                if (channels.TryGetValue(key, out var found)) return found;
                var channel = resolve(path);
                channel.Path = path; channel.Shape = shape;
                channels.Add(key, channel); return channel;
            }
            bool Compared(Channel channel) => !skipUnresolved || channel.Renderer != null;
            foreach (var value in entry.Values)
            {
                var channel = Get(value.Path, value.Shape);
                if (channel.HasValue && !channel.Weight.Equals(value.Weight) && Compared(channel)) throw conflict(channel);
                channel.HasValue = true; channel.Weight = value.Weight;
            }
            foreach (var value in entry.Animation)
            {
                var channel = Get(value.Path, value.Shape);
                if (channel.Curve != null && !VrChatExpressionBaker.SameCurve(channel.Curve, value.Curve) && Compared(channel))
                    throw conflict(channel);
                channel.Curve = value.Curve;
            }
            // The readers record an animated channel's static value as the
            // clip's first pose, so a merged value must still match that.
            foreach (var channel in channels.Values.Where(channel => Compared(channel) && channel.Curve != null))
            {
                var initial = (float)channel.Curve.Evaluate(0);
                if (channel.HasValue && !channel.Weight.Equals(initial)) throw conflict(channel);
                channel.Weight = initial;
            }
            foreach (var group in channels.Values.Where(Compared).GroupBy(channel => (channel.Renderer, channel.Shape)))
            {
                var first = group.First();
                foreach (var other in group.Skip(1))
                {
                    if (!first.Weight.Equals(other.Weight) || !(VrChatExpressionBaker.SameCurve(first.Curve, other.Curve) ||
                        VrChatExpressionBaker.ConstantAt(first.Curve, first.Weight) && VrChatExpressionBaker.ConstantAt(other.Curve, other.Weight)))
                        throw conflict(first);
                    sameGroup?.Invoke(first, other);
                }
                yield return first;
            }
        }
    }
}
