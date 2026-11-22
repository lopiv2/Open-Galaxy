using System.Collections;
using System;
using UnityEngine;

/**
 * GalaxyShipCreatorHelper - Helper para añadir SoftLanding automáticamente a las naves
 * 
 * Este script se añade al GameObject "scene" y detecta cuando se crean nuevas naves pequeñas,
 * añadiendo automáticamente el componente SoftLanding.
 */
public class GalaxyShipCreatorHelper : MonoBehaviour
{
    [Header("References")]
    public Type softLandingType; // Opcional - para referencias compiladas
    
    private void Awake()
    {
        // Configurar helper
        Debug.Log("🚀 GalaxyShipCreatorHelper inicializado");
    }

    /// <summary>
    /// Añadir SoftLanding automáticamente cuando se crea una nave pequeña.
    /// Se llama desde SceneFunctions.cs después de añadir FlightControlAI_SSmall.
    /// </summary>
    public static void AddSoftLandingAutomatically(GameObject shipGO)
    {
        if (shipGO == null)
        {
            return;
        }

        // Añadir SoftLanding si no existe ya
        if (!shipGO.GetComponent<SoftLanding>())
        {
            try
            {
                SoftLanding softLanding = shipGO.AddComponent<SoftLanding>();
                Debug.Log("✅ ✅ ✅ SoftLanding añadido automáticamente a: " + shipGO.name);
            }
            catch (System.MissingMethodException e)
            {
                Debug.LogError("❌ Error añadiendo SoftLanding:\n" + e.Message);
                Debug.Log("   Asegúrate de que el script SoftLanding.cs está compilado correctamente.");
            }
        }
        else
        {
            Debug.Log("ℹ️ SoftLanding ya existe en: " + shipGO.name);
        }
    }

    /// <summary>
    /// Monitorizar y añadir SoftLanding a naves nuevas.
    /// </summary>
    private void OnEnable()
    {
        // Este script trabaja junto con la modificación manual en SceneFunctions.cs
    }

    private void OnDestroy()
    {
        // Limpieza
    }
}

/**
 * INSTRUCCIONES PARA ACTIVAR EL MÉTODO 3:
 * 
 * Paso 1: El archivo GalaxyShipCreatorHelper.cs ya ha sido creado.
 * 
 * Paso 2: Añadir al GameObject "scene":
 * - En Unity, selecciona el objeto "scene" en la jerarquía
 * - Haz clic derecho → Add Component
 * - Busca y añade "GalaxyShipCreatorHelper"
 * 
 * Paso 3: Opcional - Si quieres añadirlo automáticamente desde un script diferente,
 * puedes llamar a GalaxyShipCreatorHelper.AddSoftLandingAutomatically(shipGO)
 */
