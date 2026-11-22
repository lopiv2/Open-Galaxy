# Sistema de Aterrizaje Suave (Soft Landing)

## Descripción

Este sistema permite aterrizar tu nave en la superficie del planeta cuando estás dentro de la atmósfera y pulsas la tecla **Z** a menos de 250 metros del suelo. La nave desciende, se nivela con el horizonte y queda posada con los controles bloqueados hasta que despegas.

## Ubicación del Script

El script se encuentra en:
```
Assets/Scripts/Galaxy/SoftLanding.cs
```

## Cómo Funciona

### Tecla de Aterrizaje
- **Tecla:** `Z` (puedes cambiar esto en las configuraciones de entrada)
- **Altura objetivo:** 10 metros sobre el suelo
- **Protección:** La nave está invulnerable durante la secuencia de aterrizaje

### Requisitos para Aterrizaje
1. Debes estar en un planeta (Galaxy Mode)
2. La nave debe estar cerca del suelo (hasta 250 m sobre el terreno)
3. No debe estar en hyperspace ni acoplándose
4. La nave no debe ser controlada por IA

### Secuencia de Aterrizaje
1. Al pulsar Z, la nave desciende suavemente y se nivela con el horizonte
2. El marcador visual (plano transparente verde) se coloca sobre el suelo
3. El juego reproduce sonidos de motor durante el descenso
4. La nave se posa a 10 m del suelo y queda bloqueada
5. Se muestra un mensaje "LANDED" en el HUD
6. Para despegar, pulsa Z o acelera con el mando (W)

## Configuración

### Modificar Altura de Aterrizaje

Abre `Assets/Scripts/Galaxy/SoftLanding.cs` y cambia estas líneas:

```csharp
public float landingAltitude = 10f;     // Altura final sobre el suelo (metros)
public float descentSpeed = 15f;        // Velocidad de descenso (m/s)
public float maxLandingAltitude = 250f; // Altura máxima a la que Z aterriza
public float markerRange = 250f;        // Distancia a la que aparece el marcador
```

### Cambiar Tecla de Aterrizaje

Para cambiar la tecla Z por otra, edita este bloque en `SoftLanding.cs`:

```csharp
Keyboard keyboard = Keyboard.current;

if (keyboard != null && keyboard.zKey.wasPressedThisFrame == true)
{
    AttemptSoftLanding();
    return;
}
```

Puedes cambiar `zKey` por otra tecla como `Enter`, `E`, etc.

### Cambiar Zonas de Aterrizaje

El sistema permite aterrizar en cualquier punto del planeta, siempre que haya terreno (`Tile_*`) debajo de la nave y estés por debajo de `maxLandingAltitude`.

## Añadir el Componente Manualmente

### Método 1: Agregar vía Inspector (Recomendado para desarrollo)

1. Haz clic derecho en la escena de Unity → Create → Empty
2. Nómbralo "SoftLandingRoot"
3. Arrastra el script `SoftLanding` al nuevo objeto
4. En el inspector, arrastra tu nave al campo `Ship` del componente

### Método 2: Agregar vía Script

Crea un script llamado `AddSoftLandingToShip.cs`:

```csharp
using UnityEngine;
using Object;

public class AddSoftLandingToShip : MonoBehaviour
{
    void Start()
    {
        Ship ship = GetComponent<Ship>();
        
        if (ship != null)
        {
            SoftLanding softLanding = gameObject.AddComponent<SoftLanding>();
            Debug.Log("Soft Landing component added to: " + gameObject.name);
        }
    }
}
```

## Troubleshooting

### El sistema no detecta el suelo

Asegúrate de que estés en Galaxy Mode y que el planeta tenga tiles generados correctamente. `SampleGroundHeight` solo acepta colliders llamados `Tile_*` (ignora la propia nave y otros objetos), así que si devuelve NaN significa que no hay terreno generado debajo.

### Visual Marker no aparece

El marcador visual se crea automáticamente en `Start()`. Asegúrate de que el script se compila correctamente sin errores en la consola.

## Notas Técnicas

- El sistema usa raycasts para detectar el suelo
- Los materiales del marcador son transparentes usando URP/Unlit shader
- El marcador es un plano simple (no ProBuilder necesario)
- Se gestiona automáticamente la vida útil del objeto para evitar leaks
