using System;
using System.Collections.Generic;
using UnityEngine;

// A single prop instance generated from the PlanetPOIs data. Both the game
// (PlanetNavigation) and the editor preview use this so a town looks exactly
// the same in both places.
public struct PlanetPropPlacement
{
    public string type;
    public float x;
    public float z;
    public float rotY;
    public float scaleMul;
    public string siteId;
    public string mode;
}

// Shared rules for interpreting PlanetPOIs.json:
// - props[]  -> isolated landmarks (or legacy swarm groups with count > 1)
// - sites[]  -> towns/outposts generated from PropsTypes catalog types
//
// JsonUtility can only serialize arrays and primitive/string values, so the
// layouts and kinds are plain strings. Everything here is deterministic from
// the seed so the preview and the game always agree.
public static class PlanetContentUtility
{
    public const string LayoutScatter = "scatter";
    public const string LayoutGrid = "grid";
    public const string LayoutRadial = "radial";
    public const string LayoutAlongPath = "alongPath";

    public static void Normalize(PlanetPOILocation location)
    {
        if (location == null)
            return;

        if (location.pois == null)
            location.pois = new PlanetPOIEntry[0];
        if (location.props == null)
            location.props = new PlanetPOIProp[0];
        if (location.sites == null)
            location.sites = new PlanetPOISite[0];
        if (location.creatures == null)
            location.creatures = new PlanetPOICreature[0];

        foreach (PlanetPOISite site in location.sites)
        {
            if (site != null && site.rules == null)
                site.rules = new PlanetPOISiteRule[0];
        }
    }

    public static string GetPropType(PlanetPOIProp prop)
    {
        if (prop == null)
            return "";
        return !string.IsNullOrEmpty(prop.type) ? prop.type : (prop.prefab ?? "");
    }

    public static string GetPoiId(PlanetPOIEntry poi)
    {
        if (poi == null)
            return "";
        return !string.IsNullOrEmpty(poi.id) ? poi.id : (poi.name ?? "");
    }

    public static string GetSiteId(PlanetPOISite site)
    {
        if (site == null)
            return "";
        return !string.IsNullOrEmpty(site.id) ? site.id : (site.name ?? "");
    }

    public static string NormalizeLayout(string layout)
    {
        if (string.Equals(layout, LayoutGrid, StringComparison.OrdinalIgnoreCase))
            return LayoutGrid;
        if (string.Equals(layout, LayoutRadial, StringComparison.OrdinalIgnoreCase))
            return LayoutRadial;
        if (string.Equals(layout, LayoutAlongPath, StringComparison.OrdinalIgnoreCase))
            return LayoutAlongPath;
        return LayoutScatter;
    }

    // Scale uses its own RNG stream. Sharing it with position/yaw meant that
    // enabling scaleVar consumed an extra random value and silently changed
    // the yaw (and scatter position) of every following prop.
    private static float ScaleVariation(System.Random scaleRng, float scaleVar)
    {
        if (scaleVar <= 0f)
            return 1f;

        float amount = Mathf.Clamp01(scaleVar);
        float mul = 1f + ((float)scaleRng.NextDouble() * 2f - 1f) * amount;
        // A multiplier below zero mirrors the model and looks like a rotation.
        return Mathf.Max(0.05f, mul);
    }

    // Returns every prop the planet should spawn, in deterministic order.
    // `relaxedCount` counts instances whose requested spacing could not be
    // honoured; `warnings` contains at most one readable line per group.
    public static List<PlanetPropPlacement> BuildPlacements(PlanetPOILocation location,
        out int relaxedCount, out List<string> warnings)
    {
        List<PlanetPropPlacement> result = new List<PlanetPropPlacement>();
        relaxedCount = 0;
        warnings = new List<string>();

        if (location == null)
            return result;

        Normalize(location);

        foreach (PlanetPOIProp prop in location.props)
        {
            string type = GetPropType(prop);
            if (prop == null || string.IsNullOrEmpty(type))
                continue;

            BuildLegacyProp(prop, type, result, ref relaxedCount, warnings);
        }

        foreach (PlanetPOISite site in location.sites)
            BuildSite(site, result, ref relaxedCount, warnings);

        return result;
    }

    private static void BuildLegacyProp(PlanetPOIProp prop, string type,
        List<PlanetPropPlacement> result, ref int relaxedCount, List<string> warnings)
    {
        int count = Mathf.Max(prop.count, 1);
        System.Random rng = new System.Random(prop.seed);
        System.Random scaleRng = new System.Random(prop.seed * 7 + 3);
        List<Vector2> accepted = new List<Vector2>();
        bool relaxed = false;

        for (int n = 0; n < count; n++)
        {
            float px = prop.x;
            float pz = prop.z;

            if (count > 1 && prop.radius > 0f)
            {
                Vector2 point = PropSwarmPlacement.PlaceOne(
                    rng, new Vector2(prop.x, prop.z), prop.radius, prop.minDist,
                    accepted, out bool currentRelaxed);
                px = point.x;
                pz = point.y;

                if (currentRelaxed)
                {
                    relaxed = true;
                    relaxedCount++;
                }
            }

            float rotY = prop.randomYaw ? (float)(rng.NextDouble() * 360.0) : prop.rotY;
            float scaleMul = Mathf.Approximately(prop.scale, 0f) ? 1f : prop.scale;
            scaleMul *= ScaleVariation(scaleRng, prop.scaleVar);

            result.Add(new PlanetPropPlacement
            {
                type = type,
                x = px,
                z = pz,
                rotY = rotY,
                scaleMul = scaleMul,
                siteId = prop.site,
                mode = "legacy"
            });
        }

        if (relaxed)
        {
            warnings.Add("Prop " + type + ": no caben " + count
                + " unidades con separación " + prop.minDist + " m en un radio de "
                + prop.radius + " m; se colocan con separación reducida. "
                + "Conviértelo en un asentamiento para ajustar radio/separación.");
        }

        if (count > 1 && prop.radius <= 0f)
        {
            warnings.Add("Prop " + type + ": radio 0 con " + count
                + " unidades; se apilan en el mismo punto.");
        }
    }

    private static void BuildSite(PlanetPOISite site, List<PlanetPropPlacement> result,
        ref int relaxedCount, List<string> warnings)
    {
        if (site == null || site.rules == null || site.rules.Length == 0)
            return;

        List<PlanetPOISiteRule> rules = new List<PlanetPOISiteRule>();
        int total = 0;

        foreach (PlanetPOISiteRule rule in site.rules)
        {
            if (rule == null || string.IsNullOrEmpty(rule.type) || rule.count <= 0)
                continue;

            rules.Add(rule);
            total += rule.count;
        }

        if (total <= 0)
            return;

        string layout = NormalizeLayout(site.layout);
        string siteId = GetSiteId(site);
        float radius = Mathf.Max(site.radius, 0f);
        float spacing = Mathf.Max(site.spacing, 1f);

        if (layout == LayoutScatter)
        {
            BuildScatterSite(site, siteId, rules, total, radius, spacing,
                result, ref relaxedCount, warnings);
            return;
        }

        List<Vector2> localPositions = GenerateStructuredPositions(
            layout, site, radius, spacing, total, warnings);

        if (localPositions.Count < total)
        {
            FillWithScatter(localPositions, total, radius, spacing, site.seed,
                out int fillRelaxed);

            if (fillRelaxed > 0)
            {
                relaxedCount += fillRelaxed;
                warnings.Add("Sitio " + DescribeSite(site) + ": el layout " + layout
                    + " no tiene cabida para " + total
                    + " props; se rellenó el resto con separación reducida.");
            }
        }

        if (localPositions.Count > total)
            localPositions.RemoveRange(total, localPositions.Count - total);

        int[] remaining = new int[rules.Count];
        for (int i = 0; i < rules.Count; i++)
            remaining[i] = Mathf.Max(rules[i].count, 0);

        System.Random ruleRng = new System.Random(site.seed * 31 + 17);
        System.Random detailRng = new System.Random(site.seed * 131 + 7);
        System.Random scaleRng = new System.Random(site.seed * 17 + 11);
        float rotation = site.rotation;

        foreach (Vector2 local in localPositions)
        {
            int ruleIndex = PickRule(rules, remaining, ruleRng);

            if (ruleIndex < 0)
                break;

            PlanetPOISiteRule rule = rules[ruleIndex];
            remaining[ruleIndex]--;

            Vector2 world = Rotate(local, rotation);
            float rotY = rule.randomYaw
                ? (float)(detailRng.NextDouble() * 360.0)
                : rotation;
            float scaleMul = Mathf.Approximately(rule.scale, 0f) ? 1f : rule.scale;
            scaleMul *= ScaleVariation(scaleRng, rule.scaleVar);

            result.Add(new PlanetPropPlacement
            {
                type = rule.type,
                x = site.x + world.x,
                z = site.z + world.y,
                rotY = rotY,
                scaleMul = scaleMul,
                siteId = siteId,
                mode = rule.mode
            });
        }
    }

    // Scatter deliberately uses one RNG for positions and rotations so a
    // legacy prop group converted to a site keeps exactly the same layout.
    private static void BuildScatterSite(PlanetPOISite site, string siteId,
        List<PlanetPOISiteRule> rules, int total, float radius, float spacing,
        List<PlanetPropPlacement> result, ref int relaxedCount, List<string> warnings)
    {
        System.Random rng = new System.Random(site.seed);
        System.Random ruleRng = new System.Random(site.seed * 31 + 17);
        System.Random scaleRng = new System.Random(site.seed * 17 + 11);
        List<Vector2> accepted = new List<Vector2>();
        int[] remaining = new int[rules.Count];

        for (int i = 0; i < rules.Count; i++)
            remaining[i] = Mathf.Max(rules[i].count, 0);

        int relaxed = 0;

        for (int n = 0; n < total; n++)
        {
            Vector2 point = new Vector2(site.x, site.z);

            if (radius > 0f && total > 1)
            {
                point = PropSwarmPlacement.PlaceOne(
                    rng, new Vector2(site.x, site.z), radius, spacing,
                    accepted, out bool currentRelaxed);

                if (currentRelaxed)
                {
                    relaxed++;
                    relaxedCount++;
                }
            }
            else if (total > 1)
            {
                accepted.Add(point);
            }

            int ruleIndex = PickRule(rules, remaining, ruleRng);

            if (ruleIndex < 0)
                break;

            PlanetPOISiteRule rule = rules[ruleIndex];
            remaining[ruleIndex]--;

            float rotY = rule.randomYaw
                ? (float)(rng.NextDouble() * 360.0)
                : site.rotation;
            float scaleMul = Mathf.Approximately(rule.scale, 0f) ? 1f : rule.scale;
            scaleMul *= ScaleVariation(scaleRng, rule.scaleVar);

            result.Add(new PlanetPropPlacement
            {
                type = rule.type,
                x = point.x,
                z = point.y,
                rotY = rotY,
                scaleMul = scaleMul,
                siteId = siteId,
                mode = rule.mode
            });
        }

        if (relaxed > 0)
        {
            warnings.Add("Sitio " + DescribeSite(site) + ": no caben " + total
                + " props con separación " + spacing + " m en un radio de "
                + radius + " m; se colocan con separación reducida.");
        }

        if (total > 1 && radius <= 0f)
            warnings.Add("Sitio " + DescribeSite(site) + ": radio 0 con " + total
                + " props; se apilan en el mismo punto.");
    }

    private static int PickRule(List<PlanetPOISiteRule> rules, int[] remaining,
        System.Random rng)
    {
        float totalWeight = 0f;

        for (int i = 0; i < rules.Count; i++)
        {
            if (remaining[i] > 0)
                totalWeight += Mathf.Max(rules[i].weight, 0.001f);
        }

        if (totalWeight <= 0f)
            return -1;

        double roll = rng.NextDouble() * totalWeight;

        for (int i = 0; i < rules.Count; i++)
        {
            if (remaining[i] <= 0)
                continue;

            float weight = Mathf.Max(rules[i].weight, 0.001f);

            if (roll < weight)
                return i;

            roll -= weight;
        }

        for (int i = 0; i < rules.Count; i++)
        {
            if (remaining[i] > 0)
                return i;
        }

        return -1;
    }

    private static List<Vector2> GenerateStructuredPositions(string layout,
        PlanetPOISite site, float radius, float spacing, int total,
        List<string> warnings)
    {
        List<Vector2> candidates = new List<Vector2>();
        System.Random rng = new System.Random(site.seed * 17 + 5);

        if (radius <= 0f)
        {
            candidates.Add(Vector2.zero);
            return candidates;
        }

        switch (layout)
        {
            case LayoutGrid:
                BuildGridCandidates(candidates, radius, spacing);
                break;
            case LayoutRadial:
                BuildRadialCandidates(candidates, radius, spacing);
                break;
            case LayoutAlongPath:
                BuildAlongPathCandidates(candidates, radius, spacing);
                break;
            default:
                BuildScatterCandidates(candidates, radius, spacing, total, rng);
                break;
        }

        Shuffle(candidates, rng);
        return candidates;
    }

    private static void BuildGridCandidates(List<Vector2> candidates,
        float radius, float spacing)
    {
        int steps = Mathf.Max(1, Mathf.FloorToInt(radius / spacing));

        for (int x = -steps; x <= steps; x++)
        {
            for (int z = -steps; z <= steps; z++)
            {
                Vector2 local = new Vector2(x * spacing, z * spacing);

                if (local.sqrMagnitude <= radius * radius + 0.01f)
                    candidates.Add(local);
            }
        }
    }

    private static void BuildRadialCandidates(List<Vector2> candidates,
        float radius, float spacing)
    {
        candidates.Add(Vector2.zero);
        int rings = Mathf.Max(1, Mathf.FloorToInt(radius / spacing));

        for (int ring = 1; ring <= rings; ring++)
        {
            float ringRadius = ring * spacing;
            int count = Mathf.Max(6, Mathf.FloorToInt(2f * Mathf.PI * ring));
            float offset = ring * 0.37f;

            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)count) * Mathf.PI * 2f + offset;
                candidates.Add(new Vector2(
                    Mathf.Cos(angle) * ringRadius,
                    Mathf.Sin(angle) * ringRadius));
            }
        }
    }

    private static void BuildAlongPathCandidates(List<Vector2> candidates,
        float radius, float spacing)
    {
        int steps = Mathf.Max(0, Mathf.FloorToInt(radius / spacing));
        float side = Mathf.Max(spacing * 0.5f, 1f);

        for (int i = -steps; i <= steps; i++)
        {
            float x = i * spacing;

            if (Mathf.Abs(x) > radius)
                continue;

            candidates.Add(new Vector2(x, -side));
            candidates.Add(new Vector2(x, side));
        }
    }

    private static void BuildScatterCandidates(List<Vector2> candidates,
        float radius, float spacing, int total, System.Random rng)
    {
        List<Vector2> accepted = new List<Vector2>();

        for (int i = 0; i < total; i++)
        {
            Vector2 point = PropSwarmPlacement.PlaceOne(
                rng, Vector2.zero, radius, spacing, accepted, out bool _);
            candidates.Add(point);
        }
    }

    private static void FillWithScatter(List<Vector2> positions, int total,
        float radius, float spacing, int seed, out int relaxedCount)
    {
        relaxedCount = 0;

        if (positions.Count >= total)
            return;

        if (radius <= 0f)
        {
            relaxedCount = Mathf.Max(0, total - positions.Count);

            while (positions.Count < total)
                positions.Add(Vector2.zero);

            return;
        }

        List<Vector2> accepted = new List<Vector2>(positions);
        System.Random rng = new System.Random(seed * 197 + 23);

        while (positions.Count < total)
        {
            Vector2 point = PropSwarmPlacement.PlaceOne(
                rng, Vector2.zero, radius, spacing, accepted, out bool relaxed);

            if (relaxed)
                relaxedCount++;

            positions.Add(point);
        }
    }

    private static void Shuffle(List<Vector2> values, System.Random rng)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            Vector2 temp = values[i];
            values[i] = values[j];
            values[j] = temp;
        }
    }

    private static Vector2 Rotate(Vector2 value, float degrees)
    {
        if (Mathf.Approximately(degrees, 0f))
            return value;

        float radians = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        return new Vector2(
            value.x * cos - value.y * sin,
            value.x * sin + value.y * cos);
    }

    private static string DescribeSite(PlanetPOISite site)
    {
        if (!string.IsNullOrEmpty(site.name))
            return site.name;
        if (!string.IsNullOrEmpty(site.id))
            return site.id;
        return "(" + site.x + ", " + site.z + ")";
    }
}
