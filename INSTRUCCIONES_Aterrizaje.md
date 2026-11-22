# Instrucciones para Añadir Sistema de Aterrizaje Suave

## ✅ Script Completado

El script `SoftLanding.cs` ya ha sido creado en:
```
Assets/Scripts/Galaxy/SoftLanding.cs
```

## 🔧 Cómo Activar el Sistema

### Método 1: Agregar manualmente a la nave principal (Recomendado)

1. Abre Unity y ve a tu escena
2. Encuentra tu nave principal (debería ser un GameObject que contiene `Ship`, `FlightControlSystem_SSmall`, etc.)
3. En el Panel Project, busca el script `SoftLanding` en `Assets/Scripts/Galaxy/`
4. Arrastra el script sobre tu nave principal (o haz clic derecho → Add Component → SoftLanding)

### Método 2: Editar SceneFunctions.cs manualmente

1. Abre `Assets/Scripts/Scene/SceneFunctions.cs`
2. Ve a la línea 951-952
3. Inserta una nueva línea después de la línea 951:

```csharp
                    SoftLanding softLanding = shipGO.AddComponent<SoftLanding>();
```

### Método 3: Agregar al crear un nuevo GameObject

Si quieres que el sistema se añada automáticamente a nuevas naves, edita también `GalaxyEntry.cs`:

1. Abre `Assets/Scripts/Galaxy/GalaxyEntry.cs`
2. Busca la función donde se añade el `Ship` componente
3. Añade:
   ```csharp
   SoftLanding softLanding = shipGO.AddComponent<SoftLanding>();
   ```

## 🎮 Cómo Usar el Sistema de Aterrizaje

### Controles
- **Tecla:** `Z` para aterrizar y para despegar (también puedes despegar acelerando con W)
- **Altura de aterrizaje:** 10 metros sobre el suelo del planeta
- **Rango máximo:** 250 metros sobre el terreno

### Secuencia de Uso
1. Vuela tu nave dentro de la atmósfera del planeta
2. Acerca la nave a menos de 250 metros del suelo
3. Pulsa `Z` para iniciar la secuencia de aterrizaje suave
4. La nave descenderá suavemente hasta los 10 metros y se quedará posada
5. Pulsa `Z` o acelera con W para despegar

### Requisitos
- Estar en Galaxy Mode (sobre un planeta)
- No estar en hyperspace ni acoplándose
- Altura inferior a `maxLandingAltitude` (250 m por defecto)

## 📝 Configuración Disponible

En `SoftLanding.cs` puedes personalizar:

```csharp
public float landingAltitude = 10f;     // Altura final (metros)
public float descentSpeed = 15f;        // Velocidad de descenso (m/s)
public float maxLandingAltitude = 250f; // Rango máximo para aterrizar
```

## 🎨 Marcador Visual

El sistema crea automáticamente un marcador visual transparente verde cuando estás cerca del rango de aterrizaje. Se gestiona solo y no requiere configuración adicional.

## 🔍 Debugging

### Verificar que funciona
1. Ateriza tu nave sobre un planeta
2. Acerca los 50m del suelo
3. Deberías ver el marcador verde aparecer
4. Pulsa Z para aterrizar

### Problemas comunes

**Marcador no aparece:**
- Revisa la consola de Unity por errores de compilación
- Asegúrate de que `GalaxyPlanetEntry.IsOnPlanet == true`
- Verifica que `scene.mainShip == this.gameObject`

**Aterrizaje fallido:**
- Revisa los mensajes en el HUD (MissionLog)
- Asegúrate de no estar en hyperspace
- La nave no debe ser AI (isAI = false)
- Debes estar por debajo de `maxLandingAltitude` y con tiles de terreno debajo

## 📦 Archivos Relacionados

```
Assets/Scripts/Galaxy/SoftLanding.cs              ← Script principal
Assets/Resources/Models/LandingMarker.prefab      ← Marcador visual (simple plano)
README_Aterrizaje.md                              ← Documentación general
INSTRUCCIONES_Aterrizaje.md                       ← Instrucciones paso a paso
```

## ✨ Próximos Pasos Opcionales

1. **Crear vfx de aterrizaje:** Puedes añadir partículas en el punto de aterrizaje
2. **Audio personalizado:** Añadir efectos de sonido para el touchdown final
3. **Docking automático:** Conectar con el sistema de docking del hangar

## 📚 Referencias

- `PlanetNavigation.cs` - Usa la misma lógica de raycast para detectar suelo
- `HudFunctions.AddToMissionLog()` - Para mensajes al aterrizar
- `AudioFunctions.PlayAudioClip()` - Para efectos de sonido durante el aterrizaje

---

**Hecho por tu agente de OpenCode en Open-Galaxy 🚀**
