#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CombatTestMenu
{
    private const string StartFighterTestOnPlayKey = "OpenGalaxy.StartCombatTestOnPlay";
    private const string StartCapitalTestOnPlayKey = "OpenGalaxy.StartCapitalCombatTestOnPlay";
    private static int readyTicks;

    static CombatTestMenu()
    {
        EditorApplication.update -= RunWhenReady;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        if (SessionState.GetBool(StartFighterTestOnPlayKey, false) || SessionState.GetBool(StartCapitalTestOnPlayKey, false))
        {
            EditorApplication.update += RunWhenReady;
        }
    }

    [UnityEditor.MenuItem("Tools/Open Galaxy/Test combate: X-Wing vs caza estatico")]
    private static void RunFighterTest()
    {
        if (EditorApplication.isPlaying)
        {
            CombatTestTool.TestFighterCombat();
            return;
        }

        ArmAndPlay(false);
    }

    [UnityEditor.MenuItem("Tools/Open Galaxy/Test combate: X-Wing vs nave capital estatica")]
    private static void RunCapitalTest()
    {
        if (EditorApplication.isPlaying)
        {
            CombatTestTool.TestCapitalCombat();
            return;
        }

        ArmAndPlay(true);
    }

    private static void ArmAndPlay(bool capitalTest)
    {
        SessionState.SetBool(StartFighterTestOnPlayKey, !capitalTest);
        SessionState.SetBool(StartCapitalTestOnPlayKey, capitalTest);
        readyTicks = 0;
        EditorApplication.update -= RunWhenReady;
        EditorApplication.update += RunWhenReady;
        EditorApplication.isPlaying = true;
    }

    private static void RunWhenReady()
    {
        bool startFighterTest = SessionState.GetBool(StartFighterTestOnPlayKey, false);
        bool startCapitalTest = SessionState.GetBool(StartCapitalTestOnPlayKey, false);

        if (startFighterTest == false && startCapitalTest == false)
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
                SessionState.SetBool(StartFighterTestOnPlayKey, false);
                SessionState.SetBool(StartCapitalTestOnPlayKey, false);
                EditorApplication.update -= RunWhenReady;
                Debug.LogWarning("[CombatTest] MainMenu no encontrado, test cancelado.");
            }
            return;
        }

        SessionState.SetBool(StartFighterTestOnPlayKey, false);
        SessionState.SetBool(StartCapitalTestOnPlayKey, false);
        EditorApplication.update -= RunWhenReady;

        if (startCapitalTest == true)
        {
            CombatTestTool.TestCapitalCombat();
        }
        else
        {
            CombatTestTool.TestFighterCombat();
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(StartFighterTestOnPlayKey, false);
            SessionState.SetBool(StartCapitalTestOnPlayKey, false);
            EditorApplication.update -= RunWhenReady;
        }
    }
}
#endif
