#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Editor tool: adds an existing StarSystems.json planet to the galaxy map
// without hand-editing code/JSON. It reproduces exactly what a manual add does:
//   1. Add the exact planet name to GalaxyModeFunctions.StartPlanets.
//   2. Optionally add a visual-only offset to GalaxyModeFunctions.MapDisplayOffsets.
//   3. Optionally add/update a hyperroute stop in that planet's JSON object.
public class GalaxyMapPlanetAdder : EditorWindow
{
    private const string GalaxyFunctionsRelativePath = "Scripts/Galaxy/GalaxyModeFunctions.cs";
    private const string StartPlanetsMarker = "StartPlanets";
    private const string OffsetsMarker = "MapDisplayOffsets";
    private const string HyperrouteStopsProperty = "hyperrouteStops";
    private const string StatusMessageKey = "OpenGalaxy.GalaxyMapPlanetAdder.Status";
    private const string StatusTypeKey = "OpenGalaxy.GalaxyMapPlanetAdder.StatusType";
    private const int GridColumns = 4;

    private static readonly string[] RoutePresets =
    {
        "Corellian Run",
        "Triellus Trade Route",
        "Otro (nombre personalizado)"
    };

    [SerializeField] private string searchText = "";
    [SerializeField] private string selectedPlanet = "";
    [SerializeField] private bool applyOffset;
    [SerializeField] private Vector2 mapOffset = Vector2.zero;
    [SerializeField] private bool applyRoute;
    [SerializeField] private int routePresetIndex;
    [SerializeField] private string customRoute = "";
    [SerializeField] private int routeOrder;

    private string[] allPlanets = new string[0];
    private string[] filteredPlanets = new string[0];
    private string[] mapPlanets = new string[0];
    private Vector2 mapGridScroll = Vector2.zero;
    private string cachedSearch;
    private StarSystems starSystems;
    private bool dataLoaded;
    private string statusMessage = "Selecciona un planeta de StarSystems.json.";
    private MessageType statusType = MessageType.Info;

    [Serializable]
    private class StopListWrapper
    {
        public List<HyperrouteStop> stops;
    }

    [UnityEditor.MenuItem("Tools/Open Galaxy/Add Planet to Galaxy Map")]
    private static void ShowWindow()
    {
        GalaxyMapPlanetAdder window = GetWindow<GalaxyMapPlanetAdder>("Add Planet to Galaxy Map");
        window.minSize = new Vector2(560f, 620f);

        Rect position = window.position;
        position.width = Mathf.Max(position.width, 680f);
        position.height = Mathf.Max(position.height, 760f);
        window.position = position;
    }

    private void OnEnable()
    {
        // Recompiling after a write reloads the window; keep the last result visible.
        string savedMessage = SessionState.GetString(StatusMessageKey, "");
        MessageType savedType = (MessageType)SessionState.GetInt(StatusTypeKey, (int)MessageType.Info);

        LoadData();

        if (!string.IsNullOrEmpty(savedMessage))
        {
            statusMessage = savedMessage;
            statusType = savedType;
        }
    }

    private void LoadData()
    {
        TextAsset asset = Resources.Load<TextAsset>(OGGetAddress.files + "StarSystems");

        if (asset == null)
        {
            dataLoaded = false;
            allPlanets = new string[0];
            SetStatus("No se encontró Assets/Resources/files/StarSystems.json.", MessageType.Error);
            return;
        }

        starSystems = JsonUtility.FromJson<StarSystems>(asset.text);

        if (starSystems == null || starSystems.starSystemsData == null)
        {
            dataLoaded = false;
            allPlanets = new string[0];
            SetStatus("StarSystems.json no se pudo parsear.", MessageType.Error);
            return;
        }

        // Same rule as GalaxyModeFunctions.GetStartSystems(): first duplicate wins.
        HashSet<string> seen = new HashSet<string>();
        List<string> names = new List<string>();

        foreach (StarSystem system in starSystems.starSystemsData)
        {
            if (system == null || string.IsNullOrEmpty(system.Planet))
            {
                continue;
            }

            if (seen.Add(system.Planet))
            {
                names.Add(system.Planet);
            }
        }

        names.Sort(StringComparer.Ordinal);
        allPlanets = names.ToArray();
        cachedSearch = null;
        dataLoaded = allPlanets.Length > 0;

        RebuildMapPlanets();

        SetStatus(dataLoaded
            ? string.Format("Cargados {0} sistemas únicos desde StarSystems.json.", allPlanets.Length)
            : "StarSystems.json no contiene planetas con nombre.",
            dataLoaded ? MessageType.Info : MessageType.Warning);
    }

    // Planets that are actually reachable today: whitelist entries that also
    // have data in StarSystems.json (same filter used by GetStartSystems()).
    private void RebuildMapPlanets()
    {
        List<string> active = new List<string>();
        HashSet<string> seen = new HashSet<string>();

        foreach (string planetName in GalaxyModeFunctions.StartPlanets)
        {
            if (string.IsNullOrEmpty(planetName) || !seen.Add(planetName))
            {
                continue;
            }

            if (FindSystem(planetName) != null)
            {
                active.Add(planetName);
            }
        }

        active.Sort(StringComparer.Ordinal);
        mapPlanets = active.ToArray();
    }

    private StarSystem FindSystem(string planetName)
    {
        if (starSystems == null || starSystems.starSystemsData == null || string.IsNullOrEmpty(planetName))
        {
            return null;
        }

        foreach (StarSystem system in starSystems.starSystemsData)
        {
            if (system != null && system.Planet == planetName)
            {
                return system;
            }
        }

        return null;
    }

    private void DrawMapGrid()
    {
        EditorGUILayout.LabelField("En el mapa ahora: " + mapPlanets.Length + " sistemas", EditorStyles.boldLabel);

        if (mapPlanets.Length == 0)
        {
            EditorGUILayout.HelpBox("No hay sistemas activos en GalaxyModeFunctions.StartPlanets.", MessageType.Warning);
            return;
        }

        int activeIndex = Array.IndexOf(mapPlanets, selectedPlanet);
        if (activeIndex >= 0)
        {
            int newIndex = EditorGUILayout.Popup("Ir a sistema activo", activeIndex, mapPlanets);
            if (newIndex != activeIndex)
            {
                SelectPlanet(mapPlanets[newIndex]);
            }
        }
        else
        {
            EditorGUILayout.LabelField("Ir a sistema activo", selectedPlanet + " (todavía no está en el mapa)");
        }

        float buttonWidth = Mathf.Max(108f, (position.width - 52f) / GridColumns - 4f);

        mapGridScroll = EditorGUILayout.BeginScrollView(mapGridScroll, GUILayout.Height(180f));
        EditorGUILayout.BeginHorizontal();

        for (int i = 0; i < mapPlanets.Length; i++)
        {
            string planetName = mapPlanets[i];
            bool isSelected = planetName == selectedPlanet;
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = isSelected ? new Color(0.35f, 0.72f, 1f, 1f) : Color.white;

            if (GUILayout.Button("● " + planetName, EditorStyles.miniButton, GUILayout.Width(buttonWidth)))
            {
                SelectPlanet(planetName);
            }

            GUI.backgroundColor = previousBackground;

            if ((i + 1) % GridColumns == 0)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();

        int missing = GalaxyModeFunctions.StartPlanets.Length - mapPlanets.Length;
        if (missing > 0)
        {
            EditorGUILayout.HelpBox(
                missing + " nombre(s) de StartPlanets no tienen datos en StarSystems.json y no se muestran.",
                MessageType.Warning);
        }

        EditorGUILayout.HelpBox(
            "Pulsa cualquier punto para ver todos sus datos en el panel inferior. " +
            "El punto azul es el sistema seleccionado.", MessageType.None);
    }

    private void SelectPlanet(string planetName)
    {
        selectedPlanet = planetName;
        searchText = "";
        cachedSearch = null;
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Añadir planeta al mapa de la galaxia", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "El planeta se añade a la whitelist del mapa igual que los sistemas ya incluidos. " +
            "No se modifican las coordenadas canónicas de StarSystems.json.",
            MessageType.Info);

        if (GUILayout.Button("Recargar StarSystems.json", GUILayout.Height(22)))
        {
            LoadData();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("1 · Sistemas activos en el mapa", EditorStyles.boldLabel);
        DrawMapGrid();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2 · Buscar en StarSystems.json", EditorStyles.boldLabel);

        string newSearch = EditorGUILayout.TextField("Buscar", searchText);
        if (newSearch != searchText)
        {
            searchText = newSearch;
            cachedSearch = null;
        }

        RebuildFilterIfNeeded();

        if (filteredPlanets.Length == 0)
        {
            EditorGUILayout.HelpBox("No hay planetas que coincidan con el filtro.", MessageType.Warning);
        }
        else
        {
            int index = Array.IndexOf(filteredPlanets, selectedPlanet);
            if (index < 0) index = 0;

            int newIndex = EditorGUILayout.Popup("Planeta", index, filteredPlanets);
            if (newIndex != index)
            {
                SelectPlanet(filteredPlanets[Mathf.Clamp(newIndex, 0, filteredPlanets.Length - 1)]);
            }

            EditorGUILayout.LabelField("Sistemas visibles", filteredPlanets.Length + " / " + allPlanets.Length);
        }

        StarSystem system = FindSystem(selectedPlanet);

        if (system != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("3 · Datos del sistema (solo lectura)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Sistema", system.System);
            EditorGUILayout.LabelField("Región / Sector", (system.Region ?? "-") + " / " + (system.Sector ?? "-"));
            EditorGUILayout.LabelField("Grid / Facción", (system.Grid ?? "-") + " / " + (system.faction ?? "-"));
            EditorGUILayout.LabelField("Tipo / Prefab", (system.planetType ?? "-") + " / " +
                                                       (string.IsNullOrEmpty(system.planetPrefab) ? "(según tipo)" : system.planetPrefab));
            EditorGUILayout.LabelField("Coordenadas", string.Format(CultureInfo.InvariantCulture,
                "X {0:0.##}  Y {1:0.##}  Z {2:0.##}", system.X, system.Y, system.Z));
            EditorGUILayout.LabelField("Distancia orbital", system.planetDistance > 0f
                ? system.planetDistance.ToString(CultureInfo.InvariantCulture)
                : "(por defecto)");
            EditorGUILayout.LabelField("Soles", system.suns == null ? "0 (usa el sol por defecto)" : system.suns.Count.ToString());
            EditorGUILayout.LabelField("Rutas actuales", FormatStops(system.hyperrouteStops));

            bool inMap = IsInStartPlanets(selectedPlanet);
            EditorGUILayout.LabelField("Estado del mapa", inMap ? "YA ESTÁ EN EL MAPA" : "NO ESTÁ EN EL MAPA");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("4 · Opciones (opcional)", EditorStyles.boldLabel);

        applyOffset = EditorGUILayout.ToggleLeft("Offset visual (solo si dos sistemas se solapan)", applyOffset);
        if (applyOffset)
        {
            mapOffset = EditorGUILayout.Vector2Field("Offset (X, Y)", mapOffset);
            EditorGUILayout.HelpBox(
                "Es un desplazamiento visual: no altera las coordenadas del JSON. " +
                "Déjalo en (0, 0) para usar la posición real.", MessageType.None);
        }

        applyRoute = EditorGUILayout.ToggleLeft("Añadir / actualizar parada de hiperruta", applyRoute);
        if (applyRoute)
        {
            routePresetIndex = EditorGUILayout.Popup("Ruta", routePresetIndex, RoutePresets);
            if (routePresetIndex == RoutePresets.Length - 1)
            {
                customRoute = EditorGUILayout.TextField("Nombre de la ruta", customRoute);
            }

            routeOrder = EditorGUILayout.IntField("Orden en la ruta", routeOrder);
            EditorGUILayout.HelpBox(
                "Escribe hyperrouteStops en la entrada del sistema. Si ya existía esa ruta, se actualiza su orden.",
                MessageType.None);
        }

        EditorGUILayout.Space();
        EditorGUI.BeginDisabledGroup(EditorApplication.isPlaying || !dataLoaded || string.IsNullOrEmpty(selectedPlanet));
        if (GUILayout.Button("Añadir al mapa", GUILayout.Height(30)))
        {
            ApplyChanges(true);
        }

        if (GUILayout.Button("Quitar del mapa"))
        {
            ApplyChanges(false);
        }
        EditorGUI.EndDisabledGroup();

        if (EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Sal de Play Mode antes de modificar los archivos del proyecto.", MessageType.Warning);
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(statusMessage, statusType);
        }
    }

    private void RebuildFilterIfNeeded()
    {
        if (cachedSearch == searchText && filteredPlanets != null)
        {
            return;
        }

        cachedSearch = searchText;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            filteredPlanets = allPlanets;
            return;
        }

        string needle = searchText.Trim();
        filteredPlanets = allPlanets.Where(p => p.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
    }

    private static string FormatStops(List<HyperrouteStop> stops)
    {
        if (stops == null || stops.Count == 0)
        {
            return "(ninguna)";
        }

        return string.Join(", ", stops
            .OrderBy(s => s.order)
            .Select(s => (s.route ?? "?") + "(" + s.order + ")")
            .ToArray());
    }

    private static bool IsInStartPlanets(string planetName)
    {
        return !string.IsNullOrEmpty(planetName) && Array.IndexOf(GalaxyModeFunctions.StartPlanets, planetName) >= 0;
    }

    private void ApplyChanges(bool add)
    {
        if (EditorApplication.isPlaying)
        {
            SetStatus("Sal de Play Mode antes de aplicar cambios.", MessageType.Warning);
            return;
        }

        if (string.IsNullOrEmpty(selectedPlanet))
        {
            SetStatus("Selecciona un planeta.", MessageType.Warning);
            return;
        }

        string route = GetRouteName();
        if (applyRoute && string.IsNullOrEmpty(route))
        {
            SetStatus("Escribe un nombre de ruta válido.", MessageType.Warning);
            return;
        }

        string sourcePath = Path.Combine(Application.dataPath, GalaxyFunctionsRelativePath);
        string jsonPath = GetStarSystemsPath();

        if (!File.Exists(sourcePath) || string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
        {
            SetStatus("No se encontraron GalaxyModeFunctions.cs o StarSystems.json.", MessageType.Error);
            return;
        }

        string source = File.ReadAllText(sourcePath);
        string json = File.ReadAllText(jsonPath);
        string originalSource = source;
        string originalJson = json;
        string sourceNewline = DetectNewline(source);
        string jsonNewline = DetectNewline(json);
        List<string> changes = new List<string>();

        int open;
        int close;

        // --- Whitelist (GalaxyModeFunctions.StartPlanets) ---
        if (!TryFindCSharpBlock(source, StartPlanetsMarker, out open, out close))
        {
            SetStatus("No se encontró el bloque StartPlanets en GalaxyModeFunctions.cs.", MessageType.Error);
            return;
        }

        List<string> entries = GetQuotedEntries(source, open, close);
        bool wasInMap = entries.Contains(selectedPlanet);

        if (add && !wasInMap)
        {
            entries.Add(selectedPlanet);
            source = RebuildStringArray(source, open, close, entries, sourceNewline);
            changes.Add(" StartPlanets");
        }
        else if (!add && wasInMap)
        {
            entries.Remove(selectedPlanet);
            source = RebuildStringArray(source, open, close, entries, sourceNewline);
            changes.Add(" StartPlanets");
        }

        // --- Visual offset (GalaxyModeFunctions.MapDisplayOffsets) ---
        if (add && applyOffset && (Mathf.Abs(mapOffset.x) > 0.0001f || Mathf.Abs(mapOffset.y) > 0.0001f))
        {
            if (!TryFindCSharpBlock(source, OffsetsMarker, out open, out close))
            {
                SetStatus("No se encontró el bloque MapDisplayOffsets en GalaxyModeFunctions.cs.", MessageType.Error);
                return;
            }

            string withOffset = SetOffsetEntry(source, open, close, selectedPlanet, mapOffset, sourceNewline);
            if (withOffset != source)
            {
                source = withOffset;
                changes.Add(" MapDisplayOffsets");
            }
        }
        else if (!add)
        {
            if (TryFindCSharpBlock(source, OffsetsMarker, out open, out close))
            {
                string withoutOffset = RemoveOffsetEntry(source, open, close, selectedPlanet, sourceNewline);
                if (withoutOffset != source)
                {
                    source = withoutOffset;
                    changes.Add(" MapDisplayOffsets");
                }
            }
        }

        // --- Hyperroute stop (StarSystems.json) ---
        if (add && applyRoute)
        {
            int objectStart;
            int objectEnd;

            if (!TryFindSystemObject(json, selectedPlanet, out objectStart, out objectEnd))
            {
                SetStatus("No se encontró la entrada de " + selectedPlanet + " en StarSystems.json.", MessageType.Error);
                return;
            }

            string objectText = json.Substring(objectStart, objectEnd - objectStart + 1);
            string updatedObject = SetHyperrouteStop(objectText, route, routeOrder, jsonNewline);

            if (updatedObject != objectText)
            {
                json = json.Substring(0, objectStart) + updatedObject + json.Substring(objectEnd + 1);
                changes.Add(" hyperrouteStops");
            }
        }

        if (changes.Count == 0)
        {
            SetStatus(add
                    ? selectedPlanet + " ya estaba en el mapa y no hay opciones nuevas que aplicar."
                    : selectedPlanet + " no estaba en el mapa.",
                MessageType.Info);
            return;
        }

        if (source != originalSource)
        {
            WriteTextKeepingEncoding(sourcePath, source);
        }

        if (json != originalJson)
        {
            WriteTextKeepingEncoding(jsonPath, json);
        }

        AssetDatabase.Refresh();
        RebuildMapPlanets();

        SetStatus((add ? "Añadido al mapa: " : "Quitado del mapa: ") + selectedPlanet +
                  ". Cambios:" + string.Join(", ", changes.ToArray()) + ".",
            MessageType.Info);
    }

    private string GetRouteName()
    {
        if (!applyRoute)
        {
            return "";
        }

        if (routePresetIndex >= 0 && routePresetIndex < RoutePresets.Length - 1)
        {
            return RoutePresets[routePresetIndex];
        }

        return customRoute == null ? "" : customRoute.Trim();
    }

    private static string GetStarSystemsPath()
    {
        TextAsset asset = Resources.Load<TextAsset>(OGGetAddress.files + "StarSystems");
        if (asset == null)
        {
            return null;
        }

        string assetPath = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
    }

    private void SetStatus(string message, MessageType type)
    {
        statusMessage = message;
        statusType = type;
        SessionState.SetString(StatusMessageKey, message);
        SessionState.SetInt(StatusTypeKey, (int)type);
        Repaint();
    }

    // ------------------------------------------------------------------
    // C# source helpers
    // ------------------------------------------------------------------

    private static bool TryFindCSharpBlock(string source, string marker, out int open, out int close)
    {
        open = -1;
        close = -1;

        int markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        open = source.IndexOf('{', markerIndex);
        if (open < 0)
        {
            return false;
        }

        close = FindMatchingBrace(source, open);
        return close > open;
    }

    private static int FindMatchingBrace(string text, int open)
    {
        int depth = 0;
        bool inString = false;
        bool inChar = false;
        bool inLineComment = false;
        bool inBlockComment = false;

        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (inString)
            {
                if (c == '\\') { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }

            if (inChar)
            {
                if (c == '\\') { i++; continue; }
                if (c == '\'') inChar = false;
                continue;
            }

            if (c == '/' && next == '/') { inLineComment = true; i++; continue; }
            if (c == '/' && next == '*') { inBlockComment = true; i++; continue; }
            if (c == '"') { inString = true; continue; }
            if (c == '\'') { inChar = true; continue; }

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    private static readonly Regex QuotedStringRegex = new Regex("\"((?:\\\\.|[^\"\\\\])*)\"");

    private static List<string> GetQuotedEntries(string source, int open, int close)
    {
        List<string> result = new List<string>();
        string inner = source.Substring(open + 1, close - open - 1);

        foreach (Match match in QuotedStringRegex.Matches(inner))
        {
            result.Add(UnescapeCSharpString(match.Groups[1].Value));
        }

        return result;
    }

    private static string RebuildStringArray(string source, int open, int close, List<string> entries, string nl)
    {
        string inner = source.Substring(open + 1, close - open - 1);
        Match first = QuotedStringRegex.Match(inner);
        string entryIndent = first.Success ? DetectIndent(source, open + 1 + first.Index) : "        ";
        string closeIndent = DetectIndent(source, close);

        StringBuilder builder = new StringBuilder();
        builder.Append('{').Append(nl);

        for (int i = 0; i < entries.Count; i++)
        {
            builder.Append(entryIndent)
                   .Append('"').Append(EscapeCSharpString(entries[i])).Append('"');

            if (i < entries.Count - 1)
            {
                builder.Append(',');
            }

            builder.Append(nl);
        }

        builder.Append(closeIndent).Append('}');

        return source.Substring(0, open) + builder.ToString() + source.Substring(close + 1);
    }

    private static string SetOffsetEntry(string source, int open, int close, string planet, Vector2 offset, string nl)
    {
        if (TryGetOffsetEntryLine(source, open, close, planet, out int lineStart, out int lineEnd))
        {
            source = source.Remove(lineStart, lineEnd - lineStart);
            if (!TryFindCSharpBlock(source, OffsetsMarker, out open, out close))
            {
                return source;
            }
        }

        string indent = "        ";
        Match first = QuotedStringRegex.Match(source.Substring(open + 1, close - open - 1));
        if (first.Success)
        {
            indent = DetectIndent(source, open + 1 + first.Index);
        }

        string x = offset.x.ToString("0.###", CultureInfo.InvariantCulture);
        string y = offset.y.ToString("0.###", CultureInfo.InvariantCulture);
        string entry = indent + "{ \"" + EscapeCSharpString(planet) + "\", new Vector2(" + x + "f, " + y + "f) },";

        source = source.Insert(open + 1, nl + entry);
        return NormalizeOffsetCommas(source, open, close + nl.Length + entry.Length, nl);
    }

    private static string RemoveOffsetEntry(string source, int open, int close, string planet, string nl)
    {
        int lineStart;
        int lineEnd;

        if (!TryGetOffsetEntryLine(source, open, close, planet, out lineStart, out lineEnd))
        {
            return source;
        }

        source = source.Remove(lineStart, lineEnd - lineStart);
        if (!TryFindCSharpBlock(source, OffsetsMarker, out open, out close))
        {
            return source;
        }

        return NormalizeOffsetCommas(source, open, close, nl);
    }

    private static bool TryGetOffsetEntryLine(string source, int open, int close, string planet,
        out int lineStart, out int lineEnd)
    {
        lineStart = -1;
        lineEnd = -1;

        string pattern = "^\\s*\\{\\s*\"" + Regex.Escape(EscapeCSharpString(planet)) +
                         "\"\\s*,\\s*new Vector2\\([^)]*\\)\\s*\\},?\\s*$";
        Regex regex = new Regex(pattern);

        int position = open + 1;
        while (position < close)
        {
            int end = source.IndexOf('\n', position);
            if (end < 0 || end >= close)
            {
                end = close;
            }

            string line = source.Substring(position, end - position).TrimEnd('\r');
            if (regex.IsMatch(line))
            {
                lineStart = position;
                lineEnd = end < close ? end + 1 : end;
                return true;
            }

            if (end >= close)
            {
                break;
            }

            position = end + 1;
        }

        return false;
    }

    private static string NormalizeOffsetCommas(string source, int open, int close, string nl)
    {
        string inner = source.Substring(open + 1, close - open - 1);
        string[] lines = inner.Split(new[] { nl }, StringSplitOptions.None);
        Regex entryRegex = new Regex("^\\s*\\{\\s*\".*?\"\\s*,\\s*new Vector2");
        List<int> entryLines = new List<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (entryRegex.IsMatch(lines[i].TrimEnd('\r')))
            {
                entryLines.Add(i);
            }
        }

        if (entryLines.Count == 0)
        {
            return source;
        }

        for (int i = 0; i < entryLines.Count; i++)
        {
            int line = entryLines[i];
            string text = lines[line].TrimEnd('\r').TrimEnd();
            if (text.EndsWith(",", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1);
            }

            if (i < entryLines.Count - 1)
            {
                text += ",";
            }

            lines[line] = text;
        }

        return source.Substring(0, open + 1) + string.Join(nl, lines) + source.Substring(close);
    }

    private static string DetectIndent(string text, int index)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        int position = lineStart;

        while (position < text.Length && (text[position] == ' ' || text[position] == '\t'))
        {
            position++;
        }

        return text.Substring(lineStart, position - lineStart);
    }

    private static string DetectNewline(string text)
    {
        return text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
    }

    private static string EscapeCSharpString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
    }

    private static string UnescapeCSharpString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                builder.Append(value[i]);
                continue;
            }

            char next = value[++i];
            switch (next)
            {
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case '"': builder.Append('"'); break;
                case '\'': builder.Append('\''); break;
                case '\\': builder.Append('\\'); break;
                default: builder.Append(next); break;
            }
        }

        return builder.ToString();
    }

    private static void WriteTextKeepingEncoding(string path, string text)
    {
        byte[] existing = File.ReadAllBytes(path);
        bool hasBom = existing.Length >= 3 && existing[0] == 0xEF && existing[1] == 0xBB && existing[2] == 0xBF;
        File.WriteAllText(path, text, new UTF8Encoding(hasBom));
    }

    // ------------------------------------------------------------------
    // JSON helpers (targeted edit: no full-file reserialization)
    // ------------------------------------------------------------------

    private static bool TryFindSystemObject(string json, string planetName, out int start, out int end)
    {
        start = -1;
        end = -1;

        List<int> objectStack = new List<int>();

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (c == '"')
            {
                i = SkipJsonString(json, i);
                continue;
            }

            if (c == '{')
            {
                objectStack.Add(i);
                continue;
            }

            if (c != '}' || objectStack.Count == 0)
            {
                continue;
            }

            int objectStart = objectStack[objectStack.Count - 1];
            objectStack.RemoveAt(objectStack.Count - 1);

            // The system objects are the children of the root object (root remains).
            if (objectStack.Count != 1)
            {
                continue;
            }

            string objectText = json.Substring(objectStart, i - objectStart + 1);
            StarSystem system = JsonUtility.FromJson<StarSystem>(objectText);

            if (system != null && system.Planet == planetName)
            {
                start = objectStart;
                end = i;
                return true;
            }
        }

        return false;
    }

    private static int SkipJsonString(string text, int index)
    {
        index++;
        while (index < text.Length)
        {
            char c = text[index];
            if (c == '\\')
            {
                index += 2;
                continue;
            }

            if (c == '"')
            {
                return index;
            }

            index++;
        }

        return text.Length - 1;
    }

    private static bool TryFindJsonProperty(string objectText, string propertyName,
        out int keyStart, out int valueStart, out int valueEnd)
    {
        keyStart = -1;
        valueStart = -1;
        valueEnd = -1;

        int depth = 0;

        for (int i = 1; i < objectText.Length - 1; i++)
        {
            char c = objectText[i];

            if (c == '"')
            {
                int close = SkipJsonString(objectText, i);

                if (depth == 0)
                {
                    string token = objectText.Substring(i + 1, close - i - 1);

                    if (token == propertyName)
                    {
                        int p = close + 1;
                        while (p < objectText.Length && char.IsWhiteSpace(objectText[p])) p++;

                        if (p < objectText.Length && objectText[p] == ':')
                        {
                            keyStart = i;
                            p++;
                            while (p < objectText.Length && char.IsWhiteSpace(objectText[p])) p++;
                            valueStart = p;
                            valueEnd = FindJsonValueEnd(objectText, p);
                            return true;
                        }
                    }
                }

                i = close;
                continue;
            }

            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']') depth--;
        }

        return false;
    }

    private static int FindJsonValueEnd(string text, int start)
    {
        if (start >= text.Length)
        {
            return start;
        }

        char first = text[start];

        if (first == '{' || first == '[')
        {
            char open = first;
            char close = first == '{' ? '}' : ']';
            int depth = 0;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '"')
                {
                    i = SkipJsonString(text, i);
                    continue;
                }

                if (c == open) depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0) return i + 1;
                }
            }

            return text.Length;
        }

        int position = start;
        for (; position < text.Length; position++)
        {
            char c = text[position];

            if (c == '"')
            {
                position = SkipJsonString(text, position);
                continue;
            }

            if (c == ',' || c == '}' || c == ']')
            {
                break;
            }
        }

        return position;
    }

    private static string SetHyperrouteStop(string objectText, string route, int order, string nl)
    {
        List<HyperrouteStop> stops = ReadStops(objectText);
        stops.RemoveAll(s => s != null && s.route == route);
        stops.Add(new HyperrouteStop { route = route, order = order });
        stops.Sort((a, b) => a.order.CompareTo(b.order));

        StringBuilder value = new StringBuilder();
        value.Append('[');

        for (int i = 0; i < stops.Count; i++)
        {
            if (i > 0)
            {
                value.Append(',').Append(nl).Append("      ");
            }

            value.Append(JsonUtility.ToJson(stops[i]));
        }

        value.Append(']');

        return SetJsonProperty(objectText, HyperrouteStopsProperty, value.ToString(), nl);
    }

    private static List<HyperrouteStop> ReadStops(string objectText)
    {
        int valueStart;
        int valueEnd;

        if (!TryFindJsonProperty(objectText, HyperrouteStopsProperty, out _, out valueStart, out valueEnd))
        {
            return new List<HyperrouteStop>();
        }

        string arrayText = objectText.Substring(valueStart, valueEnd - valueStart);
        StopListWrapper wrapper = JsonUtility.FromJson<StopListWrapper>("{\"stops\":" + arrayText + "}");

        return wrapper != null && wrapper.stops != null ? wrapper.stops : new List<HyperrouteStop>();
    }

    private static string SetJsonProperty(string objectText, string propertyName, string valueJson, string nl)
    {
        int valueStart;
        int valueEnd;

        if (TryFindJsonProperty(objectText, propertyName, out _, out valueStart, out valueEnd))
        {
            return objectText.Substring(0, valueStart) + valueJson + objectText.Substring(valueEnd);
        }

        int close = objectText.LastIndexOf('}');
        if (close < 0)
        {
            return objectText;
        }

        int lastContent = close - 1;
        while (lastContent >= 0 && char.IsWhiteSpace(objectText[lastContent]))
        {
            lastContent--;
        }

        int closeLineStart = objectText.LastIndexOf('\n', Math.Max(0, close - 1)) + 1;
        string result = objectText;

        if (lastContent >= 0 && objectText[lastContent] != '{' && objectText[lastContent] != ',')
        {
            result = result.Insert(lastContent + 1, ",");
            if (lastContent + 1 <= closeLineStart)
            {
                closeLineStart++;
            }
        }

        int planetKey = result.IndexOf("\"Planet\"", StringComparison.Ordinal);
        string indent = planetKey >= 0 ? DetectIndent(result, planetKey) : "      ";

        result = result.Insert(closeLineStart,
            indent + "\"" + propertyName + "\": " + valueJson + nl);

        return result;
    }
}
#endif
