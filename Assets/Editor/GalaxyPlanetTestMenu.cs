#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class GalaxyPlanetTestMenu
{
    private const string StartTestOnPlayKey = "OpenGalaxy.StartPlanetTestOnPlay";
    private static int readyTicks;

    static GalaxyPlanetTestMenu()
    {
        EditorApplication.update -= RunWhenReady;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        if (SessionState.GetBool(StartTestOnPlayKey, false))
        {
            EditorApplication.update += RunWhenReady;
        }
    }

    [UnityEditor.MenuItem("Tools/Open Galaxy/Test A-Wing en Tatooine (planeta)")]
    private static void RunTest()
    {
        if (EditorApplication.isPlaying)
        {
            GalaxyPlanetTestTool.TestAwingTatooine();
            return;
        }

        SessionState.SetBool(StartTestOnPlayKey, true);
        readyTicks = 0;
        EditorApplication.update -= RunWhenReady;
        EditorApplication.update += RunWhenReady;
        EditorApplication.isPlaying = true;
    }

    private static void RunWhenReady()
    {
        if (!SessionState.GetBool(StartTestOnPlayKey, false))
        {
            EditorApplication.update -= RunWhenReady;
            return;
        }

        if (!EditorApplication.isPlaying)
        {
            return;
        }

        readyTicks++;
        if (readyTicks < 5) return;

        // Espera a que el menu principal exista antes de lanzar el test.
        if (Object.FindAnyObjectByType<MainMenu>() == null)
        {
            if (readyTicks > 600)
            {
                SessionState.SetBool(StartTestOnPlayKey, false);
                EditorApplication.update -= RunWhenReady;
                Debug.LogWarning("[PlanetTest] MainMenu no encontrado, test cancelado.");
            }
            return;
        }

        SessionState.SetBool(StartTestOnPlayKey, false);
        EditorApplication.update -= RunWhenReady;
        GalaxyPlanetTestTool.TestAwingTatooine();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(StartTestOnPlayKey, false);
            EditorApplication.update -= RunWhenReady;
        }
    }
}
#endif
