# Mapa de la galaxia (Galaxy Map)

## Hiperrutas (`GalaxyRoutes.cs` + `StarSystems.json`)

- Las rutas se definen **en el JSON**, por sistema: cada entrada puede llevar
  `hyperrouteStops` con la ruta y su posición (`order`). Ejemplo:
  `"hyperrouteStops": [{ "route": "Corellian Run", "order": 2 }]`.
- `GalaxyRoutes` agrupa por nombre de ruta, ordena por `order` y cada pareja
  consecutiva es un tramo directo; el primero y el último son los términos.
- Primera ruta: **Corellian Run** = `Coruscant(0) → Corellia(1) → Wrea(2) →
  Smuggler's Run(3)` (`Smuggler's Run` es el término, conectado con `Wrea`).
- Si el JSON no trae paradas usables, hay un fallback hardcodeado con esos
  mismos 4 sistemas para que el mapa siga funcionando.
- API: `GetRoute()`, `GetRoutesForSystem()`, `GetConnectedSystems()`,
  `AreConnected()`, `Hyperroute.IsTerminus()`, `Hyperroute.GetNext()`.
- Para añadir una parada: poner `hyperrouteStops` con el `order` que toque en
  la entrada del sistema en `StarSystems.json`. Al añadir más sistemas al mapa,
  insertar sus paradas canónicas entre `Corellia` y `Wrea`, manteniendo la cola
  `[..., Wrea, Smuggler's Run]`.
- El panel "System Intel" muestra `Route:` (con `(terminus)` si aplica) y
  `Connected:` (ver `GalaxyMapController.FormatSystemDetails()`).
- `GalaxyMap3DView.CreateRouteLines()` dibuja un tramo dorado por cada pareja
  consecutiva cuyos dos extremos estén en el mapa.
- Los viajes siguen siendo libres (sin restricción a vecinos); la conexión es
  de momento datos + visual.

## Estilo de marcadores (`GalaxyMap3DView.cs`)

- Núcleo: esfera 3D real (`PrimitiveType.Sphere`) con material **Lit**
  (URP Lit, fallback a Standard) + luz propia del mapa (`MapKeyLight`,
  solo capa 31). El sombreado se consigue con luz real, no con textura.
- Halo: quad con textura radial generada por código (`CreateHaloTexture()`),
  siempre mirando a cámara (billboard en `LateUpdate()`). Es solo el
  resplandor; el planeta es la esfera.
- Colores: núcleo blanco-azulado + halo rojo apagado por defecto;
  dorado en selección/hover, verde en sistema actual.
- Etiquetas: el nombre se proyecta **debajo** del planeta y **se oculta con
  zoom lejano** (cámara más lejos que `labelsMaxDistance`), para que la vista
  general quede limpia. La capa "System names" del diálogo Layers sigue siendo
  el interruptor maestro.

## Ajuste de tamaños (en vivo, Play Mode)

1. Ejecutar el juego, entrar en Galaxy Mode y abrir el mapa con `M`.
2. En Hierarchy seleccionar el objeto `GalaxyMap3DView`.
3. En el Inspector, sección **Marker style**:
   - `Planet Size` (0.34): diámetro de las esferas 3D.
   - `Halo Size` (1.6): diámetro del resplandor.
   - `Label Height` (0.55): distancia del nombre por debajo del planeta.
   - `Label Font Size` (12): tamaño del texto.
   - `Labels Max Distance` (56): a partir de qué distancia se ocultan nombres.
4. Para hacerlo permanente, copiar los valores a los defaults de esos campos
   en `GalaxyMap3DView.cs`.

## Añadir sistemas al mapa

1. Añadir el nombre exacto (como en `StarSystems.json`) a
   `GalaxyModeFunctions.StartPlanets`.
2. Queda visible en el mapa 3D y en el menú Galaxy Map automáticamente
   (planeta, distancia y soles se resuelven desde el JSON).
