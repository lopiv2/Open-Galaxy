#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Herramienta de editor para crear naves pilotables sin depender de la IA.
//
// Una nave "funcional" en Open Galaxy NO lleva scripts en el prefab: los anade
// SceneFunctions.LoadSingleShip al instanciarla (Ship, FlightControlSystem_Small,
// FlightControlAI_Small, EnergyManagementSystem, DamageSystem, TargetingSystem,
// WingSystems, EnergyWeapon, TorpedoSystem, WeaponManagement, ShieldVisual,
// SoftLanding, Rigidbody y mesh colliders). Lo que el prefab DEBE tener son los
// transforms con los nombres que esos scripts buscan:
//   camera / followcamera / focuscamera -> camara, seguimiento y enfoque
//   dockingPoint                        -> acoplamiento
//   gunbank01/gunbank01-01..04          -> 4 cañones de energia
//   missilebank01/missilebank01-01..04  -> 4 tubos de torpedos
//   engine1..N                          -> visual de motores (opcional)
// Ademas el tipo debe estar en Resources/files/ShipTypes.json con un
// cockpitPrefab valido de Resources/objects/cockpits y scriptType "smallship"
// para que el jugador pueda pilotarla.
public class ShipCreatorWindow : EditorWindow
{
    private const string MenuPath = "Tools/Open Galaxy/Ship Creator (ShipTypes)";
    private const string ShipTypesPath = "Assets/Resources/files/ShipTypes.json";
    private const string ShipsFolder = "Assets/Resources/objects/ships/";
    private const string CockpitsResourceFolder = "objects/cockpits";
    private const string CockpitAssetsFolder = "Assets/Resources/" + CockpitsResourceFolder;

    private enum CreationMode { NewFromScratch, LoadFromJson }

    [SerializeField] private CreationMode mode = CreationMode.NewFromScratch;
    [SerializeField] private ShipType current = DefaultShip();

    [SerializeField] private Object exteriorSource;
    [SerializeField] private Object cockpitSource;
    [SerializeField] private Object engineSource;
    [SerializeField] private Object shieldSource;

    [SerializeField] private bool noseNegativeZ = true;
    [SerializeField] private int engineCount = 2;
    [SerializeField] private float engineSpacing = -1f;
    [SerializeField] private float engineYOffset;
    [SerializeField] private float engineYawOffset;
    [SerializeField] private bool addGunBank = true;
    [SerializeField] private bool addMissileBank;
    [SerializeField] private bool overwritePrefab = true;

    private string typeSearch = "";
    private string[] allTypes = new string[0];
    private string[] filteredTypes = new string[0];
    private int selectedTypeIndex;
    private string[] cockpitNames = new string[0];
    private int cockpitIndex;
    private string[] engineGlowNames = new string[0];
    private int engineGlowIndex;

    private Vector3 measuredSize = Vector3.zero;
    private bool hasMeasurement;
    private Vector2 scroll;

    private string statusMessage = "Arrastra un modelo exterior (FBX/prefab) y pulsa Crear. El tipo se guarda en ShipTypes.json.";
    private MessageType statusType = MessageType.Info;

    [UnityEditor.MenuItem(MenuPath)]
    private static void ShowWindow()
    {
        GetWindow<ShipCreatorWindow>("Ship Creator");
    }

    private void OnEnable()
    {
        RefreshTypeList();
        RefreshCockpitList();
        RefreshEngineGlowList();
        if (current == null)
            current = DefaultShip();
        AssignDefaultCockpit();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Open Galaxy - Ship Creator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Genera el prefab visual con todos los anclajes que usa la logica de vuelo/Combate " +
            "y registra el tipo en ShipTypes.json. No anade componentes de gameplay al prefab: " +
            "el juego los crea al instanciar la nave.",
            MessageType.None);

        mode = (CreationMode)EditorGUILayout.EnumPopup("Modo", mode);

        if (mode == CreationMode.LoadFromJson)
            DrawLoadSection();
        else
            DrawNewSection();

        EditorGUILayout.Space(4f);
        DrawModelSection();
        EditorGUILayout.Space(4f);
        DrawCatalogSection();
        EditorGUILayout.Space(4f);
        DrawSpecsSection();
        EditorGUILayout.Space(4f);
        DrawWeaponsSection();
        EditorGUILayout.Space(4f);
        DrawVisualsSection();
        EditorGUILayout.Space(4f);
        DrawAuthorshipSection();

        EditorGUILayout.Space(4f);
        overwritePrefab = EditorGUILayout.Toggle("Sobrescribir prefab si existe", overwritePrefab);

        EditorGUILayout.Space(8f);
        GUI.enabled = CanCreate();
        if (GUILayout.Button("Crear / actualizar prefab + ShipTypes", GUILayout.Height(34f)))
            CreateShip();
        GUI.enabled = true;

        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox(statusMessage, statusType);
        EditorGUILayout.EndScrollView();
    }

    private void DrawLoadSection()
    {
        EditorGUILayout.LabelField("1. Cargar especificaciones desde ShipTypes.json", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        string newSearch = EditorGUILayout.TextField("Buscar tipo", typeSearch ?? "");
        if (EditorGUI.EndChangeCheck())
        {
            typeSearch = newSearch;
            ApplyTypeFilter();
            selectedTypeIndex = 0;
        }

        if (filteredTypes.Length == 0)
        {
            EditorGUILayout.HelpBox("No hay tipos que coincidan con la busqueda.", MessageType.Warning);
            return;
        }

        selectedTypeIndex = EditorGUILayout.Popup("Tipo", Mathf.Clamp(selectedTypeIndex, 0, filteredTypes.Length - 1), filteredTypes);
        if (GUILayout.Button("Cargar tipo seleccionado"))
            LoadSelected();
        if (GUILayout.Button("Recargar lista desde JSON"))
            RefreshTypeList();
    }

    private void DrawNewSection()
    {
        EditorGUILayout.LabelField("1. Nueva nave desde cero", EditorStyles.boldLabel);
        if (GUILayout.Button("Iniciar valores por defecto"))
        {
            current = DefaultShip();
            exteriorSource = null;
            cockpitSource = null;
            engineSource = null;
            shieldSource = null;
            AssignDefaultCockpit();
            SetStatus("Nueva nave lista. Asigna el modelo exterior.", MessageType.Info);
        }
        EditorGUILayout.HelpBox(
            "Los valores por defecto son los de un caza smallship rapido, pilotable y con 4 laseres.",
            MessageType.None);
    }

    private void DrawModelSection()
    {
        EditorGUILayout.LabelField("2. Malla exterior, cabina y motores", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        Object newExterior = EditorGUILayout.ObjectField("Malla / Prefab exterior", exteriorSource, typeof(Object), false);
        if (EditorGUI.EndChangeCheck())
        {
            exteriorSource = newExterior;
            hasMeasurement = false;
            SuggestTypeFromSource();
        }

        EditorGUILayout.HelpBox(
            "Acepta un FBX de Assets/Data, un prefab de Resources/objects/ships o un Mesh. " +
            "Se instanciara como hijo 'Model' del prefab resultante.",
            MessageType.None);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Medir modelo"))
            MeasureSource();
        if (GUILayout.Button("Usar Z medido como shipLength"))
        {
            if (!hasMeasurement)
                MeasureSource();
            if (hasMeasurement && current != null)
            {
                current.shipLength = Mathf.Max(measuredSize.y, measuredSize.z);
                SetStatus("shipLength = " + F(current.shipLength) + " m.", MessageType.Info);
            }
        }
        EditorGUILayout.EndHorizontal();

        if (hasMeasurement)
            EditorGUILayout.LabelField("Medido:", F(measuredSize.x) + " x " + F(measuredSize.y) + " x " + F(measuredSize.z) + " m", EditorStyles.miniLabel);

        EditorGUILayout.Space(3f);

        if (cockpitNames.Length > 0)
        {
            int newIndex = EditorGUILayout.Popup("Cabina (Resources)", Mathf.Clamp(cockpitIndex, 0, cockpitNames.Length - 1), cockpitNames);
            if (newIndex != cockpitIndex)
            {
                cockpitIndex = newIndex;
                string name = cockpitNames[cockpitIndex];
                cockpitSource = name == "none" ? null : Resources.Load<GameObject>(CockpitsResourceFolder + "/" + name);
                EnsureCurrent();
                current.cockpitPrefab = cockpitSource == null ? "none" : name;
            }
        }

        EditorGUI.BeginChangeCheck();
        Object newCockpit = EditorGUILayout.ObjectField("Cabina (prefabs o modelos FBX/OBJ)", cockpitSource, typeof(Object), false);
        if (EditorGUI.EndChangeCheck())
        {
            cockpitSource = newCockpit;
            SyncCockpitIndex();
            EnsureCurrent();
            current.cockpitPrefab = ResolveCockpitName();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Crear prefab de cabina en Resources"))
        {
            EnsureCockpitInResources();
            Repaint();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "Si arrastras un FBX/OBJ de cabina se genera automaticamente un prefab en " +
            CockpitAssetsFolder + " antes de guardar la nave. Si la cabina seleccionada no se puede " +
            "convertir en un prefab valido, la nave no se crea. El JSON guarda solo su nombre.",
            cockpitSource == null ? MessageType.Warning : MessageType.None);

        EditorGUILayout.Space(3f);

        if (engineGlowNames.Length > 0)
        {
            int newGlow = EditorGUILayout.Popup("Glow de motor (Resources)", Mathf.Clamp(engineGlowIndex, 0, engineGlowNames.Length - 1), engineGlowNames);
            if (newGlow != engineGlowIndex)
            {
                engineGlowIndex = newGlow;
                engineSource = engineGlowNames[engineGlowIndex] == "none"
                    ? null
                    : Resources.Load<GameObject>("particles/engineglows/" + engineGlowNames[engineGlowIndex]);
            }
        }

        EditorGUI.BeginChangeCheck();
        Object newEngine = EditorGUILayout.ObjectField("Malla de motor (opcional)", engineSource, typeof(Object), false);
        if (EditorGUI.EndChangeCheck())
        {
            engineSource = newEngine;
            SyncEngineGlowIndex();
        }
        noseNegativeZ = EditorGUILayout.Toggle("Morro en -Z (convencion OG)", noseNegativeZ);
        engineCount = Mathf.Clamp(EditorGUILayout.IntField("Numero de motores", engineCount), 0, 8);
        if (engineSource != null)
        {
            engineSpacing = EditorGUILayout.FloatField("Separacion motores (-1 = automatica)", engineSpacing);
            engineYOffset = EditorGUILayout.FloatField("Offset Y motores", engineYOffset);
            engineYawOffset = EditorGUILayout.FloatField("Yaw extra motor", engineYawOffset);
        }
        EditorGUILayout.HelpBox(
            "Los motores se colocan automaticamente en la cola (Z opuesto al morro), repartidos " +
            "simetricamente en X y centrados en Y segun los limites del modelo.",
            MessageType.None);

        EditorGUILayout.Space(3f);
        shieldSource = EditorGUILayout.ObjectField("Malla de escudo (opcional)", shieldSource, typeof(Object), false);
        EditorGUILayout.HelpBox(
            "Si dejas la malla de escudo vacia, ShieldVisual crea automaticamente una burbuja. " +
            "Si asignas una, debe tener MeshFilter + Renderer y su nombre contendra 'shield'.",
            MessageType.None);
    }

    private void DrawCatalogSection()
    {
        EditorGUILayout.LabelField("3. Identidad y catalogo", EditorStyles.boldLabel);
        EnsureCurrent();
        current.type = EditorGUILayout.TextField("Type (id)", current.type ?? "");
        current.displayName = EditorGUILayout.TextField("Display name", current.displayName ?? "");
        current.prefab = EditorGUILayout.TextField("Prefab (nombre)", current.prefab ?? "");
        current.callsign = EditorGUILayout.TextField("Callsign", current.callsign ?? "");
        current.scriptType = EditorGUILayout.TextField("Script type", current.scriptType ?? "");
        current.shipClass = EditorGUILayout.TextField("Ship class", current.shipClass ?? "");
        EditorGUILayout.LabelField("Prefab destino:", ShipsFolder + (string.IsNullOrEmpty(current.prefab) ? "<type>" : SanitizeId(current.prefab)) + ".prefab", EditorStyles.miniLabel);
    }

    private void DrawSpecsSection()
    {
        EditorGUILayout.LabelField("4. Especificaciones de vuelo y supervivencia", EditorStyles.boldLabel);
        EnsureCurrent();

        current.accelerationRating = EditorGUILayout.FloatField("Acceleration", current.accelerationRating);
        current.speedRating = EditorGUILayout.FloatField("Speed", current.speedRating);
        current.maneuverabilityRating = EditorGUILayout.FloatField("Maneuverability", current.maneuverabilityRating);
        current.hullRating = EditorGUILayout.FloatField("Hull", current.hullRating);
        current.shieldRating = EditorGUILayout.FloatField("Shield", current.shieldRating);
        current.systemsRating = EditorGUILayout.FloatField("Systems", current.systemsRating);
        current.shipLength = EditorGUILayout.FloatField("Ship length (m)", current.shipLength);

        EditorGUILayout.LabelField("Presets de vuelo", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Muy rapido")) ApplySmallPreset(74f, 160f, 80f, 50f, 50f, 50f, 85f, 50f, 35f);
        if (GUILayout.Button("Rapido")) ApplySmallPreset(62f, 145f, 75f, 120f, 80f, 100f, 83f, 50f, 30f);
        if (GUILayout.Button("Medio")) ApplySmallPreset(62f, 110f, 70f, 120f, 120f, 100f, 80f, 50f, 25f);
        if (GUILayout.Button("Lento")) ApplySmallPreset(62f, 102f, 54f, 95f, 160f, 100f, 80f, 50f, 20f);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("Presets de tamaño (largeship)", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Small")) ApplyLargePreset("small", 67f, 60f, 20f, 1900f, 1600f, 1600f);
        if (GUILayout.Button("Medium")) ApplyLargePreset("medium", 65f, 55f, 17f, 3800f, 2560f, 2560f);
        if (GUILayout.Button("Large")) ApplyLargePreset("large", 62f, 50f, 15f, 5700f, 4800f, 4800f);
        if (GUILayout.Button("Superlarge")) ApplyLargePreset("superlarge", 62f, 45f, 10f, 28500f, 24000f, 24000f);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawWeaponsSection()
    {
        EditorGUILayout.LabelField("5. Armamento y escudos", EditorStyles.boldLabel);
        EnsureCurrent();

        addGunBank = EditorGUILayout.Toggle("Banco de cañones (4 ranuras)", addGunBank);
        addMissileBank = EditorGUILayout.Toggle("Banco de torpedos (4 tubos)", addMissileBank);

        current.laserFireRating = EditorGUILayout.FloatField("Laser fire rating", current.laserFireRating);
        current.laserRating = EditorGUILayout.FloatField("Laser rating", current.laserRating);
        current.wepRating = EditorGUILayout.FloatField("Wep rating", current.wepRating);
        current.torpedoRating = EditorGUILayout.FloatField("Torpedo rating (0 = sin torpedos)", current.torpedoRating);
        current.torpedoType = EditorGUILayout.TextField("Torpedo type", current.torpedoType ?? "");
        current.smallturret = EditorGUILayout.FloatField("Small turret damage", current.smallturret);
        current.largeturret = EditorGUILayout.FloatField("Large turret damage", current.largeturret);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("4 proton torpedos")) { current.torpedoRating = 6f; current.torpedoType = "proton torpedo"; addMissileBank = true; }
        if (GUILayout.Button("12 proton torpedos")) { current.torpedoRating = 12f; current.torpedoType = "proton torpedo"; addMissileBank = true; }
        if (GUILayout.Button("6 concussion missiles")) { current.torpedoRating = 6f; current.torpedoType = "concussion missile"; addMissileBank = true; }
        if (GUILayout.Button("Sin torpedos")) { current.torpedoRating = 0f; addMissileBank = false; }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawVisualsSection()
    {
        EditorGUILayout.LabelField("6. Audio y efectos", EditorStyles.boldLabel);
        EnsureCurrent();

        current.laserAudio = EditorGUILayout.TextField("Laser audio", current.laserAudio ?? "");
        current.engineAudio = EditorGUILayout.TextField("Engine audio", current.engineAudio ?? "");
        current.thrustType = EditorGUILayout.TextField("Thrust type", current.thrustType ?? "");
        current.explosionType = EditorGUILayout.TextField("Explosion type", current.explosionType ?? "");
        current.shieldType = EditorGUILayout.TextField("Shield type", current.shieldType ?? "");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Laser X-Wing")) current.laserAudio = "weapon02_xwinglaser";
        if (GUILayout.Button("Laser TIE")) current.laserAudio = "weapon01_tiefighterlaser";
        if (GUILayout.Button("Motor X-Wing")) current.engineAudio = "engine02_xwing";
        if (GUILayout.Button("Motor TIE")) current.engineAudio = "engine01_tiefighter";
        EditorGUILayout.EndHorizontal();
    }

    private void DrawAuthorshipSection()
    {
        EditorGUILayout.LabelField("7. Autoría / faccion / era", EditorStyles.boldLabel);
        EnsureCurrent();

        current.modelauthor = EditorGUILayout.TextField("Model author", current.modelauthor ?? "");
        current.textureauthor = EditorGUILayout.TextField("Texture author", current.textureauthor ?? "");
        current.allegiance = EditorGUILayout.TextField("Allegiance", current.allegiance ?? "");
        current.era = EditorGUILayout.TextField("Era", current.era ?? "");
    }

    private bool CanCreate()
    {
        return exteriorSource != null && current != null
            && !string.IsNullOrWhiteSpace(current.type)
            && current.shipLength > 0f;
    }

    private void CreateShip()
    {
        if (!CanCreate())
        {
            SetStatus("Faltan datos: modelo exterior, type o shipLength.", MessageType.Warning);
            return;
        }

        current.type = SanitizeId(current.type);
        if (string.IsNullOrEmpty(current.prefab))
            current.prefab = current.type;
        current.prefab = SanitizeId(current.prefab);
        if (string.IsNullOrWhiteSpace(current.displayName))
            current.displayName = current.type;
        if (string.IsNullOrWhiteSpace(current.scriptType))
            current.scriptType = "smallship";

        // Si el JSON cargado referencia una cabina que ya no existe, no escribimos
        // una nave rota: dejamos de crear el prefab y el registro.
        if (cockpitSource == null && !string.IsNullOrEmpty(current.cockpitPrefab)
            && !string.Equals(current.cockpitPrefab, "none", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Falta el prefab de cabina '" + current.cockpitPrefab +
                "'. Selecciona una cabina valida antes de crear la nave.", MessageType.Error);
            return;
        }

        // La cabina se materializa en Resources ANTES de guardar el prefab de la nave.
        if (!EnsureCockpitInResources())
            return;

        current.cockpitPrefab = ResolveCockpitName();

        string prefabPath = ShipsFolder + current.prefab + ".prefab";
        if (!overwritePrefab && File.Exists(prefabPath))
        {
            SetStatus("Ya existe " + prefabPath + ". Activa 'Sobrescribir prefab' si quieres reemplazarlo.", MessageType.Warning);
            return;
        }

        GameObject root = new GameObject(current.prefab);
        try
        {
            GameObject model = InstantiateSourceAsChild(root, exteriorSource, "Model");
            if (model == null)
            {
                SetStatus("No se pudo instanciar el modelo exterior.", MessageType.Error);
                return;
            }

            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;

            Bounds bounds;
            if (!ComputeRendererBounds(root, out bounds))
            {
                SetStatus("El modelo exterior no tiene mallas visibles.", MessageType.Error);
                return;
            }

            measuredSize = bounds.size;
            hasMeasurement = true;

            // Anclajes que los scripts de gameplay buscan por nombre.
            CreateAnchor(root, "camera",
                new Vector3(bounds.center.x, bounds.center.y + 0.2f * bounds.size.y,
                    bounds.center.z + Forward * 0.35f * bounds.size.z));

            CreateAnchor(root, "followcamera",
                new Vector3(bounds.center.x, bounds.center.y + 0.8f * bounds.size.y,
                    bounds.center.z - Forward * 1.0f * bounds.size.z));

            CreateAnchor(root, "focuscamera",
                new Vector3(bounds.center.x, bounds.center.y + 0.3f * bounds.size.y,
                    bounds.center.z + Forward * 0.9f * bounds.size.z));

            CreateAnchor(root, "dockingPoint",
                new Vector3(bounds.center.x, bounds.center.y - 0.5f * bounds.size.y, bounds.center.z));

            CreateEngines(root, bounds);

            if (addGunBank)
                CreateWeaponBank(root, bounds, "gunbank01", 0.40f, 0.05f);
            if (addMissileBank)
                CreateWeaponBank(root, bounds, "missilebank01", 0.25f, -0.10f);

            if (shieldSource != null)
            {
                GameObject shield = InstantiateSourceAsChild(root, shieldSource, "shieldfield");
                if (shield != null)
                    shield.transform.localPosition = bounds.center;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(prefabPath));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (saved == null)
            {
                SetStatus("No se pudo guardar el prefab.", MessageType.Error);
                return;
            }

            UpsertShipType(current);

            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(ShipTypesPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));

            bool pilotable = current.cockpitPrefab != "none"
                && string.Equals(current.scriptType, "smallship", StringComparison.OrdinalIgnoreCase);
            string cockpitOk = pilotable ? " SI" : " NO (cabina none o scriptType != smallship)";
            SetStatus("Prefab guardado: " + prefabPath + ". Tipo '" + current.type +
                "' en ShipTypes.json. Pilotable" + cockpitOk +
                " (cabina: " + current.cockpitPrefab + ").", pilotable ? MessageType.Info : MessageType.Warning);
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message, MessageType.Error);
        }
        finally
        {
            DestroyImmediate(root);
        }
    }

    private float Forward { get { return noseNegativeZ ? -1f : 1f; } }

    private void CreateEngines(GameObject root, Bounds bounds)
    {
        if (engineCount <= 0)
            return;

        float rear = noseNegativeZ ? bounds.max.z : bounds.min.z;
        float spacing = engineSpacing > 0f
            ? engineSpacing
            : bounds.size.x * 0.5f / Mathf.Max(1f, engineCount - 1f);
        Quaternion rotation = Quaternion.Euler(0f, (noseNegativeZ ? 0f : 180f) + engineYawOffset, 0f);

        for (int i = 0; i < engineCount; i++)
        {
            float x = bounds.center.x + (i - (engineCount - 1) * 0.5f) * spacing;
            Vector3 pos = new Vector3(x, bounds.center.y + engineYOffset, rear - Forward * 0.02f * bounds.size.z);
            GameObject engine = new GameObject("engine" + (i + 1));
            engine.transform.SetParent(root.transform, false);
            engine.transform.localPosition = pos;
            engine.transform.localRotation = rotation;

            if (engineSource != null)
                InstantiateSourceAsChild(engine, engineSource, "EngineMesh");
        }
    }

    private void CreateWeaponBank(GameObject root, Bounds bounds, string bankName, float zFraction, float yFraction)
    {
        GameObject bank = CreateAnchor(root, bankName,
            new Vector3(bounds.center.x, bounds.center.y + yFraction * bounds.size.y,
                bounds.center.z + Forward * zFraction * bounds.size.z));

        for (int i = 1; i <= 4; i++)
        {
            float offset = (i - 2.5f) * 0.2f * bounds.size.x;
            GameObject slot = new GameObject(bankName + "-" + i.ToString("00", CultureInfo.InvariantCulture));
            slot.transform.SetParent(bank.transform, false);
            slot.transform.localPosition = new Vector3(offset, 0f, 0f);
            slot.transform.localRotation = Quaternion.identity;
        }
    }

    private static GameObject CreateAnchor(GameObject root, string name, Vector3 localPosition)
    {
        GameObject anchor = new GameObject(name);
        anchor.transform.SetParent(root.transform, false);
        anchor.transform.localPosition = localPosition;
        anchor.transform.localRotation = Quaternion.identity;
        return anchor;
    }

    private void MeasureSource()
    {
        if (exteriorSource == null)
        {
            SetStatus("Selecciona primero el modelo exterior.", MessageType.Warning);
            return;
        }

        GameObject temp = BuildTempInstance(exteriorSource);
        if (temp == null)
        {
            SetStatus("No se pudo instanciar el modelo.", MessageType.Error);
            return;
        }

        try
        {
            Bounds bounds;
            if (ComputeRendererBounds(temp, out bounds))
            {
                measuredSize = bounds.size;
                hasMeasurement = true;
                SetStatus("Medido: " + F(bounds.size.x) + " x " + F(bounds.size.y) + " x " + F(bounds.size.z) + " m.", MessageType.Info);
            }
            else
            {
                hasMeasurement = false;
                SetStatus("El modelo no tiene MeshRenderers con malla.", MessageType.Warning);
            }
        }
        finally
        {
            DestroyImmediate(temp);
        }
    }

    private static Material CreateFallbackMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        return shader == null ? null : new Material(shader);
    }

    private static GameObject BuildTempInstance(Object source)
    {
        if (source is GameObject sourceGo)
        {
            GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(sourceGo);
            if (temp == null)
                temp = Instantiate(sourceGo);
            Quaternion sourceRotation = temp.transform.rotation;
            temp.transform.position = Vector3.zero;
            temp.transform.rotation = sourceRotation;
            return temp;
        }

        if (source is Mesh mesh)
        {
            GameObject holder = new GameObject(source.name);
            MeshFilter filter = holder.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = holder.AddComponent<MeshRenderer>();
            Material material = CreateFallbackMaterial();
            if (material != null)
                renderer.sharedMaterial = material;
            return holder;
        }

        return null;
    }

    private static GameObject InstantiateSourceAsChild(GameObject root, Object source, string childName)
    {
        if (source is GameObject sourceGo)
        {
            GameObject child = (GameObject)PrefabUtility.InstantiatePrefab(sourceGo);
            if (child == null)
                child = Instantiate(sourceGo);
            child.name = childName;
            Vector3 sourceScale = child.transform.localScale;
            Quaternion sourceRotation = child.transform.rotation;
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = sourceRotation;
            child.transform.localScale = sourceScale;
            return child;
        }

        if (source is Mesh mesh)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(root.transform, false);
            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            Material material = CreateFallbackMaterial();
            if (material != null)
                renderer.sharedMaterial = material;
            return child;
        }

        return null;
    }

    private static bool ComputeRendererBounds(GameObject root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bounds = new Bounds(root.transform.position, Vector3.zero);
        bool init = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;
            if (!init)
            {
                bounds = renderer.bounds;
                init = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return init;
    }

    private void SuggestTypeFromSource()
    {
        if (exteriorSource == null || current == null)
            return;
        if (!string.IsNullOrWhiteSpace(current.type) && current.type != "new_ship")
            return;

        string name = exteriorSource.name;
        string path = AssetDatabase.GetAssetPath(exteriorSource);
        if (!string.IsNullOrEmpty(path))
            name = Path.GetFileNameWithoutExtension(path);

        current.type = SanitizeId(name);
        current.displayName = name;
        if (string.IsNullOrWhiteSpace(current.prefab))
            current.prefab = current.type;
    }

    // SceneFunctions.ActivateCockpit solo busca en Resources/objects/cockpits.
    // Antes de guardar la nave convertimos la cabina seleccionada en un prefab
    // real dentro de esa carpeta. Si no se puede, devolvemos false para NO crear
    // un prefab de nave con una cabina inexistente (bug del assault gunboat).
    private bool EnsureCockpitInResources()
    {
        EnsureCurrent();

        if (cockpitSource == null)
        {
            current.cockpitPrefab = "none";
            return true; // Naves no pilotables (largeship) siguen siendo validas.
        }

        string sourcePath = AssetDatabase.GetAssetPath(cockpitSource);
        if (string.IsNullOrEmpty(sourcePath))
        {
            SetStatus("La cabina seleccionada no es un asset del proyecto.", MessageType.Error);
            return false;
        }

        string name = SanitizeId(Path.GetFileNameWithoutExtension(sourcePath));
        string targetPath = CockpitAssetsFolder + "/" + name + ".prefab";
        PrefabAssetType prefabType = cockpitSource is GameObject
            ? PrefabUtility.GetPrefabAssetType(cockpitSource)
            : PrefabAssetType.NotAPrefab;
        bool isRealPrefab = prefabType == PrefabAssetType.Regular || prefabType == PrefabAssetType.Variant;
        bool inResources = sourcePath.StartsWith(CockpitAssetsFolder + "/", StringComparison.OrdinalIgnoreCase);

        if (isRealPrefab && inResources)
        {
            cockpitSource = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            RefreshCockpitList();
            SyncCockpitIndex();
            return cockpitSource != null;
        }

        if (isRealPrefab)
        {
            Directory.CreateDirectory(CockpitAssetsFolder);
            if (!File.Exists(targetPath) && !AssetDatabase.CopyAsset(sourcePath, targetPath))
            {
                SetStatus("No se pudo copiar la cabina a " + targetPath, MessageType.Error);
                return false;
            }
            cockpitSource = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            RefreshCockpitList();
            SyncCockpitIndex();
            return cockpitSource != null;
        }

        // Modelo importado (FBX/OBJ) o Mesh: generamos el prefab wrapper.
        if (!CreateCockpitPrefabFromSource(sourcePath, targetPath))
            return false;

        cockpitSource = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
        RefreshCockpitList();
        SyncCockpitIndex();
        if (cockpitSource == null)
        {
            SetStatus("No se pudo cargar el prefab de cabina creado en " + targetPath, MessageType.Error);
            return false;
        }

        SetStatus("Prefab de cabina creado: " + targetPath, MessageType.Info);
        return true;
    }

    private bool CreateCockpitPrefabFromSource(string sourcePath, string targetPath)
    {
        GameObject tempRoot = null;
        try
        {
            Directory.CreateDirectory(CockpitAssetsFolder);
            string prefabName = Path.GetFileNameWithoutExtension(targetPath);
            tempRoot = new GameObject(prefabName);

            GameObject instance = null;
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (modelAsset != null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
                if (instance == null)
                    instance = Instantiate(modelAsset);
                instance.transform.SetParent(tempRoot.transform, false);
            }
            else
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(sourcePath);
                if (mesh == null)
                {
                    SetStatus("La cabina debe ser un GameObject/prefab o un Mesh.", MessageType.Error);
                    return false;
                }
                instance = new GameObject(prefabName);
                instance.transform.SetParent(tempRoot.transform, false);
                instance.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = instance.AddComponent<MeshRenderer>();
                Material fallback = CreateFallbackMaterial();
                if (fallback != null)
                    renderer.sharedMaterial = fallback;
            }

            int cockpitLayer = LayerMask.NameToLayer("cockpit");
            if (cockpitLayer >= 0)
            {
                foreach (Transform t in tempRoot.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = cockpitLayer;
            }

            ApplyCockpitMaterialsFromTextures(instance, sourcePath);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(tempRoot, targetPath);
            if (saved == null)
            {
                SetStatus("No se pudo guardar el prefab de cabina en " + targetPath, MessageType.Error);
                return false;
            }

            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("Error al crear el prefab de cabina: " + ex.Message, MessageType.Error);
            return false;
        }
        finally
        {
            if (tempRoot != null)
                DestroyImmediate(tempRoot);
        }
    }

    // Las cabina FBX suelen importarse sin texturas. Buscamos texturas hermanas
    // y creamos materiales URP/Lit al lado del modelo para que la cabina creada
    // se vea igual que las incluidas en el proyecto.
    private static void ApplyCockpitMaterialsFromTextures(GameObject instance, string sourcePath)
    {
        string folder = Path.GetDirectoryName(sourcePath).Replace('\\', '/');
        if (string.IsNullOrEmpty(folder))
            return;

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
        if (guids == null || guids.Length == 0)
            return;

        List<string> texturePaths = new List<string>();
        foreach (string guid in guids)
            texturePaths.Add(AssetDatabase.GUIDToAssetPath(guid));

        string materialsFolder = folder + "/materials";
        if (!AssetDatabase.IsValidFolder(materialsFolder))
            AssetDatabase.CreateFolder(folder, "materials");

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            return;

        Dictionary<string, Material> map = new Dictionary<string, Material>();
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material original = materials[i];
                if (original == null || map.ContainsKey(original.name))
                    continue;

                string materialPath = materialsFolder + "/" + SanitizeId(original.name) + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader);
                    material.name = original.name;
                    material.color = original.color;
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", original.color);
                    AssetDatabase.CreateAsset(material, materialPath);
                }

                Texture texture = FindBestTexture(texturePaths, original.name);
                if (texture != null && material.mainTexture == null)
                {
                    material.mainTexture = texture;
                    if (material.HasProperty("_BaseMap"))
                        material.SetTexture("_BaseMap", texture);
                    EditorUtility.SetDirty(material);
                }

                map[original.name] = material;
            }
        }

        if (map.Count == 0)
            return;

        AssetDatabase.SaveAssets();
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material original = materials[i];
                if (original != null && map.ContainsKey(original.name))
                    materials[i] = map[original.name];
            }
            renderer.sharedMaterials = materials;
        }
    }

    private static Texture FindBestTexture(List<string> texturePaths, string materialName)
    {
        string wanted = NormalizeMaterialName(materialName);
        if (string.IsNullOrEmpty(wanted))
            return null;

        Texture partial = null;
        foreach (string path in texturePaths)
        {
            string candidate = NormalizeMaterialName(Path.GetFileNameWithoutExtension(path));
            if (string.IsNullOrEmpty(candidate))
                continue;
            if (string.Equals(wanted, candidate, StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (partial == null && (candidate.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)
                || wanted.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)
                || (wanted.Length >= 5 && candidate.Length >= 5
                    && string.Equals(wanted.Substring(0, 5), candidate.Substring(0, 5), StringComparison.OrdinalIgnoreCase))))
            {
                partial = AssetDatabase.LoadAssetAtPath<Texture>(path);
            }
        }
        return partial;
    }

    private static string NormalizeMaterialName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";
        StringBuilder sb = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private string ResolveCockpitName()
    {
        if (cockpitSource == null)
            return "none";
        string path = AssetDatabase.GetAssetPath(cockpitSource);
        string name = string.IsNullOrEmpty(path) ? cockpitSource.name : Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrEmpty(name) ? "none" : name;
    }

    private void AssignDefaultCockpit()
    {
        if (cockpitSource != null)
        {
            SyncCockpitIndex();
            return;
        }

        // Por defecto las naves nuevas deben ser pilotables: el jugador solo
        // tiene que arrastrar el modelo exterior.
        cockpitSource = Resources.Load<GameObject>(CockpitsResourceFolder + "/xwing_cockpit");
        if (cockpitSource == null && cockpitNames.Length > 1)
            cockpitSource = Resources.Load<GameObject>(CockpitsResourceFolder + "/" + cockpitNames[1]);
        SyncCockpitIndex();
    }

    private void RefreshEngineGlowList()
    {
        GameObject[] glows = Resources.LoadAll<GameObject>("particles/engineglows");
        List<string> names = new List<string> { "none" };
        if (glows != null)
        {
            foreach (GameObject go in glows)
            {
                if (go != null && !names.Contains(go.name))
                    names.Add(go.name);
            }
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        names.Remove("none");
        names.Insert(0, "none");
        engineGlowNames = names.ToArray();
        SyncEngineGlowIndex();
    }

    private void SyncEngineGlowIndex()
    {
        string name = engineSource == null ? "none" : engineSource.name;
        engineGlowIndex = Array.FindIndex(engineGlowNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (engineGlowIndex < 0)
            engineGlowIndex = 0;
    }

    private void RefreshCockpitList()
    {
        GameObject[] prefabs = Resources.LoadAll<GameObject>(CockpitsResourceFolder);
        List<string> names = new List<string> { "none" };
        if (prefabs != null)
        {
            foreach (GameObject go in prefabs)
            {
                if (go != null && !names.Contains(go.name))
                    names.Add(go.name);
            }
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        // "none" siempre primero.
        names.Remove("none");
        names.Insert(0, "none");
        cockpitNames = names.ToArray();
        SyncCockpitIndex();
    }

    private void SyncCockpitIndex()
    {
        string name = ResolveCockpitName();
        cockpitIndex = Array.FindIndex(cockpitNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (cockpitIndex < 0)
            cockpitIndex = 0;
    }

    private void RefreshTypeList()
    {
        ShipTypes wrapper = ReadShipTypes();
        List<string> types = new List<string>();
        if (wrapper != null && wrapper.shipTypeData != null)
        {
            foreach (ShipType ship in wrapper.shipTypeData)
            {
                if (ship != null && !string.IsNullOrEmpty(ship.type))
                    types.Add(ship.type);
            }
        }
        types.Sort(StringComparer.OrdinalIgnoreCase);
        allTypes = types.ToArray();
        ApplyTypeFilter();
    }

    private void ApplyTypeFilter()
    {
        string needle = (typeSearch ?? "").Trim();
        if (string.IsNullOrEmpty(needle))
        {
            filteredTypes = allTypes;
            return;
        }

        List<string> matches = new List<string>();
        foreach (string type in allTypes)
        {
            if (type.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                matches.Add(type);
        }
        filteredTypes = matches.ToArray();
    }

    private void LoadSelected()
    {
        if (filteredTypes.Length == 0)
        {
            SetStatus("No hay ningun tipo seleccionado.", MessageType.Warning);
            return;
        }

        string type = filteredTypes[Mathf.Clamp(selectedTypeIndex, 0, filteredTypes.Length - 1)];
        ShipTypes wrapper = ReadShipTypes();
        ShipType found = null;
        if (wrapper != null && wrapper.shipTypeData != null)
        {
            foreach (ShipType ship in wrapper.shipTypeData)
            {
                if (ship != null && string.Equals(ship.type, type, StringComparison.Ordinal))
                {
                    found = ship;
                    break;
                }
            }
        }

        if (found == null)
        {
            SetStatus("No se encontro el tipo '" + type + "'.", MessageType.Warning);
            return;
        }

        current = CopyShip(found);
        hasMeasurement = false;

        bool cockpitMissing = false;
        if (!string.IsNullOrEmpty(current.cockpitPrefab) && current.cockpitPrefab != "none")
        {
            cockpitSource = Resources.Load<GameObject>(CockpitsResourceFolder + "/" + current.cockpitPrefab);
            cockpitMissing = cockpitSource == null;
        }
        else
        {
            cockpitSource = null;
        }
        SyncCockpitIndex();

        exteriorSource = AssetDatabase.LoadAssetAtPath<GameObject>(ShipsFolder + current.prefab + ".prefab");
        engineSource = null;
        shieldSource = null;
        SyncEngineGlowIndex();
        addGunBank = true;
        addMissileBank = current.torpedoRating > 0f;

        if (cockpitMissing)
            SetStatus("Cargado '" + type + "', PERO falta el prefab de cabina '" + current.cockpitPrefab +
                "'. Selecciona una cabina valida antes de crear/guardar la nave.", MessageType.Error);
        else
            SetStatus("Cargado '" + type + "' (" + current.scriptType + ", cabina " + current.cockpitPrefab + ").", MessageType.Info);
    }

    private void UpsertShipType(ShipType spec)
    {
        ShipTypes wrapper = ReadShipTypes();
        List<ShipType> entries = new List<ShipType>();
        if (wrapper != null && wrapper.shipTypeData != null)
        {
            foreach (ShipType ship in wrapper.shipTypeData)
            {
                if (ship != null)
                    entries.Add(ship);
            }
        }

        ShipType existing = entries.Find(e => string.Equals(e.type, spec.type, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new ShipType();
            entries.Add(existing);
        }

        existing.type = spec.type;
        existing.displayName = spec.displayName;
        existing.prefab = spec.prefab;
        existing.cockpitPrefab = spec.cockpitPrefab;
        existing.callsign = spec.callsign;
        existing.scriptType = spec.scriptType;
        existing.accelerationRating = spec.accelerationRating;
        existing.speedRating = spec.speedRating;
        existing.maneuverabilityRating = spec.maneuverabilityRating;
        existing.hullRating = spec.hullRating;
        existing.shieldRating = spec.shieldRating;
        existing.systemsRating = spec.systemsRating;
        existing.laserFireRating = spec.laserFireRating;
        existing.laserRating = spec.laserRating;
        existing.wepRating = spec.wepRating;
        existing.torpedoRating = spec.torpedoRating;
        existing.torpedoType = spec.torpedoType;
        existing.laserAudio = spec.laserAudio;
        existing.engineAudio = spec.engineAudio;
        existing.thrustType = spec.thrustType;
        existing.shipClass = spec.shipClass;
        existing.explosionType = spec.explosionType;
        existing.smallturret = spec.smallturret;
        existing.largeturret = spec.largeturret;
        existing.shipLength = spec.shipLength;
        existing.shieldType = spec.shieldType;
        existing.modelauthor = spec.modelauthor;
        existing.textureauthor = spec.textureauthor;
        existing.allegiance = spec.allegiance;
        existing.era = spec.era;

        entries.Sort((a, b) => string.Compare(a.type, b.type, StringComparison.OrdinalIgnoreCase));
        WriteShipTypes(entries);
        RefreshTypeList();
    }

    private static ShipTypes ReadShipTypes()
    {
        if (!File.Exists(ShipTypesPath))
            return new ShipTypes { shipTypeData = new ShipType[0] };

        try
        {
            ShipTypes wrapper = JsonUtility.FromJson<ShipTypes>(File.ReadAllText(ShipTypesPath));
            if (wrapper == null || wrapper.shipTypeData == null)
                return new ShipTypes { shipTypeData = new ShipType[0] };
            return wrapper;
        }
        catch (Exception ex)
        {
            throw new Exception("ShipTypes.json corrupto: " + ex.Message);
        }
    }

    // Escritura manual para conservar exactamente los 30 campos del esquema y no
    // perder informacion si el JSON tiene entradas antiguas con valores vacios.
    private static void WriteShipTypes(List<ShipType> entries)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\n  \"shipTypeData\": [\n");
        for (int i = 0; i < entries.Count; i++)
        {
            ShipType e = entries[i];
            sb.Append("    {\n");
            Str(sb, "type", e.type, true);
            Str(sb, "displayName", e.displayName, true);
            Str(sb, "prefab", e.prefab, true);
            Str(sb, "cockpitPrefab", e.cockpitPrefab, true);
            Str(sb, "callsign", e.callsign, true);
            Str(sb, "scriptType", e.scriptType, true);
            Num(sb, "accelerationRating", e.accelerationRating, true);
            Num(sb, "speedRating", e.speedRating, true);
            Num(sb, "maneuverabilityRating", e.maneuverabilityRating, true);
            Num(sb, "hullRating", e.hullRating, true);
            Num(sb, "shieldRating", e.shieldRating, true);
            Num(sb, "systemsRating", e.systemsRating, true);
            Num(sb, "laserFireRating", e.laserFireRating, true);
            Num(sb, "laserRating", e.laserRating, true);
            Num(sb, "wepRating", e.wepRating, true);
            Num(sb, "torpedoRating", e.torpedoRating, true);
            Str(sb, "torpedoType", e.torpedoType, true);
            Str(sb, "laserAudio", e.laserAudio, true);
            Str(sb, "engineAudio", e.engineAudio, true);
            Str(sb, "thrustType", e.thrustType, true);
            Str(sb, "shipClass", e.shipClass, true);
            Str(sb, "explosionType", e.explosionType, true);
            Num(sb, "smallturret", e.smallturret, true);
            Num(sb, "largeturret", e.largeturret, true);
            Num(sb, "shipLength", e.shipLength, true);
            Str(sb, "shieldType", e.shieldType, true);
            Str(sb, "modelauthor", e.modelauthor, true);
            Str(sb, "textureauthor", e.textureauthor, true);
            Str(sb, "allegiance", e.allegiance, true);
            Str(sb, "era", e.era, false);
            sb.Append("    }");
            if (i < entries.Count - 1)
                sb.Append(",");
            sb.Append("\n");
        }
        sb.Append("  ]\n}\n");
        File.WriteAllText(ShipTypesPath, sb.ToString());
    }

    private static void Str(StringBuilder sb, string name, string value, bool comma)
    {
        sb.Append("      \"").Append(name).Append("\": ").Append(JsonString(value ?? ""));
        sb.Append(comma ? ",\n" : "\n");
    }

    private static void Num(StringBuilder sb, string name, float value, bool comma)
    {
        sb.Append("      \"").Append(name).Append("\": ")
            .Append(value.ToString("0.0###############", CultureInfo.InvariantCulture));
        sb.Append(comma ? ",\n" : "\n");
    }

    private static string JsonString(string value)
    {
        StringBuilder sb = new StringBuilder("\"");
        foreach (char c in value ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append("\"");
        return sb.ToString();
    }

    private static string SanitizeId(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "new_ship";
        StringBuilder sb = new StringBuilder();
        foreach (char c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else if (c == ' ' || c == '-')
                sb.Append('_');
        }
        string result = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(result) ? "new_ship" : result;
    }

    private static string F(float value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private void EnsureCurrent()
    {
        if (current == null)
            current = DefaultShip();
    }

    private void ApplySmallPreset(float accel, float speed, float maneuver, float hull, float shield, float systems, float laserFire, float laser, float wep)
    {
        EnsureCurrent();
        current.scriptType = "smallship";
        current.accelerationRating = accel;
        current.speedRating = speed;
        current.maneuverabilityRating = maneuver;
        current.hullRating = hull;
        current.shieldRating = shield;
        current.systemsRating = systems;
        current.laserFireRating = laserFire;
        current.laserRating = laser;
        current.wepRating = wep;
    }

    private void ApplyLargePreset(string shipClass, float accel, float speed, float maneuver, float hull, float shield, float systems)
    {
        EnsureCurrent();
        current.scriptType = "largeship";
        current.shipClass = shipClass;
        current.accelerationRating = accel;
        current.speedRating = speed;
        current.maneuverabilityRating = maneuver;
        current.hullRating = hull;
        current.shieldRating = shield;
        current.systemsRating = systems;
        current.laserFireRating = 60f;
        current.laserRating = 50f;
        current.wepRating = 0f;
        current.torpedoRating = 0f;
    }

    private static ShipType DefaultShip()
    {
        return new ShipType
        {
            type = "new_ship",
            displayName = "New Ship",
            prefab = "new_ship",
            cockpitPrefab = "xwing_cockpit",
            callsign = "new",
            scriptType = "smallship",
            accelerationRating = 62f,
            speedRating = 145f,
            maneuverabilityRating = 75f,
            hullRating = 120f,
            shieldRating = 80f,
            systemsRating = 100f,
            laserFireRating = 83f,
            laserRating = 50f,
            wepRating = 30f,
            torpedoRating = 0f,
            torpedoType = "proton torpedo",
            laserAudio = "weapon02_xwinglaser",
            engineAudio = "engine02_xwing",
            thrustType = "none",
            shipClass = "small",
            explosionType = "default",
            smallturret = 10f,
            largeturret = 50f,
            shipLength = 10f,
            shieldType = "default",
            modelauthor = "warb_null",
            textureauthor = "warb_null",
            allegiance = "rebel",
            era = "empire"
        };
    }

    private static ShipType CopyShip(ShipType source)
    {
        return new ShipType
        {
            type = source.type,
            displayName = source.displayName,
            prefab = source.prefab,
            cockpitPrefab = source.cockpitPrefab,
            callsign = source.callsign,
            scriptType = source.scriptType,
            accelerationRating = source.accelerationRating,
            speedRating = source.speedRating,
            maneuverabilityRating = source.maneuverabilityRating,
            hullRating = source.hullRating,
            shieldRating = source.shieldRating,
            systemsRating = source.systemsRating,
            laserFireRating = source.laserFireRating,
            laserRating = source.laserRating,
            wepRating = source.wepRating,
            torpedoRating = source.torpedoRating,
            torpedoType = source.torpedoType,
            laserAudio = source.laserAudio,
            engineAudio = source.engineAudio,
            thrustType = source.thrustType,
            shipClass = source.shipClass,
            explosionType = source.explosionType,
            smallturret = source.smallturret,
            largeturret = source.largeturret,
            shipLength = source.shipLength,
            shieldType = source.shieldType,
            modelauthor = source.modelauthor,
            textureauthor = source.textureauthor,
            allegiance = source.allegiance,
            era = source.era
        };
    }

    private void SetStatus(string message, MessageType type)
    {
        statusMessage = message;
        statusType = type;
    }
}
#endif
