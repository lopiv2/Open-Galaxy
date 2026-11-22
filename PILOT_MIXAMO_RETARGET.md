# Pilot → Mixamo animation retargeting

Estado actual del `.blend`: **el esqueleto se ha conservado intacto**. No se han borrado huesos,
modificadores ni vertex groups. El armature `Armature` tiene 200 huesos, 20 mallas hijas y está en
pose de reposo/T-pose (ningún pose bone con rotación, posición o escala distinta de la identidad).

## Por qué no basta con subir el FBX tal cual

Las animaciones de Mixamo están hechas para su propio esqueleto (`mixamorig:Hips`,
`mixamorig:LeftUpLeg`, …). Este pilot usa nombres propios (`root hips`, `leg left thigh`, …), así que
Unity no puede emparejarlos automáticamente. Hay que crear un **Avatar Humanoid** y rellenar el
mapa de huesos una vez; después cualquier animación Humanoid de Mixamo (caminar, correr, saltar…)
se reproducirá sobre este rig sin tocar la malla ni los pesos.

Ventaja de esta ruta: se conservan los 200 huesos, incluidos cara, dedos y accesorios. Mixamo sólo
moverá el cuerpo; los huesos faciales/auxiliares (`unused…`) quedan quietos, que es lo esperado.

## 1. Exportar desde Blender

1. Selecciona el armature `Armature` y las 20 mallas (todas son hijas suyas).
2. El armature tiene `Scale = 10`, por lo que en la escena el modelo mide ~18,6 m. Para que Mixamo
   y Unity lo vean a escala humana (~1,86 m), **pon temporalmente el armature a `Scale = 1`** antes
   de exportar, o deja la exportación tal cual y aplica `Scale Factor = 0.1` en el importador de
   Unity. No es necesario aplicar la escala a la malla.
3. `File → Export → FBX`:
   - Object Types: **Armature + Mesh** (no Camera/Light).
   - `Add Leaf Bones`: desactivado.
   - `Bake Anim`: desactivado (queremos la malla en T-pose, sin acciones).
   - `Apply Transform`: activado.
4. Restaura el armature a `Scale = 10` en la escena original si quieres dejar el `.blend` como estaba.

## 2. Importar en Unity

1. Arranca el FBX a `Assets/...`.
2. En el **Model Importer → Rig**:
   - `Animation Type`: **Humanoid**
   - `Avatar Definition`: **Create From This Model**
   - Pulsa **Configure…**. Si Unity dice que no puede crear el avatar automáticamente, no pasa nada:
     abre la pestaña **Mapping** y asigna los huesos de la tabla siguiente.
3. En la pestaña **Pose**, si hace falta, `Reset` / `Enforce T-Pose`. El modelo ya está en reposo,
   así que normalmente no hará falta corregir nada.
4. **Done / Apply**. Guarda el Avatar resultante. Ese Avatar es el que debes usar en el `Animator`
   del prefab del pilot.
5. Importa el FBX de animación de Mixamo también como **Humanoid**. Como las dos avatares son
   Humanoid, no necesitan tener los mismos nombres de huesos: Unity retargetea automáticamente.

## 3. Mapa de huesos (Unity Humanoid → este rig)

Huesos principales (obligatorios para que Unity acepte el Avatar):

| Unity Humanoid bone | Hueso en este rig |
| --- | --- |
| Hips | `root hips` |
| Spine | `spine lower` |
| Chest | `spine middle` |
| UpperChest | `spine upper` |
| Neck | `head neck lower` |
| Head | `head neck upper` |
| Left Shoulder | `arm left shoulder 1` |
| Left Upper Arm | `arm left shoulder 2` |
| Left Lower Arm | `arm left elbow` |
| Left Hand | `arm left wrist` |
| Right Shoulder | `arm right shoulder 1` |
| Right Upper Arm | `arm right shoulder 2` |
| Right Lower Arm | `arm right elbow` |
| Right Hand | `arm right wrist` |
| Left Upper Leg | `leg left thigh` |
| Left Lower Leg | `leg left knee` |
| Left Foot | `leg left ankle` |
| Left Toes | `leg left toes` |
| Right Upper Leg | `leg right thigh` |
| Right Lower Leg | `leg right knee` |
| Right Foot | `leg right ankle` |
| Right Toes | `leg right toes` |

Dedos (opcional; sólo si quieres que las animaciones de Mixamo muevan los dedos):

| Unity | Este rig |
| --- | --- |
| Left Hand Thumb 1/2/3 | `arm left finger thumb 1/2/3` |
| Left Hand Index 1/2/3 | `arm left finger index 1/2/3` |
| Left Hand Middle 1/2/3 | `arm left finger middle 1/2/3` |
| Left Hand Ring 1/2/3 | `arm left finger ring 1/2/3` |
| Left Hand Little 1/2/3 | `arm left finger pinky 1/2/3` |
| Right Hand Thumb 1/2/3 | `arm right finger thumb 1/2/3` |
| Right Hand Index 1/2/3 | `arm right finger index 1/2/3` |
| Right Hand Middle 1/2/3 | `arm right finger middle 1/2/3` |
| Right Hand Ring 1/2/3 | `arm right finger ring 1/2/3` |
| Right Hand Little 1/2/3 | `arm right finger pinky 1/2/3` |

`root ground` se ignora (Unity usa `Hips` como raíz del root motion). También se ignoran
`pelvis`, los huesos `…adj…`, `collar back`, `belt …`, todos los `head …` faciales, `hair …` y
los ~32 huesos que empiezan por `unused`. No los borres: siguen siendo necesarios para los pesos
de las mallas.

## 4. Si Unity se empeña en no crear el Avatar

Es por los nombres, no por la estructura (la jerarquía sí es humanoid). La solución limpia es
duplicar el armature + mallas en Blender y renombrar **sólo en la copia** los huesos de la tabla a
los nombres de Unity, renombrando también los vertex groups correspondientes para que los pesos
sigan pegados. El original queda intacto; dime si quieres que prepare esa copia.

## 5. Animación de caminar genérica

`Assets/Data/objects/readyrooms/characters/animations/Walking.fbx` está importada como **Humanoid**
(Avatar `WalkingAvatar`, válido) con el clip renombrado `Walking`, 1,033 s y `Loop Time` activado.
El controlador `Walking.controller` sigue siendo el ejemplo mínimo con un único estado `Walk`.
`Running.fbx` también está importado como Humanoid (`RunningAvatar`, clip `Running` de 0,633 s en
bucle) y `CharacterLocomotion.controller` usa `Idle` / `Walk` / `Run` con los parámetros `Speed`
y `IsRunning`. Ese es el controlador asignado ahora a `rebel_pilot`, con `Apply Root Motion = false`
para que el movimiento lo haga el script y no la animación.
Los personajes que no tienen Avatar Humanoid (los droides del readyroom) no pueden retargetearlo
hasta que se les configure uno.

## 6. Modo a pie (prototipo)

`Assets/Scripts/Galaxy/GroundCharacterController.cs` se crea solo al arrancar en play mode y no
necesita escenas ni prefabs modificados:

- Con **C** cerca de una nave aterrizada (o a menos de 20 m del terreno) instancia `rebel_pilot`
  desde `Resources/objects/readyrooms/readyroom_white` y lo deja sobre un tile `Tile_x_y`.
- **WASD** mueve al personaje relativo a la cámara (también funciona el stick izquierdo del mando)
  y **Shift** corre.
- Cámara en tercera persona a 2,2 m, con **botón derecho del ratón** o **stick derecho** para
  orbitar. `OGCamera` se desactiva mientras estás a pie para que su `FixedUpdate` no pelee con el
  `LateUpdate` del personaje (evita la vibración); al volver a la nave se restaura la transformada
  local de la cámara y se reactiva `OGCamera`. La cámara tampoco baja del terreno.
- El Animator usa `Speed` e `IsRunning` para elegir entre `Idle`, `Walk` y `Run`.
- Pulsar **C** otra vez, pulsar **Z** (vuelve a la nave; un segundo **Z** despega) o salir del
  planeta devuelve el control a la nave.
- Mientras estás a pie, `FlightControlSystem_Small` se desactiva y `OGInputFunctions.shipInputLocked`
  deja a cero todo el input de la nave (empuje, armas, poder, apuntado y contramedidas), así que
  WASD sólo mueve al personaje.
- `SoftLanding` no se desactiva: se pone `suspended = true`. Desactivarlo disparaba su `OnDisable`,
  que llamaba a `ResetLandingState()` y ponía `IsLanded = false`; entonces `GalaxyPlanetEntry`
  aplicaba el clamp de 25 m y la nave parecía despegar al bajar. Al volver a la nave se reanuda tras
  dos frames para que el `Z` del personaje no se interprete como despegue.
- Si el terreno no tiene tiles `Tile_x_y` debajo, el modo no se activa y se avisa en el log.

## 7. Materiales de la cabeza

El FBX incluye las mallas `mat_head`, `mat_eyes` y `mat_teeth`. El prefab variant
`rebel_pilot.prefab` sobrescribe sus materiales con `MAT_Head`, `MAT_Eyes` y `MAT_Teeth`
(en `Assets/Data/objects/readyrooms/characters/rebel_pilot/Materials/`), todos URP Lit y con las
texturas de `head/`: color `T_Heads_Roger_01_CS`, normal `T_Heads_Roger_01_NW`, oclusión
`T_Heads_Base_Male_RSSSS`, ojos `T_Eyes_Brown_C` y dientes `T_MP_Teeth_D`. Se aplican como
overrides del prefab para no modificar el FBX importado ni dejar el material gris `Lit`.
