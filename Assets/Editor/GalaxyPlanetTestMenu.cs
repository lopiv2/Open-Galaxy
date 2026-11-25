#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class GalaxyPlanetTestMenu
{
    private const string StartTestOnPlayKey = "OpenGalaxy.StartPlanetTestOnPlay";
    private const string StartFootTestOnPlayKey = "OpenGalaxy.StartFootTestOnPlay";
    private static int readyTicks;

    static GalaxyPlanetTestMenu()
    {
        EditorApplication.update -= RunWhenReady;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        if (SessionState.GetBool(StartTestOnPlayKey, false) || SessionState.GetBool(StartFootTestOnPlayKey, false))
        {
            EditorApplication.update += RunWhenReady;
        }
    }

    [UnityEditor.MenuItem("Tools/Open Galaxy/Test X-Wing en Tatooine (planeta)")]
    private static void RunTest()
    {
        if (EditorApplication.isPlaying)
        {
            GalaxyPlanetTestTool.TestAwingTatooine();
            return;
        }

        ArmAndPlay(false);
    }

    [UnityEditor.MenuItem("Tools/Open Galaxy/Test a pie en Tatooine (aterrizado)")]
    private static void RunFootTest()
    {
        if (EditorApplication.isPlaying)
        {
            GalaxyPlanetTestTool.TestOnFootTatooine();
            return;
        }

        ArmAndPlay(true);
    }

    private static void ArmAndPlay(bool footTest)
    {
        SessionState.SetBool(StartTestOnPlayKey, !footTest);
        SessionState.SetBool(StartFootTestOnPlayKey, footTest);
        readyTicks = 0;
        EditorApplication.update -= RunWhenReady;
        EditorApplication.update += RunWhenReady;
        EditorApplication.isPlaying = true;
    }

    private static void RunWhenReady()
    {
        bool startTest = SessionState.GetBool(StartTestOnPlayKey, false);
        bool startFootTest = SessionState.GetBool(StartFootTestOnPlayKey, false);

        if (startTest == false && startFootTest == false)
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
                SessionState.SetBool(StartFootTestOnPlayKey, false);
                EditorApplication.update -= RunWhenReady;
                Debug.LogWarning("[PlanetTest] MainMenu no encontrado, test cancelado.");
            }
            return;
        }

        SessionState.SetBool(StartTestOnPlayKey, false);
        SessionState.SetBool(StartFootTestOnPlayKey, false);
        EditorApplication.update -= RunWhenReady;

        if (startFootTest == true)
        {
            GalaxyPlanetTestTool.TestOnFootTatooine();
        }
        else
        {
            GalaxyPlanetTestTool.TestAwingTatooine();
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(StartTestOnPlayKey, false);
            SessionState.SetBool(StartFootTestOnPlayKey, false);
            EditorApplication.update -= RunWhenReady;
        }
    }
}
#endif
