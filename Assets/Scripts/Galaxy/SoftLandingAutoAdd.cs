using UnityEngine;
using System.Collections;
using System;

/**
 * SoftLandingAutoAdd - Añadir automáticamente el componente SoftLanding a todas las naves pequeñas.
 * 
 * Este script se añade al objeto principal de la escena y detecta cuando se crean nuevas naves
 * para añadirles automáticamente el componente SoftLanding.
 */
public class SoftLandingAutoAdd : MonoBehaviour
{
    [Header("References")]
    public Type softLandingType; // Referencia al tipo SoftLanding (opcional, si tienes referencias compiladas)
    
    private void Awake()
    {
        // Monitorizar creación de objetos Ship automáticamente
        // Se añade en SceneFunctions.cs donde se crea la nave
    }

    #region Unity Lifecycle
    
    private void Start()
    {
        // Este script funciona mediante el método alternativo:
        // Añadir SoftLanding directamente cuando se crea la nave en SceneFunctions.cs
    }
    
    private void OnDestroy()
    {
        // Limpieza automática si fuera necesario
    }

    #endregion
    
    /// <summary>
    /// Añadir el componente SoftLanding a un GameObject de nave pequeña.
    /// </summary>
    /// <param name="shipGO">GameObject de la nave (ship, no scene)</param>
    public static void AddSoftLandingToShip(GameObject shipGO)
    {
        if (shipGO == null || !shipGO.GetComponent<Ship>())
        {
            Debug.LogWarning("Objeto no es una nave válida (no tiene componente Ship).");
            return;
        }

        // Añadir SoftLanding si no existe ya
        if (!shipGO.GetComponent<SoftLanding>())
        {
            try
            {
                SoftLanding softLanding = shipGO.AddComponent<SoftLanding>();
                
                Debug.Log("✅ SoftLanding añadido a: " + shipGO.name);
                Debug.Log("   Tipo de nave: " + (shipGO.GetComponent<Ship>() != null ? "(Small Ship)" : "(Unknown)"));
            }
            catch (System.MissingMethodException e)
            {
                Debug.LogError("❌ Error añadiendo SoftLanding:\n" + e.Message);
                Debug.Log("   Asegúrate de que el script SoftLanding.cs está compilado correctamente.");
            }
        }
    }

    /// <summary>
    /// Comprobar si una nave tiene SoftLanding.
    /// </summary>
    public static bool HasSoftLanding(GameObject shipGO)
    {
        return shipGO != null && shipGO.GetComponent<SoftLanding>() != null;
    }

    /// <summary>
    /// Obtener el componente SoftLanding de una nave.
    /// </summary>
    public static SoftLanding GetSoftLanding(GameObject shipGO)
    {
        return shipGO?.GetComponent<SoftLanding>();
    }
}
