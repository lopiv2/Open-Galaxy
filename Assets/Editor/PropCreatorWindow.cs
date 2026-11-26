#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Creates planet props without AI: pick a model, auto-fit a collider,
// save it as a prefab under Resources/objects/... and register it in
// PropsTypes.json so PlanetNavigation / the terrain preview can spawn it.
public class PropCreatorWindow : EditorWindow
{
    private const string MenuPath = "Tools/Open Galaxy/Prop Creator (PropsTypes)";
    private const string PropsTypesPath = "Assets/Resources/files/PropsTypes.json";

    private static readonly string[] FolderPresets =
    {
        "objects/props/",
        "objects/buildings/",
        "objects/vehicles/",
        "objects/creatures/",
        "objects/trees/",
    };

    private static readonly string[] CategoryPresets =
    {
        "prop",
        "building",
        "vehicle",
        "landmark",
        "nature",
    };

    private static readonly string[] ColliderOptions =
    {
        "Box",
        "Sphere",
        "Capsule",
        "Mesh (convex)",
        "None",
    };

    [SerializeField] private UnityEngine.Object sourceObject;
    [SerializeField] private string propType = "";
    [SerializeField] private string displayName = "";
    [SerializeField] private int folderIndex;
    [SerializeField] private string customFolder = "objects/props/";
    [SerializeField] private bool useCustomFolder;
    [SerializeField] private float targetLength = -1f;
    [SerializeField] private int categoryIndex;
    [SerializeField] private string tagsText = "";
    [SerializeField] private int colliderIndex;
    [SerializeField] private bool overwritePrefab = true;

    private Vector3 measuredSize = Vector3.zero;
    private Vector3 measuredCenter = Vector3.zero;
    private bool hasMeasurement;
    private string statusMessage = "Arrastra un modelo (FBX, prefab o GameObject) para empezar.";
    private MessageType statusType = MessageType.Info;
    private Vector2 scroll;

    [UnityEditor.MenuItem(MenuPath)]
    private static void ShowWindow()
    {
        GetWindow<PropCreatorWindow>("Prop Creator");
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("1. Modelo origen", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        UnityEngine.Object newSource = EditorGUILayout.ObjectField(
            "Modelo / Prefab", sourceObject, typeof(UnityEngine.Object), false);
        if (EditorGUI.EndChangeCheck())
        {
            sourceObject = newSource;
            hasMeasurement = false;
            if (sourceObject != null && string.IsNullOrEmpty(propType))
            {
                propType = SanitizeTypeName(sourceObject.name);
                displayName = sourceObject.name;
            }
        }

        EditorGUILayout.HelpBox(
            "Acepta un FBX de Assets/Data, un prefab de Resources/objects "
            + "o un GameObject de la escena. Los materiales del origen se conservan.",
            MessageType.None);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("2. Catálogo (PropsTypes.json)", EditorStyles.boldLabel);
        propType = EditorGUILayout.TextField("Type (id)", propType ?? "");
        displayName = EditorGUILayout.TextField("Display name", displayName ?? "");

        useCustomFolder = EditorGUILayout.Toggle("Carpeta personalizada", useCustomFolder);
        if (useCustomFolder)
            customFolder = EditorGUILayout.TextField("Carpeta", customFolder ?? "");
        else
            folderIndex = EditorGUILayout.Popup("Carpeta", folderIndex, FolderPresets);

        string folder = useCustomFolder ? customFolder : FolderPresets[folderIndex % FolderPresets.Length];
        EditorGUILayout.LabelField("Prefab destino:",
            "Assets/Resources/" + folder + (string.IsNullOrEmpty(propType) ? "<type>" : propType) + ".prefab",
            EditorStyles.miniLabel);

        categoryIndex = EditorGUILayout.Popup("Category", categoryIndex, CategoryPresets);
        tagsText = EditorGUILayout.TextField("Tags (coma)", tagsText ?? "");
        targetLength = EditorGUILayout.FloatField("Target length (m)", targetLength);
        EditorGUILayout.HelpBox(
            "Target length = mayor dimensión del modelo en metros (X/Y/Z), medida antes de aplicar el yaw de colocación. "
            + "-1 o 0 conserva el tamaño nativo del FBX.",
            MessageType.None);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("3. Collider", EditorStyles.boldLabel);
        colliderIndex = EditorGUILayout.Popup("Tipo", colliderIndex, ColliderOptions);
        overwritePrefab = EditorGUILayout.Toggle("Sobrescribir prefab", overwritePrefab);

        if (hasMeasurement)
        {
            EditorGUILayout.LabelField(
                "Medido: " + F(measuredSize.x) + " x " + F(measuredSize.y) + " x " + F(measuredSize.z) + " m"
                + "  (largo mayor: " + F(Mathf.Max(measuredSize.x, measuredSize.z)) + " m)",
                EditorStyles.miniLabel);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Medir modelo"))
            MeasureSource();
        if (GUILayout.Button("Usar largo medido"))
        {
            if (!hasMeasurement)
                MeasureSource();
            if (hasMeasurement)
                targetLength = Mathf.Max(measuredSize.x, measuredSize.y, measuredSize.z);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6f);
        GUI.enabled = sourceObject != null && !string.IsNullOrEmpty(propType);
        if (GUILayout.Button("Crear / actualizar prefab + PropsTypes", GUILayout.Height(32f)))
            CreateProp();
        GUI.enabled = true;

        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox(statusMessage, statusType);
        EditorGUILayout.EndScrollView();
    }

    private void MeasureSource()
    {
        if (sourceObject == null)
        {
            SetStatus("Selecciona primero un modelo.", MessageType.Warning);
            return;
        }

        GameObject temp = BuildTempInstance();
        if (temp == null)
        {
            SetStatus("No se pudo instanciar el modelo (¿es un GameObject o Mesh?).", MessageType.Error);
            return;
        }

        try
        {
            if (ComputeRendererBounds(temp, out Bounds bounds))
            {
                measuredSize = bounds.size;
                measuredCenter = bounds.center;
                hasMeasurement = true;
                SetStatus("Medido: " + F(bounds.size.x) + " x " + F(bounds.size.y)
                    + " x " + F(bounds.size.z) + " m.", MessageType.Info);
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

    private void CreateProp()
    {
        if (sourceObject == null || string.IsNullOrEmpty(propType))
        {
            SetStatus("Falta el modelo o el type.", MessageType.Warning);
            return;
        }

        propType = SanitizeTypeName(propType);
        string folder = useCustomFolder ? (customFolder ?? "") : FolderPresets[folderIndex % FolderPresets.Length];
        folder = NormalizeFolder(folder);
        if (folder == null)
        {
            SetStatus("La carpeta debe empezar por \"objects/\" y terminar en \"/\".", MessageType.Error);
            return;
        }

        string prefabPath = "Assets/Resources/" + folder + propType + ".prefab";
        if (!overwritePrefab && File.Exists(prefabPath))
        {
            SetStatus("Ya existe " + prefabPath + " (activa Sobrescribir).", MessageType.Warning);
            return;
        }

        GameObject root = new GameObject(propType);
        GameObject modelChild = null;
        try
        {
            modelChild = InstantiateSourceAsChild(root);
            if (modelChild == null)
            {
                SetStatus("No se pudo instanciar el modelo.", MessageType.Error);
                DestroyImmediate(root);
                return;
            }

            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;

            if (!ComputeRendererBounds(root, out Bounds bounds))
            {
                SetStatus("El modelo no tiene geometría visible.", MessageType.Error);
                DestroyImmediate(root);
                DestroyImmediate(modelChild);
                return;
            }

            measuredSize = bounds.size;
            measuredCenter = bounds.center;
            hasMeasurement = true;

            ApplyCollider(root, bounds, ColliderOptions[colliderIndex % ColliderOptions.Length]);

            Directory.CreateDirectory(Path.GetDirectoryName(prefabPath));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (saved == null)
            {
                SetStatus("No se pudo guardar el prefab.", MessageType.Error);
                return;
            }

            string category = CategoryPresets[categoryIndex % CategoryPresets.Length];
            float finalTarget = targetLength;
            if (finalTarget < 0f)
                finalTarget = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

            string[] tags = ParseTags(tagsText);
            UpsertPropsTypes(propType, displayName, folder, propType, finalTarget, category, tags);

            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(PropsTypesPath, ImportAssetOptions.ForceUpdate);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));

            SetStatus("Prefab guardado en " + prefabPath + " y PropsTypes actualizado"
                + " (" + F(bounds.size.x) + " x " + F(bounds.size.y) + " x " + F(bounds.size.z) + " m).",
                MessageType.Info);
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message, MessageType.Error);
        }
        finally
        {
            if (root != null)
                DestroyImmediate(root);
        }
    }

    private GameObject BuildTempInstance()
    {
        if (sourceObject is GameObject sourceGo)
        {
            GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(sourceGo);
            if (temp == null)
                temp = Instantiate(sourceGo);
            // FBX imports often carry a correction rotation (e.g. -90 on X).
            // Keep it: resetting it here measures the wrong orientation and
            // forces the user to compensate on the prefab root later.
            Quaternion sourceRotation = temp.transform.rotation;
            temp.transform.position = Vector3.zero;
            temp.transform.rotation = sourceRotation;
            return temp;
        }

        if (sourceObject is Mesh mesh)
        {
            GameObject holder = new GameObject(sourceObject.name);
            MeshFilter filter = holder.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Standard"));
            return holder;
        }

        return null;
    }

    private GameObject InstantiateSourceAsChild(GameObject root)
    {
        if (sourceObject is GameObject sourceGo)
        {
            GameObject child = (GameObject)PrefabUtility.InstantiatePrefab(sourceGo);
            if (child == null)
                child = Instantiate(sourceGo);
            child.name = "Model";
            // Preserve both the source scale and its rotation. Resetting
            // rotation here loses the FBX correction, which then has to be
            // faked on the prefab root and gets overwritten at spawn time.
            Vector3 sourceScale = child.transform.localScale;
            Quaternion sourceRotation = child.transform.rotation;
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = sourceRotation;
            child.transform.localScale = sourceScale;
            return child;
        }

        if (sourceObject is Mesh mesh)
        {
            GameObject child = new GameObject("Model");
            child.transform.SetParent(root.transform, false);
            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Standard"));
            return child;
        }

        return null;
    }

    private static void ApplyCollider(GameObject root, Bounds bounds, string kind)
    {
        foreach (Collider existing in root.GetComponents<Collider>())
            DestroyImmediate(existing, true);

        // Bounds are in world space with the root at the origin, so the
        // collider centre is just the bounds centre.
        Vector3 center = bounds.center - root.transform.position;
        Vector3 size = bounds.size;

        switch (kind)
        {
            case "Sphere":
            {
                SphereCollider sphere = root.AddComponent<SphereCollider>();
                sphere.center = center;
                sphere.radius = Mathf.Max(size.x, size.y, size.z) * 0.5f;
                break;
            }
            case "Capsule":
            {
                CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = center;
                capsule.direction = 1;
                capsule.radius = Mathf.Max(size.x, size.z) * 0.5f;
                capsule.height = Mathf.Max(size.y, 0.01f);
                break;
            }
            case "Mesh (convex)":
            {
                MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>();
                bool added = false;
                foreach (MeshFilter filter in filters)
                {
                    if (filter == null || filter.sharedMesh == null)
                        continue;
                    if (filter.GetComponent<MeshCollider>() != null)
                    {
                        added = true;
                        continue;
                    }
                    MeshCollider meshCol = filter.gameObject.AddComponent<MeshCollider>();
                    meshCol.sharedMesh = filter.sharedMesh;
                    meshCol.convex = true;
                    added = true;
                }
                if (!added)
                {
                    BoxCollider fallback = root.AddComponent<BoxCollider>();
                    fallback.center = center;
                    fallback.size = size;
                }
                break;
            }
            case "None":
                break;
            default:
            {
                BoxCollider box = root.AddComponent<BoxCollider>();
                box.center = center;
                box.size = size;
                break;
            }
        }
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

    private static string[] ParseTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new string[0];
        string[] parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        List<string> tags = new List<string>();
        foreach (string part in parts)
        {
            string tag = part.Trim();
            if (!string.IsNullOrEmpty(tag))
                tags.Add(tag);
        }
        return tags.ToArray();
    }

    private static string SanitizeTypeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "";
        StringBuilder builder = new StringBuilder();
        foreach (char c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                builder.Append(c);
            else if (c == ' ' || c == '-')
                builder.Append('_');
        }
        string result = builder.ToString().Trim('_');
        return string.IsNullOrEmpty(result) ? "NewProp" : result;
    }

    private static string NormalizeFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        string fixedFolder = folder.Trim().Replace('\\', '/');
        if (!fixedFolder.StartsWith("objects/", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!fixedFolder.EndsWith("/"))
            fixedFolder += "/";
        return fixedFolder;
    }

    private void UpsertPropsTypes(string type, string prettyName, string folder,
        string prefab, float finalTarget, string category, string[] tags)
    {
        PropTypes catalog = null;
        if (File.Exists(PropsTypesPath))
        {
            try
            {
                catalog = JsonUtility.FromJson<PropTypes>(File.ReadAllText(PropsTypesPath));
            }
            catch (Exception ex)
            {
                throw new Exception("PropsTypes.json corrupto: " + ex.Message);
            }
        }

        List<PropType> entries = new List<PropType>(
            catalog != null && catalog.propTypesData != null ? catalog.propTypesData : new PropType[0]);

        PropType existing = entries.Find(e =>
            e != null && string.Equals(e.type, type, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new PropType { type = type };
            entries.Add(existing);
        }

        existing.type = type;
        existing.displayName = string.IsNullOrEmpty(prettyName) ? type : prettyName;
        existing.folder = folder;
        existing.prefab = prefab;
        existing.targetLength = finalTarget;
        existing.category = category;
        existing.tags = tags ?? new string[0];

        StringBuilder builder = new StringBuilder();
        builder.Append("{\n  \"propTypesData\": [\n");
        for (int i = 0; i < entries.Count; i++)
        {
            PropType entry = entries[i];
            builder.Append("    {\n");
            AppendJsonField(builder, "type", entry.type, true);
            AppendJsonField(builder, "displayName", entry.displayName, true);
            AppendJsonField(builder, "folder", entry.folder, true);
            AppendJsonField(builder, "prefab", entry.prefab, true);
            builder.Append("      \"targetLength\": ")
                .Append(entry.targetLength.ToString("0.0", CultureInfo.InvariantCulture)).Append(",\n");
            AppendJsonField(builder, "category", entry.category ?? "", true);
            builder.Append("      \"tags\": [");
            if (entry.tags != null && entry.tags.Length > 0)
            {
                for (int t = 0; t < entry.tags.Length; t++)
                {
                    builder.Append(JsonString(entry.tags[t]));
                    if (t < entry.tags.Length - 1)
                        builder.Append(", ");
                }
            }
            builder.Append("]\n    }");
            if (i < entries.Count - 1)
                builder.Append(",");
            builder.Append("\n");
        }
        builder.Append("  ]\n}\n");
        File.WriteAllText(PropsTypesPath, builder.ToString());
    }

    private static void AppendJsonField(StringBuilder builder, string name, string value, bool comma)
    {
        builder.Append("      ").Append(JsonString(name)).Append(": ")
            .Append(JsonString(value ?? ""));
        builder.Append(comma ? ",\n" : "\n");
    }

    private static string JsonString(string value)
    {
        if (value == null)
            return "\"\"";
        StringBuilder builder = new StringBuilder("\"");
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(c); break;
            }
        }
        builder.Append("\"");
        return builder.ToString();
    }

    private static string F(float value)
    {
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private void SetStatus(string message, MessageType type)
    {
        statusMessage = message;
        statusType = type;
    }
}
#endif
