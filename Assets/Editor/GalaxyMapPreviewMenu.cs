#if UNITY_EDITOR
using UnityEditor;

[InitializeOnLoad]
public static class GalaxyMapPreviewMenu
{
    private const string StartPreviewOnPlayKey = "OpenGalaxy.StartGalaxyMapPreviewOnPlay";
    private static int previewReadyTicks;

    static GalaxyMapPreviewMenu()
    {
        EditorApplication.update -= OpenPreviewWhenReady;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        if (SessionState.GetBool(StartPreviewOnPlayKey, false))
        {
            EditorApplication.update += OpenPreviewWhenReady;
        }
    }

    [UnityEditor.MenuItem("Tools/Open Galaxy/Preview 3D Galaxy Map")]
    private static void OpenPreview()
    {
        if (EditorApplication.isPlaying)
        {
            GalaxyMapController.ShowPreview();
            return;
        }

        SessionState.SetBool(StartPreviewOnPlayKey, true);
        previewReadyTicks = 0;
        EditorApplication.update -= OpenPreviewWhenReady;
        EditorApplication.update += OpenPreviewWhenReady;
        EditorApplication.isPlaying = true;
    }

    private static void OpenPreviewWhenReady()
    {
        if (!SessionState.GetBool(StartPreviewOnPlayKey, false))
        {
            EditorApplication.update -= OpenPreviewWhenReady;
            return;
        }

        if (!EditorApplication.isPlaying)
        {
            return;
        }

        previewReadyTicks++;
        if (previewReadyTicks < 5) return;

        SessionState.SetBool(StartPreviewOnPlayKey, false);
        EditorApplication.update -= OpenPreviewWhenReady;
        GalaxyMapController.ShowPreview();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(StartPreviewOnPlayKey, false);
            EditorApplication.update -= OpenPreviewWhenReady;
        }
    }
}
#endif
