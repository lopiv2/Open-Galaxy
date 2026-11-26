using System.Collections.Generic;
using UnityEngine;

// Shared swarm scatter used by PlanetNavigation (runtime) and the planet
// terrain preview tool. Keeping one implementation guarantees the tool shows
// exactly the same prop layout the game spawns.
//
// radius    = radius (m) of the disc around the prop centre that is filled.
// minDist   = desired minimum distance (m) between two placed props.
//
// The old code silently skipped an instance when it could not find room in
// 24 attempts, so a minDist larger than the disc radius could make most of a
// group disappear from the world (and from the preview). Now the farthest
// candidate of the last round is used instead, marking the placement as
// relaxed: the requested count is always spawned and only the spacing gives.
public static class PropSwarmPlacement
{
    public const int AttemptsPerInstance = 24;

    // Places one prop of a swarm and appends it to `accepted`.
    // `relaxed` is true when the requested minDist could not be honoured.
    public static Vector2 PlaceOne(System.Random rng, Vector2 center, float radius,
        float minDistance, List<Vector2> accepted, out bool relaxed)
    {
        float spacing = Mathf.Max(minDistance, 1f);
        Vector2 best = center;
        float bestNearest = float.NegativeInfinity;
        relaxed = false;

        for (int attempt = 0; attempt < AttemptsPerInstance; attempt++)
        {
            double angle = rng.NextDouble() * Mathf.PI * 2.0;
            double distance = radius * System.Math.Sqrt(rng.NextDouble());

            Vector2 candidate = new Vector2(
                center.x + (float)(System.Math.Cos(angle) * distance),
                center.y + (float)(System.Math.Sin(angle) * distance));

            float nearest = float.PositiveInfinity;

            foreach (Vector2 other in accepted)
            {
                float candidateDistance = Vector2.Distance(candidate, other);

                if (candidateDistance < nearest)
                    nearest = candidateDistance;
            }

            if (nearest >= spacing)
            {
                accepted.Add(candidate);
                return candidate;
            }

            if (nearest > bestNearest)
            {
                bestNearest = nearest;
                best = candidate;
            }
        }

        // No candidate respected the spacing: keep the count by using the
        // roomiest one instead of dropping the prop.
        relaxed = true;
        accepted.Add(best);
        return best;
    }
}
