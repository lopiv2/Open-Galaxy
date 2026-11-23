# Mapa de la galaxia (Galaxy Map)

## Estado / fases (19-nov-2026)

- ✅ Mapa 3D, sistemas, rejilla, etiquetas con auto-colocación, zoom 8–90.
- ✅ Hiperrutas por JSON: Corellian Run (19 paradas), Triellus (6).
- ✅ Entrada atmosférica Fase 1 (tecla X, Tatooine): fundido, skybox
  desértico + niebla, soles binarios, terreno procedural con semilla,
  capa de nubes plana, spawn a 2200 y descenso. Salida por techo + X.
- ⏳ Pendiente de probar en Play Mode (compila sin errores).
- 🔜 Fase 2: tabla de biomas por `planetType` (agua/ciudad), colisión real
  con daño, niebla/skybox por bioma, nubes volumétricas (render feature URP).
- 🔜 Fase 3+: POIs/aterrizaje, techo por planeta, tráfico local.

## Hiperrutas (`GalaxyRoutes.cs` + `StarSystems.json`)

- Las rutas se definen **en el JSON**, por sistema: cada entrada puede llevar
  `hyperrouteStops` con la ruta y su posición (`order`). Ejemplo:
  `"hyperrouteStops": [{ "route": "Corellian Run", "order": 2 }]`.
- `GalaxyRoutes` agrupa por nombre de ruta, ordena por `order` y cada pareja
  consecutiva es un tramo directo; el primero y el último son los términos.
- Primera ruta: **Corellian Run** = `Coruscant(0) → Corellia(1) → New Cov(2) →
  Doldur(3) → Druckenwell(4) → Kabray(5) → Algara(6) → Andosha II(7) →
  Mon Gazza(8) → Herdessa(9) → Habassa(10) → Radnor(11) → Christophsis(12) →
  Arkanis(13) → Gorno(14) → Dalchon(15) → Ryloth(16) → Wrea(17) →
  Smuggler's Run(18)` (`Smuggler's Run` es el término).
- Si el JSON no trae paradas usables, hay un fallback hardcodeado con esos
  mismos 4 sistemas para que el mapa siga funcionando.
- API: `GetRoute()`, `GetRoutesForSystem()`, `GetConnectedSystems()`,
  `AreConnected()`, `Hyperroute.IsTerminus()`, `Hyperroute.GetNext()`.
- Segunda ruta: **Triellus Trade Route** = `Tatooine(0) → Andooweel(1) →
  Kemal Station(2) → New Ator(3) → Issor(4) → Arkanis(5)` (Arkanis es
  cruce con la Corellian Run: un sistema puede estar en varias rutas).
- Para añadir una parada: poner `hyperrouteStops` con el `order` que toque en
  la entrada del sistema en `StarSystems.json`. Al añadir más sistemas al mapa,
  insertar sus paradas canónicas en orden, manteniendo las colas de cada ruta.
- El panel "System Intel" muestra `Route:` (con `(terminus)` si aplica) y
  `Connected:` (ver `GalaxyMapController.FormatSystemDetails()`).
- `GalaxyMap3DView.CreateRouteLines()` dibuja un tramo por cada pareja
  consecutiva cuyos dos extremos estén en el mapa. Las rutas principales se
  dibujan con halo azul + núcleo blanco (dos `LineRenderer` por tramo:
  halo 0.22 + núcleo 0.07).
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
   - `Planet Size` (0.6): multiplicador global del tamaño de esferas.
   - `Planet Size Near` (0.083): diámetro con zoom al máximo (cerca).
   - `Planet Size Far` (0.343): diámetro con zoom al mínimo (lejos). El tamaño
     interpola linealmente entre ambos según la distancia de cámara (8–90).
   - `Halo Size` (0): diámetro del resplandor (0 = sin halo).
   - `Label Height` (0.36): separación entre punto y nombre. Si hay solape, el
     nombre se recoloca solo: debajo → derecha → izquierda → arriba, evitando
     otros nombres y otros planetas.
   - `Label Font Size` (14): tamaño del texto.
   - `Labels Max Distance` (47.7): a partir de qué distancia se ocultan
     los nombres. Se lee cada frame como el resto: si lo mueves y no ves
     cambio, es que el zoom actual no cruza el umbral (p. ej. en vista
     general ya están ocultos hagas lo que hagas).
   - `Labels Max Distance` (56): a partir de qué distancia se ocultan nombres.
4. Para hacerlo permanente, copiar los valores a los defaults de esos campos
   en `GalaxyMap3DView.cs`.

## Solapes: offsets visuales (`GalaxyModeFunctions.MapDisplayOffsets`)

- Las coordenadas canónicas de `StarSystems.json` no se tocan nunca.
- Regla: solo lleva offset la pareja que ni el zoom máximo separa
  (a distancia 8, ~17 uds llenan un punto). El resto se lee acercando,
  estilo atlas: cadena Triellus, Bespin/Hoth, Herdessa/Habassa van en
  posición real.
- El offset se suma al dibujar: `GalaxyMap3DView.ToMapPosition()` y el
  fallback 2D de `GalaxyMapController.RefreshMapButtons()`.
- Entrada actual: Geonosis `(300, 0)` (a la derecha de Tatooine, misma
  altura; comparten posición canónica a ~9 uds, ambos R16).

## Entrada atmosférica (`GalaxyPlanetEntry.cs`, Fase 1: Tatooine)

- Tecla `X` (o `Y`/Triángulo en mando) cerca de un planeta soportado:
  fundido color arena, se oculta la esfera y se cambia el cielo a
  `loworbit_desert01` (fallback `sky_clouds01`) + niebla arenosa
  (800–7000). Los soles binarios siguen en escena y brillan entre la calima.
- Se genera `TerrainStreamer` (semilla del JSON, texturas `sandy`, 5×5
  tiles de 1 km, altura máx 250) más una **capa de nubes** procedural
  (FBM, a 1400, deriva lenta, visible por arriba y por abajo).
- La nave aparece a 2200, **sobre las nubes**, y desciende a la superficie
  (clamp de suelo a 25 uds por raycast a los tiles).
- Salir: subir de 1500 sobre el terreno (se vuelve a cruzar la capa) y
  pulsar `X`: fundido negro, se destruyen tiles y nubes, vuelve el cielo
  estrellado + niebla profunda y la nave regresa al punto de aproximación.
  El salto hiperespacial desde el planeta sale solo primero.
- `SupportedPlanets = { "Tatooine" }`: otros sistemas avisan "not yet
  supported". Viaje por mapa bloqueado durante las transiciones.
- V1: sin colisión real (clamp), nubes planas (las volumétricas de verdad
  van en Fase 2: render feature de URP + presupuesto de rendimiento).

## Añadir sistemas al mapa

### Herramienta de editor (recomendado)

`Tools > Open Galaxy > Add Planet to Galaxy Map` (`Assets/Editor/GalaxyMapPlanetAdder.cs`):

1. La ventana se abre ancha y arriba muestra **todos los sistemas ya activos**
   (los nombres de `StartPlanets` que tienen datos en el JSON): un grid de
   puntos clicables y un desplegable `Ir a sistema activo`. Pulsar cualquier
   punto rellena el panel de datos inferior, así no hace falta buscar sistema
   por sistema para saber si está añadido. El punto azul marca la selección.
2. Para añadir otro, buscar el planeta en el desplegable de la sección 2
   (lista de los nombres únicos de `StarSystems.json`; el contador muestra
   `visibles / total`).
3. Opcionalmente marcar **offset visual** (solo para sistemas que se solapan
   incluso con zoom máximo) y/o **parada de hiperruta** (ruta + `order`).
4. Pulsar **Añadir al mapa**. La herramienta escribe el nombre exacto en
   `GalaxyModeFunctions.StartPlanets`, añade el offset a `MapDisplayOffsets`
   y/o el `hyperrouteStops` en la entrada del JSON, y refreshea Unity.
   **Quitar del mapa** revierte la whitelist y el offset (no borra datos
   canónicos del JSON).

No hace falta tocar código ni JSON a mano. La edición del JSON es puntual:
no se reserializa el archivo completo, por lo que se conservan todos los
campos y el formato original.

### Manual

1. Añadir el nombre exacto (como en `StarSystems.json`) a
   `GalaxyModeFunctions.StartPlanets`.
2. Queda visible en el mapa 3D y en el menú Galaxy Map automáticamente
   (planeta, distancia y soles se resuelven desde el JSON).
