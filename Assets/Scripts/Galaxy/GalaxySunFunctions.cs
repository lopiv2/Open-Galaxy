using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Procedural suns for Galaxy Mode systems (test case: Tatooine's binary suns).
// Each sun is a bright sphere plus a data-driven SRP lens flare.
// Sun definitions come from the optional "suns" array of a StarSystems.json entry.
public static class GalaxySunFunctions
{
    private static readonly List<GameObject> suns = new List<GameObject>();
    private static LensFlareDataSRP cachedFlareData;

    public static void SpawnSunsForSystem(string planetName)
    {
        ClearSuns();

        StarSystemSun[] definitions = GetSunDefinitions(planetName);

        if (definitions == null || definitions.Length == 0)
        {
            return;
        }

        Scene scene = SceneFunctions.GetScene();

        if (scene == null || scene.sceneLightGO == null)
        {
            return;
        }

        LensFlareDataSRP flareData = GetOrCreateFlareData();

        if (flareData == null)
        {
            return;
        }

        Vector3 lightForward = scene.sceneLightGO.transform.forward.normalized;
        Vector3 lightRight = scene.sceneLightGO.transform.right.normalized;

        foreach (StarSystemSun sun in definitions)
        {
            if (sun == null)
            {
                continue;
            }

            float distance = sun.distance > 0f ? sun.distance : 22000f;
            float scale = sun.scale > 0f ? sun.scale : 800f;

            float lateral = Mathf.Tan(sun.angle * Mathf.Deg2Rad) * distance;
            Vector3 sunPosition = -lightForward * distance + lightRight * lateral;

            GameObject sunGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sunGO.name = "sun_" + planetName + "_" + suns.Count;
            sunGO.transform.position = sunPosition;
            sunGO.transform.localScale = Vector3.one * scale;
            sunGO.layer = 0;

            MeshRenderer renderer = sunGO.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

                if (unlit != null)
                {
                    Material sunMaterial = new Material(unlit);
                    sunMaterial.color = new Color(5f, 3.4f, 1.8f, 1f);
                    renderer.material = sunMaterial;
                }

                Object.Destroy(sunGO.GetComponent<Collider>());
            }

            LensFlareComponentSRP flare = sunGO.AddComponent<LensFlareComponentSRP>();
            flare.lensFlareData = flareData;
            flare.intensity = 2f;
            flare.scale = 1f;
            flare.useOcclusion = false;

            if (scene.transform != null)
            {
                sunGO.transform.SetParent(scene.transform, true);
            }

            suns.Add(sunGO);
        }

        HudFunctions.AddToMissionLog(definitions.Length + " sun(s) visible at " + planetName + ".");
    }

    public static void ClearSuns()
    {
        foreach (GameObject sun in suns)
        {
            if (sun != null)
            {
                GameObject.Destroy(sun);
            }
        }

        suns.Clear();
    }

    private static StarSystemSun[] GetSunDefinitions(string planetName)
    {
        TextAsset starSystemFile = Resources.Load(OGGetAddress.files + "StarSystems") as TextAsset;

        if (starSystemFile == null)
        {
            return null;
        }

        StarSystems starSystems = JsonUtility.FromJson<StarSystems>(starSystemFile.text);

        if (starSystems == null || starSystems.starSystemsData == null)
        {
            return null;
        }

        foreach (StarSystem system in starSystems.starSystemsData)
        {
            if (system != null && system.Planet == planetName && system.suns != null && system.suns.Count > 0)
            {
                return system.suns.ToArray();
            }
        }

        return null;
    }

    private static LensFlareDataSRP GetOrCreateFlareData()
    {
        if (cachedFlareData != null)
        {
            return cachedFlareData;
        }

        cachedFlareData = ScriptableObject.CreateInstance<LensFlareDataSRP>();
        cachedFlareData.elements = new LensFlareDataElementSRP[2];

        // Bright core.
        LensFlareDataElementSRP core = new LensFlareDataElementSRP();
        core.flareType = SRPLensFlareType.Circle;
        core.uniformScale = 1.4f;
        core.localIntensity = 1.6f;
        core.tint = new Color(1f, 0.85f, 0.6f, 0.6f);
        core.modulateByLightColor = false;
        cachedFlareData.elements[0] = core;

        // Wide soft halo.
        LensFlareDataElementSRP halo = new LensFlareDataElementSRP();
        halo.flareType = SRPLensFlareType.Circle;
        halo.uniformScale = 4.5f;
        halo.localIntensity = 0.35f;
        halo.tint = new Color(1f, 0.7f, 0.4f, 0.35f);
        halo.modulateByLightColor = false;
        cachedFlareData.elements[1] = halo;

        return cachedFlareData;
    }
}
