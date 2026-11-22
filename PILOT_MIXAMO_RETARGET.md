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
