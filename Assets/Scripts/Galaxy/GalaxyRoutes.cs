using System.Collections.Generic;
using UnityEngine;

// Hyperroutes (official Star Wars hyperlanes) for the galaxy map.
// Stops are read from StarSystems.json ("hyperrouteStops" per system) and
// sorted by order: each consecutive pair is a directly connected hyperspace
// leg. The first and last entries are the termini of the route.
// If the JSON carries no usable stops, a hardcoded fallback keeps the map
// working.
public static class GalaxyRoutes
{
    public sealed class Hyperroute
    {
        public readonly string Name;
        public readonly string[] SystemsInOrder;

        public Hyperroute(string name, string[] systemsInOrder)
        {
            Name = name;
            SystemsInOrder = systemsInOrder;
        }

        public bool IsTerminus(string planetName)
        {
            if (string.IsNullOrEmpty(planetName) || SystemsInOrder == null || SystemsInOrder.Length == 0)
            {
                return false;
            }

            return SystemsInOrder[0] == planetName || SystemsInOrder[SystemsInOrder.Length - 1] == planetName;
        }

        public string GetNext(string planetName)
        {
            if (string.IsNullOrEmpty(planetName) || SystemsInOrder == null)
            {
                return null;
            }

            for (int i = 0; i < SystemsInOrder.Length; i++)
            {
                if (SystemsInOrder[i] == planetName)
                {
                    // Next towards the terminus (forward direction).
                    if (i + 1 < SystemsInOrder.Length)
                    {
                        return SystemsInOrder[i + 1];
                    }

                    return null;
                }
            }

            return null;
        }
    }

    // Fallback when the JSON has no stops (keeps the map functional).
    // Corellian Run: Smuggler's Run is its trailing terminus in the Outer Rim
    // (Gaulus Sector), directly connected to the previous stop, Wrea.
    private static readonly string[] FallbackCorellianRunStops = new string[]
    {
        "Coruscant",
        "Corellia",
        "Wrea",
        "Smuggler's Run"
    };

    private static List<Hyperroute> cachedRoutes;

    public static Hyperroute CorellianRun
    {
        get { return GetRoute("Corellian Run"); }
    }

    public static Hyperroute[] Routes
    {
        get
        {
            EnsureLoaded();
            return cachedRoutes.ToArray();
        }
    }

    private static void EnsureLoaded()
    {
        if (cachedRoutes != null)
        {
            return;
        }

        cachedRoutes = new List<Hyperroute>();

        // Group stops by route name, then sort by order.
        Dictionary<string, SortedList<int, string>> stopsByRoute = LoadStopsFromJson();

        foreach (KeyValuePair<string, SortedList<int, string>> entry in stopsByRoute)
        {
            if (string.IsNullOrEmpty(entry.Key) || entry.Value == null || entry.Value.Count < 2)
            {
                continue;
            }

            List<string> ordered = new List<string>(entry.Value.Values);

            // Drop accidental duplicates while keeping first-seen order.
            for (int i = ordered.Count - 1; i > 0; i--)
            {
                if (ordered.IndexOf(ordered[i]) != i)
                {
                    ordered.RemoveAt(i);
                }
            }

            if (ordered.Count >= 2)
            {
                cachedRoutes.Add(new Hyperroute(entry.Key, ordered.ToArray()));
            }
        }

        // NOTE: only map-supported stops are in the JSON for now. As more
        // StartPlanets systems are added, insert their canonical intermediate
        // stops between Corellia and Wrea keeping the [..., Wrea, Smuggler's Run] tail.
        if (GetRouteInternal("Corellian Run") == null)
        {
            cachedRoutes.Add(new Hyperroute("Corellian Run", FallbackCorellianRunStops));
        }
    }

    private static Dictionary<string, SortedList<int, string>> LoadStopsFromJson()
    {
        Dictionary<string, SortedList<int, string>> result = new Dictionary<string, SortedList<int, string>>();

        TextAsset starSystemFile = Resources.Load(OGGetAddress.files + "StarSystems") as TextAsset;

        if (starSystemFile == null)
        {
            return result;
        }

        StarSystems starSystems = JsonUtility.FromJson<StarSystems>(starSystemFile.text);

        if (starSystems == null || starSystems.starSystemsData == null)
        {
            return result;
        }

        foreach (StarSystem system in starSystems.starSystemsData)
        {
            if (system == null || string.IsNullOrEmpty(system.Planet) || system.hyperrouteStops == null)
            {
                continue;
            }

            foreach (HyperrouteStop stop in system.hyperrouteStops)
            {
                if (stop == null || string.IsNullOrEmpty(stop.route))
                {
                    continue;
                }

                SortedList<int, string> ordered;

                if (!result.TryGetValue(stop.route, out ordered))
                {
                    ordered = new SortedList<int, string>();
                    result.Add(stop.route, ordered);
                }

                // One system per order slot; collisions resolve deterministically.
                int slot = stop.order;

                while (ordered.ContainsKey(slot))
                {
                    slot++;
                }

                ordered.Add(slot, system.Planet);
            }
        }

        return result;
    }

    private static Hyperroute GetRouteInternal(string routeName)
    {
        if (string.IsNullOrEmpty(routeName) || cachedRoutes == null)
        {
            return null;
        }

        foreach (Hyperroute route in cachedRoutes)
        {
            if (route != null && route.Name == routeName)
            {
                return route;
            }
        }

        return null;
    }

    public static Hyperroute GetRoute(string routeName)
    {
        EnsureLoaded();
        return GetRouteInternal(routeName);
    }

    // Every route this system belongs to (usually zero or one).
    public static List<Hyperroute> GetRoutesForSystem(string planetName)
    {
        EnsureLoaded();
        List<Hyperroute> result = new List<Hyperroute>();

        if (string.IsNullOrEmpty(planetName) || cachedRoutes == null)
        {
            return result;
        }

        foreach (Hyperroute route in cachedRoutes)
        {
            if (route == null || route.SystemsInOrder == null)
            {
                continue;
            }

            foreach (string stop in route.SystemsInOrder)
            {
                if (stop == planetName)
                {
                    result.Add(route);
                    break;
                }
            }
        }

        return result;
    }

    // Direct hyperspace neighbours of a system along any known route.
    // For Smuggler's Run this returns ["Wrea"]; for Wrea, ["Corellia", "Smuggler's Run"].
    public static List<string> GetConnectedSystems(string planetName)
    {
        EnsureLoaded();
        List<string> result = new List<string>();

        if (string.IsNullOrEmpty(planetName) || cachedRoutes == null)
        {
            return result;
        }

        foreach (Hyperroute route in cachedRoutes)
        {
            if (route == null || route.SystemsInOrder == null)
            {
                continue;
            }

            for (int i = 0; i < route.SystemsInOrder.Length; i++)
            {
                if (route.SystemsInOrder[i] != planetName)
                {
                    continue;
                }

                if (i - 1 >= 0 && !result.Contains(route.SystemsInOrder[i - 1]))
                {
                    result.Add(route.SystemsInOrder[i - 1]);
                }

                if (i + 1 < route.SystemsInOrder.Length && !result.Contains(route.SystemsInOrder[i + 1]))
                {
                    result.Add(route.SystemsInOrder[i + 1]);
                }
            }
        }

        return result;
    }

    // True when both systems share a direct leg on any route.
    public static bool AreConnected(string planetA, string planetB)
    {
        if (string.IsNullOrEmpty(planetA) || string.IsNullOrEmpty(planetB))
        {
            return false;
        }

        return GetConnectedSystems(planetA).Contains(planetB);
    }
}
